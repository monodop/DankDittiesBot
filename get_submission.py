import praw
import json
import requests
import sys

with open("secrets.json", "r") as file:
    secrets = json.loads(file.read())

reddit = praw.Reddit(
    client_id=secrets["clientId"],
    client_secret=secrets["clientSecret"],
    user_agent=secrets["userAgent"]
    )

submission = reddit.submission(sys.argv[1])
data = {
    "id": submission.id,
    "title": submission.title,
    "hasAuthor": submission.author is not None,
}
j = json.dumps(data, indent=2)
print(j)