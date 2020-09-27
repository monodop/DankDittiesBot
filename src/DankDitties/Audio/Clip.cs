using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public abstract class Clip : IAsyncDisposable
    {
        public bool IsReady { get; private set; }
        public bool IsPreparing { get; private set; }
        public bool IsDisposed { get; private set; }

        public async Task PrepareAsync()
        {
            if (IsDisposed)
                throw new InvalidOperationException("You cannot prepare a disposed clip");

            if (IsPreparing || IsReady)
                return;

            IsPreparing = true;

            await DoPrepareAsync();

            IsReady = true;
            IsPreparing = false;
        }

        protected abstract Task DoPrepareAsync();

        public Task<int> ReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (IsDisposed)
                throw new InvalidOperationException("You cannot read from a disposed clip");
            if (!IsReady)
                throw new InvalidOperationException("You must prepare a clip before trying to read from it");
            return DoReadAsync(outputBuffer, offset, count, cancellationToken);
        }
        protected abstract Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken);

        public virtual ValueTask DisposeAsync()
        {
            IsReady = false;
            IsPreparing = false;
            IsDisposed = true;
            return new ValueTask(Task.FromResult(0));
        }
    }

    public static class ClipExtensions
    {
        public static async Task<int> ReadAsync(this Clip clip, short[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            var buffer = new byte[count * 2];
            var bytesRead = await clip.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

            for (int sample = 0; sample < bytesRead / 2; sample++)
            {
                int i = sample * 2;

                short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                short b2 = (short)(buffer[i] & 0xff);

                outputBuffer[sample + offset] = (short)(b1 | b2);
            }

            return bytesRead / 2;
        }
    }
}
