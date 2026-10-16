using System;
using System.Collections.Generic;

namespace AutopilotCore
{
    // Thrust reversers: animations on engine parts that turn the exhaust forward, recognized by the
    // animation name, as on the stock Wheesley and Goliath and the AirplanePlus turbofans. The end of
    // the animation is the deployed position.
    public sealed class ThrustReversers
    {
        // Progress at which an animation counts as at its end.
        private const float End = 0.99f;

        private readonly List<ModuleAnimateGeneric> _animations = new List<ModuleAnimateGeneric>();
        private Guid _vesselId;
        private int _partCount = -1;
        private bool _hasCommand;
        private bool _deploy;

        public bool Available
        {
            get { return _animations.Count > 0; }
        }

        // Fully deployed, so reverse power pushes backward. Only the progress is used: in flight IsMoving
        // stayed true after the animation had ended.
        public bool Deployed
        {
            get
            {
                for (int i = 0; i < _animations.Count; i++)
                {
                    ModuleAnimateGeneric animation = _animations[i];
                    if (animation != null && animation.Progress < End)
                    {
                        return false;
                    }
                }

                return Available;
            }
        }

        // Average deployment, 0 stowed to 1 deployed.
        public float Progress
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < _animations.Count; i++)
                {
                    total += _animations[i] != null ? _animations[i].Progress : 0f;
                }

                return Available ? total / _animations.Count : 0f;
            }
        }

        // Called at each touchdown; the reversers may have been moved by hand since the last command.
        public void Update(Vessel vessel)
        {
            _hasCommand = false;
            if (vessel.id == _vesselId && vessel.parts.Count == _partCount)
            {
                return;
            }

            _vesselId = vessel.id;
            _partCount = vessel.parts.Count;
            _animations.Clear();
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part.FindModuleImplementing<ModuleEngines>() == null)
                {
                    continue;
                }

                List<ModuleAnimateGeneric> animations = part.FindModulesImplementing<ModuleAnimateGeneric>();
                for (int m = 0; m < animations.Count; m++)
                {
                    string name = animations[m].animationName;
                    if (name != null && name.IndexOf("revers", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _animations.Add(animations[m]);
                    }
                }
            }
        }

        // Toggles each reverser on the other side once per change of direction: a toggle while it is still
        // leaving its end would send it back, and the progress takes a few frames to show the movement.
        public void Command(bool deploy)
        {
            if (_hasCommand && _deploy == deploy)
            {
                return;
            }

            _hasCommand = true;
            _deploy = deploy;
            for (int i = 0; i < _animations.Count; i++)
            {
                ModuleAnimateGeneric animation = _animations[i];
                if (animation != null && (animation.Progress > 0.5f) != deploy)
                {
                    animation.Toggle();
                }
            }
        }
    }
}
