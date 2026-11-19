using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.Services;
using English_Listen_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace English_Listen_WinUI.Views
{
    /// <summary>
    /// 个人中心：资料/编辑资料/修改密码/成绩分析。
    /// 三态：在线账户（设备登录）、本地账户、未登录（离线）。
    /// 编辑资料与修改密码校验对齐 web 端 API 语义。
    /// </summary>
    public sealed partial class ProfilePage : Page
    {
        private readonly MainViewModel _viewModel;

        public ProfilePage()
        {
            this.InitializeComponent();
            _viewModel = App.SharedViewModel!;
            Loaded += ProfilePage_Loaded;
            WebApiService.LoginStateChanged += AuthService_LoginStateChanged;
            Unloaded += (_, _) => WebApiService.LoginStateChanged -= AuthService_LoginStateChanged;
        }

        private void AuthService_LoginStateChanged()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LoadProfile();
                LoadStats();
                _ = LoadSentenceStatsAsync();
            });
        }

        private void ProfilePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadProfile();
            LoadStats();
            _ = LoadSentenceStatsAsync();
        }

        // ── 账户状态 ─────────────────────────────────────────

        private void LoadProfile()
        {
            // 状态 1：在线账户（设备登录，jetcpp.ccwu.cc）
            var web = WebApiService.Instance;
            if (web.IsLoggedIn)
            {
                var nickname = string.IsNullOrWhiteSpace(web.Nickname) ? (web.Email ?? "在线账户") : web.Nickname!;

                NicknameText.Text = nickname;
                MetaText.Text = "在线账户";
                EmailText.Text = web.Email ?? "";

                ApplyAvatar(nickname, web.AvatarUrl);

                EditProfileCard.Visibility = Visibility.Visible;
                ChangePasswordCard.Visibility = Visibility.Visible;
                LoginPromptPanel.Visibility = Visibility.Collapsed;
                LogoutButton.Visibility = Visibility.Visible;

                NicknameBox.Text = nickname;
                AvatarUrlBox.Text = web.AvatarUrl ?? "";
                AvatarUrlBox.IsEnabled = true;
                return;
            }

            // 状态 2：本地账户
            var currentUser = _viewModel.Settings?.Settings?.CurrentUser;
            var localUser = _viewModel.Users?.FirstOrDefault(u => u.Username == currentUser);
            if (localUser != null && !string.IsNullOrEmpty(currentUser)
                && currentUser != LoginFlow.OfflineUser)
            {
                NicknameText.Text = string.IsNullOrWhiteSpace(localUser.Nickname)
                    ? localUser.Username
                    : localUser.Nickname;
                MetaText.Text = $"本地账户 · {localUser.Username} · 注册于 {localUser.CreatedTime:yyyy-MM-dd}";
                EmailText.Text = "";

                ApplyAvatar(NicknameText.Text, null);

                // 本地账户：仅支持改昵称，保存到本地
                EditProfileCard.Visibility = Visibility.Visible;
                ChangePasswordCard.Visibility = Visibility.Collapsed;
                LoginPromptPanel.Visibility = Visibility.Collapsed;
                LogoutButton.Visibility = Visibility.Visible;

                NicknameBox.Text = NicknameText.Text;
                AvatarUrlBox.Text = "";
                AvatarUrlBox.IsEnabled = false;
                return;
            }

            // 状态 3：未登录（离线）
            AvatarUrlBox.IsEnabled = true;
            NicknameText.Text = "未登录";
            MetaText.Text = "离线模式，数据保存在本地";
            EmailText.Text = "";
            ApplyAvatar("未", null);
            EditProfileCard.Visibility = Visibility.Collapsed;
            ChangePasswordCard.Visibility = Visibility.Collapsed;
            LoginPromptPanel.Visibility = Visibility.Visible;
            LogoutButton.Visibility = Visibility.Collapsed;
        }

        private void ApplyAvatar(string displayName, string? avatarUrl)
        {
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                var img = new Image
                {
                    Source = new BitmapImage(new Uri(avatarUrl)),
                    Stretch = Stretch.UniformToFill
                };
                AvatarBorder.Child = img;
                AvatarInitialText.Text = "";
                return;
            }

            var initial = displayName.Length > 0 ? char.ToUpperInvariant(displayName[0]).ToString() : "?";
            AvatarBorder.Child = AvatarInitialText;
            AvatarInitialText.Text = initial;
        }

        private void GoLoginButton_Click(object sender, RoutedEventArgs e)
        {
            // 复用标题栏头像的登录入口
            if (App.MainWindow is MainWindow mw)
            {
                mw.ShowLoginDialogFromProfile();
            }
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (WebApiService.Instance.IsLoggedIn)
            {
                await WebApiService.Instance.SignOutAsync();
            }
            else
            {
                // 本地账户退出
                if (_viewModel?.Settings?.Settings != null)
                {
                    _viewModel.Settings.Settings.CurrentUser = null;
                    await _viewModel.Settings.SaveSettingsAsync();
                    WebApiService.RaiseLoginStateChanged();
                }
            }

            MainWindow.ShowNotification("已退出登录");
        }

        // ── 编辑资料 / 修改密码 ──────────────────────────────

        private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var nickname = NicknameBox.Text?.Trim() ?? "";

            // 本地账户：只改昵称，保存到本地
            if (!WebApiService.Instance.IsLoggedIn)
            {
                var currentUser = _viewModel.Settings?.Settings?.CurrentUser;
                var localUser = _viewModel.Users?.FirstOrDefault(u => u.Username == currentUser);
                if (localUser == null) return;

                if (nickname.Length < 2 || nickname.Length > 24)
                {
                    MainWindow.ShowNotification("昵称长度需在 2-24 个字符之间");
                    return;
                }

                localUser.Nickname = nickname;
                if (_viewModel?.Settings != null)
                    await _viewModel.Settings.SaveUsersAsync(_viewModel.Users!.ToList());
                WebApiService.RaiseLoginStateChanged();
                MainWindow.ShowNotification("已保存");
                return;
            }

            // 在线账户：PATCH /api/auth/me（校验对齐 web 端）
            var error = await WebApiService.Instance.UpdateProfileAsync(nickname, AvatarUrlBox.Text?.Trim());
            if (error != null)
            {
                MainWindow.ShowNotification(error);
                return;
            }

            MainWindow.ShowNotification("已保存");
            LoadProfile();
        }

        private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            var newPassword = NewPasswordBox.Password;
            if (newPassword != ConfirmPasswordBox.Password)
            {
                MainWindow.ShowNotification("两次输入的新密码不一致");
                return;
            }

            // 校验对齐 web 端 POST /api/auth/change-password
            var error = await WebApiService.Instance.ChangePasswordAsync(CurrentPasswordBox.Password, newPassword);
            if (error != null)
            {
                MainWindow.ShowNotification(error);
                return;
            }

            CurrentPasswordBox.Password = "";
            NewPasswordBox.Password = "";
            ConfirmPasswordBox.Password = "";
            MainWindow.ShowNotification("密码已更新");
        }

        // ── 成绩分析（原 ProgressPage 统计迁移） ─────────────

        private void LoadStats()
        {
            var history = _viewModel?.TestHistory;
            if (history == null) return;

            TotalTestsText.Text = history.Count.ToString();

            // 排除学习记录（learning），只统计测试
            var testHistory = history.Where(h => h.RecordType != "learning").ToList();
            if (testHistory.Count > 0)
            {
                AvgAccuracyText.Text = $"{testHistory.Average(h => h.Accuracy):F1}%";
                TotalWordsText.Text = history.Sum(h => h.TotalWords).ToString();
                StreakDaysText.Text = CalculateStreak(history).ToString();
                DrawAccuracyChart(testHistory);
            }
            else
            {
                TotalWordsText.Text = history.Sum(h => h.TotalWords).ToString();
                AvgAccuracyText.Text = "-";
                StreakDaysText.Text = CalculateStreak(history).ToString();
                AccuracyChartCanvas.Children.Clear();
            }
        }

        private void DrawAccuracyChart(List<TestResult> history)
        {
            AccuracyChartCanvas.Children.Clear();

            var chartData = ChartService.GenerateAccuracyTrendData(history, 8);
            if (chartData.Count == 0) return;

            double canvasWidth = AccuracyChartCanvas.ActualWidth;
            double canvasHeight = AccuracyChartCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                canvasWidth = 400;
                canvasHeight = 200;
            }

            double padding = 20;
            double chartWidth = canvasWidth - 2 * padding;
            double chartHeight = canvasHeight - 2 * padding;

            double maxValue = chartData.Max(d => d.Value);
            if (maxValue == 0) maxValue = 100;

            var gridBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray);
            var textBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

            for (int i = 0; i <= 4; i++)
            {
                double y = padding + chartHeight - (i * chartHeight / 4);
                AccuracyChartCanvas.Children.Add(new Line
                {
                    X1 = padding,
                    Y1 = y,
                    X2 = padding + chartWidth,
                    Y2 = y,
                    Stroke = gridBrush,
                    StrokeThickness = 0.5
                });

                var label = new TextBlock
                {
                    Text = $"{i * 25}%",
                    FontSize = 10,
                    Foreground = textBrush
                };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                AccuracyChartCanvas.Children.Add(label);
            }

            double pointRadius = 4.0;
            var lineStroke = new SolidColorBrush(Microsoft.UI.Colors.Blue);
            var pointStroke = new SolidColorBrush(Microsoft.UI.Colors.White);

            var points = new List<Point>();
            for (int i = 0; i < chartData.Count; i++)
            {
                double x = padding + (i * chartWidth / Math.Max(1, chartData.Count - 1));
                double y = padding + chartHeight - (chartData[i].Value * chartHeight / maxValue);
                points.Add(new Point(x, y));

                var ellipse = new Ellipse
                {
                    Width = pointRadius * 2,
                    Height = pointRadius * 2,
                    Fill = new SolidColorBrush(chartData[i].Color),
                    Stroke = pointStroke,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(ellipse, x - pointRadius);
                Canvas.SetTop(ellipse, y - pointRadius);
                AccuracyChartCanvas.Children.Add(ellipse);

                var dataLabel = new TextBlock
                {
                    Text = chartData[i].Label,
                    FontSize = 10,
                    Foreground = textBrush,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(dataLabel, x - 20);
                Canvas.SetTop(dataLabel, padding + chartHeight + 5);
                AccuracyChartCanvas.Children.Add(dataLabel);
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                AccuracyChartCanvas.Children.Add(new Line
                {
                    X1 = points[i].X,
                    Y1 = points[i].Y,
                    X2 = points[i + 1].X,
                    Y2 = points[i + 1].Y,
                    Stroke = lineStroke,
                    StrokeThickness = 2
                });
            }
        }

        // ── 句子训练统计（泛听/精听/复习三阶段 + 卡点词） ────

        /// <summary>
        /// 读 training_sessions.json（当前用户）与句子训练历史记录，填充句子训练统计卡。
        /// 提示次数按 HintCount ?? 0 兼容旧记录。
        /// </summary>
        private async Task LoadSentenceStatsAsync()
        {
            var settings = _viewModel?.Settings;
            if (settings == null) return;

            var username = settings.Settings.CurrentUser;
            var sessionService = new TrainingSessionService(settings);
            await sessionService.LoadAsync(username);
            var sessions = sessionService.Sessions.ToList();

            // 卡点词与提示次数来自 test_history 的句子训练记录
            var history = _viewModel?.TestHistory ?? new List<TestResult>();
            var sentenceRecords = history.Where(h => h.RecordType == "sentence").ToList();
            var hintTotal = sentenceRecords.Sum(h => h.HintCount ?? 0);
            var topWords = sentenceRecords
                .Where(h => h.SentenceWords != null)
                .SelectMany(h => h.SentenceWords!)
                .Where(w => !w.Correct)
                .GroupBy(w => w.Expected, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Word: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Word, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            SentenceSourceCountText.Text = sessions.Count.ToString();
            SentenceHintCountText.Text = hintTotal.ToString();

            var totalSentences = sessions.Sum(s => s.Progress.Count);
            var mastered = sessions.Sum(s => s.Progress.Count(p => p.IsMastered));
            SentenceMasteryText.Text = totalSentences == 0 ? "-" : $"{mastered} / {totalSentences}";

            var attempted = sessions.Sum(s => s.Progress.Count(p => p.Attempts > 0));
            IntensiveProgressText.Text = totalSentences == 0 ? "-" : $"{attempted} / {totalSentences}";

            var extAvg = sessions.Count == 0 ? 0
                : sessions.Average(s => s.Progress.Count == 0 ? 0
                    : (double)Math.Min(s.ExtensivePlayedCount, s.Progress.Count) / s.Progress.Count);
            ExtensiveProgressSummaryText.Text = $"泛听完成度 {extAvg:P0}";
            ExtensiveProgressSummaryBar.Value = extAvg * 100;

            TopWordsText.Text = topWords.Count == 0
                ? "暂无错误记录"
                : string.Join("   ", topWords.Select(w => $"{w.Word} ×{w.Count}"));

            SentenceStatsCard.Visibility = sessions.Count == 0 && sentenceRecords.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private static int CalculateStreak(List<TestResult> history)
        {
            if (history.Count == 0) return 0;

            var dates = history
                .Select(h => h.Timestamp.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            int streak = 1;
            var today = DateTime.Today;

            if (dates[0] != today && dates[0] != today.AddDays(-1))
                return 0;

            for (int i = 1; i < dates.Count; i++)
            {
                if (dates[i - 1].AddDays(-1) == dates[i])
                {
                    streak++;
                }
                else
                {
                    break;
                }
            }

            return streak;
        }
    }
}
