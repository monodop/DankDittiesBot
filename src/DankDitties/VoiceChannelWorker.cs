using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private readonly WitAiClient _witAiClient;
        private readonly List<string> _playlist = new List<string>();
        private readonly Random _random = new Random();

        private Process _currentOverlayProcess;
        private Stream _currentOverlayStream;
        private readonly Queue<string> _thingsToSay = new Queue<string>();

        public IEnumerable<string> Playlist => _playlist;
        public PostMetadata CurrentSong { get; private set; }
        private bool _shouldSkip;

        public VoiceChannelWorker(SocketVoiceChannel voiceChannel, MetadataManager metadataManager, WitAiClient witAiClient)
        {
            _voiceChannel = voiceChannel;
            _metadataManager = metadataManager;
            _witAiClient = witAiClient;
        }

        public bool TrySkip()
        {
            if (_shouldSkip)
            {
                return false;
            }

            Console.WriteLine("Skipping Song");
            Say("Ok, I am skipping this song");
            _shouldSkip = true;
            return true;
        }

        public void EnqueueSong(string songId)
        {
            // TODO: ensure song exists
            _playlist.Add(songId);
        }

        public void Say(string text)
        {
            Console.WriteLine("Enqueuing Say: " + text);
            _thingsToSay.Enqueue(text);

            if (_currentOverlayProcess == null && _thingsToSay.Count == 1)
            {
                Task.Run(() => _sayNext());
            }
        }

        private void _sayNext()
        {
            if (!_thingsToSay.TryDequeue(out var text))
                return;

            Task.Run(async () =>
            {
                Console.WriteLine("Saying: " + text);
                var filename = "tts.mp3";
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    var scriptDir = Path.Join(Program.ScriptDir, "tts.py");
                    await Program.Call(Program.PythonExecutable, $"{scriptDir} {filename} \"{text.Replace("\"", "\\\"")}\"");
                }
                else
                {
                    filename = "tts.wav";
                    await Program.Call("pico2wave", $"-w {filename} -l en-GB \"{text.Replace("\"", "\\\"")}\"");
                }

                _currentOverlayProcess = FFmpeg.CreateReadProcess(filename);
                _currentOverlayStream = _currentOverlayProcess.StandardOutput.BaseStream;
            });
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

        private bool _canAccessVoiceAssistant(SocketGuildUser user)
        {
            if (!Program.EnableVoiceCommands)
                return false;

            if (user.IsBot)
                return false;

            if (Program.VoiceCommandRole != null && !user.Roles.Any(r => r.Id == Program.VoiceCommandRole))
                return false;

            return true;
        }

        private async Task _assistantManager(CancellationToken cancellationToken)
        {
            var _assistants = new Dictionary<ulong, VoiceAssistantWorker>();

            IEnumerable<Task<bool>> stops;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var prevIds = _assistants.Keys.ToList();
                    var users = _voiceChannel.Users.ToDictionary(u => u.Id, u => u);
                    var newIds = users.Values
                        .Where(_canAccessVoiceAssistant)
                        .Select(u => u.Id)
                        .ToList();

                    var toStart = newIds.Except(prevIds);
                    var toStop = prevIds.Except(newIds);

                    foreach (var id in toStart)
                    {
                        var assistant = new VoiceAssistantWorker(users[id], this, _metadataManager, _witAiClient);
                        assistant.Start();
                        _assistants[id] = assistant;
                    }

                    stops = toStop.Select(id => _assistants[id].TryEnsureStoppedAsync());
                    await Task.WhenAll(stops);
                    foreach (var stop in toStop)
                    {
                        _assistants.Remove(stop);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            stops = _assistants.Values.Select(a => a.TryEnsureStoppedAsync());
            await Task.WhenAll(stops);
        }

        protected override async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("Voice Channel Worker Connecting");
                var audioClient = await _voiceChannel.ConnectAsync();

                _thingsToSay.Clear();

                _ = Task.Run(() => _assistantManager(cancellationToken));

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

                                if (_currentOverlayStream != null)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var overlayBuffer = new byte[blockSize];
                                    var overlayByteCount = await _currentOverlayStream.ReadAsync(overlayBuffer, 0, blockSize, cancellationToken);

                                    if (overlayByteCount == 0)
                                    {
                                        _currentOverlayStream?.Dispose();
                                        _currentOverlayStream = null;
                                        _currentOverlayProcess?.Dispose();
                                        _currentOverlayProcess = null;
                                        _sayNext();
                                    }
                                    else
                                    {
                                        var len = Math.Min(overlayByteCount, byteCount);
                                        for (int i = 0; i < len; i += 2)
                                        {
                                            short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                                            short b2 = (short)(buffer[i] & 0xff);

                                            short o1 = (short)((overlayBuffer[i + 1] & 0xff) << 8);
                                            short o2 = (short)(overlayBuffer[i] & 0xff);

                                            short data = (short)(b1 | b2);
                                            short data2 = (short)(o1 | o2);
                                            data = (short)((data * (Program.SoundVolume / 100f) * 0.9f) + (data2 * (Program.VoiceAssistantVolume / 100f)));

                                            buffer[i] = (byte)data;
                                            buffer[i + 1] = (byte)(data >> 8);
                                        }
                                    }
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
