using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace _Src.Test {
    /// <summary>
    /// GGPO 测试诊断日志。运行期间只写内存，用户点击保存后才写入磁盘，
    /// 避免频繁 Debug.Log 或文件 IO 改变网络测试时序。
    /// </summary>
    public static class TestDiagnostics {
        private const int MaxLineCount = 50000;
        private static readonly List<string> Lines = new List<string>();
        private static readonly Stopwatch Stopwatch = new Stopwatch();
        private static int s_DroppedLineCount;

        public static int LineCount => Lines.Count;
        public static string LastSavedPath { get; private set; }

        public static void BeginSession(string description) {
            Lines.Clear();
            s_DroppedLineCount = 0;
            LastSavedPath = null;
            Stopwatch.Restart();
            Record("SESSION", description);
            Record(
                "ENV",
                $"Utc={DateTime.UtcNow:O} Platform={Application.platform} " +
                $"Unity={Application.unityVersion} TargetFrameRate={Application.targetFrameRate}");
        }

        public static void Record(string category, string message) {
            if (!Stopwatch.IsRunning)
                Stopwatch.Start();

            if (Lines.Count >= MaxLineCount) {
                s_DroppedLineCount++;
                return;
            }

            Lines.Add($"{Stopwatch.Elapsed.TotalMilliseconds,10:F3}ms " +
                      $"[{category}] {message}");
        }

        public static string Save(string fileLabel) {
            var directory = Path.Combine(
                Application.persistentDataPath,
                "GgpoDiagnostics");
            Directory.CreateDirectory(directory);

            var safeLabel = SanitizeFileName(fileLabel);
            var fileName =
                $"ggpo_{DateTime.Now:yyyyMMdd_HHmmss}_{safeLabel}.log";
            var path = Path.Combine(directory, fileName);
            var output = new List<string>(Lines.Count + 2);
            output.Add("# GGPO diagnostic log");
            output.Add($"# Lines={Lines.Count} Dropped={s_DroppedLineCount}");
            output.AddRange(Lines);
            File.WriteAllLines(path, output, new UTF8Encoding(false));
            LastSavedPath = path;
            return path;
        }

        private static string SanitizeFileName(string value) {
            if (string.IsNullOrWhiteSpace(value))
                return "session";

            var result = value;
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                result = result.Replace(invalidCharacter, '_');
            return result.Replace(' ', '_');
        }
    }
}
