using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AutopilotHost
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class AutopilotHost : MonoBehaviour
    {
        private const string CoreTypeName = "AutopilotCore.Controller";
        private const string CoreFileName = "AutopilotCore.bin";
        private const float DebounceSeconds = 0.3f;

        private GameObject _coreObject;
        private string _dllPath;
        private string _loadedFullName;
        private DateTime _loadedWriteTime = DateTime.MinValue;
        private float _reloadAt = -1f;

        private void Start()
        {
            _dllPath = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "MyAutopilot",
                "Dev",
                CoreFileName);

            Debug.Log("[AutopilotHost] Watching " + _dllPath + " (F8 to force reload).");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _reloadAt = Time.unscaledTime;
            }

            if (File.Exists(_dllPath))
            {
                DateTime writeTime = File.GetLastWriteTimeUtc(_dllPath);
                if (writeTime != _loadedWriteTime && _reloadAt < 0f)
                {
                    _reloadAt = Time.unscaledTime + DebounceSeconds;
                }
            }

            if (_reloadAt >= 0f && Time.unscaledTime >= _reloadAt)
            {
                _reloadAt = -1f;
                Reload();
            }
        }

        private void Reload()
        {
            try
            {
                if (!File.Exists(_dllPath))
                {
                    DestroyCoreObject();
                    Debug.LogError("[AutopilotHost] " + CoreFileName + " not found: " + _dllPath);
                    return;
                }

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(_dllPath);
                }
                catch (IOException)
                {
                    _reloadAt = Time.unscaledTime + 0.15f;
                    return;
                }

                string expectedName = AssemblyName.GetAssemblyName(_dllPath).FullName;
                Assembly assembly = Assembly.Load(bytes);
                if (assembly.FullName != expectedName)
                {
                    _loadedWriteTime = File.GetLastWriteTimeUtc(_dllPath);
                    Debug.LogError("[AutopilotHost] Runtime returned cached " + assembly.FullName +
                                   " instead of " + expectedName + ". The Core assembly name must change every build.");
                    return;
                }

                Type controllerType = assembly.GetType(CoreTypeName);
                if (controllerType == null)
                {
                    Debug.LogError("[AutopilotHost] Type " + CoreTypeName + " not found in " + CoreFileName + ".");
                    return;
                }

                string fullName = assembly.FullName;
                _loadedWriteTime = File.GetLastWriteTimeUtc(_dllPath);
                if (_coreObject != null && _loadedFullName == fullName)
                {
                    Debug.Log("[AutopilotHost] Core already current: " + fullName);
                    return;
                }

                DestroyCoreObject();
                _coreObject = new GameObject(CoreTypeName);
                _coreObject.AddComponent(controllerType);
                _loadedFullName = fullName;
                Debug.Log("[AutopilotHost] Core reloaded: " + fullName);
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutopilotHost] Reload failed: " + ex);
            }
        }

        private void OnDestroy()
        {
            DestroyCoreObject();
        }

        private void DestroyCoreObject()
        {
            if (_coreObject != null)
            {
                Destroy(_coreObject);
                _coreObject = null;
            }
        }
    }
}
