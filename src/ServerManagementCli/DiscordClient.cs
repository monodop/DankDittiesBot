using Discord;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerManagementCli
{
    public class DiscordClient
    {
        private readonly DiscordSocketClient _client;
        private readonly string _apiKey;

        public DiscordClient(string apiKey)
        {
            _client = new DiscordSocketClient(new DiscordSocketConfig()
            {

            });
            _client.Log += OnLog;
            //_client.Ready += OnReady;
            //_client.MessageReceived += OnMessageReceived;

            _apiKey = apiKey;
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
