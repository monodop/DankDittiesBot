using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class MemoryBufferClip : Clip
    {
        private readonly Clip _clip;
        private readonly byte[] _buffer;
        private long _bytesInBuffer;
        private Stream _bufferStream;

        public MemoryBufferClip(Clip clip, int bufferSize)
        {
            _clip = clip;
            _buffer = new byte[bufferSize];
            _bufferStream = new MemoryStream();
        }

        public void Seek(long offset, SeekOrigin seekOrigin)
            => _bufferStream.Seek(offset, seekOrigin);

        protected async override Task DoPrepareAsync()
        {
            await _clip.PrepareAsync();
        }

        protected override async Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (_bytesInBuffer < count)
            {
                var byteCount = await _clip.ReadAsync(_buffer, 0, _buffer.Length, cancellationToken);
                if (byteCount == 0)
                    break;

                // Write to buffer stream and seek back the number of bytes we wrote
                var current = _bufferStream.Position;
                _bufferStream.Seek(0, SeekOrigin.End);
                await _bufferStream.WriteAsync(_buffer, 0, byteCount, cancellationToken);
                _bufferStream.Seek(current, SeekOrigin.Begin);
                _bytesInBuffer += byteCount;
            }

            var bytesToRead = (int)Math.Min(_bytesInBuffer, count);
            _bytesInBuffer -= bytesToRead;
            return await _bufferStream.ReadAsync(outputBuffer, offset, bytesToRead, cancellationToken);
        }

        public async override ValueTask DisposeAsync()
        {
            await _bufferStream.DisposeAsync();
            await _clip.DisposeAsync();
            await base.DisposeAsync();
        }
    }

    public static class ForceBufferSizeClipExtensions
    {
        public static MemoryBufferClip UseMemoryBuffer(this Clip clip, int bufferSize)
            => new MemoryBufferClip(clip, bufferSize);
    }
}
