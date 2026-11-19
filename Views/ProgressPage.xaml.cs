using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Storage;
using Windows.Storage.Pickers;
using English_Listen_WinUI.Models;
using English_Listen_WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace English_Listen_WinUI.Views
{
    /// <summary>
    /// 学习历史：只负责记录历史。统计/成绩分析在个人中心页。
    /// </summary>
    public sealed partial class ProgressPage : Page
    {
        private readonly MainViewModel _viewModel;

        public ProgressPage()
        {
            this.InitializeComponent();
            _viewModel = App.SharedViewModel!;
            this.DataContext = _viewModel;
            Loaded += ProgressPage_Loaded;
        }

        private void ProgressPage_Loaded(object sender, RoutedEventArgs e)
        {
            ApplySort();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySort();
        }

        private void ApplySort()
        {
            if (_viewModel?.TestHistory == null) return;

            var tag = (SortComboBox?.SelectedItem as ComboBoxItem)?.Tag as string;
            IEnumerable<TestResult> sorted = tag switch
            {
                "Oldest" => _viewModel.TestHistory.OrderBy(h => h.Timestamp),
                "Accuracy" => _viewModel.TestHistory.OrderByDescending(h => h.Accuracy),
                _ => _viewModel.TestHistory.OrderByDescending(h => h.Timestamp),
            };

            _viewModel.TestHistoryViewModels.Clear();
            foreach (var r in sorted)
                _viewModel.TestHistoryViewModels.Add(new TestResultViewModel { Result = r });

            HistoryListView.ItemsSource = _viewModel.TestHistoryViewModels;
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = $"test_history_{DateTime.Now:yyyyMMdd}"
            };
            picker.FileTypeChoices.Add("CSV", new[] { ".csv" });

            // Set XamlRoot for WinUI3 compatibility
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                var lines = new List<string>
                {
                    "时间,词库名称,总单词数,正确数,正确率"
                };

                foreach (var result in _viewModel.TestHistory)
                {
                    lines.Add(
                        $"{result.Timestamp:yyyy-MM-dd HH:mm},{result.WordListName},{result.TotalWords},{result.CorrectCount},{result.Accuracy:F1}%");
                }

                await FileIO.WriteTextAsync(file, string.Join(Environment.NewLine, lines));
            }
        }

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "确认清空",
                    Content = "确定要清空所有学习记录吗？此操作不可恢复。",
                    PrimaryButtonText = "清空",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    _viewModel.TestHistory.Clear();
                    var currentUser = _viewModel?.Settings?.Settings?.CurrentUser;
                    if (_viewModel?.Settings != null)
                    {
                        await _viewModel.Settings.SaveTestHistoryAsync(currentUser ?? "", _viewModel.TestHistory);
                    }

                    ApplySort();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClearButton_Click error: {ex.Message}");
            }
        }

        private async void RedictateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not TestResultViewModel vm) return;
            if (vm.Result.Words is not { Count: > 0 } words) return;

            var wordList = words
                .Select(w => new DictationTestPage.WordTranslationPair { Word = w.Word, Translation = w.Translation })
                .ToList();

            var settingsDialog = new ContentDialog
            {
                Title = "听考选项",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var stackPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Spacing = 10 };

            var modeStack = new StackPanel { Spacing = 5 };
            var onlineModeRadio = new RadioButton { Content = "在线听写", IsChecked = true, GroupName = "RedictateMode" };
            var paperModeRadio = new RadioButton { Content = "纸笔听写", GroupName = "RedictateMode" };
            modeStack.Children.Add(onlineModeRadio);
            modeStack.Children.Add(paperModeRadio);
            stackPanel.Children.Add(modeStack);

            var randomOrderSwitch = new ToggleSwitch { Header = "随机顺序", IsOn = false };
            stackPanel.Children.Add(randomOrderSwitch);

            var readTranslationSwitch = new ToggleSwitch { Header = "朗读翻译", IsOn = false };
            stackPanel.Children.Add(readTranslationSwitch);

            settingsDialog.Content = stackPanel;

            var result = await settingsDialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var testParams = new WordsPage.DictationTestParamsWithTranslations(
                wordList,
                randomOrderSwitch.IsOn,
                readTranslationSwitch.IsOn,
                paperModeRadio.IsChecked ?? false);

            Frame?.Navigate(typeof(DictationTestPage), testParams);
        }

        private void RelearnButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not TestResultViewModel vm) return;
            if (vm.Result.Words is not { Count: > 0 } words) return;

            var wordList = words
                .Select(w => new DictationTestPage.WordTranslationPair { Word = w.Word, Translation = w.Translation })
                .ToList();

            Frame?.Navigate(typeof(MemorizePage), wordList);
        }
    }
}
