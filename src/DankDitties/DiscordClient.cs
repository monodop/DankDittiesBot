using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties
{
    public class DiscordClient
    {
        private readonly DiscordSocketClient _client;
        private string _apiKey;
        private readonly WitAiClient _witAiClient;
        private readonly MetadataManager _metadataManager;
        private Random _random = new Random();

        private CancellationTokenSource _currentRunnerCts;
        private IVoiceChannel _currentVoiceChannel;
        private IAudioClient _currentAudioClient;

        private ConcurrentQueue<string> _thingsToSay = new ConcurrentQueue<string>();
        private Process _currentOverlayProcess;
        private Stream _currentOverlayStream;

        private List<string> _queue = new List<string>();
        private bool _shouldSkip = false;
        private PostMetadata _currentSong;

        private ConcurrentDictionary<ulong, (Task, SocketGuildUser, CancellationTokenSource)> _voiceAssistantRunners = new ConcurrentDictionary<ulong, (Task, SocketGuildUser, CancellationTokenSource)>();

        public DiscordClient(string apiKey, WitAiClient witAiClient, MetadataManager metadataManager)
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig()
            {

            });
            _client.Log += OnLog;
            _client.Ready += OnReady;
            _client.MessageReceived += OnMessageReceived;

            _apiKey = apiKey;
            _witAiClient = witAiClient;
            _metadataManager = metadataManager;
        }

        private async Task _killVoiceAssistantRunnerAsync(ulong id)
        {
            if (_voiceAssistantRunners.TryRemove(id, out var tuple))
            {
                var (runner, _, cts) = tuple;
                //Console.WriteLine("Cancelling user token " + cts.GetHashCode());
                cts.Cancel();
                await runner;
            }
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

        private void _addVoiceAssistantRunner(SocketGuildUser user)
        {
            if (!_canAccessVoiceAssistant(user))
                return;

            var cts = new CancellationTokenSource();
            //Console.WriteLine("Creating new cancellation token for " + user.Username + " " + cts.GetHashCode());
            var runner = Task.Run(() => _voiceAssistantRunner(user, cts.Token));
            if (!_voiceAssistantRunners.TryAdd(user.Id, (runner, user, cts)))
            {
                cts.Cancel();
            }
        }

        private async Task _refreshVoiceAssistantRunners(SocketVoiceChannel voiceChannel)
        {
            foreach (var user in voiceChannel.Users)
            {
                if (!_canAccessVoiceAssistant(user))
                    continue;

                if (!_voiceAssistantRunners.ContainsKey(user.Id))
                {
                    //Console.WriteLine("creating user assistant runner " + user.Username);
                    _addVoiceAssistantRunner(user);
                }
            }

            foreach (var (id, (_, user, _)) in _voiceAssistantRunners)
            {
                if (!voiceChannel.Users.Contains(user))
                {
                    //Console.WriteLine("killing user assistant runner " + user.Username);
                    await _killVoiceAssistantRunnerAsync(id);
                }
            }
        }

        private async Task _voiceAssistantRunner(SocketGuildUser user, CancellationToken cancellationToken)
        {
            //Console.WriteLine("Starting runner for " + user.Username);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var userStream = user.AudioStream;
                    if (userStream == null)
                        return;

                    try
                    {
                        var data = await _witAiClient.ParseAudioStream(userStream, cancellationToken);
                        if (data != null)
                        {
                            var text = data.Text?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                Console.WriteLine(user.Username + ": " + text);
                                var playSongIntent = data.Intents.FirstOrDefault(i => i.Name == "play_song");

                                if (text.ToLower().StartsWith("i'm "))
                                {
                                    _say("Hello " + text.Substring("i'm ".Length) + ", I'm Dank Ditties bot.");
                                }
                                else if (text.ToLower().StartsWith("play "))
                                {
                                    var searchString = text.Substring("play ".Length);

                                    if (searchString == "next")
                                    {
                                        _shouldSkip = true;
                                        _say("Ok, I am skipping this song");
                                        continue;
                                    }

                                    var matches = from post in _metadataManager.Posts
                                                  where post.IsReady
                                                  let relevance = FuzzySharp.Fuzz.Ratio(post.Title, searchString)
                                                  select (post, relevance);
                                    var topMatch = matches.OrderByDescending(m => m.relevance);
                                    Console.WriteLine("matches: \n" + string.Join("\n", topMatch.Take(3).Select(m => $"{m.post.Title}: {m.relevance}")));
                                    Console.WriteLine();
                                    var closestMatch = topMatch.FirstOrDefault().post;
                                    if (closestMatch != null)
                                    {
                                        _queue.Add(closestMatch.Id);
                                        _say("I have added your song, " + closestMatch.Title + " to the queue");
                                        Console.WriteLine("Added " + closestMatch.Title + " to queue");
                                    }
                                }
                                else if (text.ToLower() == "what song is this")
                                {
                                    _say("I am currently playing " + _currentSong.Title);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                _say("Goodbye, " + user.Nickname ?? user.Username);
            }
            finally
            {
                //Console.WriteLine("Killing runner for " + user.Username);
                _killVoiceAssistantRunnerAsync(user.Id);
            }
        }

        private async Task OnReady()
        {
            var guild = _client.Guilds.FirstOrDefault(g => g.Id == Program.ServerId);
            var voiceChannel = guild.VoiceChannels.FirstOrDefault(c => c.Id == Program.ChannelId);

            _startRunner(voiceChannel);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        await _refreshVoiceAssistantRunners(voiceChannel);
                        if (_currentVoiceChannel == null)
                            _startRunner(voiceChannel);
                    }
                    catch
                    {

                    }
                }
            });
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            foreach (var user in voiceChannel.Users)
            {
                var userStream = user.AudioStream;
            }
        }

        private void _say(string text)
        {
            Console.WriteLine("Enqueing Say: " + text);
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

                _currentOverlayProcess = _createStream(filename);
                _currentOverlayStream = _currentOverlayProcess.StandardOutput.BaseStream;
            });
        }

        private async Task OnMessageReceived(SocketMessage arg)
        {
            var author = arg.Author as IGuildUser;
            var voiceChannel = author?.VoiceChannel as SocketVoiceChannel;

            if (voiceChannel?.Id != Program.ChannelId)
                return;

            if (arg.Content == "!dd start" && voiceChannel != null)
            {
                Console.WriteLine("Joining voice channel: " + voiceChannel.Name);
                _startRunner(voiceChannel);
            }
            else if (arg.Content == "!dd skip" && voiceChannel != null)
            {
                _shouldSkip = true;
            }
            else if (arg.Content == "!dd stop")
            {
                _currentRunnerCts.Cancel();
            }
            else if (arg.Content == "!dd info")
            {
                arg.Channel.SendMessageAsync("Now playing " + _currentSong?.Title + " - " + _currentSong?.Url);
            }
            else if (arg.Content.StartsWith("!dd play "))
            {
                var url = arg.Content.Substring("!dd play ".Length);

                var post = _metadataManager.Posts.FirstOrDefault(p => p.Url == url);
                var id = post?.Id;
                if (post == null)
                {
                    id = Guid.NewGuid().ToString();
                    post = new PostMetadata()
                    {
                        Id = id,
                        Domain = null,
                        Title = "Ad Hoc Queue'd Video",
                        IsApproved = false,
                        IsReviewed = true,
                        IsUserRequested = true,
                        Url = url,
                    };
                    _metadataManager.AddRecord(id, post);
                }
                _queue.Add(id);
                arg.Channel.SendMessageAsync("The song has been added to the queue");
            }
        }

        private void _startRunner(SocketVoiceChannel channel)
        {
            _currentRunnerCts?.Cancel();
            var cts = new CancellationTokenSource();
            _currentRunnerCts = cts;
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _runner(channel, cts.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private string _getNext()
        {
            foreach (var id in _queue)
            {
                if (_metadataManager.HasRecord(id))
                {
                    var record = _metadataManager.GetRecord(id);
                    if (record.DownloadCacheFilename != null)
                    {
                        _queue.Remove(id);
                        _currentSong = record;
                        return record.DownloadCacheFilename;
                    }

                    Console.WriteLine("An item in the queue was skipped because it has not been downloaded yet");
                }
            }

            var posts = _metadataManager.Posts.ToList().Where(p => p.IsReady).ToList();
            if (posts.Count == 0)
                return null;
            var nextIndex = _random.Next(posts.Count);
            _currentSong = posts[nextIndex];
            return posts[nextIndex].DownloadCacheFilename;
        }

        private async Task _runner(SocketVoiceChannel voiceChannel, CancellationToken cancellationToken)
        {
            try
            {
                Console.WriteLine("Job Runner Starting");

                if (_currentVoiceChannel?.Id != voiceChannel.Id)
                {
                    _currentVoiceChannel = voiceChannel;
                    _currentAudioClient = await voiceChannel.ConnectAsync();
                }
                var audioClient = _currentAudioClient;

                audioClient.StreamCreated += (s, e) =>
                {
                    var match = voiceChannel.Users.FirstOrDefault(u => u.AudioStream == e);
                    if (match != null)
                    {
                        if (_canAccessVoiceAssistant(match))
                        {
                            _say("Welcome to the discord channel, " + match.Nickname ?? match.Username);
                            _addVoiceAssistantRunner(match);
                        }
                    }
                    return Task.FromResult(0);
                };
                audioClient.Disconnected += (e) =>
                {
                    Console.WriteLine(e);
                    _currentRunnerCts?.Cancel();
                    return Task.FromResult(0);
                };

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var filename = _getNext();
                        if (filename == null)
                        {
                            await Task.Yield();
                            continue;
                        }
                        using var process = _createStream(filename);
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
                            _shouldSkip = false;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }

                Console.WriteLine("Job Runner Ending");
            }
            finally
            {
                _currentAudioClient = null;
                _currentRunnerCts = null;
                _currentSong = null;
                _currentVoiceChannel = null;
            }
        }

        private Process _createStream(string filename)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-hide_banner -loglevel panic -i \"{filename}\" -ac 2 -f s16le -ar 48000 pipe:1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
        }

        private Process _createOutStream()
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-hide_banner -ac 2 -f s16le -ar 48000 -i pipe:0 -acodec pcm_u8 -ar 22050 -f wav test_out.wav",
                UseShellExecute = false,
                RedirectStandardInput = true,
            });
        }

        public async Task StartAsync()
        {
            await _client.LoginAsync(TokenType.Bot, _apiKey);
            await _client.StartAsync();
        }

        private Task OnLog(LogMessage arg)
        {
            Console.WriteLine(arg.Message);
            return Task.FromResult(0);
        }
    }
}
