using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// jetcpp.ccwu.cc Web API 客户端（设备登录）。
    /// 流程：网页端 /device 生成 8 位一次性登录码（5 分钟有效）→
    /// 桌面端 ExchangeCodeAsync 换 30 天 JWT（jti 指向 devices 行，网页端可吊销）。
    /// 资料/改密走 /api/auth/*，云数据走 /api/data/{key} KV 端点（上限 10MB）。
    /// </summary>
    public sealed class WebApiService
    {
        public const string BaseUrl = "https://jetcpp.ccwu.cc";

        private static WebApiService? _instance;
        public static WebApiService Instance => _instance ??= new WebApiService();

        /// <summary>登录状态变化通知（登录/登出/会话恢复/凭证被吊销），UI 层订阅后刷新。</summary>
        public static event Action? LoginStateChanged;

        public static void RaiseLoginStateChanged() => LoginStateChanged?.Invoke();

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        private string? _token;
        private string? _deviceId;
        private long _expiresAtUnix;

        public string? UserId { get; private set; }
        public string? Email { get; private set; }
        public string? Nickname { get; private set; }
        public string? AvatarUrl { get; private set; }

        /// <summary>已登录且 JWT 未过期（提前 5 分钟视为过期，触发重新登录）。</summary>
        public bool IsLoggedIn =>
            !string.IsNullOrEmpty(_token) &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() < _expiresAtUnix - 300;

        // ---------------- 登录码换凭证 ----------------

        /// <summary>用网页端生成的登录码换取 30 天设备凭证。</summary>
        public async Task<WebAuthResult> ExchangeCodeAsync(string code, string? deviceName = null)
        {
            var normalized = (code ?? "").Trim().ToUpperInvariant();
            if (normalized.Length != 8)
                return new WebAuthResult { ErrorMessage = "请输入 8 位登录码" };

            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    code = normalized,
                    deviceName = string.IsNullOrWhiteSpace(deviceName) ? Environment.MachineName : deviceName,
                });
                var (status, text) = await SendAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/devices/token")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
                if (status != 200)
                    return new WebAuthResult { ErrorMessage = ReadError(status, text) };

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                SetSession(
                    root.GetProperty("token").GetString() ?? "",
                    root.TryGetProperty("deviceId", out var d) ? d.GetString() : null,
                    root.GetProperty("user"));
                SaveState();
                RaiseLoginStateChanged();
                return new WebAuthResult { Success = true };
            }
            catch (HttpRequestException)
            {
                return new WebAuthResult { ErrorMessage = "网络连接失败，请检查网络" };
            }
            catch (Exception ex)
            {
                return new WebAuthResult { ErrorMessage = "登录失败: " + ex.Message };
            }
        }

        /// <summary>启动时恢复本地保存的会话；返回是否仍有效。</summary>
        public Task<bool> RestoreSessionAsync()
        {
            LoadState();
            return Task.FromResult(IsLoggedIn);
        }

        /// <summary>登出：吊销当前设备（尽力而为）并清除本地凭证。</summary>
        public async Task SignOutAsync()
        {
            if (IsLoggedIn && !string.IsNullOrEmpty(_deviceId))
            {
                try
                {
                    await SendAsync(MakeRequest(HttpMethod.Delete,
                        "/api/devices/" + Uri.EscapeDataString(_deviceId)));
                }
                catch
                {
                    // 吊销失败不阻塞登出，本地凭证仍会清除
                }
            }
            ClearState();
            RaiseLoginStateChanged();
        }

        // ---------------- 资料 / 密码 ----------------

        /// <summary>拉取最新资料（GET /api/auth/me），刷新昵称/头像/邮箱。</summary>
        public async Task<bool> RefreshProfileAsync()
        {
            if (!IsLoggedIn) return false;
            try
            {
                var (status, text) = await SendAsync(MakeRequest(HttpMethod.Get, "/api/auth/me"));
                if (status == 401) { HandleUnauthorized(); return false; }
                if (status != 200) return false;

                using var doc = JsonDocument.Parse(text);
                var user = doc.RootElement.GetProperty("user");
                UserId = GetString(user, "id");
                Email = GetString(user, "email");
                Nickname = GetString(user, "nickname");
                AvatarUrl = GetString(user, "avatarUrl");
                SaveState();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 更新资料（PATCH /api/auth/me：昵称 2-24 字，头像须 http(s) 链接或空）。
        /// 返回 null 表示成功，否则返回错误信息。
        /// </summary>
        public async Task<string?> UpdateProfileAsync(string nickname, string? avatarUrl)
        {
            var nick = nickname?.Trim() ?? "";
            if (nick.Length < 2 || nick.Length > 24)
                return "昵称长度需在 2-24 个字符之间";
            if (!string.IsNullOrEmpty(avatarUrl) &&
                !(avatarUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                  avatarUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return "头像需为 http(s) 链接";

            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    nickname = nick,
                    avatarUrl = string.IsNullOrEmpty(avatarUrl) ? null : avatarUrl,
                });
                var (status, text) = await SendAsync(new HttpRequestMessage(HttpMethod.Patch, BaseUrl + "/api/auth/me")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
                if (status == 401) { HandleUnauthorized(); return "登录已失效，请重新登录"; }
                if (status != 200) return ReadError(status, text);

                using var doc = JsonDocument.Parse(text);
                var user = doc.RootElement.GetProperty("user");
                Nickname = GetString(user, "nickname");
                AvatarUrl = GetString(user, "avatarUrl");
                SaveState();
                RaiseLoginStateChanged();
                return null;
            }
            catch (HttpRequestException)
            {
                return "网络连接失败，请检查网络";
            }
            catch (Exception ex)
            {
                return "保存失败: " + ex.Message;
            }
        }

        /// <summary>
        /// 修改密码（POST /api/auth/change-password）。当前密码可留空（第三方登录未设密码的账号）。
        /// 服务端返回的新 token 是网页式 token（无 jti、不可吊销），故忽略，继续使用设备 JWT。
        /// 返回 null 表示成功，否则返回错误信息。
        /// </summary>
        public async Task<string?> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            if (newPassword.Length < 8) return "新密码至少 8 位";

            try
            {
                var body = JsonSerializer.Serialize(new { currentPassword, newPassword });
                var (status, text) = await SendAsync(new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/auth/change-password")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
                if (status == 401) { HandleUnauthorized(); return "登录已失效，请重新登录"; }
                if (status != 200) return ReadError(status, text);
                return null;
            }
            catch (HttpRequestException)
            {
                return "网络连接失败，请检查网络";
            }
            catch (Exception ex)
            {
                return "修改失败: " + ex.Message;
            }
        }

        // ---------------- 云端 KV（云同步数据） ----------------

        /// <summary>读取云端 KV（GET /api/data/{key}）。云端无该数据（404）返回 null；其他错误抛异常。</summary>
        public async Task<string?> GetJsonAsync(string key)
        {
            var (status, body) = await SendAsync(MakeRequest(HttpMethod.Get,
                "/api/data/" + Uri.EscapeDataString(key)));
            if (status == 404) return null;
            if (status == 401) { HandleUnauthorized(); return null; }
            if (status != 200) throw new InvalidOperationException(ReadError(status, body));
            return body;
        }

        /// <summary>写入云端 KV（PUT /api/data/{key}，上限 10MB）。返回 null 表示成功。</summary>
        public async Task<string?> PutJsonAsync(string key, string json)
        {
            var request = MakeRequest(HttpMethod.Put, "/api/data/" + Uri.EscapeDataString(key));
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var (status, body) = await SendAsync(request);
            if (status == 401) { HandleUnauthorized(); return "登录已失效，请重新登录"; }
            if (status != 200) return ReadError(status, body);
            return null;
        }

        // ---------------- 内部实现 ----------------

        private HttpRequestMessage MakeRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, BaseUrl + path);
            if (!string.IsNullOrEmpty(_token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return request;
        }

        private static async Task<(int Status, string Body)> SendAsync(HttpRequestMessage request)
        {
            using var response = await Http.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private static string ReadError(int status, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var error) && error.GetString() is { } message)
                    return message;
            }
            catch (JsonException)
            {
                // 非 JSON 响应体，走通用提示
            }
            return $"请求失败 (HTTP {status})";
        }

        /// <summary>凭证被吊销/过期：清除本地会话并通知 UI。</summary>
        private void HandleUnauthorized()
        {
            ClearState();
            RaiseLoginStateChanged();
        }

        private static string? GetString(JsonElement element, string property)
        {
            if (element.TryGetProperty(property, out var value) && value.GetString() is { } text && text.Length > 0)
                return text;
            return null;
        }

        private void SetSession(string token, string? deviceId, JsonElement user)
        {
            _token = token;
            _deviceId = deviceId;
            _expiresAtUnix = ParseJwtExp(token);
            UserId = GetString(user, "id");
            Email = GetString(user, "email");
            Nickname = GetString(user, "nickname");
            AvatarUrl = GetString(user, "avatarUrl");
        }

        /// <summary>解析 JWT payload 的 exp（Unix 秒）。解析失败返回 0（视为已过期）。</summary>
        private static long ParseJwtExp(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length < 2) return 0;
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                var remainder = payload.Length % 4;
                if (remainder == 2) payload += "==";
                else if (remainder == 3) payload += "=";
                using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
                if (doc.RootElement.TryGetProperty("exp", out var exp))
                    return exp.GetInt64();
            }
            catch (Exception)
            {
                // 坏 token 视为已过期
            }
            return 0;
        }

        private static string StateFilePath
        {
            get
            {
                string appData;
                try { appData = Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
                catch { appData = AppContext.BaseDirectory; }
                return Path.Combine(appData, "config", "web_auth.json");
            }
        }

        private sealed class AuthState
        {
            public string? Token { get; set; }
            public string? DeviceId { get; set; }
            public long ExpiresAt { get; set; }
            public string? UserId { get; set; }
            public string? Email { get; set; }
            public string? Nickname { get; set; }
            public string? AvatarUrl { get; set; }
        }

        private void SaveState()
        {
            try
            {
                var state = new AuthState
                {
                    Token = _token,
                    DeviceId = _deviceId,
                    ExpiresAt = _expiresAtUnix,
                    UserId = UserId,
                    Email = Email,
                    Nickname = Nickname,
                    AvatarUrl = AvatarUrl,
                };
                var path = StateFilePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonSerializer.Serialize(state));
            }
            catch (Exception)
            {
                // 持久化失败不影响当前会话
            }
        }

        private void LoadState()
        {
            try
            {
                var path = StateFilePath;
                if (!File.Exists(path)) return;
                var state = JsonSerializer.Deserialize<AuthState>(File.ReadAllText(path));
                if (state == null || string.IsNullOrEmpty(state.Token)) return;
                _token = state.Token;
                _deviceId = state.DeviceId;
                _expiresAtUnix = state.ExpiresAt > 0 ? state.ExpiresAt : ParseJwtExp(state.Token);
                UserId = state.UserId;
                Email = state.Email;
                Nickname = state.Nickname;
                AvatarUrl = state.AvatarUrl;
            }
            catch (Exception)
            {
                // 损坏的状态文件按未登录处理
                ClearState();
            }
        }

        private void ClearState()
        {
            _token = null;
            _deviceId = null;
            _expiresAtUnix = 0;
            UserId = null;
            Email = null;
            Nickname = null;
            AvatarUrl = null;
            try
            {
                var path = StateFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 删除失败不影响登出
            }
        }
    }

    public sealed class WebAuthResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
