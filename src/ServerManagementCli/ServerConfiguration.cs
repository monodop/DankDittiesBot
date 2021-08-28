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
        public Dictionary<string, ServerConfigurationChannel> Channels { get; set; } = new Dictionary<string, ServerConfigurationChannel>();
    }

    public class ServerConfigurationRole
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public int Position { get; set; }
        public string? Color { get; set; }
        public PermissionSet Permissions { get; set; } = new PermissionSet();
        public List<string> Membership { get; set; } = new List<string>();
    }

    public class ServerConfigurationCategory
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public int Position { get; set; }

        public Dictionary<string, PermissionSet> Permissions { get; set; } = new Dictionary<string, PermissionSet>();
    }

    public class ServerConfigurationChannel
    {
        public ulong Id { get; set; }
        public string? Name { get; set; }
        public string? Category { get; set; }
        public int Position { get; set; }

        public Dictionary<string, PermissionSet> Permissions { get; set; } = new Dictionary<string, PermissionSet>();
    }

    public class PermissionSet
    {
        public bool View { get; set; }
        public bool Connect { get; set; }
    }
}
