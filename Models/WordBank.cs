using System;
using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace English_Listen_WinUI.Models
{
    [Table("wordbanks")]
    public class WordBank : BaseModel
    {
        [PrimaryKey("id")]
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        [JsonPropertyName("user_id")]
        public Guid UserId { get; set; }

        [Column("name")]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [Column("file_url")]
        [JsonPropertyName("file_url")]
        public string FileUrl { get; set; } = string.Empty;

        [Column("file_size")]
        [JsonPropertyName("file_size")]
        public long FileSize { get; set; }

        [Column("word_count")]
        [JsonPropertyName("word_count")]
        public int WordCount { get; set; }

        [Column("uploaded_at")]
        [JsonPropertyName("uploaded_at")]
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        [Column("description")]
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [Column("content_hash")]
        [JsonPropertyName("content_hash")]
        public string ContentHash { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonIgnore]
        public string WordCountText => WordCount + " 词";
    }
}
