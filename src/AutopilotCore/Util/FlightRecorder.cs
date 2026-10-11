using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace AutopilotCore
{
    public sealed class FlightRecorder
    {
        private const float SampleInterval = 0.1f;
        private const float FlushInterval = 1f;

        private readonly string _path;
        private readonly string _header;
        private readonly StringBuilder _buffer = new StringBuilder();
        private float _time;
        private float _nextSample;
        private float _nextFlush;
        private bool _open;

        public FlightRecorder(string fileName, string header)
        {
            _path = Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "Logs"), Path.Combine("MyAutopilot", fileName));
            _header = header;
        }

        public void Start()
        {
            _time = 0f;
            _nextSample = 0f;
            _nextFlush = FlushInterval;
            _buffer.Length = 0;
            _buffer.AppendLine(_header);
            _open = true;
            Write(false);
        }

        public bool Tick(float dt)
        {
            _time += dt;
            if (!_open || _time < _nextSample)
            {
                return false;
            }

            _nextSample = _time + SampleInterval;
            return true;
        }

        public void Record(params float[] values)
        {
            _buffer.Append(_time.ToString("F2", CultureInfo.InvariantCulture));
            for (int i = 0; i < values.Length; i++)
            {
                _buffer.Append(',');
                _buffer.Append(values[i].ToString("F3", CultureInfo.InvariantCulture));
            }

            _buffer.AppendLine();
            if (_time >= _nextFlush)
            {
                _nextFlush = _time + FlushInterval;
                Write(true);
            }
        }

        public void Stop()
        {
            if (_open)
            {
                Write(true);
                _open = false;
            }
        }

        private void Write(bool append)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                if (append)
                {
                    File.AppendAllText(_path, _buffer.ToString());
                }
                else
                {
                    File.WriteAllText(_path, _buffer.ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AutopilotCore] Flight log write failed: " + ex.Message);
                _open = false;
            }

            _buffer.Length = 0;
        }
    }
}
