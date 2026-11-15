using System.Text.Json.Serialization;

namespace English_Listen_WinUI.Services
{
    public class SecretConfig
    {
        [JsonPropertyName("BaiduTranslate")]
        public BaiduSecretConfig? BaiduTranslate { get; set; }

        [JsonPropertyName("Supabase")]
        public SupabaseSecretConfig? Supabase { get; set; }
    }

    public class SupabaseSecretConfig
    {
        [JsonPropertyName("Url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("AnonKey")]
        public string AnonKey { get; set; } = string.Empty;

    }
}
