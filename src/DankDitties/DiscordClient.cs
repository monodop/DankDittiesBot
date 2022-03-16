using DankDitties.Data;
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
        private readonly string _apiKey;
        private readonly WitAiClient _witAiClient;
        private readonly MetadataManager _metadataManager;
        private readonly PlayHistoryManager _playHistoryManager;

        private VoiceChannelWorker? _voiceChannelWorker;

        public DiscordClient(string apiKey, WitAiClient witAiClient, MetadataManager metadataManager, PlayHistoryManager playHistoryManager)
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
            _playHistoryManager = playHistoryManager;

            if (_voiceChannelWorker != null)
                _voiceChannelWorker.OnNextSong += SetPresenceForSong;
        }

        private Task OnReady()
        {
            var guild = _client.Guilds.FirstOrDefault(g => g.Id == Program.DiscordServerId);
            var voiceChannel = guild.VoiceChannels.FirstOrDefault(c => c.Id == Program.DiscordChannelId);

            _voiceChannelWorker = new VoiceChannelWorker(voiceChannel, _metadataManager, _playHistoryManager, _witAiClient);
            _voiceChannelWorker.OnStopped += (s, e) =>
            {
                _voiceChannelWorker.TryEnsureStarted();
            };
            _voiceChannelWorker.Start();
            return Task.FromResult(0);
        }

        private async void SetPresenceForSong(object? sender, Metadata? songMetadata)
        {
            if (songMetadata != null)
                await _client.SetActivityAsync(new Game(songMetadata.Title, ActivityType.Listening));
            else
                await _client.SetActivityAsync(null);
        }

        private async Task OnMessageReceived(SocketMessage arg)
        {
            var author = arg.Author as IGuildUser;
            var voiceChannel = author?.VoiceChannel as SocketVoiceChannel;

            if (voiceChannel?.Id != Program.DiscordChannelId)
                return;

            if (arg.Content == "!dd start" && voiceChannel != null)
            {
                Console.WriteLine("Joining voice channel: " + voiceChannel.Name);
                _voiceChannelWorker?.TryEnsureStarted();
            }
            else if (arg.Content == "!dd skip" && voiceChannel != null)
            {
                _voiceChannelWorker?.TrySkip();
            }
            else if (arg.Content == "!dd stop")
            {
                if (_voiceChannelWorker != null)
                    await _voiceChannelWorker.StopAsync();
            }
            else if (arg.Content == "!dd info")
            {
                var currentSong = _voiceChannelWorker?.CurrentSong;
                _ = arg.Channel.SendMessageAsync("Now playing " + currentSong?.Title + " - " + currentSong?.Url);
            }
            else if (arg.Content.StartsWith("!dd play "))
            {
                var url = arg.Content.Substring("!dd play ".Length);

                _ = Task.Run(async () =>
                {
                    var post = await _metadataManager.AddUserRequestAsync(url, arg.Author.Username);
                    _voiceChannelWorker?.EnqueueSong(post.Id);
                    await arg.Channel.SendMessageAsync("The song has been added to the queue");
                });
            }
            else if (arg.Content.StartsWith("!dd say "))
            {
                var text = arg.Content.Substring("!dd say ".Length);

                _voiceChannelWorker?.Say(text);
            }
            else if (arg.Content.StartsWith("!dd pause"))
            {
                _voiceChannelWorker?.TryPauseMainTrack();
            }
            else if (arg.Content.StartsWith("!dd resume"))
            {
                _voiceChannelWorker?.TryResumeMainTrack();
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
