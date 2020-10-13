using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class FFmpegClip : Clip
    {
        private readonly string _filename;
        protected Process? _ffmpegProcess;
        protected Stream? _ffmpegStream;

        public FFmpegClip(string filename)
        {
            _filename = filename;
        }

        protected override Task DoPrepareAsync()
        {
            _ffmpegProcess = FFmpeg.CreateReadProcess(_filename);
            _ffmpegStream = _ffmpegProcess.StandardOutput.BaseStream;
            return Task.FromResult(0);
        }

        protected override async Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_ffmpegStream == null)
                throw new InvalidOperationException("FFMpeg stream is null");
            return await _ffmpegStream.ReadAsync(outputBuffer, offset, count, cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            _ffmpegStream?.Dispose();
            _ffmpegProcess?.Dispose();

            _ffmpegProcess = null;
            _ffmpegStream = null;

            await base.DisposeAsync();
        }
    }
}
