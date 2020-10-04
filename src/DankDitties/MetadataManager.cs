using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Linq;
using LiteDB.Async;
using System.Threading.Tasks;

namespace DankDitties
{
    public class MetadataManager : IDisposable
    {
        private readonly LiteDatabaseAsync _db;

        public MetadataManager(string filename)
        {
            _db = new LiteDatabaseAsync($"Filename={filename};Connection=shared");
        }

        private async Task<LiteCollectionAsync<Metadata>> _getMetadataCollection()
        {
            var collection = _db.GetCollection<Metadata>("metadata");
            await collection.EnsureIndexAsync(x => x.Id);
            await collection.EnsureIndexAsync(x => x.RedditId);
            await collection.EnsureIndexAsync(x => x.Type);
            await collection.EnsureIndexAsync(x => x.Url);
            return collection;
        }

        public async Task<Metadata> AddRedditPostAsync(string redditId, string url)
        {
            // Check for existing reddit post
            var match = await GetOneMetadataAsync(m => m.Type == MetadataType.Reddit && m.RedditId == redditId);
            if (match != null)
                return match;

            var collection = await _getMetadataCollection();
            var metadata = new Metadata()
            {
                Id = Guid.NewGuid().ToString(),
                Type = MetadataType.Reddit,
                RedditId = redditId,
                Url = url,
            };

            await collection.InsertAsync(metadata);

            return metadata;
        }

        public async Task<Metadata> AddUserRequestAsync(string url, string submittedBy)
        {
            // Check for existing entry
            var match = await GetOneMetadataAsync(m => m.Url == url);
            if (match != null)
                return match;

            var collection = await _getMetadataCollection();
            var metadata = new Metadata()
            {
                Id = Guid.NewGuid().ToString(),
                Type = MetadataType.UserRequested,
                Title = "Ad Hoc Queue'd Video Submitted by " + submittedBy,
                Url = url,
                SubmittedBy = submittedBy,
            };

            await collection.InsertAsync(metadata);

            return metadata;
        }

        public async Task UpdateAsync(Metadata metadata)
        {
            var collection = await _getMetadataCollection();
            await collection.UpdateAsync(metadata);
        }

        public async Task<IList<Metadata>> GetMetadataAsync(Expression<Func<Metadata, bool>> expression)
        {
            var collection = await _getMetadataCollection();
            return await collection
                .Query()
                .Where(expression)
                .ToListAsync();
        }

        public async Task<IList<Metadata>> GetMetadataAsync(
            Expression<Func<Metadata, bool>> expression,
            int limit)
        {
            var collection = await _getMetadataCollection();
            return await collection
                .Query()
                .Where(expression)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<Metadata> GetOneMetadataAsync(Expression<Func<Metadata, bool>> expression)
        {
            return (await GetMetadataAsync(expression, limit: 1)).FirstOrDefault();
        }

        public Task<Metadata> GetMetadataAsync(string id)
        {
            return GetOneMetadataAsync(m => m.Id == id);
        }

        public Task<IList<Metadata>> GetReadyToPlayMetadataAsync()
        {
            return GetMetadataAsync(m => m.IsApproved && m.AudioCacheFilename != null);
        }

        public void Dispose()
        {
            _db.Dispose();
        }
    }

    public enum MetadataType
    {
        Reddit,
        UserRequested,
    }

    public class Metadata
    {
        public string Id { get; set; }

        public MetadataType Type { get; set; }

        public string Title { get; set; }
        public string Url { get; set; }
        public string SubmittedBy { get; set; }
        public string AudioCacheFilename { get; set; }

        public bool DownloadFailed { get; set; }
        public bool IsApproved { get; set; }
        public DateTime? LastRefresh { get; set; }

        // Reddit-Specific info
        public string RedditId { get; set; }
        public bool IsNsfw { get; set; }
        public string LinkFlairText { get; set; }
        public string Subreddit { get; set; }
    }
}
