using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly MetadataManager _metadataManager;
        private Random _random = new Random();

        private CancellationTokenSource _currentRunnerCts;
        private IVoiceChannel _currentVoiceChannel;
        private IAudioClient _currentAudioClient;

        private List<string> _queue = new List<string>();

        public DiscordClient(string apiKey, MetadataManager metadataManager)
        {
            _client = new DiscordSocketClient();
            _client.Log += OnLog;
            _client.MessageReceived += OnMessageReceived;

            _apiKey = apiKey;
            _metadataManager = metadataManager;
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
                _startRunner(voiceChannel);
            }
            else if (arg.Content == "!dd stop")
            {
                _currentRunnerCts.Cancel();
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
                        return record.DownloadCacheFilename;
                    }

                    Console.WriteLine("An item in the queue was skipped because it has not been downloaded yet");
                }
            }

            var posts = _metadataManager.Posts.ToList().Where(p => p.IsReady).ToList();
            if (posts.Count == 0)
                return null;
            var nextIndex = _random.Next(posts.Count);
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
                    using var stream = process.StandardOutput.BaseStream;
                    using var pcmStream = audioClient.CreatePCMStream(AudioApplication.Music);

                    try
                    {
                        await stream.CopyToAsync(pcmStream, cancellationToken);
                    }
                    finally
                    {
                        await pcmStream.FlushAsync(cancellationToken);
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
