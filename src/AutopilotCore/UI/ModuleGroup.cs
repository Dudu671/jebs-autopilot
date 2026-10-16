using System;
using UnityEngine;

namespace AutopilotCore
{
    // Collapsible section of modes that exclude each other. A selector shows one mode's controls at a time,
    // following the engaged mode whenever that changes.
    public sealed class ModuleGroup
    {
        private readonly string _title;
        private readonly string[] _tabs;
        private readonly IAutopilotModule[] _modules;
        private IAutopilotModule _lastEngaged;
        private int _selected;

        public ModuleGroup(string title, string[] tabs, params IAutopilotModule[] modules)
        {
            _title = title;
            _tabs = tabs;
            _modules = modules;
        }

        public void Draw(ModuleManager manager)
        {
            IAutopilotModule engaged = Engaged();
            if (engaged != null && engaged != _lastEngaged)
            {
                _selected = Array.IndexOf(_modules, engaged);
            }

            _lastEngaged = engaged;
            if (!Foldout.Header(_title, engaged != null ? engaged.Summary : "OFF", engaged != null))
            {
                return;
            }

            // As with the header, a new selection shows on the next event.
            int shown = _selected;
            if (_modules.Length > 1)
            {
                _selected = GUILayout.Toolbar(_selected, _tabs);
            }

            manager.DrawGui(_modules[shown]);
            GUILayout.Space(6f);
        }

        private IAutopilotModule Engaged()
        {
            for (int i = 0; i < _modules.Length; i++)
            {
                if (_modules[i].Enabled)
                {
                    return _modules[i];
                }
            }

            return null;
        }
    }
}
