using System;
using System.Collections.Generic;
using System.Linq;

namespace English_Listen_WinUI.Models
{
    /// <summary>错误标签（可解释启发式，非 ML）。标签为派生数据：从 test_history 实时计算，不落盘。</summary>
    public enum ErrorTagKind
    {
        None = 0,
        /// <summary>弱读：常见弱读虚词漏听（输入为空）</summary>
        WeakForm = 1,
        /// <summary>音素：听到了但拼不准（编辑距离 1~2）</summary>
        Phonetic = 2,
        /// <summary>连读：输入吞并了本词与下一词</summary>
        Liaison = 3,
        /// <summary>语速：句级标签，词密度过高导致听错（&gt;2.5 词/秒）</summary>
        Speed = 4,
        /// <summary>词汇不熟：完全没听出来，或该词累计错 ≥2 次</summary>
        Vocabulary = 5
    }

    /// <summary>单个标签的累计统计</summary>
    public class ErrorTagStats
    {
        public ErrorTagKind Tag { get; set; }
        public int Count { get; set; }

        public string Label => Tag switch
        {
            ErrorTagKind.WeakForm => "弱读",
            ErrorTagKind.Phonetic => "音素",
            ErrorTagKind.Liaison => "连读",
            ErrorTagKind.Speed => "语速",
            ErrorTagKind.Vocabulary => "词汇不熟",
            _ => "其他"
        };

        public string Tip => Tag switch
        {
            ErrorTagKind.WeakForm => "虚词常弱读导致漏听，多做整句跟读",
            ErrorTagKind.Phonetic => "听到了但拼不准，注意尾音与元音区分",
            ErrorTagKind.Liaison => "词间连读吞并，注意词边界",
            ErrorTagKind.Speed => "句子语速快，可用降速精听",
            ErrorTagKind.Vocabulary => "重复听错的词，建议加入词库专项记忆",
            _ => string.Empty
        };
    }

    /// <summary>用户错误画像：标签分布 + 高频错词 + 最近错句词集。运行时派生，不持久化。</summary>
    public class ErrorProfile
    {
        /// <summary>标签统计，按次数降序</summary>
        public List<ErrorTagStats> TagStats { get; set; } = new();

        /// <summary>高频错词（规范化小写），按错误次数降序，仅含累计错 ≥2 次的词</summary>
        public List<string> FrequentWrongWords { get; set; } = new();

        /// <summary>错词总次数（含仅错 1 次的）</summary>
        public int TotalWordErrors { get; set; }

        /// <summary>最近出错句子的词集合（推荐用话题相似度）</summary>
        public List<HashSet<string>> RecentErrorSentenceWords { get; set; } = new();

        /// <summary>薄弱标签（出现过的，按次数降序）</summary>
        public List<ErrorTagKind> WeakTags => TagStats.Where(t => t.Count > 0).Select(t => t.Tag).ToList();
    }

    /// <summary>
    /// 错词打标器：纯文本可解释启发式，每个错词打 0~2 个标签。
    /// 标签体系见 <see cref="ErrorTagKind"/>；规则说明随最终交付输出。
    /// </summary>
    public static class WordErrorTagger
    {
        /// <summary>常见弱读虚词表（口语中通常弱读，漏听多为弱读导致）</summary>
        private static readonly HashSet<string> WeakFormWords = new()
        {
            "a","an","the","of","to","and","or","in","on","at","for","from","with",
            "is","are","was","were","am","be","been","has","have","had",
            "will","would","can","could","do","does","did","not","but","as","so","than","then",
            "that","this","these","those","there","here","him","them","us","her","his","its","their","our","your","my",
            "he","she","it","they","we","you","i","me","who","which","what","when","where","why","how"
        };

        /// <summary>编辑距离 ≥3 视为“完全没听出来”</summary>
        private const int VocabularyEditDistance = 3;

        /// <summary>给一个错词打标。</summary>
        /// <param name="attempt">错词尝试</param>
        /// <param name="nextExpected">同句下一词（连读判定用，可为 null）</param>
        /// <param name="historicalWrongCount">该词（规范化后）在全部历史中的错误次数</param>
        public static List<ErrorTagKind> Tag(SentenceWordAttempt attempt, string? nextExpected, int historicalWrongCount)
        {
            var tags = new List<ErrorTagKind>();
            var expected = Normalize(attempt.Expected);
            var input = Normalize(attempt.Input);

            if (expected.Length == 0) return tags;

            if (input.Length == 0)
            {
                // 漏听：虚词归弱读，实词归词汇不熟
                tags.Add(IsWeakForm(expected) ? ErrorTagKind.WeakForm : ErrorTagKind.Vocabulary);
            }
            else if (IsLiaison(expected, Normalize(nextExpected ?? string.Empty), input))
            {
                tags.Add(ErrorTagKind.Liaison);
            }
            else
            {
                var d = EditDistance(expected, input);
                tags.Add(d >= VocabularyEditDistance ? ErrorTagKind.Vocabulary : ErrorTagKind.Phonetic);
            }

            // 重复出错 → 词汇不熟（可与音素/连读并存）
            if (historicalWrongCount >= 2 && !tags.Contains(ErrorTagKind.Vocabulary))
                tags.Add(ErrorTagKind.Vocabulary);

            return tags;
        }

        public static bool IsWeakForm(string normalizedWord) => WeakFormWords.Contains(normalizedWord);

        /// <summary>连读判定：输入 ≈ 本词 + 下一词的拼接（词边界吞并）。</summary>
        private static bool IsLiaison(string expected, string nextExpected, string input)
        {
            if (nextExpected.Length == 0 || input.Length <= expected.Length) return false;
            if (!input.StartsWith(expected, StringComparison.Ordinal)) return false;
            var tail = input.Substring(expected.Length);
            return tail.Length > 0 && nextExpected.StartsWith(tail, StringComparison.Ordinal);
        }

        /// <summary>规范化：小写、仅保留字母数字。</summary>
        public static string Normalize(string s)
        {
            var chars = s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        /// <summary>Levenshtein 编辑距离（两行滚动数组）。</summary>
        public static int EditDistance(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var prev = new int[b.Length + 1];
            var curr = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;

            for (var i = 1; i <= a.Length; i++)
            {
                curr[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }
            return prev[b.Length];
        }
    }
}
