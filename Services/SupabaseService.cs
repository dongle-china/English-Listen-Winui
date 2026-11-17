using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace English_Listen_WinUI.Services
{
    public class SupabaseService
    {
        private static SupabaseService? _instance;
        private static readonly object _lock = new();

        private Supabase.Client? _client;
        private bool _initialized;

        private string _baseUrl = "";
        private string _anonKey = "";

        public static SupabaseService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new SupabaseService();
                    }
                }
                return _instance;
            }
        }

        public Supabase.Client Client => _client ?? throw new InvalidOperationException("Supabase client not initialized. Call InitializeAsync first.");

        public bool IsInitialized => _initialized;

        private SupabaseService() { }

        public async Task InitializeAsync()
        {
            if (_initialized) return;

            try
            {
                var (url, anonKey) = LoadCredentials();
                _baseUrl = url;
                _anonKey = anonKey;

                _client = new Supabase.Client(url, anonKey, new Supabase.SupabaseOptions
                {
                    AutoRefreshToken = true,
                    SessionHandler = new SupabaseSessionHandler()
                });

                await _client.InitializeAsync();
                _initialized = true;

                Debug.WriteLine("[Supabase] Client initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Supabase] Initialize failed: " + ex.Message);
                throw;
            }
        }

        public User? CurrentUser => _client?.Auth?.CurrentUser;

        public bool IsLoggedIn => CurrentUser != null;

        public string BaseUrl => _baseUrl;
        public string AnonKey => _anonKey;

        public string GetAuthApiUrl() => $"{_baseUrl}/auth/v1";

        private static (string url, string anonKey) LoadCredentials()
        {
            try
            {
                var basePath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                var secretPath = System.IO.Path.Combine(basePath, "config", "secret.json");
                if (System.IO.File.Exists(secretPath))
                {
                    var json = System.IO.File.ReadAllText(secretPath);
                    var config = JsonSerializer.Deserialize<SecretConfig>(json);
                    if (config?.Supabase != null && !string.IsNullOrEmpty(config.Supabase.Url))
                    {
                        return (config.Supabase.Url, config.Supabase.AnonKey);
                    }
                }
            }
            catch { }

            return (
                "https://gpthbupoyolrowazdexm.supabase.co",
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImdwdGhidXBveW9scm93YXpkZXhtIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTQxNzE3MDgsImV4cCI6MjEwOTc0NzcwOH0.Sq2Lexefz2b35_yoOqENfslyfZu2UDizrIxOjl75q1c"
            );
        }
    }

    public class SupabaseSessionHandler : IGotrueSessionPersistence<Session>
    {
        public void SaveSession(Session session)
        {
            try
            {
                var json = JsonSerializer.Serialize(session);
                var path = GetSessionPath();
                System.IO.File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Session] Save failed: " + ex.Message);
            }
        }

        public void DestroySession()
        {
            try
            {
                var path = GetSessionPath();
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch { }
        }

        public Session? LoadSession()
        {
            try
            {
                var path = GetSessionPath();
                if (!System.IO.File.Exists(path)) return null;

                var json = System.IO.File.ReadAllText(path);
                return JsonSerializer.Deserialize<Session>(json);
            }
            catch
            {
                return null;
            }
        }

        private static string GetSessionPath()
        {
            return System.IO.Path.Combine(
                Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                "session.json");
        }
    }
}
