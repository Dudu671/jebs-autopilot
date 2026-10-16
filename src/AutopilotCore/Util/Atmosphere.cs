using System;
using System.Reflection;
using UnityEngine;

namespace AutopilotCore
{
    // Reads wind, pressure, and temperature from Kerbal Weather Project when it is loaded.
    // The host samples them at each vessel's position; without it, KWP's active-vessel values are used.
    // Resolved once per Core assembly so a missing mod falls back to the stock atmosphere.
    public static class Atmosphere
    {
        private const string AssemblyName = "KerbalWeatherProject";
        private const string HostType = "AutopilotHost.KwpWeather, AutopilotHost";

        private delegate bool SampleFn(Vessel vessel, out Vector3d wind, out double temperatureK, out double pressurePa);

        private static SampleFn _hostSample;
        private static bool _searched;
        private static bool _present;
        private static FieldInfo _weatherEnabled;
        private static FieldInfo _usePoint;
        private static MethodInfo _climoWind;
        private static FieldInfo _climoTemperature;
        private static FieldInfo _climoPressure;
        private static MethodInfo _pointWind;
        private static FieldInfo _pointTemperature;
        private static FieldInfo _pointPressure;

        private static Vessel _cachedVessel;
        private static float _cachedFixedTime;
        private static int _cachedFrame;
        private static Vector3d _cachedWind;
        private static double _cachedTemperature;
        private static double _cachedPressure;

        public static bool Available
        {
            get
            {
                EnsureBound();
                return _present;
            }
        }

        // World-space velocity of the air mass, m/s. Zero when weather is off or KWP is absent.
        public static Vector3d WindWorld(Vessel vessel)
        {
            double temperature;
            double pressure;
            return Read(vessel, out temperature, out pressure);
        }

        public static float TemperatureK(Vessel vessel)
        {
            double temperature;
            double pressure;
            Read(vessel, out temperature, out pressure);
            return (float)temperature;
        }

        public static float PressurePa(Vessel vessel)
        {
            double temperature;
            double pressure;
            Read(vessel, out temperature, out pressure);
            return (float)pressure;
        }

        // One IAS reads wind, temperature and pressure separately, and the modules and instruments read
        // them many times a frame; the air only changes with the physics step, so the last read is kept.
        private static Vector3d Read(Vessel vessel, out double temperatureK, out double pressurePa)
        {
            if (vessel != _cachedVessel || Time.fixedTime != _cachedFixedTime || Time.frameCount != _cachedFrame)
            {
                _cachedWind = Sample(vessel, out _cachedTemperature, out _cachedPressure);
                _cachedVessel = vessel;
                _cachedFixedTime = Time.fixedTime;
                _cachedFrame = Time.frameCount;
            }

            temperatureK = _cachedTemperature;
            pressurePa = _cachedPressure;
            return _cachedWind;
        }

        private static Vector3d Sample(Vessel vessel, out double temperatureK, out double pressurePa)
        {
            temperatureK = vessel.atmosphericTemperature;
            pressurePa = vessel.staticPressurekPa * 1000.0;
            EnsureBound();

            Vector3d sampled;
            double hostTemperature;
            double hostPressure;
            if (_hostSample != null && _hostSample(vessel, out sampled, out hostTemperature, out hostPressure))
            {
                if (hostTemperature > 1.0)
                {
                    temperatureK = hostTemperature;
                }

                if (hostPressure > 1.0)
                {
                    pressurePa = hostPressure;
                }

                return sampled;
            }

            if (!_present || !Flag(_weatherEnabled))
            {
                return Vector3d.zero;
            }

            bool point = Flag(_usePoint);
            Vector3d wind = InvokeWind(point ? _pointWind : _climoWind);
            double temperature = Field(point ? _pointTemperature : _climoTemperature);
            double pressure = Field(point ? _pointPressure : _climoPressure);
            if (temperature > 1.0)
            {
                temperatureK = temperature;
            }

            if (pressure > 1.0)
            {
                pressurePa = pressure;
            }

            return wind;
        }

        private static void EnsureBound()
        {
            if (_searched)
            {
                return;
            }

            _searched = true;
            _hostSample = BindHost();
            try
            {
                Assembly assembly = FindAssembly();
                if (assembly == null)
                {
                    return;
                }

                Type util = assembly.GetType("KerbalWeatherProject.Util");
                Type climo = assembly.GetType("KerbalWeatherProject.KerbalWxClimo");
                Type point = assembly.GetType("KerbalWeatherProject.KerbalWxPoint");
                if (util == null || climo == null || point == null)
                {
                    return;
                }

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                _weatherEnabled = util.GetField("wx_enabled", flags);
                _usePoint = util.GetField("use_point", flags);
                _climoWind = climo.GetMethod("getWSWind", flags);
                _climoTemperature = climo.GetField("tempWS", flags);
                _climoPressure = climo.GetField("presWS", flags);
                _pointWind = point.GetMethod("getWSWind", flags);
                _pointTemperature = point.GetField("tempWS", flags);
                _pointPressure = point.GetField("presWS", flags);
                _present = _weatherEnabled != null && _usePoint != null &&
                           _climoWind != null && _pointWind != null &&
                           _climoTemperature != null && _climoPressure != null &&
                           _pointTemperature != null && _pointPressure != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutopilotCore] Kerbal Weather Project was not bound: " + ex.Message);
                _present = false;
            }
        }

        // Core is loaded from bytes and does not reference the host, so the host's sampler is bound by name.
        private static SampleFn BindHost()
        {
            try
            {
                Type host = Type.GetType(HostType);
                MethodInfo sample = host != null ? host.GetMethod("Sample", BindingFlags.Public | BindingFlags.Static) : null;
                return sample != null ? (SampleFn)Delegate.CreateDelegate(typeof(SampleFn), sample) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutopilotCore] Host weather sampler not bound: " + ex.Message);
                return null;
            }
        }

        private static Assembly FindAssembly()
        {
            for (int i = 0; i < AssemblyLoader.loadedAssemblies.Count; i++)
            {
                AssemblyLoader.LoadedAssembly loaded = AssemblyLoader.loadedAssemblies[i];
                if (loaded.name == AssemblyName)
                {
                    return loaded.assembly;
                }
            }

            return null;
        }

        private static bool Flag(FieldInfo field)
        {
            object value = field.GetValue(null);
            return value is bool && (bool)value;
        }

        private static double Field(FieldInfo field)
        {
            object value = field.GetValue(null);
            return value is double ? (double)value : 0.0;
        }

        private static Vector3d InvokeWind(MethodInfo method)
        {
            object value = method.Invoke(null, null);
            if (!(value is Vector3))
            {
                return Vector3d.zero;
            }

            Vector3 wind = (Vector3)value;
            return new Vector3d(wind.x, wind.y, wind.z);
        }
    }
}
