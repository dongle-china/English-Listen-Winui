using System;
using System.Text.Json.Serialization;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace English_Listen_WinUI.Models
{
    [Table("profiles")]
    public class SupabaseProfile : BaseModel
    {
        [PrimaryKey("id")]
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [Column("nickname")]
        [JsonPropertyName("nickname")]
        public string? Nickname { get; set; }

        [Column("full_name")]
        [JsonPropertyName("full_name")]
        public string? FullName { get; set; }

        [Column("avatar_url")]
        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [Column("role")]
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user";

        [Column("gender")]
        [JsonPropertyName("gender")]
        public string? Gender { get; set; }

        [Column("banned")]
        [JsonPropertyName("banned")]
        public bool Banned { get; set; }

        [Column("created_at")]
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt { get; set; }

        [Column("updated_at")]
        [JsonPropertyName("updated_at")]
        public DateTime? UpdatedAt { get; set; }
    }
}
