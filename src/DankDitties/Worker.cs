using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties
{
    public abstract class Worker : IAsyncDisposable
    {
        public Worker()
        {

        }

        public WorkerStatus Status { get; private set; }
        private Task _worker;
        private CancellationTokenSource _cancellationTokenSource;

        public event EventHandler OnStarted;
        public event EventHandler OnStopped;

        public void Start()
        {
            if (Status != WorkerStatus.Stopped)
                throw new InvalidOperationException("Worker must be stopped in order to start it");

            Status = WorkerStatus.Starting;

            _cancellationTokenSource = new CancellationTokenSource();
            _worker = _workerWrapper(_cancellationTokenSource.Token);

            OnStarted?.Invoke(this, new EventArgs());
        }

        public bool TryEnsureStarted()
        {
            switch(Status)
            {
                case WorkerStatus.Running:
                case WorkerStatus.Starting:
                    return true;
                case WorkerStatus.Stopped:
                    Start();
                    return true;
                case WorkerStatus.Stopping:
                    return false;
            }
            return false;
        }

        public async Task RestartAsync()
        {
            await StopAsync();
            Start();
        }

        public async Task<bool> TryStartOrRestart()
        {
            if (!await TryEnsureStoppedAsync())
                return false;
            if (!TryEnsureStarted())
                return false;
            return true;
        }

        private async Task _workerWrapper(CancellationToken cancellationToken)
        {
            try
            {
                Status = WorkerStatus.Running;
                await DoWorkAsync(cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine("Worker crashed: " + e.Message);
                Console.WriteLine(e.StackTrace);
            }
            _stopped();
        }
        protected abstract Task DoWorkAsync(CancellationToken cancellationToken);

        private void _stopped()
        {
            _worker = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            Status = WorkerStatus.Stopped;

            OnStopped?.Invoke(this, new EventArgs());
        }

        public async Task StopAsync()
        {
            if (Status != WorkerStatus.Running)
                throw new InvalidOperationException("Worker must be running in order to stop it");

            Status = WorkerStatus.Stopping;

            _cancellationTokenSource.Cancel();

            await _worker;

            _stopped();
        }

        public async Task<bool> TryEnsureStoppedAsync()
        {
            switch (Status)
            {
                case WorkerStatus.Stopped:
                case WorkerStatus.Stopping:
                    return true;
                case WorkerStatus.Running:
                    await StopAsync();
                    return true;
                case WorkerStatus.Starting:
                    return false;
            }
            return false;
        }

        public virtual ValueTask DisposeAsync()
        {
            _cancellationTokenSource?.Cancel();
            return new ValueTask(Task.FromResult(0));
        }

        public enum WorkerStatus
        {
            Stopped,
            Starting,
            Running,
            Stopping,
        }
    }
}
