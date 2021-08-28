using DankDitties.Audio;
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
            var responseData = JsonConvert.DeserializeObject<dynamic>(responseText);
            if (responseData == null)
                throw new Exception("Response data was null");
            return responseData;
        }

        public async Task<SpeechResponse?> ParseAudioStream(Clip clip, CancellationToken cancellationToken)
        {
            var timerToken = cancellationToken;
            try
            {
                var buffer = new byte[3840];
                var hasReadBlock = false;

                using var memoryStream = new MemoryStream();
                try
                {
                    while (!timerToken.IsCancellationRequested)
                    {
                        //Console.WriteLine("Loop Start");
                        CancellationToken newTimerToken = timerToken;
                        if (!hasReadBlock)
                        {
                            //Console.WriteLine("Init Timer");
                            // Start a longer timer once the audio has come in so that we don't process more than 10 seconds of audio at a time.
                            var ctsTimer = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            var cts = CancellationTokenSource.CreateLinkedTokenSource(ctsTimer.Token, cancellationToken);
                            newTimerToken = cts.Token;
                        }

                        var shortToken = timerToken;
                        if (hasReadBlock)
                        {
                            // If we've already read data, cancel after 0.5 seconds (hopefully they're done speaking) to allow processing of the message immediately
                            var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(0.5));
                            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(shortCts.Token, timerToken);
                            shortToken = combinedCts.Token;
                        }

                        //Console.WriteLine("ReadAsync");
                        //shortToken.Register(() => Console.WriteLine("Short Token Expired"));

                        var byteCount = await clip.ReadAsync(buffer, 0, buffer.Length, shortToken);
                        if (byteCount == 0)
                            continue;

                        //Console.WriteLine("ReadAsync Ended");
                        hasReadBlock = true;
                        timerToken = newTimerToken;
                        await memoryStream.WriteAsync(buffer, 0, buffer.Length);
                        await memoryStream.FlushAsync();

                        //Console.WriteLine("Loop End");
                    }
                }
                catch (OperationCanceledException)
                {
                    //Console.WriteLine("Big Cancelled");
                    // ignore
                }

                //Console.WriteLine("Reading Completed");
                memoryStream.Seek(0, SeekOrigin.Begin);
                if (memoryStream.Length == 0)
                    return null;

                if (cancellationToken.IsCancellationRequested)
                    return null;

                //Console.WriteLine("Sending to Api");
                var uri = _generateUri("speech", new Dictionary<string, string>());

                var content = new StreamContent(memoryStream);
                content.Headers.TryAddWithoutValidation("Content-Type", "audio/raw;encoding=signed-integer;bits=16;rate=48000;endian=little");
                content.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");
                var response = await _httpClient.PostAsync(uri, content, cancellationToken);
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
            public string? Text { get; set; }

            public Dictionary<string, List<EntityResponse>> Entities { get; set; } = new Dictionary<string, List<EntityResponse>>();
            public List<IntentResponse> Intents { get; set; } = new List<IntentResponse>();

            public class IntentResponse
            {
                public string? Name { get; set; }
                public float Confidence { get; set; }
            }

            public class EntityResponse
            {
                public string? Role { get; set; }
                public string? Body { get; set; }
            }
        }
    }
}
