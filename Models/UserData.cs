using System;
using System.Collections.Generic;

namespace English_Listen_WinUI.Models
{
    public class TestResult
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int TotalWords { get; set; }
        public int CorrectCount { get; set; }
        public double Accuracy { get; set; }

        public string WordListName { get; set; } = string.Empty;

        // ponytail: nullable List, compatible with legacy JSON. null means old record without saved words.
        public List<WordTranslationPair>? Words { get; set; }

        /// <summary>
        /// 记录类型："dictation"=听写, "learning"=学习, "sentence"=句子精听。null/空 兼容旧记录（视为听写）
        /// </summary>
        public string? RecordType { get; set; }

        // ===== 句子精听扩展（RecordType == "sentence" 时有效）。可空属性保持 test_history.json 向后兼容 =====

        /// <summary>素材 ID（MediaSource.Id）</summary>
        public string? MediaSourceId { get; set; }

        /// <summary>句子切句序号</summary>
        public int? SentenceIndex { get; set; }

        /// <summary>逐词校验明细</summary>
        public List<SentenceWordAttempt>? SentenceWords { get; set; }
    }

    /// <summary>
    /// 单词翻译对，用于保存听写记录中的单词列表
    /// </summary>
    public class WordTranslationPair
    {
        public string Word { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
    }

    public class UserData
    {
        public string Username { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedTime { get; set; } = DateTime.Now;
        public DateTime LastLoginTime { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;

        public List<string> WordLists { get; set; } = new();
        public List<TestResult> TestHistory { get; set; } = new();
        public int TotalStudyTime { get; set; }
        public int CompletedTests { get; set; }
    }

    public class AppSettings
    {
        public int ThemeMode { get; set; } = 0; // 0 = System, 1 = Light, 2 = Dark
        public int ReadInterval { get; set; } = 5;
        public bool IsRandomOrder { get; set; } = true;
        public string WordlistDirPath { get; set; } = "./wordlist";
        public string? CurrentUser { get; set; }
        public string SpeechEngineType { get; set; } = "SAPI"; // "SAPI", "WinRT", "Natural"
        public string? WindowsTtsEnglishVoiceName { get; set; }
        public string? WindowsTtsChineseVoiceName { get; set; }
        public int WindowsTtsVolume { get; set; } = 100;
        public int WindowsTtsRate { get; set; } = 0;
        public string? BaiduTranslateApiMode { get; set; } = "default";
        public string? BaiduTranslateApiKey { get; set; }

        /// <summary>Whisper ggml 模型文件路径（本地转写，路径由用户在素材库页选择）</summary>
        public string? WhisperModelPath { get; set; }

        public StudyPlanSettings StudyPlan { get; set; } = new();
    }
}