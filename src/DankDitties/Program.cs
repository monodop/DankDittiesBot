using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        private static Secrets _secrets;
        private static MetadataManager _metadataManager;
        private static string _audioDir = "audio";

        public static readonly string PythonExecutable = _getEnv("PYTHON_EXE", "python.exe");
        public static readonly string ScriptDir = _getEnv("SCRIPT_DIR");
        public static readonly string DataDir = _getEnv("DATA_DIR");
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
        public static readonly ulong ServerId = ulong.Parse(_getEnv("SERVER_ID", "493935564832374795"));
        public static readonly ulong ChannelId = ulong.Parse(_getEnv("CHANNEL_ID", "493935564832374803"));
        public static readonly string DiscordApiKeyOverride = _getEnv("DISCORD_API_KEY");
        public static readonly string WitAiApiKeyOverride = _getEnv("WITAI_API_KEY");

        private static string _getEnv(string name, string defaultValue = null)
        {
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        public static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();

            if (DataDir != null)
                Directory.SetCurrentDirectory(DataDir);

            _secrets = JsonConvert.DeserializeObject<Secrets>(File.ReadAllText("secrets.json"));
            _secrets.DiscordApiKey = DiscordApiKeyOverride ?? _secrets.DiscordApiKey;
            _secrets.WitAiApiKey = WitAiApiKeyOverride ?? _secrets.WitAiApiKey;

            using var metadataManager = new MetadataManager("metadata.json");
            _metadataManager = metadataManager;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() => _autoPopulate(cts.Token));
            Task.Run(() => _autoPrefetch(cts.Token));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            var aiClient = new WitAiClient(_secrets.WitAiApiKey);

            var client = new DiscordClient(_secrets.DiscordApiKey, aiClient, _metadataManager);
            await client.StartAsync();

            Console.CancelKeyPress += (o, e) => cts.Cancel();

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
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private static async Task _populateBasicRedditInfo()
        {
            string offset = null;
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
                var responseData = JsonConvert.DeserializeObject<dynamic>(responseText).data;

                foreach (var d in responseData)
                {
                    var id = d.id.ToString();
                    if (!_metadataManager.HasRecord(id))
                    {
                        var postMetadata = new PostMetadata()
                        {
                            Id = id,
                            Permalink = d.permalink.ToString(),
                            Title = d.title.ToString(),
                            Domain = d.domain.ToString(),
                            Url = d.url.ToString(),
                        };
                        if (_domainAllowList.Contains(postMetadata.Domain))
                            _metadataManager.AddRecord(id, postMetadata);
                    }

                    offset = d.created_utc.ToString();
                    prevResults++;
                    totalResults++;
                }
            }
        }

        private static async Task _populateUpdatedRedditInfo(CancellationToken cancellationToken)
        {
            foreach (var postMetadata in _metadataManager.Posts.ToList())
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await Task.Yield();
                if (postMetadata.IsReviewed)
                    continue;

                var approved = false;
                try
                {
                    var scriptDir = Path.Join(ScriptDir, "get_submission.py");
                    var json = await Call(PythonExecutable, scriptDir + " " + postMetadata.Id);
                    var data = JsonConvert.DeserializeObject<dynamic>(json);

                    if (data.hasAuthor == true)
                        approved = true;
                }
                catch(Exception e)
                {
                    Console.WriteLine(e);
                }

                postMetadata.IsApproved = approved;
                postMetadata.IsReviewed = true;

                Console.WriteLine($"Reviewed {postMetadata.Id}: {(approved ? "Approved" : "Denied")}");
                _metadataManager.Save();
            }
        }

        private static async Task _prefetchMp3(CancellationToken cancellationToken)
        {
            await Task.Yield();

            var postMetadata = _metadataManager.Posts.ToList()
                .Where(p => p.IsReviewed && p.DownloadCacheFilename == null && p.DownloadFailed == false && (p.IsUserRequested || p.IsApproved))
                .OrderBy(p => p.IsUserRequested ? 0 : 1)
                .FirstOrDefault();

            if (postMetadata == null)
                return;

            try
            {
                var audioDir = Path.Join(Directory.GetCurrentDirectory(), _audioDir);
                if (!Directory.Exists(audioDir))
                    Directory.CreateDirectory(audioDir);

                var scriptDir = Path.Join(ScriptDir, "download.py");

                await Call(PythonExecutable, scriptDir + " " + postMetadata.Url + " " + postMetadata.Id, audioDir, redirect: false);

                postMetadata.DownloadCacheFilename = Path.Join(audioDir, postMetadata.Id + ".mp3");
                _metadataManager.Save();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                postMetadata.DownloadFailed = true;
                _metadataManager.Save();
            }
        }

        public static async Task<string> Call(string exe, string args, string workingDirectory = null, bool redirect = true)
        {
            var process = new Process();
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = redirect;
            process.StartInfo.RedirectStandardError = redirect;
            process.StartInfo.FileName = exe;
            process.StartInfo.Arguments = args;
            process.StartInfo.WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
            process.Start();

            string output = null, stderr = null;

            if (redirect) {
                output = await process.StandardOutput.ReadToEndAsync();
                stderr = await process.StandardError.ReadToEndAsync();
            }
            process.WaitForExit();
            if (process.ExitCode > 0)
                throw new Exception(stderr);
            return output;
        }
    }
}
