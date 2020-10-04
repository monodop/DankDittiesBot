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
    "linkFlairText": submission.link_flair_text,
    "isRobotIndexable": submission.is_robot_indexable,
    "nsfw": submission.over_18,
    "author": str(submission.author),
    "subreddit": str(submission.subreddit),
}
j = json.dumps(data, indent=2)
print(j)