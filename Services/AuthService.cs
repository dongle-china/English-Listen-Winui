using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using Supabase.Gotrue;

namespace English_Listen_WinUI.Services
{
    public class AuthService
    {
        private readonly SupabaseService _supabase = SupabaseService.Instance;

        public User? CurrentUser => _supabase.CurrentUser;
        public bool IsLoggedIn => _supabase.IsLoggedIn;

        public async Task<AuthResult> SignUpAsync(string email, string password, string? nickname = null)
        {
            try
            {
                var client = _supabase.Client;

                Session? response;
                if (nickname != null)
                {
                    var attributes = new SignUpOptions
                    {
                        Data = new Dictionary<string, object> { { "nickname", nickname } }
                    };
                    response = await client.Auth.SignUp(email, password, attributes);
                }
                else
                {
                    response = await client.Auth.SignUp(email, password);
                }

                if (response?.User != null)
                {
                    Debug.WriteLine("[Auth] SignUp success: " + response.User.Id);
                    return new AuthResult { Success = true, User = response.User };
                }

                return new AuthResult { Success = false, ErrorMessage = "注册失败" };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Auth] SignUp error: " + ex.Message);
                var msg = ex.Message.Contains("already")
                    ? "该邮箱已注册"
                    : ex.Message.Contains("fetch") || ex.Message.Contains("network")
                        ? "网络连接失败，请检查网络"
                        : "注册失败: " + ex.Message;
                return new AuthResult { Success = false, ErrorMessage = msg };
            }
        }

        public async Task<AuthResult> SignInAsync(string email, string password)
        {
            try
            {
                var client = _supabase.Client;
                var response = await client.Auth.SignIn(email, password);

                if (response?.User != null)
                {
                    Debug.WriteLine("[Auth] SignIn success: " + response.User.Id);
                    return new AuthResult { Success = true, User = response.User };
                }

                return new AuthResult { Success = false, ErrorMessage = "登录失败" };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Auth] SignIn error: " + ex.Message);
                var msg = ex.Message.Contains("invalid_credentials")
                    ? "邮箱或密码错误"
                    : ex.Message.Contains("fetch") || ex.Message.Contains("network")
                        ? "网络连接失败，请检查网络"
                        : "登录失败: " + ex.Message;
                return new AuthResult { Success = false, ErrorMessage = msg };
            }
        }

        public async Task SignOutAsync()
        {
            try
            {
                await _supabase.Client.Auth.SignOut();
                Debug.WriteLine("[Auth] SignOut success");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Auth] SignOut error: " + ex.Message);
            }
        }

        public async Task<bool> RestoreSessionAsync()
        {
            try
            {
                _supabase.Client.Auth.LoadSession();
                var user = _supabase.Client.Auth.CurrentUser;
                if (user != null)
                {
                    Debug.WriteLine("[Auth] Session restored for " + user.Email);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Auth] RestoreSession error: " + ex.Message);
                return false;
            }
        }

        public async Task<SupabaseProfile?> GetProfileAsync()
        {
            try
            {
                var user = CurrentUser;
                if (user == null) return null;

                var client = _supabase.Client;
                var userId = Guid.Parse(user.Id!);
                var result = await client.From<SupabaseProfile>()
                    .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId.ToString())
                    .Get();

                return result.Models?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Auth] GetProfile error: " + ex.Message);
                return null;
            }
        }

    }

    public class AuthResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public User? User { get; set; }
    }
}
