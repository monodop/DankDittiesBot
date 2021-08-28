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
            _client.Ready += OnReadyAsync;
            //_client.MessageReceived += OnMessageReceived;

            _apiKey = apiKey;
        }

        private Task OnReadyAsync()
        {
            var guild = _client.Guilds.First(g => g.Id == Program.DiscordServerId);

            var existingConfiguration = new ServerConfiguration();
            var nextId = 0;

            foreach (var role in guild.Roles)
            {
                if (role == null)
                    continue;

                existingConfiguration.Roles.Add(role.Id.ToString(), new ServerConfigurationRole()
                {
                    Id = role.Id,
                    Name = role.Name,
                    Position = role.Position,
                    Color = role.Color.ToString().TrimStart('#'),
                    Membership = role.Members.Select(m => m.Username).ToList(),
                }); 
            }

            var channelMappings = new Dictionary<ulong, ulong>();
            foreach (var category in guild.CategoryChannels)
            {
                if (category == null)
                    continue;

                var categoryId = category.Id.ToString();
                existingConfiguration.Categories.Add(categoryId, new ServerConfigurationCategory()
                {
                    Id = category.Id,
                    Name = category.Name,
                    Position = category.Position,
                });

                foreach (var channel in category.Channels)
                {
                    if (channel == null)
                        continue;

                    existingConfiguration.Channels.Add(channel.Id.ToString(), new ServerConfigurationChannel()
                    {
                        Id = channel.Id,
                        Name = channel.Name,
                        Category = categoryId,
                        Position = channel.Position,
                    });
                }
            }

            return Task.FromResult(0);
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
