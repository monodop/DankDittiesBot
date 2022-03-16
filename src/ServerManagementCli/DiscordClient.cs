using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
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

                var results = new ConfigDiff().GetDifferences(expectedConfiguration, existingConfiguration).ToList();

                string yamlPrint(object? o, int depth = 4)
                {
                    if (o == null)
                        return "null";

                    var newLine = "\n" + new string(' ', depth);
                    var serialized = serializer!.Serialize(o).TrimEnd().Replace("\n", newLine);
                    if (serialized.Contains('\n'))
                        return newLine + serialized;
                    return serialized;
                }
                foreach (ConfigDiff.IResult change in results)
                {
                    if (change is ConfigDiff.IResult<ServerConfigurationRole> roleChange)
                    {
                        var actualRole = roleChange.Actual;
                        var expectedRole = roleChange.Expected;
                        if (actualRole == null && expectedRole != null)
                        {
                            // Add
                            Console.WriteLine("Adding new role: " + expectedRole.Name);
                            Console.WriteLine("  Friendly id: " + expectedRole.FriendlyId);
                            Console.WriteLine("  Position: " + expectedRole.Position);
                            Console.WriteLine("  Color: #" + expectedRole.Color);
                            Console.WriteLine("  HasManagedMembership: " + expectedRole.HasManagedMembership);
                            Console.WriteLine("  Permissions: " + yamlPrint(expectedRole.Permissions));
                            Console.WriteLine("  Membership: ");
                            foreach (var member in expectedRole.Membership)
                            {
                                Console.WriteLine("    " + member);
                            }
                            Console.WriteLine();

                            var requestOptions = new RequestOptions()
                            {
                                AuditLogReason = "Automatically added by DankDitties", // TODO: better message
                            };
                            // TODO: hoisted
                            // TODO: there's another thing too
                            var color = ColorTranslator.FromHtml("#" + expectedRole.Color);
                            var newRole = await guild.CreateRoleAsync(expectedRole.Name, _mapGuildPermissions(expectedRole.Permissions), new Discord.Color(color.R, color.G, color.B), false, requestOptions);
                            expectedRole.Id = newRole.Id;
                        }
                        else if (expectedRole == null && actualRole != null)
                        {
                            // Remove
                            Console.WriteLine($"Deleting existing role: {actualRole.Name} ({actualRole.Id})");
                            try
                            {
                                var requestOptions = new RequestOptions()
                                {
                                    AuditLogReason = "Automatically deleted by DankDitties", // TODO: better message
                                };
                                var guildRole = guild.Roles.FirstOrDefault(r => r.Id == actualRole.Id);
                                if (guildRole != null)
                                    await guildRole.DeleteAsync(requestOptions);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Failed to remove existing role: {actualRole.Name} ({actualRole.Id})");
                                Console.WriteLine(e);
                            }
                            Console.WriteLine();
                        }
                        else if (expectedRole != null && actualRole != null)
                        {
                            // Modify
                            Console.WriteLine($"Modifying role: {actualRole.Name} ({actualRole.Id})");
                            foreach (var prop in roleChange.ChangedProperties)
                            {
                                var e = prop.GetValue(expectedRole);
                                var a = prop.GetValue(actualRole);
                                if (prop.Name == "Permissions" || prop.Name == "Membership")
                                {
                                    e = yamlPrint(e);
                                    a = yamlPrint(a);
                                }
                                Console.WriteLine($"  {prop.Name}: {a} -> {e}");
                            }
                            Console.WriteLine();
                        }
                    }
                    else if (change is ConfigDiff.IResult<ServerConfigurationCategory> categoryChange)
                    {
                        var actualCategory = categoryChange.Actual;
                        var expectedCategory = categoryChange.Expected;
                        if (actualCategory == null && expectedCategory != null)
                        {
                            // Add
                            Console.WriteLine("Adding new category: " + expectedCategory.Name);
                            Console.WriteLine("  Friendly id: " + expectedCategory.FriendlyId);
                            Console.WriteLine("  Position: " + expectedCategory.Position);
                            Console.WriteLine("  UserPermissions: " + yamlPrint(expectedCategory.UserPermissions));
                            Console.WriteLine("  RolePermissions: " + yamlPrint(expectedCategory.RolePermissions));
                            Console.WriteLine();

                            var requestOptions = new RequestOptions()
                            {
                                AuditLogReason = "Automatically added by DankDitties", // TODO: better message
                            };
                            var newCategory = await guild.CreateCategoryChannelAsync(expectedCategory.Name, (props) =>
                            {
                                var userPermissions = from kvp in expectedCategory.UserPermissions
                                                      let userName = kvp.Key
                                                      let permission = kvp.Value
                                                      let matchingUser = guild.Users.FirstOrDefault(u => _getGoodName(u) == userName)
                                                      where matchingUser != null
                                                      select (matchingUser.Id, permission);
                                var rolePermissions = from kvp in expectedCategory.RolePermissions
                                                      let roleName = kvp.Key
                                                      let permission = kvp.Value
                                                      let matchingRole = expectedConfiguration.Roles.FirstOrDefault(r => r.FriendlyId == roleName)
                                                      where matchingRole != null
                                                      select (matchingRole.Id, permission);

                                props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(_mapPermissionOverwrites(userPermissions, rolePermissions));
                                props.Position = expectedCategory.Position;

                            }, requestOptions);
                            expectedCategory.Id = newCategory.Id;
                        }
                        else if (expectedCategory == null && actualCategory != null)
                        {
                            // Remove
                            Console.WriteLine($"Deleting existing category: {actualCategory.Name} ({actualCategory.Id})");
                            try
                            {
                                var requestOptions = new RequestOptions()
                                {
                                    AuditLogReason = "Automatically deleted by DankDitties", // TODO: better message
                                };
                                var guildCategory = guild.CategoryChannels.FirstOrDefault(r => r.Id == actualCategory.Id);
                                if (guildCategory != null)
                                    await guildCategory.DeleteAsync(requestOptions);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Failed to remove existing category: {actualCategory.Name} ({actualCategory.Id})");
                                Console.WriteLine(e);
                            }
                            Console.WriteLine();
                        }
                        else if (expectedCategory != null && actualCategory != null)
                        {
                            // Modify
                            Console.WriteLine($"Modifying category: {actualCategory.Name} ({actualCategory.Id})");
                            foreach (var prop in categoryChange.ChangedProperties)
                            {
                                var e = prop.GetValue(expectedCategory);
                                var a = prop.GetValue(actualCategory);
                                //if (prop.Name == "Permissions" || prop.Name == "Membership")
                                //{
                                //    e = JsonConvert.SerializeObject(e, Formatting.Indented).Replace("\n", "\n  ");
                                //    a = JsonConvert.SerializeObject(a, Formatting.Indented).Replace("\n", "\n  ");
                                //}
                                Console.WriteLine($"  {prop.Name}: {a} -> {e}");
                            }
                            Console.WriteLine();
                        }
                    }
                    else if (change is ConfigDiff.IResult<ServerConfigurationChannel> channelChange)
                    {
                        var actualChannel = channelChange.Actual;
                        var expectedChannel = channelChange.Expected;
                        if (actualChannel == null && expectedChannel != null)
                        {
                            // Add
                            Console.WriteLine("Adding new channel: #" + expectedChannel.Name);
                            Console.WriteLine("  Friendly id: " + expectedChannel.FriendlyId);
                            var cat = expectedConfiguration.Categories.FirstOrDefault(c => c.FriendlyId == expectedChannel.Category);
                            Console.WriteLine($"  Category: {cat!.Name} ({cat.Id} / {cat.FriendlyId})");
                            Console.WriteLine("  Position: " + expectedChannel.Position);
                            Console.WriteLine("  Sync Permissions: " + expectedChannel.SyncPermissions);
                            Console.WriteLine("  UserPermissions: " + yamlPrint(expectedChannel.UserPermissions));
                            Console.WriteLine("  RolePermissions: " + yamlPrint(expectedChannel.RolePermissions));

                            var requestOptions = new RequestOptions()
                            {
                                AuditLogReason = "Automatically added by DankDitties", // TODO: better message
                            };

                            // Todo: use text or voice depending
                            var newChannel = await guild.CreateTextChannelAsync(expectedChannel.Name, (props) =>
                            {
                                props.CategoryId = expectedConfiguration.Categories.FirstOrDefault(c => c.FriendlyId == expectedChannel.Category)!.Id;
                                var userPermissions = from kvp in expectedChannel.UserPermissions
                                                      let userName = kvp.Key
                                                      let permission = kvp.Value
                                                      let matchingUser = guild.Users.FirstOrDefault(u => _getGoodName(u) == userName)
                                                      where matchingUser != null
                                                      select (matchingUser.Id, permission);
                                var rolePermissions = from kvp in expectedChannel.RolePermissions
                                                      let roleName = kvp.Key
                                                      let permission = kvp.Value
                                                      let matchingRole = expectedConfiguration.Roles.FirstOrDefault(r => r.FriendlyId == roleName)
                                                      where matchingRole != null
                                                      select (matchingRole.Id, permission);

                                props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(_mapPermissionOverwrites(userPermissions, rolePermissions));
                                props.Position = expectedChannel.Position;
                                // TODO: everything else

                            }, requestOptions);
                            expectedChannel.Id = newChannel.Id;

                            Console.WriteLine();
                        }
                        else if (expectedChannel == null && actualChannel != null)
                        {
                            // Remove
                            Console.WriteLine($"Deleting existing channel: {actualChannel.Name} ({actualChannel.Id})");
                            try
                            {
                                var requestOptions = new RequestOptions()
                                {
                                    AuditLogReason = "Automatically deleted by DankDitties", // TODO: better message
                                };
                                var guildChannel = guild.Channels.FirstOrDefault(r => r.Id == actualChannel.Id);
                                if (guildChannel != null)
                                    await guildChannel.DeleteAsync(requestOptions);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Failed to remove existing channel: {actualChannel.Name} ({actualChannel.Id})");
                                Console.WriteLine(e);
                            }
                            Console.WriteLine();
                        }
                        else if (expectedChannel != null && actualChannel != null)
                        {
                            // Modify
                            Console.WriteLine($"Modifying channel: {actualChannel.Name} ({actualChannel.Id})");
                            foreach (var prop in channelChange.ChangedProperties)
                            {
                                var e = prop.GetValue(expectedChannel);
                                var a = prop.GetValue(actualChannel);
                                //if (prop.Name == "Permissions" || prop.Name == "Membership")
                                //{
                                //    e = JsonConvert.SerializeObject(e, Formatting.Indented).Replace("\n", "\n  ");
                                //    a = JsonConvert.SerializeObject(a, Formatting.Indented).Replace("\n", "\n  ");
                                //}
                                Console.WriteLine($"  {prop.Name}: {a} -> {e}");
                            }
                            Console.WriteLine();
                        }
                    }
                }

                //var yaml = serializer.Serialize(existingConfiguration);

                // Update yaml
                var yaml = serializer.Serialize(expectedConfiguration);
                await File.WriteAllTextAsync(Program.ServerConfigFileLocation, yaml);
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
        private static GuildPermissions _mapGuildPermissions(PermissionSet permissions)
        {
            return new GuildPermissions(
                attachFiles: permissions.AttachFiles == true,
                readMessageHistory: permissions.ReadMessageHistory == true,
                mentionEveryone: permissions.MentionEveryone == true,
                useExternalEmojis: permissions.UseExternalEmojis == true,
                connect: permissions.Connect == true,
                speak: permissions.Speak == true,
                muteMembers: permissions.MuteMembers == true,
                useVoiceActivation: permissions.UseVAD == true,
                moveMembers: permissions.MoveMembers == true,
                embedLinks: permissions.EmbedLinks == true,
                prioritySpeaker: permissions.PrioritySpeaker == true,
                stream: permissions.Stream == true,
                changeNickname: permissions.ChangeNickname == true,
                manageNicknames: permissions.ManageNicknames == true,
                manageRoles: permissions.ManageRoles == true,
                deafenMembers: permissions.DeafenMembers == true,
                manageMessages: permissions.ManageMessages == true,
                viewChannel: permissions.ViewChannel == true,
                sendMessages: permissions.SendMessages == true,
                createInstantInvite: permissions.CreateInstantInvite == true,
                banMembers: permissions.BanMembers == true,
                sendTTSMessages: permissions.SendTTSMessages == true,
                administrator: permissions.Administrator == true,
                manageChannels: permissions.ManageChannels == true,
                kickMembers: permissions.KickMembers == true,
                addReactions: permissions.AddReactions == true,
                viewAuditLog: permissions.ViewAuditLog == true,
                manageWebhooks: permissions.ManageWebhooks == true,
                manageGuild: permissions.ManageGuild == true,
                manageEmojis: permissions.ManageEmojis == true
            );
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

        private static IEnumerable<Overwrite> _mapPermissionOverwrites(IEnumerable<(ulong, PermissionSet)> userPermissions, IEnumerable<(ulong, PermissionSet)> rolePermissions)
        {
            foreach (var (roleId, permission) in rolePermissions)
            {
                yield return new Overwrite(roleId, PermissionTarget.Role, _mapPermissionOverwrites(permission));
            }

            foreach (var (userId, permission) in userPermissions)
            {
                yield return new Overwrite(userId, PermissionTarget.User, _mapPermissionOverwrites(permission));
            }
        }

        private static OverwritePermissions _mapPermissionOverwrites(PermissionSet permissions)
        {
            return new OverwritePermissions(
                attachFiles: _mapPermValue(permissions.AttachFiles),
                readMessageHistory: _mapPermValue(permissions.ReadMessageHistory),
                mentionEveryone: _mapPermValue(permissions.MentionEveryone),
                useExternalEmojis: _mapPermValue(permissions.UseExternalEmojis),
                connect: _mapPermValue(permissions.Connect),
                speak: _mapPermValue(permissions.Speak),
                sendMessages: _mapPermValue(permissions.SendMessages),
                embedLinks: _mapPermValue(permissions.EmbedLinks),
                deafenMembers: _mapPermValue(permissions.DeafenMembers),
                moveMembers: _mapPermValue(permissions.MoveMembers),
                useVoiceActivation: _mapPermValue(permissions.UseVAD),
                prioritySpeaker: _mapPermValue(permissions.PrioritySpeaker),
                stream: _mapPermValue(permissions.Stream),
                muteMembers: _mapPermValue(permissions.MuteMembers),
                manageMessages: _mapPermValue(permissions.ManageMessages),
                manageWebhooks: _mapPermValue(permissions.ManageWebhooks),
                manageRoles: _mapPermValue(permissions.ManageRoles),
                viewChannel: _mapPermValue(permissions.ViewChannel),
                addReactions: _mapPermValue(permissions.AddReactions),
                manageChannel: _mapPermValue(permissions.ManageChannel),
                createInstantInvite: _mapPermValue(permissions.CreateInstantInvite),
                sendTTSMessages: _mapPermValue(permissions.SendTTSMessages)
            );
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

        private static PermValue _mapPermValue(bool? permValue)
        {
            return permValue switch
            {
                true => PermValue.Allow,
                false => PermValue.Deny,
                _ => PermValue.Inherit,
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
            //Console.WriteLine(arg.Message);
            return Task.FromResult(0);
        }
    }
}
