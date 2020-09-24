using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DankDitties
{
    public class MetadataManager : IDisposable
    {
        private readonly string _filename;
        private Metadata _metadata;

        private object _lockObj = new object();
        public IEnumerable<PostMetadata> Posts {
            get
            {
                lock (_lockObj)
                {
                    return _metadata.Posts.Values.ToList();
                }
            }
        }

        public MetadataManager(string filename)
        {
            _filename = filename;
            _reload();
        }

        private void _reload()
        {
            if (File.Exists(_filename))
                _metadata = JsonConvert.DeserializeObject<Metadata>(File.ReadAllText(_filename));

            else
                _metadata = new Metadata();
        }

        public void Save()
        {
            File.WriteAllText(_filename, JsonConvert.SerializeObject(_metadata, Formatting.Indented));
        }

        public bool HasRecord(string id)
        {
            return _metadata.Posts.ContainsKey(id);
        }

        public PostMetadata GetRecord(string id)
        {
            return _metadata.Posts[id];
        }

        public void AddRecord(string id, PostMetadata data)
        {
            if (HasRecord(id))
                throw new InvalidOperationException("Record already exists");

            _metadata.Posts[id] = data;
            Save();
        }

        public void Dispose()
        {
            Save();
        }
    }
}
