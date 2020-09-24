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

        private Process _currentOverlayProcess;
        private Stream _currentOverlayStream;

        private List<string> _queue = new List<string>();
        private bool _shouldSkip = false;
        private PostMetadata _currentSong;

        public DiscordClient(string apiKey, WitAiClient witAiClient, MetadataManager metadataManager)
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig()
            {
                //LogLevel = LogSeverity.Debug,
            });
            _client.Log += OnLog;
            _client.Ready += OnReady;
            _client.MessageReceived += OnMessageReceived;

            _apiKey = apiKey;
            _witAiClient = witAiClient;
            _metadataManager = metadataManager;
        }

        private async Task OnReady()
        {
            var guild = _client.Guilds.FirstOrDefault(g => g.Id == 493935564832374795);
            var voiceChannel = guild.VoiceChannels.FirstOrDefault(c => c.Id == 493935564832374803);
            //var user = voiceChannel.Users.FirstOrDefault(u => u.Id == 158718441287581696);
            //var userStream = user.AudioStream;

            //var audioClient = await voiceChannel.ConnectAsync();
            _startRunner(voiceChannel);
            //audioClient.StreamCreated += async (s, e) =>
            //{
            //    Console.WriteLine(s);
            //};
            //audioClient.SpeakingUpdated += async (s, e) =>
            //{
            //    Console.WriteLine(s);
            //};

            //await _witAiClient.ParseText("play emerald booty zone");
            foreach (var user in voiceChannel.Users)
            {
                var userStream = user.AudioStream;
                Task.Run(async () =>
                {
                    while (true)
                    {
                        while (userStream == null)
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(100));
                            userStream = user.AudioStream;
                        }

                        try
                        {
                            var data = await _witAiClient.ParseAudioStream(userStream);
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

                                    Console.WriteLine("Confidence: " + playSongIntent?.Confidence * 100);
                                    if (playSongIntent?.Confidence > 0.75 && text.StartsWith("play"))
                                    {
                                        foreach (var entity in data.Entities.Values.SelectMany(e => e))
                                        {
                                            if (entity.Role == "search_query")
                                            {
                                                var searchString = entity.Body;

                                                if (searchString == "next")
                                                {
                                                    //_startRunner(voiceChannel);
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
                                                    //_startRunner(voiceChannel);
                                                    Console.WriteLine("Added to queue");
                                                }
                                                //Console.WriteLine("Closest match: " + topMatch.post.Title);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                });
            }
        }

        private async Task _say(string text)
        {
            await Program.Call("python", $"tts.py tts.mp3 \"{text.Replace("\"", "\\\"")}\"");

            _currentOverlayProcess?.Dispose();
            _currentOverlayStream?.Dispose();

            _currentOverlayProcess = _createStream("tts.mp3");
            _currentOverlayStream = _currentOverlayProcess.StandardOutput.BaseStream;
        }

        private async Task OnMessageReceived(SocketMessage arg)
        {
            //Console.WriteLine("Message received: " + arg.Content);

            var author = arg.Author as IGuildUser;
            var voiceChannel = author?.VoiceChannel;

            if (arg.Content == "!dd start" && voiceChannel != null)
            {
                Console.WriteLine("Joining voice channel: " + voiceChannel.Name);
                _startRunner(voiceChannel);
            }
            else if (arg.Content == "!dd skip" && voiceChannel != null)
            {
                //_startRunner(voiceChannel);
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

        private void _startRunner(IVoiceChannel channel)
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

        private async Task _runner(IVoiceChannel voiceChannel, CancellationToken cancellationToken)
        {
            if (_currentVoiceChannel?.Id != voiceChannel.Id)
            {
                _currentVoiceChannel = voiceChannel;
                _currentAudioClient = await voiceChannel.ConnectAsync();
            }
            var audioClient = _currentAudioClient;

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
                            var blockSize = 2880;
                            var buffer = new byte[blockSize];
                            var byteCount = await audioInStream.ReadAsync(buffer, 0, blockSize);
                            if (byteCount == 0)
                                break;

                            if (_currentOverlayStream != null)
                            {
                                var overlayBuffer = new byte[blockSize];
                                var overlayByteCount = await _currentOverlayStream.ReadAsync(overlayBuffer, 0, blockSize);

                                if (overlayByteCount == 0)
                                {
                                    _currentOverlayStream?.Dispose();
                                    _currentOverlayStream = null;
                                }
                                else
                                {
                                    var len = Math.Min(overlayByteCount, byteCount);
                                    for (int i = 0; i < len; i+=2)
                                    {
                                        short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                                        short b2 = (short)(buffer[i] & 0xff);

                                        short o1 = (short)((overlayBuffer[i + 1] & 0xff) << 8);
                                        short o2 = (short)(overlayBuffer[i] & 0xff);

                                        short data = (short)(b1 | b2);
                                        short data2 = (short)(o1 | o2);
                                        data = (short)((data / 4) + data2 * 1.25);

                                        buffer[i] = (byte)data;
                                        buffer[i + 1] = (byte)(data >> 8);
                                    }
                                }
                            }

                            for (var i = 0; i < byteCount; i += 2)
                            {
                                short b1 = (short)((buffer[i + 1] & 0xff) << 8);
                                short b2 = (short)(buffer[i] & 0xff);

                                short data = (short)(b1 | b2);
                                data = (short)(data * 0.5f); // 50% volume

                                buffer[i] = (byte)data;
                                buffer[i + 1] = (byte)(data >> 8);
                            }

                            await audioOutStream.WriteAsync(buffer, 0, byteCount);
                        }
                    }
                    finally
                    {
                        await audioOutStream.FlushAsync();
                        _shouldSkip = false;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
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
