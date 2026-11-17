using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace English_Listen_WinUI.Views
{
    /// <summary>素材库列表项（含展示用的派生属性）</summary>
    public class MaterialItem
    {
        private static readonly Brush ReadyBrush = new SolidColorBrush(Microsoft.UI.Colors.Green);
        private static readonly Brush TranscribingBrush = new SolidColorBrush(Microsoft.UI.Colors.Orange);
        private static readonly Brush FailedBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
        private static readonly Brush PendingBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

        public MediaSource Source { get; }

        public MaterialItem(MediaSource source)
        {
            Source = source;
        }

        public string Title => Source.Title;
        public bool CanTrain => Source.Status == MediaStatus.Ready && Source.Sentences.Count > 0;

        public string Subtitle
        {
            get
            {
                var parts = new List<string> { Source.FilePath, Source.CreatedAt.ToString("yyyy-MM-dd HH:mm") };
                if (!string.IsNullOrEmpty(Source.ErrorMessage))
                    parts.Add(Source.ErrorMessage);
                return string.Join("  ·  ", parts);
            }
        }

        public string StatusText => Source.Status switch
        {
            MediaStatus.NeedsTranscribe => "待转写",
            MediaStatus.Transcribing => "转写中…",
            MediaStatus.Ready => $"就绪 · {Source.Sentences.Count} 句",
            MediaStatus.Failed => "失败",
            _ => string.Empty
        };

        public Brush StatusBrush => Source.Status switch
        {
            MediaStatus.Ready => ReadyBrush,
            MediaStatus.Transcribing => TranscribingBrush,
            MediaStatus.Failed => FailedBrush,
            _ => PendingBrush
        };

        public Visibility RetryVisibility =>
            Source.Status is MediaStatus.NeedsTranscribe or MediaStatus.Failed
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// 素材库页面：导入本地音视频 → 转写切句 → 管理素材列表。
    /// 无模型时显示引导，不崩溃。
    /// </summary>
    public sealed partial class MediaLibraryPage : Page
    {
        private readonly SettingsService _settingsService = new();
        private readonly MediaLibraryService _mediaLibraryService;
        private readonly MediaImportService _importService;
        private readonly ITranscriptionService _transcriptionService = new WhisperNetTranscriptionService();
        private List<MediaSource> _library = new();

        public MediaLibraryPage()
        {
            InitializeComponent();
            _mediaLibraryService = new MediaLibraryService(_settingsService);
            _importService = new MediaImportService(_transcriptionService);
        }

        private string? CurrentUser => _settingsService.Settings.CurrentUser;

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _ = LoadLibraryAsync();
        }

        private async Task LoadLibraryAsync()
        {
            _library = await _mediaLibraryService.LoadAsync(CurrentUser);
            RefreshModelStatus();
            RefreshList();
        }

        private void RefreshModelStatus()
        {
            var modelPath = _settingsService.Settings.WhisperModelPath;
            var available = _transcriptionService.IsModelAvailable(modelPath);
            ModelStatusText.Text = available ? $"模型：{modelPath}" : "模型：未配置";
            ModelInfoBar.IsOpen = !available && _library.Count > 0;
        }

        private void RefreshList()
        {
            MaterialList.ItemsSource = _library.Select(m => new MaterialItem(m)).ToList();
            EmptyText.Visibility = _library.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowError(string message)
        {
            ErrorInfoBar.Message = message;
            ErrorInfoBar.IsOpen = true;
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary
            };
            foreach (var ext in new[] { ".mp3", ".wav", ".mp4", ".m4a", ".wma", ".aac", ".flac" })
            {
                picker.FileTypeFilter.Add(ext);
            }
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;

            var newMedia = files.Select(f => new MediaSource
            {
                Title = f.DisplayName,
                FilePath = f.Path,
                Status = MediaStatus.NeedsTranscribe
            }).ToList();

            foreach (var media in newMedia)
            {
                _library.Insert(0, media);
            }
            await _mediaLibraryService.SaveAsync(CurrentUser, _library);
            RefreshList();

            foreach (var media in newMedia)
            {
                await ImportOneAsync(media);
            }
        }

        private async Task ImportOneAsync(MediaSource media)
        {
            var modelPath = _settingsService.Settings.WhisperModelPath;
            if (!_transcriptionService.IsModelAvailable(modelPath))
            {
                ModelInfoBar.IsOpen = true;
                return;
            }

            media.Status = MediaStatus.Transcribing;
            media.ErrorMessage = null;
            RefreshList();

            ImportProgress.Visibility = Visibility.Visible;
            ImportStatusText.Visibility = Visibility.Visible;
            var progress = new Progress<string>(msg => ImportStatusText.Text = msg);

            try
            {
                var options = new TranscriptionOptions
                {
                    ModelPath = modelPath,
                    Language = "auto"
                };
                await Task.Run(() => _importService.ImportAsync(media, options, progress, CancellationToken.None));
            }
            catch (TranscriptionModelNotFoundException)
            {
                ModelInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                ShowError($"“{media.Title}”导入失败：{ex.Message}");
            }
            finally
            {
                await _mediaLibraryService.SaveAsync(CurrentUser, _library);
                RefreshModelStatus();
                RefreshList();
                ImportProgress.Visibility = Visibility.Collapsed;
                ImportStatusText.Visibility = Visibility.Collapsed;
            }
        }

        private async void ModelButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".bin");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            _settingsService.Settings.WhisperModelPath = file.Path;
            await _settingsService.SaveSettingsAsync();
            RefreshModelStatus();
        }

        private void TrainButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: MaterialItem item } && item.CanTrain)
            {
                Frame.Navigate(typeof(SentenceTrainingPage), item.Source);
            }
        }

        private async void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: MaterialItem item })
            {
                await ImportOneAsync(item.Source);
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: MaterialItem item }) return;

            var dialog = new ContentDialog
            {
                Title = "删除素材",
                Content = $"确定删除“{item.Source.Title}”吗？句子和转写数据将一并删除。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            _library.Remove(item.Source);
            await _mediaLibraryService.SaveAsync(CurrentUser, _library);
            RefreshList();
        }
    }
}
