using DankDitties;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ServerManagementCli
{
    class Program
    {
        public static readonly string ServerConfigFileLocation = Utils.GetEnv("SERVER_CONFIG_FILE") ?? throw new Exception("missing server config file location");
        public static readonly ulong DiscordServerId = ulong.Parse(Utils.GetEnv("SERVER_ID", "493935564832374795"));
        public static readonly string? DiscordApiKeyOverride = Utils.GetEnv("DISCORD_API_KEY");

        public static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            var secrets = JsonConvert.DeserializeObject<Secrets>(File.ReadAllText("secrets.json"));
            if (secrets == null)
                throw new Exception("secrets object was null");
            secrets.DiscordApiKey = DiscordApiKeyOverride ?? secrets.DiscordApiKey;

            if (secrets.DiscordApiKey == null)
                throw new NullReferenceException("DiscordApiKey must be provided.");
            var client = new DiscordClient(secrets.DiscordApiKey);
            await client.StartAsync();

            Console.CancelKeyPress += (o, e) => cts.Cancel();
            AppDomain.CurrentDomain.ProcessExit += (s, ev) => cts.Cancel();

            await cts.Token.WhenCancelled();
        }
    }
}
