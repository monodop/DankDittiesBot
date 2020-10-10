using DankDitties.Audio;
using DankDitties.Data;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                var entryDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                string[] keywordFiles;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    keywordFiles = new string[] {
                        Path.Join(entryDirectory, "picovoice_windows.ppn"),
                        Path.Join(entryDirectory, "porcupine_windows.ppn"),
                        Path.Join(entryDirectory, "bumblebee_windows.ppn"),
                    };
                }
                else
                {
                    keywordFiles = new string[] {
                        Path.Join(entryDirectory, "alexa_linux.ppn"),
                        Path.Join(entryDirectory, "porcupine_linux.ppn"),
                        Path.Join(entryDirectory, "snowboy_linux.ppn"),
                        Path.Join(entryDirectory, "dank_ditties_linux.ppn"),
                    };
                }

                using var porcupine = new Porcupine(
                    Path.Join(entryDirectory, "porcupine_params.pv"),
                    keywordFiles,
                    keywordFiles.Select(_ => 0.5f).ToArray()
                );

                var userStream = _user.AudioStream;
                if (userStream == null)
                    return;

                var picoFrameLength = porcupine.FrameLength();
                var picoSampleRate = porcupine.SampleRate();
                var picoBuffer = new short[picoFrameLength];

                await using var clip = new StreamClip(userStream).UseMemoryBuffer(3840);
                await clip.PrepareAsync();

                await using var monoClip = clip.Downsample(96000, 48000);
                await monoClip.PrepareAsync();

                await using var downsampledClip = monoClip.Downsample(48000, picoSampleRate);
                await downsampledClip.PrepareAsync();

                while (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("Waiting for wake word");
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await downsampledClip.ReadAsync(picoBuffer, 0, picoFrameLength, cancellationToken);

                        var status = porcupine.Process(picoBuffer);
                        if (status != -1)
                        {
                            break;
                        }
                    }

                    Console.WriteLine("Wake word detected");
                    clip.Seek(-picoFrameLength * 6 * (48000 / picoSampleRate), SeekOrigin.Current);

                    try
                    {
                        var data = await _witAiClient.ParseAudioStream(monoClip, cancellationToken);
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
                                    }
                                    else
                                    {
                                        var matches = from post in await _metadataManager.GetReadyToPlayMetadataAsync()
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
                                }
                                else if (text.ToLower() == "what song is this" || text.ToLower() == "what's playing" || text.ToLower() == "song" || text.ToLower() == "song name" || text.ToLower() == "damn son whered you find this")
                                {
                                    _voiceChannelWorker.Say("I am currently playing " + _voiceChannelWorker?.CurrentSong.Title);
                                }
                                else
                                {
                                    //_voiceChannelWorker.Say("I'm sorry, I didn't understand that!");
                                    _voiceChannelWorker.Say(text);
                                }
                            }
                            else
                            {
                                _voiceChannelWorker.Say("I'm sorry, I didn't understand that!");
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
