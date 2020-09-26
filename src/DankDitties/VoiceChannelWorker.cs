using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties
{
    public class VoiceChannelWorker : Worker
    {
        private readonly SocketVoiceChannel _voiceChannel;
        private readonly MetadataManager _metadataManager;
        private readonly List<string> _playlist = new List<string>();
        private readonly Random _random = new Random();

        public IEnumerable<string> Playlist => _playlist;
        public PostMetadata CurrentSong { get; private set; }
        private bool _shouldSkip;

        public VoiceChannelWorker(SocketVoiceChannel voiceChannel, MetadataManager metadataManager)
        {
            _voiceChannel = voiceChannel;
            _metadataManager = metadataManager;
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

        public void EnqueueSong(string songId)
        {
            // TODO: ensure song exists
            _playlist.Add(songId);
        }

        private string _getNext()
        {
            foreach (var id in _playlist)
            {
                if (_metadataManager.HasRecord(id))
                {
                    var record = _metadataManager.GetRecord(id);
                    if (record.DownloadCacheFilename != null)
                    {
                        _playlist.Remove(id);
                        CurrentSong = record;
                        return record.DownloadCacheFilename;
                    }

                    Console.WriteLine("An item in the queue was skipped because it has not been downloaded yet");
                }
            }

            var posts = _metadataManager.Posts.ToList().Where(p => p.IsReady).ToList();
            if (posts.Count == 0)
                return null;

            var nextIndex = _random.Next(posts.Count);
            CurrentSong = posts[nextIndex];
            return posts[nextIndex].DownloadCacheFilename;
        }

        protected override async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                //_thingsToSay.Clear();

                Console.WriteLine("Voice Channel Worker Connecting");
                var audioClient = await _voiceChannel.ConnectAsync();

                //audioClient.StreamCreated += (s, e) =>
                //{
                //    var match = _voiceChannel.Users.FirstOrDefault(u => u.AudioStream == e);
                //    if (match != null)
                //    {
                //        if (_canAccessVoiceAssistant(match))
                //        {
                //            _say("Welcome to the discord channel, " + match.Nickname ?? match.Username);
                //            _addVoiceAssistantRunner(match);
                //        }
                //    }
                //    return Task.FromResult(0);
                //};
                //audioClient.Disconnected += (e) =>
                //{
                //    Console.WriteLine(e);
                //    _currentRunnerCts?.Cancel();
                //    return Task.FromResult(0);
                //};

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        _shouldSkip = false;
                        var filename = _getNext();
                        if (filename == null)
                        {
                            await Task.Yield();
                            continue;
                        }
                        using var process = FFmpeg.CreateReadProcess(filename);
                        using var audioInStream = process.StandardOutput.BaseStream;
                        using var audioOutStream = audioClient.CreatePCMStream(AudioApplication.Music);

                        try
                        {
                            while (!process.HasExited && !cancellationToken.IsCancellationRequested && !_shouldSkip)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var blockSize = 2880;
                                var buffer = new byte[blockSize];
                                var byteCount = await audioInStream.ReadAsync(buffer, 0, blockSize, cancellationToken);
                                if (byteCount == 0)
                                    break;

                                //if (_currentOverlayStream != null)
                                //{
                                //    cancellationToken.ThrowIfCancellationRequested();
                                //    var overlayBuffer = new byte[blockSize];
                                //    var overlayByteCount = await _currentOverlayStream.ReadAsync(overlayBuffer, 0, blockSize, cancellationToken);

                                //    if (overlayByteCount == 0)
                                //    {
                                //        _currentOverlayStream?.Dispose();
                                //        _currentOverlayStream = null;
                                //        _currentOverlayProcess?.Dispose();
                                //        _currentOverlayProcess = null;
                                //        _sayNext();
                                //    }
                                //    else
                                //    {
                                //        var len = Math.Min(overlayByteCount, byteCount);
                                //        for (int i = 0; i < len; i += 2)
                                //        {
                                //            short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                                //            short b2 = (short)(buffer[i] & 0xff);

                                //            short o1 = (short)((overlayBuffer[i + 1] & 0xff) << 8);
                                //            short o2 = (short)(overlayBuffer[i] & 0xff);

                                //            short data = (short)(b1 | b2);
                                //            short data2 = (short)(o1 | o2);
                                //            data = (short)((data * (Program.SoundVolume / 100f) * 0.9f) + (data2 * (Program.VoiceAssistantVolume / 100f)));

                                //            buffer[i] = (byte)data;
                                //            buffer[i + 1] = (byte)(data >> 8);
                                //        }
                                //    }
                                //}
                                if (false)
                                {

                                }
                                else
                                {
                                    for (var i = 0; i < byteCount; i += 2)
                                    {
                                        short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                                        short b2 = (short)(buffer[i] & 0xff);

                                        short data = (short)(b1 | b2);
                                        data = (short)(data * (Program.SoundVolume / 100f));

                                        buffer[i] = (byte)data;
                                        buffer[i + 1] = (byte)(data >> 8);
                                    }
                                }

                                cancellationToken.ThrowIfCancellationRequested();
                                await audioOutStream.WriteAsync(buffer, 0, byteCount, cancellationToken);
                            }
                        }
                        finally
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await audioOutStream.FlushAsync(cancellationToken);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                Console.WriteLine("Voice Channel Worker Disconnecting");
                await _voiceChannel.DisconnectAsync();
            }
        }
    }
}
