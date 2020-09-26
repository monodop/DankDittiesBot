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

        private VoiceChannelWorker _voiceChannelWorker;

        private ConcurrentQueue<string> _thingsToSay = new ConcurrentQueue<string>();
        private Process _currentOverlayProcess;
        private Stream _currentOverlayStream;

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
                                        _voiceChannelWorker?.TrySkip();
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
                                        _voiceChannelWorker.EnqueueSong(closestMatch.Id);
                                        _say("I have added your song, " + closestMatch.Title + " to the queue");
                                        Console.WriteLine("Added " + closestMatch.Title + " to queue");
                                    }
                                }
                                else if (text.ToLower() == "what song is this")
                                {
                                    _say("I am currently playing " + _voiceChannelWorker?.CurrentSong.Title);
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

            _voiceChannelWorker = new VoiceChannelWorker(voiceChannel, _metadataManager);
            _voiceChannelWorker.OnStopped += (s, e) =>
            {
                _voiceChannelWorker.TryEnsureStarted();
            };
            _voiceChannelWorker.Start();
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

                _currentOverlayProcess = FFmpeg.CreateReadProcess(filename);
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
                _voiceChannelWorker.TryEnsureStarted();
            }
            else if (arg.Content == "!dd skip" && voiceChannel != null)
            {
                _voiceChannelWorker.TrySkip();
            }
            else if (arg.Content == "!dd stop")
            {
                await _voiceChannelWorker.StopAsync();
            }
            else if (arg.Content == "!dd info")
            {
                var currentSong = _voiceChannelWorker?.CurrentSong;
                arg.Channel.SendMessageAsync("Now playing " + currentSong?.Title + " - " + currentSong?.Url);
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
                _voiceChannelWorker.EnqueueSong(id);
                arg.Channel.SendMessageAsync("The song has been added to the queue");
            }
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
