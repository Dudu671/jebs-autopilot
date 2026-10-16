using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AutopilotHost
{
    // Kerbal Weather Project's air at any vessel's own position, by plain reflection, so the host
    // loads with or without KWP and Harmony. AutopilotKwp patches KWP's flight integrator overrides
    // to call Subject and Wind; Core reads Sample by reflection.
    public static class KwpWeather
    {
        private const string AssemblyName = "KerbalWeatherProject";
        private const string Prefix = "KerbalWeatherProject.";
        private const float SurfaceHeight = 50f;
        private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly Dictionary<Guid, Entry> Cache = new Dictionary<Guid, Entry>();
        private static readonly object[] ClimoArguments = new object[3];
        private static readonly object[] PointArguments = new object[1];

        private static bool _searched;
        private static bool _bound;
        private static FieldInfo _enabled;
        private static FieldInfo _useClimo;
        private static FieldInfo _usePoint;
        private static FieldInfo _allowThermo;
        private static FieldInfo _noSurfaceWind;
        private static FieldInfo _constantWind;
        private static FieldInfo _constantSpeed;
        private static FieldInfo _constantDirection;
        private static MethodInfo _climoAtm;
        private static MethodInfo _pointAtm;
        private static Func<Vector3> _climoActiveWind;
        private static Func<Vector3> _pointActiveWind;
        private static AtmFields _climoFields;
        private static AtmFields _pointFields;

        // True once AutopilotKwp has made KWP's wind reach wings and fins at every height, not only below 10 m.
        public static bool LiftInFlight { get; set; }

        public static bool Bound
        {
            get
            {
                EnsureBound();
                return _bound;
            }
        }

        // Air at the vessel's own position: world-space wind (m/s), temperature (K), pressure (Pa).
        // False when KWP is absent or its weather is off. Cached per vessel and physics step.
        public static bool Sample(Vessel vessel, out Vector3d wind, out double temperatureK, out double pressurePa)
        {
            wind = Vector3d.zero;
            temperatureK = 0.0;
            pressurePa = 0.0;
            EnsureBound();
            if (!_bound || vessel == null || !Flag(_enabled))
            {
                return false;
            }

            Entry entry;
            if (!Cache.TryGetValue(vessel.id, out entry) || entry.Time != Time.fixedTime)
            {
                entry = Compute(vessel);
                entry.Time = Time.fixedTime;
                Cache[vessel.id] = entry;
            }

            wind = entry.Wind;
            temperatureK = entry.Temperature;
            pressurePa = entry.Pressure;
            return entry.Valid;
        }

        // Stands in for KWP's FlightGlobals.ActiveVessel: the integrator's own vessel.
        public static Vessel Subject(FlightIntegrator fi)
        {
            Vessel own = fi != null ? fi.Vessel : null;
            return own != null ? own : FlightGlobals.ActiveVessel;
        }

        // Stands in for KWP's world-space wind, sampled at the active vessel: the wind at the integrator's vessel.
        public static Vector3 Wind(FlightIntegrator fi)
        {
            try
            {
                Vessel own = Subject(fi);
                if (own == null || own == FlightGlobals.ActiveVessel || !Bound)
                {
                    return ActiveWind();
                }

                Vector3d wind;
                double temperature;
                double pressure;
                return Sample(own, out wind, out temperature, out pressure) ? (Vector3)wind : Vector3.zero;
            }
            catch (Exception)
            {
                return ActiveWind();
            }
        }

        private static Vector3 ActiveWind()
        {
            if (Flag(_useClimo) && _climoActiveWind != null)
            {
                return _climoActiveWind();
            }

            return Flag(_usePoint) && _pointActiveWind != null ? _pointActiveWind() : Vector3.zero;
        }

        // Mirrors KWP's own wind: climatology at lat/lon/alt, or the point forecast at altitude;
        // zeroed near the ground when surface wind is off; overridden by the constant-wind setting.
        // Components are east, north, up.
        private static Entry Compute(Vessel vessel)
        {
            Entry entry = new Entry();
            bool climo = Flag(_useClimo);
            bool point = Flag(_usePoint);
            if (!climo && !point)
            {
                return entry;
            }

            object atm = climo
                ? _climoAtm.Invoke(null, ClimoArgs(vessel))
                : _pointAtm.Invoke(null, PointArgs(vessel));
            AtmFields fields = climo ? _climoFields : _pointFields;
            double east = Read(fields.WindX, atm);
            double north = Read(fields.WindY, atm);
            double up = Read(fields.WindZ, atm);

            bool nearGround = vessel.LandedOrSplashed || !(vessel.heightFromTerrain >= SurfaceHeight);
            if (nearGround && Flag(_noSurfaceWind))
            {
                east = 0.0;
                north = 0.0;
                up = 0.0;
            }

            if (Flag(_constantWind))
            {
                double speed = Int(_constantSpeed);
                double direction = Int(_constantDirection) * Math.PI / 180.0;
                east = -speed * Math.Sin(direction);
                north = -speed * Math.Cos(direction);
                up = 0.0;
            }

            entry.Wind = (vessel.east * east) + (vessel.north * north) + (vessel.upAxis * up);
            entry.Temperature = Flag(_allowThermo) ? Read(fields.Temperature, atm) : vessel.atmosphericTemperature;
            entry.Pressure = Flag(_allowThermo) ? Read(fields.Pressure, atm) : vessel.staticPressurekPa * 1000.0;
            entry.Valid = true;
            return entry;
        }

        // Argument arrays reused across calls; Compute runs per vessel every physics step.
        private static object[] ClimoArgs(Vessel vessel)
        {
            ClimoArguments[0] = vessel.latitude;
            ClimoArguments[1] = vessel.longitude;
            ClimoArguments[2] = vessel.altitude;
            return ClimoArguments;
        }

        private static object[] PointArgs(Vessel vessel)
        {
            PointArguments[0] = vessel.altitude;
            return PointArguments;
        }

        private static void EnsureBound()
        {
            if (_searched)
            {
                return;
            }

            _searched = true;
            try
            {
                _bound = Bind();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutopilotHost] Kerbal Weather Project was not bound: " + ex.Message);
                _bound = false;
            }
        }

        private static bool Bind()
        {
            Assembly kwp = FindAssembly();
            if (kwp == null)
            {
                return false;
            }

            Type util = kwp.GetType(Prefix + "Util");
            Type climoData = kwp.GetType(Prefix + "climate_data");
            Type pointData = kwp.GetType(Prefix + "weather_data");
            Type climo = kwp.GetType(Prefix + "KerbalWxClimo");
            Type point = kwp.GetType(Prefix + "KerbalWxPoint");
            if (util == null || climoData == null || pointData == null || climo == null || point == null)
            {
                return false;
            }

            _enabled = util.GetField("wx_enabled", Statics);
            _useClimo = util.GetField("use_climo", Statics);
            _usePoint = util.GetField("use_point", Statics);
            _allowThermo = util.GetField("allow_thermo", Statics);
            _noSurfaceWind = util.GetField("disable_surface_wind", Statics);
            _constantWind = util.GetField("cnst_wnd", Statics);
            _constantSpeed = util.GetField("wspd_prof", Statics);
            _constantDirection = util.GetField("wdir_prof", Statics);
            _climoAtm = climoData.GetMethod("getAmbientWx3D", Statics, null, new[] { typeof(double), typeof(double), typeof(double) }, null);
            _pointAtm = pointData.GetMethod("getAmbientWx3D", Statics, null, new[] { typeof(double) }, null);
            _climoActiveWind = ActiveWindGetter(climo);
            _pointActiveWind = ActiveWindGetter(point);
            _climoFields = _climoAtm != null ? new AtmFields(_climoAtm.ReturnType) : null;
            _pointFields = _pointAtm != null ? new AtmFields(_pointAtm.ReturnType) : null;

            bool bound = _enabled != null && _useClimo != null && _usePoint != null && _allowThermo != null &&
                         _noSurfaceWind != null && _constantWind != null && _constantSpeed != null && _constantDirection != null &&
                         _climoFields != null && _climoFields.Complete && _pointFields != null && _pointFields.Complete &&
                         _climoActiveWind != null && _pointActiveWind != null;
            if (!bound)
            {
                Debug.LogWarning("[AutopilotHost] KWP layout changed; vessels away from the active one keep its wind.");
            }

            return bound;
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

        private static Func<Vector3> ActiveWindGetter(Type type)
        {
            MethodInfo method = type.GetMethod("getWSWind", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            return method != null && method.ReturnType == typeof(Vector3)
                ? (Func<Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3>), method)
                : null;
        }

        private static bool Flag(FieldInfo field)
        {
            object value = field != null ? field.GetValue(null) : null;
            return value is bool && (bool)value;
        }

        private static int Int(FieldInfo field)
        {
            object value = field.GetValue(null);
            return value is int ? (int)value : 0;
        }

        private static double Read(FieldInfo field, object owner)
        {
            object value = field.GetValue(owner);
            return value is double ? (double)value : 0.0;
        }

        private struct Entry
        {
            public float Time;
            public bool Valid;
            public Vector3d Wind;
            public double Temperature;
            public double Pressure;
        }

        private sealed class AtmFields
        {
            public readonly FieldInfo WindX;
            public readonly FieldInfo WindY;
            public readonly FieldInfo WindZ;
            public readonly FieldInfo Temperature;
            public readonly FieldInfo Pressure;

            public AtmFields(Type type)
            {
                WindX = type.GetField("wind_x");
                WindY = type.GetField("wind_y");
                WindZ = type.GetField("wind_z");
                Temperature = type.GetField("temperature");
                Pressure = type.GetField("pressure");
            }

            public bool Complete
            {
                get { return WindX != null && WindY != null && WindZ != null && Temperature != null && Pressure != null; }
            }
        }
    }
}
