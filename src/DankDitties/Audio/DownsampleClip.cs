using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class DownsampleClip : Clip
    {
        private readonly Clip _clip;
        private readonly int _inputSampleRate;
        private readonly int _outputSampleRate;

        public DownsampleClip(Clip clip, int inputSampleRate, int outputSampleRate)
        {
            _clip = clip;
            _inputSampleRate = inputSampleRate;
            _outputSampleRate = outputSampleRate;
        }

        protected override Task DoPrepareAsync()
            => _clip.PrepareAsync();

        protected override async Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            var ratio = _inputSampleRate / (double)_outputSampleRate;

            var buffer = new byte[(int)Math.Ceiling(count * ratio)];
            var byteCount = await _clip.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            var outputByteCount = (int)(byteCount / ratio);

            for (int sample = 0; sample < outputByteCount / 2; sample++)
            {
                var i = sample * 2;
                var j = (int)(sample * ratio) * 2;
                outputBuffer[i] = buffer[j];
                outputBuffer[i + 1] = buffer[j + 1];
            }

            return outputByteCount;
        }

        public override async ValueTask DisposeAsync()
        {
            await _clip.DisposeAsync();
            await base.DisposeAsync();
        }
    }

    public static class DownsampleclipExtensions
    {
        public static DownsampleClip Downsample(this Clip clip, int inputSampleRate, int outputSampleRate)
            => new DownsampleClip(clip, inputSampleRate, outputSampleRate);
    }
}
