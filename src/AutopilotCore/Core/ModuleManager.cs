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
                _modules[i].DrawGui();
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
