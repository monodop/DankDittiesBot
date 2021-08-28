using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerManagementCli
{
    public class ServerConfiguration
    {
        public Dictionary<string, ServerConfigurationRole> Roles { get; set; } = new Dictionary<string, ServerConfigurationRole>();
        public Dictionary<string, ServerConfigurationCategory> Categories { get; set; } = new Dictionary<string, ServerConfigurationCategory>();
    }

    public class ServerConfigurationRole
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public int Position { get; set; }
        public string? Color { get; set; }
        public bool HasManagedMembership { get; set; }
        public PermissionSet Permissions { get; set; } = new PermissionSet();
        public List<string> Membership { get; set; } = new List<string>();
    }

    public class ServerConfigurationCategory
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public int Position { get; set; }

        public Dictionary<string, PermissionSet> RolePermissions { get; set; } = new Dictionary<string, PermissionSet>();
        public Dictionary<string, PermissionSet> UserPermissions { get; set; } = new Dictionary<string, PermissionSet>();
        public Dictionary<string, ServerConfigurationChannel> Channels { get; set; } = new Dictionary<string, ServerConfigurationChannel>();
    }

    public class ServerConfigurationChannel
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public int Position { get; set; }
        public bool SyncPermissions { get; set; }

        public Dictionary<string, PermissionSet> RolePermissions { get; set; } = new Dictionary<string, PermissionSet>();
        public Dictionary<string, PermissionSet> UserPermissions { get; set; } = new Dictionary<string, PermissionSet>();
    }

    public class PermissionSet
    {
        public bool? AttachFiles { get; set; }
        public bool? ReadMessageHistory { get; set; }
        public bool? MentionEveryone { get; set; }
        public bool? UseExternalEmojis { get; set; }
        public bool? Connect { get; set; }
        public bool? Speak { get; set; }
        public bool? SendMessages { get; set; }
        public bool? EmbedLinks { get; set; }
        public bool? DeafenMembers { get; set; }
        public bool? MoveMembers { get; set; }
        public bool? UseVAD { get; set; }
        public bool? PrioritySpeaker { get; set; }
        public bool? Stream { get; set; }
        public bool? MuteMembers { get; set; }
        public bool? ManageMessages { get; set; }
        public bool? ManageWebhooks { get; set; }
        public bool? ManageRoles { get; set; }
        public bool? ViewChannel { get; set; }
        public bool? AddReactions { get; set; }
        public bool? ManageChannel { get; set; }
        public bool? CreateInstantInvite { get; set; }
        public bool? SendTTSMessages { get; set; }

        public bool? ChangeNickname { get; set; }
        public bool? ManageNicknames { get; set; }
        public bool? BanMembers { get; set; }
        public bool? Administrator { get; set; }
        public bool? ManageChannels { get; set; }
        public bool? KickMembers { get; set; }
        public bool? ViewAuditLog { get; set; }
        public bool? ManageGuild { get; set; }
        public bool? ManageEmojis { get; set; }
    }
}
