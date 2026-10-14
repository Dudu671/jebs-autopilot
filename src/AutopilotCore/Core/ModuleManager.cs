using System;
using System.Collections.Generic;
using System.Reflection;

namespace AutopilotCore
{
    public sealed class ModuleManager
    {
        private readonly List<IAutopilotModule> _modules = new List<IAutopilotModule>();

        public IList<IAutopilotModule> Modules
        {
            get { return _modules; }
        }

        public ModuleManager()
        {
            Discover(Assembly.GetExecutingAssembly());
        }

        public T Find<T>() where T : class, IAutopilotModule
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                T module = _modules[i] as T;
                if (module != null)
                {
                    return module;
                }
            }

            return null;
        }

        public bool AnyEnabled<T>() where T : class, IAutopilotModule
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i].Enabled && _modules[i] is T)
                {
                    return true;
                }
            }

            return false;
        }

        public void Register(IAutopilotModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException("module");
            }

            _modules.Add(module);
        }

        public void UpdateAll(Vessel vessel, FlightCtrlState ctrl)
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                _modules[i].Update(vessel, ctrl);
            }
        }

        public void DrawAllGui()
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (i > 0)
                {
                    Divider.Draw();
                }

                IAutopilotModule module = _modules[i];
                bool wasEnabled = module.Enabled;
                module.DrawGui();
                if (!wasEnabled && module.Enabled && module is ILateralMode)
                {
                    DisableOtherLateralModes(module);
                }
            }
        }

        private void DisableOtherLateralModes(IAutopilotModule keep)
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                if (_modules[i] != keep && _modules[i] is ILateralMode)
                {
                    _modules[i].Enabled = false;
                }
            }
        }

        public void DisableAll()
        {
            for (int i = 0; i < _modules.Count; i++)
            {
                _modules[i].Enabled = false;
            }
        }

        private void Discover(Assembly assembly)
        {
            Type[] types = assembly.GetTypes();
            Type moduleType = typeof(IAutopilotModule);

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type.IsAbstract || type.IsInterface || !moduleType.IsAssignableFrom(type))
                {
                    continue;
                }

                ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    continue;
                }

                Register((IAutopilotModule)Activator.CreateInstance(type));
            }
        }
    }
}
