using System;
using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace English_Listen_WinUI.Models
{
    [Table("cloud_data")]
    public class CloudSyncData : BaseModel
    {
        [PrimaryKey("id")]
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        [JsonPropertyName("user_id")]
        public Guid UserId { get; set; }

        [Column("data_type")]
        [JsonPropertyName("data_type")]
        public string DataType { get; set; } = string.Empty;

        [Column("data_json")]
        [JsonPropertyName("data_json")]
        public string DataJson { get; set; } = string.Empty;

        [Column("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
