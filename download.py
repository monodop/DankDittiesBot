import discord
import json
import youtube_dl
import os
import subprocess
import random
import requests
import sys
import shutil

# with open("secrets.json", "r") as file:
#     secrets = json.loads(file.read())

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

result = download(sys.argv[1], sys.argv[2])
if result is None:
    exit(1)

print("Output file saved to " + result)