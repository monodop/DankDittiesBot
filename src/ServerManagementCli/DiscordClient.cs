using Discord;
using Discord.WebSocket;
using KellermanSoftware.CompareNetObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
                AlwaysDownloadUsers = true,
            });
            _client.Log += OnLog;
            _client.Ready += OnReadyAsync;
            //_client.MessageReceived += OnMessageReceived;

            _apiKey = apiKey;
        }

        private async Task OnReadyAsync()
        {
            try
            {
                var guild = _client.Guilds.First(g => g.Id == Program.DiscordServerId);
                await guild.DownloadUsersAsync();

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections | DefaultValuesHandling.OmitNull)
                    .Build();

                var expectedConfiguration = deserializer.Deserialize<ServerConfiguration>(File.ReadAllText(Program.ServerConfigFileLocation));

                var existingConfiguration = new ServerConfiguration();

                foreach (var role in guild.Roles)
                {
                    if (role == null)
                        continue;

                    var matchingRole = expectedConfiguration.Roles.FirstOrDefault(kvp => kvp.Value.Id == role.Id);
                    var friendlyId = matchingRole.Key ?? _generateFriendlyName(role.Name);
                    var needsMembership = matchingRole.Value?.HasManagedMembership == true;
                    existingConfiguration.Roles.Add(friendlyId, new ServerConfigurationRole()
                    {
                        Id = role.Id,
                        Name = role.Name,
                        Position = role.Position,
                        Color = role.Color.ToString().TrimStart('#'),
                        HasManagedMembership = needsMembership,
                        Membership = needsMembership ? role.Members.Select(_getGoodName).ToList() : new List<string>(),
                        Permissions = _mapPermissions(role.Permissions),
                    });
                }

                var channelMappings = new Dictionary<ulong, ulong>();
                foreach (var category in guild.CategoryChannels)
                {
                    if (category == null)
                        continue;

                    var matchingCategory = expectedConfiguration.Categories.FirstOrDefault(kvp => kvp.Value.Id == category.Id);
                    var categoryFriendlyId = matchingCategory.Key ?? _generateFriendlyName(category.Name);
                    existingConfiguration.Categories.Add(categoryFriendlyId, new ServerConfigurationCategory()
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Position = category.Position,
                        RolePermissions = category.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.Role)
                                        .ToDictionary(
                                            p => existingConfiguration.Roles.FirstOrDefault(role => role.Value.Id == p.TargetId).Key,
                                            p => _mapPermissions(p.Permissions)
                                        ),
                        UserPermissions = category.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.User)
                                        .ToDictionary(
                                            p => _getGoodName(guild.Users.FirstOrDefault(user => user.Id == p.TargetId)),
                                            p => _mapPermissions(p.Permissions)
                                        ),
                    });

                    foreach (var channel in category.Channels)
                    {
                        if (channel == null)
                            continue;

                        var matchingChannel = matchingCategory.Value?.Channels.FirstOrDefault(kvp => kvp.Value.Id == channel.Id);
                        var channelFriendlyId = matchingChannel?.Key ?? _generateFriendlyName(channel.Name + (channel is IVoiceChannel ? "_voice" : ""));
                        existingConfiguration.Categories[categoryFriendlyId].Channels.Add(channelFriendlyId, new ServerConfigurationChannel()
                        {
                            Id = channel.Id,
                            Name = channel.Name,
                            Category = categoryFriendlyId,
                            Position = channel.Position,
                            RolePermissions = channel.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.Role)
                                        .Select(p => new KeyValuePair<string, PermissionSet?>(
                                                existingConfiguration.Roles.FirstOrDefault(role => role.Value.Id == p.TargetId).Key,
                                                _mapPermissions(p.Permissions, category.PermissionOverwrites.FirstOrDefault(po => po.TargetId == p.TargetId && po.TargetType == p.TargetType).Permissions)
                                            ))
                                        .Where(kvp => kvp.Value != null)
                                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!),
                            UserPermissions = channel.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.User)
                                        .Select(p => new KeyValuePair<string, PermissionSet?>(
                                                _getGoodName(guild.Users.FirstOrDefault(user => user.Id == p.TargetId)),
                                                _mapPermissions(p.Permissions, category.PermissionOverwrites.FirstOrDefault(po => po.TargetId == p.TargetId && po.TargetType == p.TargetType).Permissions)
                                            ))
                                        .Where(kvp => kvp.Value != null)
                                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!),
                        });
                    }
                }

                var cl = new CompareLogic(new ComparisonConfig()
                {
                    MaxDifferences = int.MaxValue,
                });
                var comparison = cl.Compare(expectedConfiguration, existingConfiguration);

                var yaml = serializer.Serialize(existingConfiguration);
            }
            catch (Exception e)
            {
                throw;
            }
        }

        private static string _getGoodName(IUser? user)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));
            return user.Username + "#" + user.Discriminator;
        }

        private static PermissionSet _mapPermissions(GuildPermissions permissions)
        {
            var result = new PermissionSet();
            foreach (var prop in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var srcProp = permissions.GetType().GetProperty(prop.Name);
                if (srcProp == null)
                    continue;
                var v = srcProp.GetValue(permissions);
                prop.SetValue(result, v);
            }
            return result;
        }
        private static PermissionSet _mapPermissions(OverwritePermissions permissions)
        {
            var result = new PermissionSet();
            foreach (var prop in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var srcProp = permissions.GetType().GetProperty(prop.Name);
                if (srcProp == null)
                    continue;
                var v = _mapPermValue((PermValue)srcProp.GetValue(permissions)!);
                prop.SetValue(result, v);
            }
            return result;
        }
        private static PermissionSet? _mapPermissions(OverwritePermissions permissions, OverwritePermissions parent)
        {
            var result = new PermissionSet();
            var isAllNull = true;
            foreach (var prop in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var srcProp = permissions.GetType().GetProperty(prop.Name);
                if (srcProp == null)
                    continue;
                var v = _mapPermValue((PermValue)srcProp.GetValue(permissions)!);
                var pv = _mapPermValue((PermValue)srcProp.GetValue(parent)!);
                var newValue = v == pv ? null : v;
                prop.SetValue(result, newValue);

                if (newValue != null)
                    isAllNull = false;
            }

            if (isAllNull)
                return null;

            return result;
        }

        private static bool? _mapPermValue(PermValue permValue)
        {
            return permValue switch
            {
                PermValue.Allow => true,
                PermValue.Deny => false,
                _ => null,
            };
        }

        private static string _generateFriendlyName(string name)
        {
            var underscoreRegex = new Regex(@"[\-\s]");
            var garbageRegex = new Regex(@"[^\w_]");
            return garbageRegex.Replace(
                underscoreRegex.Replace(name.ToLower(), "_"),
                ""
            );
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
