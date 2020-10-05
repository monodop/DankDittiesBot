using LiteDB.Async;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DankDitties.Data
{
    public class PlayHistoryManager : IDisposable
    {
        private readonly LiteDatabaseAsync _db;

        public PlayHistoryManager(LiteDatabaseAsync db)
        {
            _db = db;
        }

        private async Task<LiteCollectionAsync<PlayHistory>> _getPlayHistoryCollection()
        {
            var collection = _db.GetCollection<PlayHistory>("play_history");
            await collection.EnsureIndexAsync(x => x.Id);
            await collection.EnsureIndexAsync(x => x.VoiceChannelId);
            await collection.EnsureIndexAsync(x => x.MetadataId);
            return collection;
        }

        public async Task RecordSongPlay(ulong voiceChannelId, string metadataId)
        {
            var collection = await _getPlayHistoryCollection();
            var playHistory = new PlayHistory()
            {
                VoiceChannelId = voiceChannelId,
                MetadataId = metadataId,
                DateLastPlayed = DateTime.UtcNow,
            };
            await collection.UpsertAsync(playHistory);
        }

        public async Task<IList<PlayHistory>> GetPlayHistory(Expression<Func<PlayHistory, bool>> expression)
        {
            var collection = await _getPlayHistoryCollection();
            return await collection
                .Query()
                .Where(expression)
                .ToListAsync();
        }

        public void Dispose()
        {

        }
    }

    public class PlayHistory
    {
        public string Id => $"{VoiceChannelId}.{MetadataId}";
            
        public ulong VoiceChannelId { get; set; }
        public string MetadataId { get; set; }
        public DateTime DateLastPlayed { get; set; }
    }
}
