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
                    .WithIndentedSequences()
                    .Build();

                var expectedConfiguration = deserializer.Deserialize<ServerConfiguration>(File.ReadAllText(Program.ServerConfigFileLocation));
                for (int i = 0; i < expectedConfiguration.Roles.Count; i++)
                {
                    expectedConfiguration.Roles[i].Position = expectedConfiguration.Roles.Count - i - 1;
                }
                var nextCategoryPos = 0;
                foreach (var category in expectedConfiguration.Categories)
                {
                    category.Position = nextCategoryPos++;
                    var nextChannelPos = 0;
                    foreach (var channel in category.Channels)
                    {
                        channel.Position = nextChannelPos++;
                    }
                }

                var existingConfiguration = new ServerConfiguration();

                var nextRolePos = guild.Roles.Count - 1;
                foreach (var role in guild.Roles.OrderByDescending(r => r.Position))
                {
                    if (role == null)
                        continue;

                    var matchingRole = expectedConfiguration.Roles.FirstOrDefault(r => r.Id == role.Id);
                    var friendlyId = matchingRole?.FriendlyId ?? _generateFriendlyName(role.Name);
                    var needsMembership = matchingRole?.HasManagedMembership == true;
                    existingConfiguration.Roles.Add(new ServerConfigurationRole()
                    {
                        FriendlyId = friendlyId,
                        Id = role.Id,
                        Name = role.Name,
                        Position = nextRolePos--,
                        Color = role.Color.ToString().TrimStart('#'),
                        HasManagedMembership = needsMembership,
                        Membership = needsMembership ? role.Members.Select(_getGoodName).OrderBy(n => n).ToList() : new List<string>(),
                        Permissions = _mapPermissions(role.Permissions),
                    });
                }

                var channelMappings = new Dictionary<ulong, ulong>();
                nextCategoryPos = 0;
                foreach (var category in guild.CategoryChannels.OrderBy(c => c.Position))
                {
                    if (category == null)
                        continue;

                    var matchingCategory = expectedConfiguration.Categories.FirstOrDefault(c => c.Id == category.Id);
                    string categoryFriendlyId = matchingCategory?.FriendlyId ?? _generateFriendlyName(category.Name);
                    existingConfiguration.Categories.Add(new ServerConfigurationCategory()
                    {
                        FriendlyId = categoryFriendlyId,
                        Id = category.Id,
                        Name = category.Name,
                        Position = nextCategoryPos++,
                        RolePermissions = category.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.Role)
                                        .ToDictionary(
                                            p => existingConfiguration.Roles.FirstOrDefault(r => r.Id == p.TargetId)!.FriendlyId!,
                                            p => _mapPermissions(p.Permissions)
                                        ),
                        UserPermissions = category.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.User)
                                        .ToDictionary(
                                            p => _getGoodName(guild.Users.FirstOrDefault(user => user.Id == p.TargetId)),
                                            p => _mapPermissions(p.Permissions)
                                        ),
                    });

                    var nextChannelPos = 0;
                    foreach (var channel in category.Channels.OrderBy(c => c.Position))
                    {
                        if (channel == null)
                            continue;

                        var matchingChannel = matchingCategory?.Channels.FirstOrDefault(c => c.Id == channel.Id);
                        string channelFriendlyId = matchingChannel?.FriendlyId ?? _generateFriendlyName(channel.Name + (channel is IVoiceChannel ? "_voice" : ""));
                        existingConfiguration.Categories.FirstOrDefault(c => c.FriendlyId == categoryFriendlyId)!.Channels.Add(new ServerConfigurationChannel()
                        {
                            FriendlyId = channelFriendlyId,
                            Id = channel.Id,
                            Name = channel.Name,
                            Category = categoryFriendlyId,
                            Position = nextChannelPos++,
                            RolePermissions = channel.PermissionOverwrites
                                        .Where(p => p.TargetType == PermissionTarget.Role)
                                        .Select(p => new KeyValuePair<string, PermissionSet?>(
                                                existingConfiguration.Roles.FirstOrDefault(r => r.Id == p.TargetId)!.FriendlyId!,
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
                    IgnoreCollectionOrder = true,
                    CollectionMatchingSpec = new Dictionary<Type, IEnumerable<string>>()
                    {
                        { typeof(ServerConfigurationRole), new string[] { nameof(ServerConfigurationRole.FriendlyId) } },
                        { typeof(ServerConfigurationCategory), new string[] { nameof(ServerConfigurationCategory.FriendlyId) } },
                        { typeof(ServerConfigurationChannel), new string[] { nameof(ServerConfigurationChannel.FriendlyId) } },
                    }
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
