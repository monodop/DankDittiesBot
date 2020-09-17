using Discord.Audio;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties
{
    public class WitAiClient
    {
        private readonly string _token;
        private HttpClient _httpClient;
        private readonly string _baseUri = "https://api.wit.ai/";
        private readonly string _version = "20200916";

        public WitAiClient(string token)
        {
            _token = token;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        }

        private string _generateUri(string apiPath, IDictionary<string, string> queryParams)
        {
            if (!queryParams.ContainsKey("v"))
                queryParams["v"] = _version;

            var queryString = string.Join(
                "&", 
                queryParams.Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value))
            );

            return _baseUri + apiPath + "?" + queryString;
        }

        public async Task<dynamic> ParseText(string text)
        {
            var data = new Dictionary<string, string>();
            data["q"] = text;
            var uri = _generateUri("message", data);
            var response = await _httpClient.GetAsync(uri);
            var responseText = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<dynamic>(responseText);
        }

        public async Task<SpeechResponse> ParseAudioStream(AudioInStream stream)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var blockSize = 3840;
                var buffer = new byte[blockSize];

                var sampleRate = 48000;
                var bytesPerSecond = sampleRate * 2;
                var sampleDuration = 20;
                var totalBytes = bytesPerSecond * sampleDuration;
                var totalBlocks = totalBytes / blockSize;

                using var memoryStream = new MemoryStream();
                try
                {
                    for (var i = 0; i < totalBlocks; i++)
                    {
                        while (true)
                        {
                            var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(shortCts.Token, cts.Token);
                            if (await stream.ReadAsync(buffer, 0, buffer.Length, combinedCts.Token) == 0)
                                break;

                            await memoryStream.WriteAsync(buffer, 0, buffer.Length);
                            await memoryStream.FlushAsync();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
                memoryStream.Seek(0, SeekOrigin.Begin);
                if (memoryStream.Length == 0)
                    return null;

                var uri = _generateUri("speech", new Dictionary<string, string>());

                var content = new StreamContent(memoryStream);
                content.Headers.TryAddWithoutValidation("Content-Type", "audio/raw;encoding=signed-integer;bits=16;rate=96000;endian=little");
                content.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
                var response = await _httpClient.PostAsync(uri, content);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(responseText);
                }

                return JsonConvert.DeserializeObject<SpeechResponse>(responseText);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return null;
        }

        public class SpeechResponse
        {
            public string Text { get; set; }

            public Dictionary<string, List<EntityResponse>> Entities { get; set; }
            public List<IntentResponse> Intents { get; set; }

            public class IntentResponse
            {
                public string Name { get; set; }
                public float Confidence { get; set; }
            }

            public class EntityResponse
            {
                public string Role { get; set; }
                public string Body { get; set; }
            }
        }
    }
}
