using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// <summary>
    /// 逐词输入项：输入、期待词、输入中即时校验、分级提示（L1 首字母 → L2 首字母+长度 → L3 整词）。
    /// INPC 支持不重投 ItemsSource 的单属性刷新（重投会丢输入焦点）。
    /// </summary>
    public class SentenceWordItem : INotifyPropertyChanged
    {
        private static readonly Brush OkBrush = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private static readonly Brush BadBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
        private static readonly Brush MissBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>每次点击提示按钮触发一次（页面累计本句提示使用次数）。</summary>
        public event Action? HintUsed;

        public string Expected
        {
            get => _expected;
            set
            {
                _expected = value;
                _placeholder = new string('·', Math.Min(value.Length, 16));
            }
        }

        private string _expected = string.Empty;
        private string _placeholder = string.Empty;

        private string _input = string.Empty;
        private bool? _correct;
        private bool _locked;
        private int _hintLevel;

        public string Input
        {
            get => _input;
            set
            {
                if (_input == value) return;
                _input = value;
                if (!_locked)
                {
                    // 即时校验：非前缀立即标错；前缀（含空）视为进行中不标
                    var exp = Normalize(Expected);
                    var inp = Normalize(value);
                    _correct = inp.Length > 0 && !exp.StartsWith(inp, StringComparison.Ordinal)
                        ? false
                        : null;
                }
                RaiseAll();
            }
        }

        public bool? Correct => _correct;

        public bool Locked
        {
            get => _locked;
            set
            {
                _locked = value;
                RaiseAll();
            }
        }

        public int HintLevel
        {
            get => _hintLevel;
            set
            {
                _hintLevel = value;
                RaiseAll();
            }
        }

        /// <summary>分级提示文本；未用提示时为空。</summary>
        public string HintText => _hintLevel == 0
            ? string.Empty
            : TrainingSessionService.GetHint(Expected, _hintLevel);

        public bool CanHint => !_locked && _hintLevel < 3;

        public string HintButtonText => _hintLevel == 0 ? "提示" : $"提示 L{_hintLevel}";

        /// <summary>按期待词长度给输入框最小宽度提示（随 Expected 设置时缓存）。</summary>
        public string Placeholder => _placeholder;

        public string Feedback
        {
            get
            {
                if (_locked && string.IsNullOrWhiteSpace(_input)) return "未输入";
                return _correct switch
                {
                    true => "✓",
                    false => _locked ? $"✗ 应为 {Expected}" : "✗",
                    _ => string.Empty
                };
            }
        }

        public Brush FeedbackBrush
        {
            get
            {
                if (_locked && string.IsNullOrWhiteSpace(_input)) return MissBrush;
                return _correct == false ? BadBrush : _correct == true ? OkBrush : MissBrush;
            }
        }

        /// <summary>提交单句：锁定并做最终逐词校验。</summary>
        public void Submit()
        {
            _correct = Normalize(_input).Equals(Normalize(Expected), StringComparison.Ordinal);
            _locked = true;
            RaiseAll();
        }

        /// <summary>提示按钮（x:Bind 事件绑定到词项自身）。</summary>
        public void RequestHint(object sender, RoutedEventArgs e)
        {
            if (!CanHint) return;
            _hintLevel++;
            RaiseAll();
            HintUsed?.Invoke();
        }

        private void RaiseAll()
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            foreach (var name in new[]
            {
                nameof(Input), nameof(Locked), nameof(Correct), nameof(HintLevel),
                nameof(HintText), nameof(CanHint), nameof(HintButtonText),
                nameof(Placeholder), nameof(Feedback), nameof(FeedbackBrush)
            })
            {
                handler.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        /// <summary>比较用规范化：小写、仅保留字母数字。</summary>
        internal static string Normalize(string s)
        {
            var chars = s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }
    }

    /// <summary>
    /// 句子训练页：泛听（整段连播/标记难点）→ 精听（逐句逐词即时校验 + 分级提示）→ 复习（只练未掌握句）。
    /// 会话状态经 TrainingSessionService 持久化；尝试记录以 RecordType="sentence" 写入 test_history.json。
    /// </summary>
    public sealed partial class SentenceTrainingPage : Page
    {
        private readonly SettingsService _settingsService;
        private MediaSource? _media;
        private SentencePlayerService? _player;
        private List<SentenceWordItem> _words = new();
        private int _index;             // 精听：当前句列表位置
        private bool _submitted;
        private int _hintsUsed;         // 本句提示使用次数（提交前累计）

        private TrainingSessionService? _sessionService;
        private TrainingSession? _session;
        private TrainingStage _stage;
        private bool _stageSwitching;   // 防 RadioButton Checked 重入

        private int _extIndex;          // 泛听：当前播放句列表位置
        private bool _extPlaying;

        private List<int> _reviewQueue = new();  // 复习：句子列表位置队列
        private int _reviewPos;

        public SentenceTrainingPage()
        {
            InitializeComponent();
            _settingsService = App.SharedViewModel?.Settings ?? new SettingsService();
        }

        private Sentence Current
        {
            get
            {
                if (_stage == TrainingStage.Review)
                    return _media!.Sentences[_reviewQueue[_reviewPos]];
                return _media!.Sentences[_index];
            }
        }

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

            TitleText.Text = $"句子训练 · {media.Title}";
            _ = InitializeSessionAsync();
        }

        private async Task InitializeSessionAsync()
        {
            if (_media == null) return;

            _sessionService = new TrainingSessionService(_settingsService);
            await _sessionService.LoadAsync(_settingsService.Settings.CurrentUser);
            _session = _sessionService.GetOrCreate(_media);
            SwitchStage(_session.Stage, notify: false);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _player?.Stop();
            _player?.Dispose();
            _player = null;
            base.OnNavigatedFrom(e);
        }

        // ── 阶段切换 ─────────────────────────────────────────

        private static string StageName(TrainingStage s) => s switch
        {
            TrainingStage.Extensive => "泛听",
            TrainingStage.Intensive => "精听",
            _ => "复习"
        };

        private void StageStep_Checked(object sender, RoutedEventArgs e)
        {
            if (_stageSwitching || sender is not RadioButton rb) return;
            var stage = rb.Tag switch
            {
                "Extensive" => TrainingStage.Extensive,
                "Review" => TrainingStage.Review,
                _ => TrainingStage.Intensive
            };
            if (_session != null && stage != _stage) SwitchStage(stage);
        }

        private void SwitchStage(TrainingStage stage, bool notify = true)
        {
            if (_media == null || _session == null || _sessionService == null) return;

            _stageSwitching = true;
            ExtensiveStep.IsChecked = stage == TrainingStage.Extensive;
            IntensiveStep.IsChecked = stage == TrainingStage.Intensive;
            ReviewStep.IsChecked = stage == TrainingStage.Review;
            _stageSwitching = false;

            _stage = stage;
            _session.Stage = stage;
            _ = _sessionService.SaveAsync(_settingsService.Settings.CurrentUser);

            if (stage == TrainingStage.Extensive)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                ExtensivePanel.Visibility = Visibility.Visible;
                UpdateExtensiveUi();
                UpdateExtPlayButton();
                if (notify)
                    ShowStageInfo("已进入① 泛听：连续播放整段素材，可标记难点句。");
            }
            else
            {
                ExtensivePanel.Visibility = Visibility.Collapsed;
                _extPlaying = false;
                _player?.Stop();

                DetailPanel.Visibility = Visibility.Visible;

                if (stage == TrainingStage.Review)
                {
                    _reviewQueue = BuildReviewQueuePositions();
                    _reviewPos = 0;
                    ShowStageInfo(_reviewQueue.Count == 0
                        ? "复习队列为空：没有错误率超阈值且未掌握的句子。"
                        : $"已进入③ 复习：队列 {_reviewQueue.Count} 句，连续 {TrainingSessionService.MasteryConsecutiveCorrect} 次全对判掌握。",
                        _reviewQueue.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational);
                }
                else if (notify)
                {
                    ShowStageInfo("已进入② 精听：逐句播放，逐词输入即时校验，提示分三级可用。");
                }

                ShowSentence(autoPlay: false);
            }
        }

        private List<int> BuildReviewQueuePositions()
        {
            if (_media == null || _session == null || _sessionService == null) return new List<int>();
            var indexes = _sessionService.BuildReviewQueue(_session, _media).ToHashSet();
            return _media.Sentences
                .Select((s, pos) => (s, pos))
                .Where(t => indexes.Contains(t.s.Index))
                .Select(t => t.pos)
                .ToList();
        }

        private void ShowStageInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            StageInfoBar.Message = message;
            StageInfoBar.Severity = severity;
            StageInfoBar.IsOpen = true;
        }

        // ── 精听 / 复习 ──────────────────────────────────────

        private static List<SentenceWordItem> SplitWords(string text)
        {
            return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Any(char.IsLetterOrDigit))
                .Select(w => new SentenceWordItem { Expected = w })
                .ToList();
        }

        private void ShowSentence(bool autoPlay)
        {
            if (_media == null || _player == null || _session == null) return;

            if (_stage == TrainingStage.Review && _reviewQueue.Count == 0)
            {
                _words = new List<SentenceWordItem>();
                WordsPanel.ItemsSource = _words;
                IndexText.Text = "复习队列为空";
                SubmitButton.IsEnabled = false;
                return;
            }

            _submitted = false;
            _hintsUsed = 0;
            var current = Current;
            _words = SplitWords(current.TranscriptText);
            foreach (var w in _words)
                w.HintUsed += () => _hintsUsed++;
            WordsPanel.ItemsSource = _words;

            IndexText.Text = _stage == TrainingStage.Review
                ? $"复习 · 第 {_reviewPos + 1} / {_reviewQueue.Count} 句"
                : $"精听 · 第 {_index + 1} / {_media.Sentences.Count} 句";

            MainWindow.SetTitleStatus($"句子训练 · {StageName(_stage)} · " +
                (_stage == TrainingStage.Review ? _reviewPos + 1 : _index + 1));

            ResultText.Text = string.Empty;
            TranscriptTextBlock.Visibility = Visibility.Collapsed;
            SubmitButton.IsEnabled = _words.Count > 0;

            if (autoPlay) PlayCurrent();
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
            if (_media == null || _session == null) return;
            if (_stage == TrainingStage.Review)
            {
                if (_reviewPos <= 0) return;
                _reviewPos--;
            }
            else
            {
                if (_index <= 0) return;
                _index--;
            }
            ShowSentence(autoPlay: true);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _session == null) return;
            if (_stage == TrainingStage.Review)
            {
                // 跳过本轮已掌握的句子
                var next = _reviewPos + 1;
                while (next < _reviewQueue.Count && IsMasteredAt(_reviewQueue[next])) next++;
                if (next >= _reviewQueue.Count)
                {
                    var allMastered = _reviewQueue.All(IsMasteredAt);
                    ShowStageInfo(allMastered
                        ? "全部句子已掌握，本素材训练完成！"
                        : "本轮复习已流完，仍有句子未掌握，可继续练习。",
                        allMastered ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
                    return;
                }
                _reviewPos = next;
            }
            else
            {
                if (_index >= _media.Sentences.Count - 1) return;
                _index++;
            }
            ShowSentence(autoPlay: true);
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_submitted || _media == null || _session == null || _sessionService == null) return;
            _submitted = true;
            SubmitButton.IsEnabled = false;

            var correct = 0;
            var attempts = new List<SentenceWordAttempt>();
            foreach (var w in _words)
            {
                w.Submit();
                if (w.Correct == true) correct++;
                attempts.Add(new SentenceWordAttempt
                {
                    Expected = w.Expected,
                    Input = w.Input,
                    Correct = w.Correct == true
                });
            }

            ResultText.Text = $"正确 {correct} / {_words.Count}";
            TranscriptTextBlock.Text = $"原文：{Current.TranscriptText}";
            TranscriptTextBlock.Visibility = Visibility.Visible;

            if (_stage == TrainingStage.Review)
            {
                var newlyMastered = _sessionService.RecordReviewAttempt(
                    _session, Current.Index, _words.Count, correct, _hintsUsed);
                if (newlyMastered)
                {
                    ShowStageInfo("本句连续全对达标，已标记“已掌握”！", InfoBarSeverity.Success);
                }
                else if (correct == _words.Count)
                {
                    var consecutive = _sessionService.GetProgress(_session, Current.Index).ConsecutiveCorrect;
                    ShowStageInfo($"全对 {consecutive} / {TrainingSessionService.MasteryConsecutiveCorrect} 次，再连续全对一次即掌握。");
                }
            }
            else
            {
                _sessionService.RecordIntensiveAttempt(
                    _session, Current.Index, _words.Count, correct, _hintsUsed);
                if (_stage == TrainingStage.Intensive && AllSentencesAttempted())
                    ShowStageInfo("全部句子已完成精听，可进入③ 复习阶段。", InfoBarSeverity.Success);
            }

            _ = _sessionService.SaveAsync(_settingsService.Settings.CurrentUser);
            _ = SaveAttemptAsync(correct, attempts);
        }

        private bool AllSentencesAttempted()
        {
            if (_media == null || _session == null || _sessionService == null) return false;
            return _media.Sentences.All(s => _sessionService.GetProgress(_session, s.Index).Attempts > 0);
        }

        private bool IsMasteredAt(int listPos)
        {
            if (_media == null || _session == null || _sessionService == null) return false;
            return _sessionService.GetProgress(_session, _media.Sentences[listPos].Index).IsMastered;
        }

        /// <summary>句子训练尝试写入 test_history.json（RecordType="sentence"，HintCount/TrainingStage 扩展字段向后兼容）。</summary>
        private async Task SaveAttemptAsync(int correct, List<SentenceWordAttempt> attempts)
        {
            try
            {
                if (_media == null || _session == null) return;

                // 离线模式：未登录时 SettingsService 回退到默认离线用户目录，记录仍本地留存
                var user = _settingsService.Settings.CurrentUser;
                var history = await _settingsService.LoadTestHistoryAsync(user ?? "");
                var record = new TestResult
                {
                    Timestamp = DateTime.Now,
                    TotalWords = _words.Count,
                    CorrectCount = correct,
                    Accuracy = _words.Count == 0 ? 0 : Math.Round((double)correct / _words.Count * 100, 1),
                    WordListName = $"句子训练 · {_media.Title} · {StageName(_stage)}",
                    RecordType = "sentence",
                    MediaSourceId = _media.Id,
                    SentenceIndex = Current.Index,
                    SentenceWords = attempts,
                    HintCount = _hintsUsed,
                    TrainingStage = _stage.ToString().ToLowerInvariant()
                };
                history.Add(record);
                await _settingsService.SaveTestHistoryAsync(user ?? "", history);

                // 已登录时自动上传到云端
                if (Services.WebApiService.Instance.IsLoggedIn)
                    _ = new Services.TestResultService().UploadResultAsync(record);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SentenceTraining] 保存尝试记录失败: {ex.Message}");
            }
        }

        // ── 泛听 ─────────────────────────────────────────────

        private void OnSegmentEnded()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_player == null || _media == null) return;
                if (_stage == TrainingStage.Extensive && _extPlaying)
                    OnExtSegmentEnded();
                else
                    UpdatePlayButton();
            });
        }

        private void OnExtSegmentEnded()
        {
            if (_media == null || _session == null || _sessionService == null) return;

            // 记录播放完成度
            if (_extIndex + 1 > _session.ExtensivePlayedCount)
            {
                _session.ExtensivePlayedCount = _extIndex + 1;
                _ = _sessionService.SaveAsync(_settingsService.Settings.CurrentUser);
            }

            _extIndex++;
            if (_extIndex < _media.Sentences.Count)
            {
                PlayExtCurrent();
            }
            else
            {
                _extPlaying = false;
                UpdateExtPlayButton();
                ShowStageInfo("泛听播放完成。可标记难点，或进入② 精听。", InfoBarSeverity.Success);
            }
        }

        private void PlayExtCurrent()
        {
            if (_media == null || _player == null) return;
            var s = _media.Sentences[_extIndex];
            _player.PlaySegment(_media.FilePath, s.StartTimeMs, s.EndTimeMs, loop: false);
            ExtSentenceText.Text = $"[{_extIndex + 1}] {s.TranscriptText}";
            UpdateExtensiveUi();
            UpdateExtPlayButton();
        }

        private void ExtPlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _player == null || _session == null) return;

            if (_extPlaying)
            {
                _player.Pause();
                _extPlaying = false;
            }
            else
            {
                if (_extIndex >= _media.Sentences.Count) _extIndex = 0; // 播完后再按播放 → 重播
                if (_player.HasSource) _player.Resume(); else PlayExtCurrent();
                _extPlaying = true;
            }
            UpdateExtPlayButton();
        }

        private void ExtRestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _player == null || _session == null) return;
            _extIndex = 0;
            _extPlaying = true;
            PlayExtCurrent();
        }

        private void MarkDifficultButton_Click(object sender, RoutedEventArgs e)
        {
            if (_media == null || _session == null || _sessionService == null) return;

            var s = _media.Sentences[_extIndex];
            var now = !_sessionService.GetProgress(_session, s.Index).IsMarkedDifficult;
            _sessionService.ToggleDifficultMark(_session, s.Index, now);
            _ = _sessionService.SaveAsync(_settingsService.Settings.CurrentUser);
            UpdateExtensiveUi();
            ShowStageInfo(now
                ? $"已标记第 {_extIndex + 1} 句为难点（将进入复习队列）。"
                : $"已取消第 {_extIndex + 1} 句的难点标记。");
        }

        private void ShowTranscriptSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            ExtSentenceText.Visibility = ShowTranscriptSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateExtensiveUi()
        {
            if (_media == null || _session == null || _sessionService == null) return;
            var total = _media.Sentences.Count;
            var played = Math.Min(_session.ExtensivePlayedCount, total);
            ExtProgressText.Text = $"完成度：已完整播放 {played} / {total} 句";
            ExtProgressBar.Value = total == 0 ? 0 : (double)played / total * 100;
            MarkDifficultButton.Content = _sessionService.GetProgress(_session, _media.Sentences[_extIndex].Index).IsMarkedDifficult
                ? "取消难点"
                : "标记难点";
        }

        private void UpdateExtPlayButton()
        {
            ExtPlayPauseButton.Content = _extPlaying ? "暂停" : "播放";
        }
    }
}
