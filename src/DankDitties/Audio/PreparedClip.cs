using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class PreparedClip : Clip
    {
        private readonly Clip _clip;
        private Stream _stream;

        public PreparedClip(Clip clip)
        {
            _clip = clip;
        }

        protected override async Task DoPrepareAsync()
        {
            _stream = new MemoryStream();

            await _clip.PrepareAsync();

            var cts = new CancellationTokenSource();
            while (true)
            {
                var buffer = new byte[2880];
                var byteCount = await _clip.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (byteCount == 0)
                    break;

                await _stream.WriteAsync(buffer, 0, byteCount, cts.Token);
            }
        }

        public void Restart()
        {
            _stream.Seek(0, SeekOrigin.Begin);
        }

        public override Task<int> ReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _stream.ReadAsync(outputBuffer, 0, count, cancellationToken);
        }

        public override async ValueTask DisposeAsync()
        {
            await _clip.DisposeAsync();
            await _stream.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
