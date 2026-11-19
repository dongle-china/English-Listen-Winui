using System;
using System.IO;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace English_Listen_WinUI.Services
{
    /// <summary>
    /// 句段播放器：在媒体文件的 [start, end] 时间区间内播放，支持区间循环。
    /// 基于 MediaPlayer + DispatcherTimer 轮询 Position 实现区间边界。
    /// </summary>
    public class SentencePlayerService : IDisposable
    {
        private readonly MediaPlayer _player = new();
        private readonly DispatcherTimer _watchdog;
        private string? _sourcePath;
        private double _startMs;
        private double _endMs;
        private bool _loop;

        /// <summary>非循环模式下播完/到达句尾时触发（用于自动进入下一句等）</summary>
        public event Action? SegmentEnded;

        /// <summary>是否已加载媒体源（泛听暂停后区分"续播"与"重新开始"）。</summary>
        public bool HasSource => _player.Source != null;

        public bool IsPlaying { get; private set; }

        public SentencePlayerService()
        {
            _player.AudioCategory = MediaPlayerAudioCategory.Speech;
            _watchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _watchdog.Tick += (_, _) => WatchPosition();
        }

        /// <summary>播放指定文件的一个区间</summary>
        public void PlaySegment(string filePath, double startMs, double endMs, bool loop)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            if (!string.Equals(filePath, _sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                _player.Source = MediaSource.CreateFromUri(new Uri(filePath));
                _sourcePath = filePath;
            }

            _startMs = startMs;
            _endMs = endMs;
            _loop = loop;
            _player.Position = TimeSpan.FromMilliseconds(startMs);
            _player.Play();
            IsPlaying = true;
            _watchdog.Start();
        }

        public void SetLoop(bool loop)
        {
            _loop = loop;
        }

        public void Pause()
        {
            _player.Pause();
            IsPlaying = false;
        }

        public void Resume()
        {
            if (_player.Source == null) return;
            _player.Play();
            IsPlaying = true;
            _watchdog.Start();
        }

        public void Stop()
        {
            _watchdog.Stop();
            _player.Pause();
            IsPlaying = false;
        }

        private void WatchPosition()
        {
            if (!IsPlaying) return;
            if (_player.Position.TotalMilliseconds < _endMs) return;

            if (_loop)
            {
                _player.Position = TimeSpan.FromMilliseconds(_startMs);
            }
            else
            {
                _player.Pause();
                IsPlaying = false;
                _watchdog.Stop();
                SegmentEnded?.Invoke();
            }
        }

        public void Dispose()
        {
            _watchdog.Stop();
            _player.Dispose();
        }
    }
}
