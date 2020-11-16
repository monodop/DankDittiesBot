using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class Track : Clip
    {
        public ICollection<Clip> Playlist => _playlist;
        private List<Clip> _playlist = new List<Clip>();
        private Clip? _currentClip;
        private bool _shouldSkip;

        private bool _paused;

        public event EventHandler<Clip>? OnClipCompleted;

        public Track()
        {

        }

        public void Enqueue(Clip clip)
        {
            _playlist.Add(clip);
        }

        public bool TrySkip()
        {
            if (_shouldSkip)
            {
                return false;
            }

            Console.WriteLine("Skipping Song");
            _shouldSkip = true;
            return true;
        }

        public bool TryPause()
        {
            if (_paused)
            {
                return false;
            }
            Console.WriteLine("Pausing Song");
            _paused = true;
            return true;
        }

        public bool TryResume()
        {
            if (!_paused)
            {
                return false;
            }
            Console.WriteLine("Resume Song");
            _paused = false;
            return true;
        }

        protected override Task DoPrepareAsync()
        {
            return Task.FromResult(0);
        }

        protected override async Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            void _complete()
            {
                var completedClip = _currentClip;
                if (completedClip != null)
                {
                    _ = completedClip.DisposeAsync();
                    _playlist.Remove(completedClip);
                    _currentClip = null;
                    OnClipCompleted?.Invoke(this, completedClip);
                }
            }

            // Check if the current song should be skipped
            if (_shouldSkip)
            {
                _shouldSkip = false;
                _complete();
            }

            // Check if any items need to start preparing
            if (_playlist.Count(p => (p.IsReady || p.IsPreparing) && p != _currentClip) == 0)
            {
                var toPrepare = _playlist.FirstOrDefault(p => !p.IsReady && !p.IsPreparing);
                if (toPrepare != null)
                {
                    _ = toPrepare.PrepareAsync();
                }
            }

            // Set the current clip to the next clip if necessary
            if (_currentClip == null)
            {
                _currentClip = _playlist.FirstOrDefault(p => p.IsReady);
            }

            if (_currentClip == null)
                return 0;

            if (_paused)
            {
                return 0;
            }

            var byteCount = await _currentClip.ReadAsync(outputBuffer, offset, count, cancellationToken);
            if (byteCount == 0)
            {
                _complete();
            }

            return byteCount;
        }

        public async override ValueTask DisposeAsync()
        {
            foreach (var clip in _playlist)
            {
                await clip.DisposeAsync();
            }
            await base.DisposeAsync();
        }
    }
}
