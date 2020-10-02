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

        private string _escapeQuotes(string text) => 
            text.Replace("\"", "\\\"");

        protected override async Task DoPrepareAsync()
        {
            Console.WriteLine("Saying next: " + _text);
            _filename = Path.Join("audio", "tmp", _guid + ".wav");
            var absPath = Path.Combine(Directory.GetCurrentDirectory(), _filename);

            var exe = Program.DecTalkExecutable;
            var wd = Program.DecTalkWorkingDirectory;
            var args = Program.DecTalkArgTemplate
                .Replace("{{FILENAME}}", "\"" + _escapeQuotes(absPath) + "\"")
                .Replace("{{TEXT}}", "\"" + _escapeQuotes(_text) + "\"");
            try
            {
                await Program.Call(exe, args, wd);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
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
