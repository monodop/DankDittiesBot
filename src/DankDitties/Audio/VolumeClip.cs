using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class VolumeClip : Clip
    {
        private readonly Clip _clip;
        private readonly float _volume;

        public VolumeClip(Clip clip, float volume)
        {
            _clip = clip;
            _volume = volume;
        }

        protected override Task DoPrepareAsync() => _clip.PrepareAsync();
        public override async Task<int> ReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            var byteCount = await _clip.ReadAsync(outputBuffer, offset, count, cancellationToken);
            for (int i = offset; i < offset + byteCount; i += 2)
            {
                short b1 = (short)((outputBuffer[i + 1] & 0xff) << 8);
                short b2 = (short)(outputBuffer[i] & 0xff);

                short data = (short)(b1 | b2);
                float sample = data / (float)short.MaxValue;
                data = (short)(sample * short.MaxValue * _volume);

                outputBuffer[i] = (byte)data;
                outputBuffer[i + 1] = (byte)(data >> 8);
            }

            return byteCount;
        }
        public override ValueTask DisposeAsync() => _clip.DisposeAsync();
    }

    public static class VolumeClipExtensions
    {
        public static Clip SetVolume(this Clip clip, float volume)
        {
            return new VolumeClip(clip, volume);
        }
    }
}
