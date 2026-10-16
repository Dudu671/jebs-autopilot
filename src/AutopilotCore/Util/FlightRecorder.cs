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

        private readonly string _fileName;
        private readonly string _header;
        private readonly bool _perRun;
        private readonly StringBuilder _buffer = new StringBuilder();
        private string _path;
        private float _time;
        private float _nextSample;
        private float _nextFlush;
        private bool _open;

        // perRun: each Start writes a new file stamped with the time, instead of replacing the last one.
        public FlightRecorder(string fileName, string header, bool perRun = false)
        {
            _fileName = fileName;
            _header = header;
            _perRun = perRun;
        }

        // One folder per vessel, so vessels flown at the same time do not share a file.
        public void Start(Vessel vessel)
        {
            string root = Path.Combine(Path.Combine(KSPUtil.ApplicationRootPath, "Logs"), "MyAutopilot");
            string fileName = _perRun
                ? Path.GetFileNameWithoutExtension(_fileName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(_fileName)
                : _fileName;
            _path = Path.Combine(Path.Combine(root, FolderName(vessel)), fileName);
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

        private static string FolderName(Vessel vessel)
        {
            string name = vessel.vesselName ?? "vessel";
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder clean = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                clean.Append(Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i]);
            }

            return clean.ToString().Trim() + "-" + vessel.id.ToString("N").Substring(0, 8);
        }
    }
}
