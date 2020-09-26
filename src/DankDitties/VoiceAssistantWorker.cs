using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DankDitties
{
    public class VoiceAssistantWorker : Worker
    {
        private readonly VoiceChannelWorker _voiceChannelWorker;
        private readonly MetadataManager _metadataManager;
        private readonly WitAiClient _witAiClient;
        private readonly SocketGuildUser _user;

        public VoiceAssistantWorker(SocketGuildUser user, VoiceChannelWorker voiceChannelWorker, MetadataManager metadataManager, WitAiClient witAiClient)
        {
            _voiceChannelWorker = voiceChannelWorker;
            _metadataManager = metadataManager;
            _witAiClient = witAiClient;
            _user = user;
        }

        protected override async Task DoWorkAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Starting voice assistant runner for " + _user.Username);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var userStream = _user.AudioStream;
                    if (userStream == null)
                        return;

                    try
                    {
                        var data = await _witAiClient.ParseAudioStream(userStream, cancellationToken);
                        if (data != null)
                        {
                            var text = data.Text?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                Console.WriteLine(_user.Username + ": " + text);
                                var playSongIntent = data.Intents.FirstOrDefault(i => i.Name == "play_song");

                                if (text.ToLower().StartsWith("i'm "))
                                {
                                    _voiceChannelWorker.Say("Hello " + text.Substring("i'm ".Length) + ", I'm Dank Ditties bot.");
                                }
                                else if (text.ToLower().StartsWith("play "))
                                {
                                    var searchString = text.Substring("play ".Length);

                                    if (searchString == "next")
                                    {
                                        _voiceChannelWorker?.TrySkip();
                                        continue;
                                    }

                                    var matches = from post in _metadataManager.Posts
                                                  where post.IsReady
                                                  let relevance = FuzzySharp.Fuzz.Ratio(post.Title, searchString)
                                                  select (post, relevance);
                                    var topMatch = matches.OrderByDescending(m => m.relevance);
                                    Console.WriteLine("matches: \n" + string.Join("\n", topMatch.Take(3).Select(m => $"{m.post.Title}: {m.relevance}")));
                                    Console.WriteLine();
                                    var closestMatch = topMatch.FirstOrDefault().post;
                                    if (closestMatch != null)
                                    {
                                        _voiceChannelWorker.EnqueueSong(closestMatch.Id);
                                        _voiceChannelWorker.Say("I have added your song, " + closestMatch.Title + " to the queue");
                                        Console.WriteLine("Added " + closestMatch.Title + " to queue");
                                    }
                                }
                                else if (text.ToLower() == "what song is this")
                                {
                                    _voiceChannelWorker.Say("I am currently playing " + _voiceChannelWorker?.CurrentSong.Title);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
                _voiceChannelWorker.Say("Goodbye, " + _user.Nickname ?? _user.Username);
            }
            finally
            {
                Console.WriteLine("Stopping voice assistant runner for " + _user.Username);
            }
        }
    }
}
