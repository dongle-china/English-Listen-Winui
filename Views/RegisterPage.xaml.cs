using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using English_Listen_WinUI.Services;
using System;
using System.Diagnostics;

namespace English_Listen_WinUI.Views
{
    public sealed partial class RegisterPage : Page
    {
        private readonly AuthService _authService = new();

        public RegisterPage()
        {
            this.InitializeComponent();
        }

        private async void SignUpBtn_Click(object sender, RoutedEventArgs e)
        {
            var email = EmailBox.Text?.Trim();
            var nickname = NicknameBox.Text?.Trim();
            var password = PasswordBox.Password;
            var confirm = ConfirmPasswordBox.Password;

            if (string.IsNullOrEmpty(email))
            {
                MainWindow.ShowNotification("请输入邮箱");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                MainWindow.ShowNotification("请输入密码");
                return;
            }

            if (password.Length < 6)
            {
                MainWindow.ShowNotification("密码至少6位");
                return;
            }

            if (password != confirm)
            {
                MainWindow.ShowNotification("两次输入的密码不一致");
                return;
            }

            SignUpBtn.IsEnabled = false;

            var result = await _authService.SignUpAsync(email, password, string.IsNullOrEmpty(nickname) ? null : nickname);

            if (result.Success)
            {
                RegisterFormBorder.Visibility = Visibility.Collapsed;
                SuccessBorder.Visibility = Visibility.Visible;
            }
            else
            {
                MainWindow.ShowNotification($"注册失败: {result.ErrorMessage}");
            }

            SignUpBtn.IsEnabled = true;
        }

        private void GoToLogin_Click(object sender, RoutedEventArgs e)
        {
            MainWindow.GoToPage(typeof(CloudPage));
        }
    }
}
