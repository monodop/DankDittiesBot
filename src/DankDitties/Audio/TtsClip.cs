using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class TtsClip : FFmpegClip
    {
        private readonly string _guid = Guid.NewGuid().ToString();
        private readonly string _text;
        private string _filename;

        public TtsClip(string text) : base(null)
        {
            _text = text;
        }

        protected override async Task DoPrepareAsync()
        {
            Console.WriteLine("Saying next: " + _text);
            _filename = Path.Join("audio", "tmp", _guid + ".mp3");
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var scriptDir = Path.Join(Program.ScriptDir, "tts.py");
                await Program.Call(Program.PythonExecutable, $"{scriptDir} {_filename} \"{_text.Replace("\"", "\\\"")}\"");
            }
            else
            {
                _filename = Path.Join(_guid + ".wav");
                await Program.Call("pico2wave", $"-w {_filename} -l en-GB \"{_text.Replace("\"", "\\\"")}\"");
            }

            _ffmpegProcess = FFmpeg.CreateReadProcess(_filename);
            _ffmpegStream = _ffmpegProcess.StandardOutput.BaseStream;
        }

        public override ValueTask DisposeAsync()
        {
            if (File.Exists(_filename))
            {
                File.Delete(_filename);
            }
            return base.DisposeAsync();
        }
    }
}
