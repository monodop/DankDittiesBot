using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class MixedTrack : Clip
    {
        private List<Clip> _clips;

        public MixedTrack(params Clip[] clips)
        {
            _clips = clips.ToList();
        }

        protected override Task DoPrepareAsync()
        {
            return Task.FromResult(0);
        }

        protected override async Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            var tasks = _clips.Select(async clip =>
            {
                var buffer = new byte[count];
                var byteCount = await clip.ReadAsync(buffer, 0, count, cancellationToken);
                return (buffer, byteCount);
            });
            var results = await Task.WhenAll(tasks);

            var maxCount = results.Max(r => r.byteCount);
            for (int i = 0; i < maxCount; i += 2)
            {
                float sample = 0;
                short data;
                foreach (var (buffer, byteCount) in results)
                {
                    if (i > byteCount)
                        continue;

                    short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                    short b2 = (short)(buffer[i] & 0xff);
                    data = (short)(b1 | b2);
                    sample += data / (float)short.MaxValue;
                }

                data = (short)(sample * short.MaxValue);
                outputBuffer[i] = (byte)data;
                outputBuffer[i + 1] = (byte)(data >> 8);
            }

            return maxCount;
        }
    }
}
