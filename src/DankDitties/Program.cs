using DankDitties.Data;
using LiteDB.Async;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties
{
    class Program
    {
        private static HttpClient _client = new HttpClient();

        private static string[] _domainAllowList = new string[] {
            "youtube.com",
            "youtu.be",
            "soundcloud.com",
        };
        private static MetadataManager? _metadataManager;
        private static string _audioDir = "audio";

        public static readonly string DecTalkExecutable = _getEnv("DECTALK_EXE", "wine");
        public static readonly string DecTalkWorkingDirectory = _getEnv("DECTALK_WD", "/app/dectalk");
        public static readonly string DecTalkArgTemplate = _getEnv("DECTALK_ARG_TEMPLATE", "/app/dectalk/say.exe -pre \"[:phoneme on]\" -w {{FILENAME}} {{TEXT}}");
        public static readonly string PythonExecutable = _getEnv("PYTHON_EXE", "python");
        public static readonly string? ScriptDir = _getEnv("SCRIPT_DIR");
        public static readonly string? DataDir = _getEnv("DATA_DIR");
        public static readonly bool EnableVoiceCommands = _getEnv("ENABLE_VOICE_COMMANDS", "true") == "true";
        public static ulong? VoiceCommandRole
        {
            get
            {
                var roleStr = _getEnv("VOICE_COMMAND_ROLE");
                if (roleStr != null)
                    return ulong.Parse(roleStr);
                return null;
            }
        }
        public static readonly ulong DiscordServerId = ulong.Parse(_getEnv("SERVER_ID", "493935564832374795"));
        public static readonly ulong DiscordChannelId = ulong.Parse(_getEnv("CHANNEL_ID", "493935564832374803"));
        public static readonly string? DiscordApiKeyOverride = _getEnv("DISCORD_API_KEY");
        public static readonly string? WitAiApiKeyOverride = _getEnv("WITAI_API_KEY");
        public static readonly int SoundVolume = int.Parse(_getEnv("SOUND_VOLUME", "30"));
        public static readonly int VoiceAssistantVolume = int.Parse(_getEnv("VA_VOLUME", "200"));
        public static readonly Dictionary<string, double> FlairMultipliers;

        static Program()
        {
            var segments = Regex.Split(_getEnv("FLAIR_MULTIPLIERS", ""), @"(?<!\\);");
            FlairMultipliers = new Dictionary<string, double>();
            foreach (var segment in segments)
            {
                var eqIndex = segment.LastIndexOf("=");
                if (eqIndex >= 0)
                {
                    FlairMultipliers[segment.Substring(0, eqIndex)] = double.Parse(segment.Substring(eqIndex + 1));
                }
            }
        }

        private static string? _getEnv(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }
        private static string _getEnv(string name, string defaultValue)
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        public static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            if (DataDir != null)
                Directory.SetCurrentDirectory(DataDir);

            var audioTmpDir = Path.Join(_audioDir, "tmp");
            if (Directory.Exists(audioTmpDir))
                Directory.Delete(audioTmpDir, recursive: true);
            Directory.CreateDirectory(audioTmpDir);

            var secrets = JsonConvert.DeserializeObject<Secrets>(File.ReadAllText("secrets.json"));
            if (secrets == null)
                throw new Exception("secrets object was null");
            secrets.DiscordApiKey = DiscordApiKeyOverride ?? secrets.DiscordApiKey;
            secrets.WitAiApiKey = WitAiApiKeyOverride ?? secrets.WitAiApiKey;

            using var db = new LiteDatabaseAsync($"Filename=metadata.db;Connection=shared");
            using var metadataManager = new MetadataManager(db);
            using var playHistoryManager = new PlayHistoryManager(db);
            _metadataManager = metadataManager;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => _autoPopulate(cts.Token));
            Task.Run(() => _autoPrefetch(cts.Token));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            if (secrets.WitAiApiKey == null)
                throw new ArgumentNullException("WitAiApiKey must be provided.");
            var aiClient = new WitAiClient(secrets.WitAiApiKey);

            if (secrets.DiscordApiKey == null)
                throw new ArgumentNullException("DiscordApiKey must be provided.");
            var client = new DiscordClient(secrets.DiscordApiKey, aiClient, _metadataManager, playHistoryManager);
            await client.StartAsync();

            Console.CancelKeyPress += (o, e) => cts.Cancel();
            AppDomain.CurrentDomain.ProcessExit += (s, ev) => cts.Cancel();

            await cts.Token.WhenCancelled();
        }

        private static async Task _autoPopulate(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Yield();
                    await _populateBasicRedditInfo();
                    await _populateUpdatedRedditInfo(cancellationToken);
                    await Task.Delay(TimeSpan.FromMinutes(30));
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private static async Task _autoPrefetch(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Yield();
                    await _prefetchMp3(cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(1));

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private static async Task _populateBasicRedditInfo()
        {
            string? offset = null;
            var maxResults = 10000;
            var pageSize = 100;

            var totalResults = 0;
            var prevResults = 1;

            while (totalResults < maxResults && prevResults > 0)
            {
                await Task.Yield();
                prevResults = 0;

                var url = "https://api.pushshift.io/reddit/search/submission/"
                    + "?subreddit=dankditties&sort=desc&sort_type=created_utc"
                    + "&size=" + pageSize;

                if (offset != null)
                {
                    url += "&before=" + offset;
                }

                Console.WriteLine(url);

                var response = await _client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                    return;
                }

                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonConvert.DeserializeObject<dynamic>(responseText)?.data;

                if (responseData == null)
                    throw new Exception("reddit response data was null");

                foreach (var d in responseData)
                {
                    var id = d?.id.ToString();
                    await _metadataManager?.AddRedditPostAsync(id, d?.url.ToString());

                    offset = d?.created_utc.ToString();
                    prevResults++;
                    totalResults++;
                }
            }
        }

        private static async Task _populateUpdatedRedditInfo(CancellationToken cancellationToken)
        {
            var refreshCutoff = DateTime.UtcNow - TimeSpan.FromDays(1);

            if (_metadataManager == null)
                throw new InvalidOperationException("_metadataManager not set");

            var metadata = await _metadataManager.GetMetadataAsync(m =>
                (m.LastRefresh == null || m.LastRefresh < refreshCutoff)
                && m.Type == MetadataType.Reddit
            );
            foreach (var m in metadata)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await Task.Yield();

                try
                {
                    var scriptDir = Path.Join(ScriptDir, "get_submission.py");
                    var json = await Call(PythonExecutable, scriptDir + " " + m.RedditId);
                    var data = JsonConvert.DeserializeObject<dynamic>(json ?? "{}") ?? new object(); // TODO: is this right?

                    m.IsApproved = data.isRobotIndexable;
                    m.IsNsfw = data.nsfw;
                    m.LinkFlairText = data.linkFlairText?.ToString();
                    m.Title = data.title.ToString();
                    m.SubmittedBy = data.author.ToString();
                    m.Subreddit = data.subreddit.ToString();
                }
                catch(Exception e)
                {
                    m.IsApproved = false;
                    Console.WriteLine(e);
                }
                m.LastRefresh = DateTime.UtcNow;

                await _metadataManager.UpdateAsync(m);

                Console.WriteLine($"Reviewed {m.Id}: {(m.IsApproved ? "Approved" : "Denied")}");
            }
        }

        private static async Task _prefetchMp3(CancellationToken cancellationToken)
        {
            await Task.Yield();

            Metadata metadata;
            try
            {
                if (_metadataManager == null)
                    throw new InvalidOperationException("_metadataManager not set");

                metadata = await _metadataManager.GetOneMetadataAsync(m =>
                    m.Type == MetadataType.UserRequested
                    && m.AudioCacheFilename == null
                    && m.DownloadFailed == false
                );
                if (metadata == null)
                {
                    metadata = await _metadataManager.GetOneMetadataAsync(m =>
                        m.Type == MetadataType.Reddit
                        && m.IsApproved
                        && m.AudioCacheFilename == null
                        && m.DownloadFailed != true
                    );
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return;
            }

            if (metadata == null)
                return;

            try
            {
                var audioDir = Path.Join(Directory.GetCurrentDirectory(), _audioDir);
                if (!Directory.Exists(audioDir))
                    Directory.CreateDirectory(audioDir);

                var scriptDir = Path.Join(ScriptDir ?? Directory.GetCurrentDirectory(), "download.py");

                await Call(PythonExecutable, scriptDir + " " + metadata.Url + " " + metadata.Id, audioDir, redirect: false);

                metadata.AudioCacheFilename = Path.Join(audioDir, metadata.Id + ".mp3");
                await _metadataManager.UpdateAsync(metadata);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                metadata.DownloadFailed = true;
                await _metadataManager.UpdateAsync(metadata);
            }
        }

        public static async Task<string?> Call(string exe, string args, string? workingDirectory = null, bool redirect = true)
        {
            var process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = redirect;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.FileName = exe;
            process.StartInfo.Arguments = args;
            process.StartInfo.WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
            process.Start();

            string? output = null;
            if (redirect) {
                output = await process.StandardOutput.ReadToEndAsync();
            }
            string stderr = await process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode > 0)
                throw new Exception(stderr);
            return output;
        }
    }
}
