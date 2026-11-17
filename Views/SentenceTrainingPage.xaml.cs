using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace English_Listen_WinUI.Views
{
    /// <summary>逐词输入项：输入、期待词、提交后校验状态。</summary>
    public class SentenceWordItem
    {
        private static readonly Brush OkBrush = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private static readonly Brush BadBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);

        public string Expected { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
        public bool Locked { get; set; }
        public bool? Correct { get; set; }

        private string? _placeholder;

        /// <summary>按期待词长度给输入框最小宽度提示。</summary>
        public string Placeholder => _placeholder ??= new string('·', Math.Min(Expected.Length, 16));

        public string Feedback => Correct switch
        {
            true => "✓",
            false => $"✗ 应为 {Expected}",
            _ => string.Empty
        };

        public Brush FeedbackBrush => Correct == false ? BadBrush : OkBrush;

        public Visibility FeedbackVisibility =>
            Correct == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// 句子精听训练页：单句播放、循环、上一句/下一句、逐词输入校验。
    /// 尝试结果以 RecordType="sentence" 写入 test_history.json。
    /// </summary>
    public sealed partial class SentenceTrainingPage : Page
    {
        private readonly SettingsService _settingsService = new();
        private MediaSource? _media;
        private SentencePlayerService? _player;
        private List<SentenceWordItem> _words = new();
        private int _index;
        private bool _submitted;

        public SentenceTrainingPage()
        {
            InitializeComponent();
        }

        private Sentence Current => _media!.Sentences[_index];

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is not MediaSource media || media.Sentences.Count == 0)
            {
                Frame.GoBack();
                return;
            }

            _media = media;
            _player = new SentencePlayerService();
            _player.SegmentEnded += OnSegmentEnded;

            TitleText.Text = $"句子精听 · {media.Title}";
            ShowSentence(autoPlay: true);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _player?.Dispose();
            _player = null;
            base.OnNavigatedFrom(e);
        }

        private void OnSegmentEnded()
        {
            DispatcherQueue.TryEnqueue(UpdatePlayButton);
        }

        private void ShowSentence(bool autoPlay)
        {
            if (_media == null || _player == null) return;

            _submitted = false;
            _words = Current.TranscriptText
                .Split(' ', (StringSplitOptions)StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Any(char.IsLetterOrDigit))
                .Select(w => new SentenceWordItem { Expected = w })
                .ToList();
            WordsPanel.ItemsSource = _words;

            IndexText.Text = $"第 {_index + 1} / {_media.Sentences.Count} 句";

            // 训练阶段状态上报到窗口标题栏
            MainWindow.SetTitleStatus($"句子精听 · 第 {_index + 1} / {_media.Sentences.Count} 句");

            ResultText.Text = string.Empty;
            TranscriptTextBlock.Visibility = Visibility.Collapsed;
            SubmitButton.IsEnabled = _words.Count > 0;

            if (autoPlay)
            {
                PlayCurrent();
            }
            UpdatePlayButton();
        }

        private void PlayCurrent()
        {
            if (_media == null || _player == null) return;
            _player.PlaySegment(_media.FilePath, Current.StartTimeMs, Current.EndTimeMs, LoopSwitch.IsOn);
            UpdatePlayButton();
        }

        private void UpdatePlayButton()
        {
            PlayPauseButton.Content = _player is { IsPlaying: true } ? "暂停" : "播放";
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null) return;
            if (_player.IsPlaying)
            {
                _player.Pause();
            }
            else
            {
                PlayCurrent();
            }
            UpdatePlayButton();
        }

        private void ReplayButton_Click(object sender, RoutedEventArgs e)
        {
            PlayCurrent();
        }

        private void LoopSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            _player?.SetLoop(LoopSwitch.IsOn);
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _index <= 0) return;
            _index--;
            ShowSentence(autoPlay: true);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _index >= _media.Sentences.Count - 1) return;
            _index++;
            ShowSentence(autoPlay: true);
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_submitted || _media == null) return;
            _submitted = true;
            SubmitButton.IsEnabled = false;

            var correct = 0;
            var attempts = new List<SentenceWordAttempt>();
            foreach (var w in _words)
            {
                var ok = Normalize(w.Input).Equals(Normalize(w.Expected), StringComparison.Ordinal);
                w.Correct = ok;
                w.Locked = true;
                if (ok) correct++;
                attempts.Add(new SentenceWordAttempt
                {
                    Expected = w.Expected,
                    Input = w.Input,
                    Correct = ok
                });
            }

            // 重新投影以刷新逐词校验状态
            WordsPanel.ItemsSource = _words.ToList();
            ResultText.Text = $"正确 {correct} / {_words.Count}";
            TranscriptTextBlock.Text = $"原文：{Current.TranscriptText}";
            TranscriptTextBlock.Visibility = Visibility.Visible;

            _ = SaveAttemptAsync(correct, attempts);
        }

        /// <summary>句子级尝试写入 test_history.json（RecordType="sentence"，向后兼容）。</summary>
        private async Task SaveAttemptAsync(int correct, List<SentenceWordAttempt> attempts)
        {
            try
            {
                // 离线模式：未登录时 SettingsService 回退到默认离线用户目录，记录仍本地留存
                if (_media == null) return;

                var user = _settingsService.Settings.CurrentUser;
                var history = await _settingsService.LoadTestHistoryAsync(user ?? "");
                var record = new TestResult
                {
                    Timestamp = DateTime.Now,
                    TotalWords = _words.Count,
                    CorrectCount = correct,
                    Accuracy = _words.Count == 0 ? 0 : Math.Round((double)correct / _words.Count * 100, 1),
                    WordListName = $"句子精听 · {_media.Title}",
                    RecordType = "sentence",
                    MediaSourceId = _media.Id,
                    SentenceIndex = Current.Index,
                    SentenceWords = attempts
                };
                history.Add(record);
                await _settingsService.SaveTestHistoryAsync(user ?? "", history);

                // 已登录时自动上传到云端
                if (Services.SupabaseService.Instance.IsLoggedIn)
                    _ = new Services.TestResultService().UploadResultAsync(record);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SentenceTraining] 保存尝试记录失败: {ex.Message}");
            }
        }

        /// <summary>比较用规范化：小写、仅保留字母数字。</summary>
        private static string Normalize(string s)
        {
            var chars = s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }
    }
}
