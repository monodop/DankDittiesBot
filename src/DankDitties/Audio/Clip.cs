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
        //public int BufferSize => 2880;

        public async Task PrepareAsync()
        {
            if (IsPreparing || IsReady)
                throw new InvalidOperationException("Cannot prepare clip while preparing or while it is ready");

            IsPreparing = true;

            await DoPrepareAsync();

            IsReady = true;
            IsPreparing = false;
        }

        protected abstract Task DoPrepareAsync();

        public abstract Task<int> ReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken);

        public virtual ValueTask DisposeAsync()
        {
            return new ValueTask(Task.FromResult(0));
        }

    }
}
