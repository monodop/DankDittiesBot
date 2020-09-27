using Discord.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties.Audio
{
    public class StreamClip : Clip
    {
        private readonly Stream _audioStream;

        public StreamClip(Stream audioStream)
        {
            _audioStream = audioStream;
        }

        protected override Task DoPrepareAsync()
        {
            return Task.FromResult(0);
        }

        protected override Task<int> DoReadAsync(byte[] outputBuffer, int offset, int count, CancellationToken cancellationToken)
            => _audioStream.ReadAsync(outputBuffer, offset, count, cancellationToken);
    }
}
