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

        private async Task OnReady()
        {
            var guild = _client.Guilds.FirstOrDefault(g => g.Id == Program.ServerId);
            var voiceChannel = guild.VoiceChannels.FirstOrDefault(c => c.Id == Program.ChannelId);

            _voiceChannelWorker = new VoiceChannelWorker(voiceChannel, _metadataManager, _witAiClient);
            _voiceChannelWorker.OnStopped += (s, e) =>
            {
                _voiceChannelWorker.TryEnsureStarted();
            };
            _voiceChannelWorker.Start();
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
