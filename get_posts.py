#import praw
import json
import requests

# with open("secrets.json", "r") as file:
#     secrets = json.loads(file.read())

# reddit = praw.Reddit(
#     client_id=secrets["clientId"],
#     client_secret=secrets["clientSecret"],
#     user_agent=secrets["userAgent"]
#     )

# for submission in reddit.subreddit("dankditties").hot(limit=10):
#     print(submission.title)

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

print(json.dumps(get_posts(100, 10000), indent=2))