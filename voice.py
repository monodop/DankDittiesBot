import discord
import json
import youtube_dl
import os
import subprocess
import random
import requests
import sys
import shutil

with open("secrets.json", "r") as file:
    secrets = json.loads(file.read())

def download(url, name):
    filename = name + ".mp3"
    tmp_dir = "tmp/sc_" + name
    if os.path.exists(filename):
        os.remove(filename)

    if 'youtube.com' in url or 'youtu.be' in url:
        if os.path.exists(tmp_dir):
            shutil.rmtree(tmp_dir)
        os.makedirs(tmp_dir)

        tmp_filename = tmp_dir + "/" + name + ".mp3"

        ydl_opts = {
            'format': 'bestaudio/best',
            'postprocessors': [{
                'key': 'FFmpegExtractAudio',
                'preferredcodec': 'mp3',
                'preferredquality': '192',
            }],
            'outtmpl': tmp_dir + "/" + name + '.%(ext)s',
        }
        with youtube_dl.YoutubeDL(ydl_opts) as ydl:
            ydl.download([url])

        os.rename(tmp_filename, filename)
        os.rmdir(tmp_dir)
        
        return filename
    
    if 'soundcloud.com' in url:
        if os.path.exists(tmp_dir):
            shutil.rmtree(tmp_dir)
        os.makedirs(tmp_dir)

        subprocess.call("soundscrape " + url, cwd=tmp_dir, shell=True)
        tmp_filename = tmp_dir + "/" + os.listdir(tmp_dir)[0]
        os.rename(tmp_filename, filename)
        
        os.rmdir(tmp_dir)
        return filename

    return None

domain_whitelist = [
    "youtube.com",
    "youtu.be",
    "soundcloud.com"
]

def get_posts(page_size, limit):
    links = []
    offset = None
    prevResults = 1

    while len(links) < limit and prevResults > 0:
        url = 'https://api.pushshift.io/reddit/search/submission/' + \
            '?subreddit=dankditties&sort=desc&sort_type=created_utc' + \
            '&size=' + str(page_size)

        if offset is not None:
            url += '&before=' + str(offset)

        print(url)
        response = requests.get(url)
        response_data = response.json()["data"]
        prevResults = len(response_data)
        i = 0
        for d in response_data:
            i += 1
            print(d["url"])
            print(d["created_utc"])
            print(i, prevResults)
            url = d["url"]
            offset = d["created_utc"]

            if d["domain"] not in domain_whitelist:
                print("domain not in whitelist. skipping")
                continue

            if url not in links:
                links.append(url)

    return links

# download("https://soundcloud.com/nbgmusicyt/skrill-hill", "out")
# exit()

class MyClient(discord.Client):

    def __init__(self):
        print("Initializing")
        self.current_url = None
        self.queue = []
        self.urls = get_posts(100, 10000)
        self.recently_played = []
        print("Initialized")
        super().__init__()

    async def on_ready(self):
        print('Logged on as {0}!'.format(self.user))

    async def on_message(self, message):
        if message.author.voice is not None and message.content == "!dd start":
            channel = message.author.voice.channel
            print("Joining voice chat: " + str(channel))
            self.play_next(await self.get_voice_client(channel))

        elif message.author.voice is not None and message.content == "!dd skip":
            channel = message.author.voice.channel
            self.skip(await self.get_voice_client(channel))
        
        elif message.content == "!dd info":
            await message.channel.send("Currently playing: " + self.current_url)
        
        elif message.content.startswith("!dd play "):
            url = message.content[len("!dd play "):].strip()
            self.queue.append(url)
            await message.channel.send("Your song has been enqueued")
    
    async def get_voice_client(self, default_channel=None):
        if len(self.voice_clients) > 0:
            return self.voice_clients[0]
        elif default_channel is not None:
            return await default_channel.connect()
        return None
    
    def get_next_url(self):
        
        url = None
        while True:
            if len(self.queue) == 0:
                url = random.choice(self.urls)
            else:
                url = self.queue[0]
                self.queue = self.queue[1:]

            if url not in self.recently_played:
                break
        
        self.recently_played.append(url)
        if len(self.recently_played) > 20:
            self.recently_played = self.recently_played[1:]

        return url
    
    def play_next(self, voice_client):
        # voice_client = await self.get_voice_client(default_channel)
        # voice_client.volume = 100

        if not voice_client.is_playing():
            while True:
                try:
                    url = self.get_next_url()
                    self.current_url = url
                    print ("Playing " + url)
                    filename = download(url, "out")
                    
                    if filename:
                        voice_client.play(discord.FFmpegPCMAudio(filename), after=lambda e: self.play_next(voice_client))
                        break
                except Exception as e:
                    print(e)
                    print("Oh fuck, the thing failed for some reason")
    
    def skip(self, voice_client):
        if voice_client.is_playing():
            voice_client.stop()


client = MyClient()
client.run(secrets["discordApiKey"])