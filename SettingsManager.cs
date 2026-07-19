using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MangaViewer
{
    /// <summary>
    /// 設定ファイル (setting.json) の読み書きを担当する静的ユーティリティクラス。
    /// </summary>
    public static class SettingsManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        /// <summary>
        /// 設定ファイルをローディングして Settings オブジェクトとして返す。
        /// ファイルが存在しない場合はデフォルト値を返す。
        /// </summary>
        public static Settings Load()
        {
            string path = AppPaths.SettingsFilePath;
            if (!File.Exists(path))
                return Settings.Default;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var settings = new Settings();

                if (doc.RootElement.TryGetProperty("MinDisplayCountEnabled", out var ep))
                    settings.MinDisplayCountEnabled = ep.GetBoolean();

                if (doc.RootElement.TryGetProperty("MinDisplayCount", out var cp) && cp.ValueKind == JsonValueKind.Number)
                {
                    int val = cp.GetInt32();
                    if (val >= 2 && val <= 100)
                        settings.MinDisplayCount = val;
                }

                if (doc.RootElement.TryGetProperty("MinEvaluation", out var evp) && evp.ValueKind == JsonValueKind.Number)
                {
                    int val = evp.GetInt32();
                    if (val >= 0 && val <= 10)
                        settings.MinEvaluation = val;
                }

                if (doc.RootElement.TryGetProperty("LastRootFolder", out var lrp) && lrp.ValueKind == JsonValueKind.String)
                    settings.LastRootFolder = lrp.ToString();

                if (doc.RootElement.TryGetProperty("DisplayCount", out var dp) && dp.ValueKind == JsonValueKind.Number)
                {
                    int val = dp.GetInt32();
                    // 2 または 8 のみ許可。それ以外はデフォルトの 2 にする
                    if (val == 2 || val == 8)
                        settings.DisplayCount = val;
                    else
                        settings.DisplayCount = 2;
                }

                return settings;
            }
            catch
            {
                return Settings.Default;
            }
        }

        /// <summary>
        /// Settings オブジェクトの内容を setting.json に保存する。
        /// </summary>
        public static void Save(Settings settings)
        {
            try
            {
                 var obj = new Dictionary<string, object>
                 {
                     { "MinDisplayCountEnabled", settings.MinDisplayCountEnabled },
                     { "MinDisplayCount", settings.MinDisplayCount },
                     { "MinEvaluation", settings.MinEvaluation },
                     { "LastRootFolder", settings.LastRootFolder },
                     { "DisplayCount", settings.DisplayCount }
                 };

                File.WriteAllText(AppPaths.SettingsFilePath, JsonSerializer.Serialize(obj, JsonOptions));
            }
            catch { /* 保存失敗はサイレントに無視 */ }
        }

        /// <summary>
        /// 既存設定を読み込み、LastRootFolder のみを更新して上書き保存する。
        /// </summary>
        public static void SaveRootFolder(string rootPath)
        {
            var settings = Load();
            settings.LastRootFolder = rootPath;
            Save(settings);
        }
    }
}