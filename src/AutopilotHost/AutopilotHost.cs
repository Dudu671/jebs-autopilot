using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AutopilotHost
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class AutopilotHost : MonoBehaviour
    {
        private const string CoreTypeName = "AutopilotCore.Controller";
        private const float DebounceSeconds = 0.3f;

        private FileSystemWatcher _watcher;
        private GameObject _coreObject;
        private volatile bool _dirty;
        private string _dllPath;

        private void Start()
        {
            _dllPath = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "MyAutopilot",
                "Dev",
                "AutopilotCore.dll");

            string dir = Path.GetDirectoryName(_dllPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Debug.LogError("[AutopilotHost] Pasta Dev não encontrada: " + dir);
                return;
            }

            _watcher = new FileSystemWatcher(dir, "AutopilotCore.dll");
            _watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                                    NotifyFilters.CreationTime | NotifyFilters.FileName;
            _watcher.Changed += OnDllChanged;
            _watcher.Created += OnDllChanged;
            _watcher.Renamed += OnDllRenamed;
            _watcher.EnableRaisingEvents = true;

            _dirty = true;
        }

        private void OnDllChanged(object sender, FileSystemEventArgs e)
        {
            _dirty = true;
        }

        private void OnDllRenamed(object sender, RenamedEventArgs e)
        {
            _dirty = true;
        }

        private void Update()
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            StopAllCoroutines();
            StartCoroutine(ReloadAfterDebounce());
        }

        private IEnumerator ReloadAfterDebounce()
        {
            yield return new WaitForSeconds(DebounceSeconds);
            Reload();
        }

        private void Reload()
        {
            try
            {
                DestroyCoreObject();

                if (!File.Exists(_dllPath))
                {
                    Debug.LogError("[AutopilotHost] AutopilotCore.dll não encontrada: " + _dllPath);
                    return;
                }

                Assembly assembly = Assembly.Load(File.ReadAllBytes(_dllPath));
                Type controllerType = assembly.GetType(CoreTypeName);
                if (controllerType == null)
                {
                    Debug.LogError("[AutopilotHost] Tipo " + CoreTypeName + " não encontrado na DLL.");
                    return;
                }

                _coreObject = new GameObject(CoreTypeName);
                _coreObject.AddComponent(controllerType);
                Debug.Log("[AutopilotHost] Core recarregado.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutopilotHost] Falha no reload: " + ex);
            }
        }

        private void OnDestroy()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Changed -= OnDllChanged;
                _watcher.Created -= OnDllChanged;
                _watcher.Renamed -= OnDllRenamed;
                _watcher.Dispose();
                _watcher = null;
            }

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
