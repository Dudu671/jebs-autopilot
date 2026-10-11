using UnityEngine;

namespace AutopilotCore
{
    public sealed class Pid
    {
        public float Kp;
        public float Ki;
        public float Kd;
        public float IntegralLimit;

        private float _integral;
        private float _lastError;
        private bool _hasLastError;

        public Pid(float kp, float ki, float kd, float integralLimit)
        {
            Kp = kp;
            Ki = ki;
            Kd = kd;
            IntegralLimit = integralLimit;
        }

        public float Step(float error, float dt)
        {
            if (dt < 1e-4f)
            {
                dt = 1e-4f;
            }

            _integral += error * dt;
            _integral = Mathf.Clamp(_integral, -IntegralLimit, IntegralLimit);

            float derivative = 0f;
            if (_hasLastError)
            {
                derivative = (error - _lastError) / dt;
            }

            _lastError = error;
            _hasLastError = true;

            return (Kp * error) + (Ki * _integral) + (Kd * derivative);
        }

        public void Reset()
        {
            _integral = 0f;
            _lastError = 0f;
            _hasLastError = false;
        }
    }
}
