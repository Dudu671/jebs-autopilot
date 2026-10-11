using UnityEngine;

namespace AutopilotCore
{
    public sealed class SpeedHold : IAutopilotModule
    {
        private readonly Pid _pid = new Pid(0.008f, 0.0004f, 0.002f, 4f);
        private readonly FloatField _target = new FloatField(120f);

        public string Name
        {
            get { return "Speed Hold"; }
        }

        public bool Enabled { get; set; }

        public float TargetKt
        {
            get { return _target.Value; }
        }

        public void Update(Vessel vessel, FlightCtrlState ctrl)
        {
            if (!Enabled || vessel == null)
            {
                _pid.Reset();
                return;
            }

            float error = _target.Value - vessel.GetIasKt();
            float output = _pid.Step(error, TimeWarp.fixedDeltaTime);
            ctrl.mainThrottle = Mathf.Clamp01(output);
        }

        public void DrawGui()
        {
            Enabled = GUILayout.Toggle(Enabled, Name);
            _target.Draw("Target (kt)");
        }
    }
}
