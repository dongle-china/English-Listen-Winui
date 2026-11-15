using System;
using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace English_Listen_WinUI.Models
{
    [Table("test_results")]
    public class CloudTestResult : BaseModel
    {
        [PrimaryKey("id")]
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [Column("user_id")]
        [JsonPropertyName("user_id")]
        public Guid UserId { get; set; }

        [Column("timestamp")]
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [Column("total_words")]
        [JsonPropertyName("total_words")]
        public int TotalWords { get; set; }

        [Column("correct_count")]
        [JsonPropertyName("correct_count")]
        public int CorrectCount { get; set; }

        [Column("accuracy")]
        [JsonPropertyName("accuracy")]
        public double Accuracy { get; set; }

        [Column("word_list_name")]
        [JsonPropertyName("word_list_name")]
        public string WordListName { get; set; } = string.Empty;

        [Column("record_type")]
        [JsonPropertyName("record_type")]
        public string RecordType { get; set; } = "dictation";

        [Column("word_details")]
        [JsonPropertyName("word_details")]
        public string? WordDetailsJson { get; set; }
    }
}
