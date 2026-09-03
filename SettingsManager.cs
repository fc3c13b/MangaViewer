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
        /// 後方互換用のLoad（UIでのエラー通知は行わない）。
        /// </summary>
        public static Settings Load()
        {
            var (settings, _) = LoadWithValidation();
            return settings;
        }

        /// <summary>
        /// 設定を読み込みつつ、不正な値がある場合はエラーメッセージを収集する。
        /// UI側で MessageBox などに使用可能。
        /// </summary>
        public static (Settings Settings, List<string> Errors) LoadWithValidation()
        {
            var errors = new List<string>();
            string path = AppPaths.SettingsFilePath;

            // ファイルなし → デフォルト返却（エラー表示不要）
            if (!File.Exists(path))
                return (Settings.Default, errors);

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch
            {
                // 読み取り不可 → デフォルト＋エラーメッセージ
                errors.Add("設定ファイルの読み込みに失敗しました。デフォルト値を使用します。");
                return (Settings.Default, errors);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var settings = new Settings(); // デフォルト値ベース

            // MinDisplayCountEnabled
            if (root.TryGetProperty("MinDisplayCountEnabled", out var ep) && (ep.ValueKind == JsonValueKind.True || ep.ValueKind == JsonValueKind.False))
                settings.MinDisplayCountEnabled = ep.GetBoolean();

            // MinDisplayCount: 1〜99999 の範囲（1を含む）
            const int minDisplayCountDef = 20;
            if (root.TryGetProperty("MinDisplayCount", out var cp) && cp.ValueKind == JsonValueKind.Number)
            {
                int val = cp.GetInt32();
                if (val >= 1 && val <= 99999)
                    settings.MinDisplayCount = val;
                else
                {
                    errors.Add($"MinDisplayCount の値 '{val}' は無効です（有効範囲: 1〜99999）。デフォルト値 ({minDisplayCountDef}) を使用します。");
                    settings.MinDisplayCount = minDisplayCountDef;
                }
            }

            // MaxDisplayCount: MinDisplayCount+1 以上
            const int maxDisplayCountDef = 1000;
            if (root.TryGetProperty("MaxDisplayCount", out var mcp) && mcp.ValueKind == JsonValueKind.Number)
            {
                int val = mcp.GetInt32();
                if (val > settings.MinDisplayCount)
                    settings.MaxDisplayCount = val;
                else
                {
                    errors.Add($"MaxDisplayCount の値 '{val}' は無効です（MinDisplayCount より大きい値が必要）。デフォルト値 ({maxDisplayCountDef}) を使用します。");
                    settings.MaxDisplayCount = maxDisplayCountDef;
                }
            }

            // MinEvaluation: 0〜10
            const int minEvalDef = 8;
            if (root.TryGetProperty("MinEvaluation", out var evp) && evp.ValueKind == JsonValueKind.Number)
            {
                int val = evp.GetInt32();
                if (val >= 0 && val <= 10)
                    settings.MinEvaluation = val;
                else
                {
                    errors.Add($"MinEvaluation の値 '{val}' は無効です（有効範囲: 0〜10）。デフォルト値 ({minEvalDef}) を使用します。");
                    settings.MinEvaluation = minEvalDef;
                }
            }

            // DisplayCount: 1, 2, または 8 のみ有効
            const int displayCountDef = 2;
            if (root.TryGetProperty("DisplayCount", out var dp) && dp.ValueKind == JsonValueKind.Number)
            {
                int val = dp.GetInt32();
                if (val == 1 || val == 2 || val == 8)
                    settings.DisplayCount = val;
                else
                {
                    errors.Add($"DisplayCount の値 '{val}' は無効です（有効範囲: 1, 2, 8）。デフォルト値 ({displayCountDef}) を使用します。");
                    settings.DisplayCount = displayCountDef;
                }
            }

            // NormalModeImageAreaPercent: 50〜99
            const int normalPctDef = 72;
            if (root.TryGetProperty("NormalModeImageAreaPercent", out var np) && np.ValueKind == JsonValueKind.Number)
            {
                int val = np.GetInt32();
                if (val >= 50 && val <= 99)
                    settings.NormalModeImageAreaPercent = val;
                else
                {
                    errors.Add($"NormalModeImageAreaPercent の値 '{val}' は無効です（有効範囲: 50〜99）。デフォルト値 ({normalPctDef}) を使用します。");
                    settings.NormalModeImageAreaPercent = normalPctDef;
                }
            }

            // FullScreenModeImageAreaPercent: 50〜99
            const int fullPctDef = 85;
            if (root.TryGetProperty("FullScreenModeImageAreaPercent", out var fp) && fp.ValueKind == JsonValueKind.Number)
            {
                int val = fp.GetInt32();
                if (val >= 50 && val <= 99)
                    settings.FullScreenModeImageAreaPercent = val;
                else
                {
                    errors.Add($"FullScreenModeImageAreaPercent の値 '{val}' は無効です（有効範囲: 50〜99）。デフォルト値 ({fullPctDef}) を使用します。");
                    settings.FullScreenModeImageAreaPercent = fullPctDef;
                }
            }

            // LastRootFolder
            if (root.TryGetProperty("LastRootFolder", out var lrp) && lrp.ValueKind == JsonValueKind.String)
                settings.LastRootFolder = lrp.ToString();

            // MinEvaluationFilterEnabled: true/false
            if (root.TryGetProperty("MinEvaluationFilterEnabled", out var mefp) && (mefp.ValueKind == JsonValueKind.True || mefp.ValueKind == JsonValueKind.False))
                settings.MinEvaluationFilterEnabled = mefp.GetBoolean();

            // MinEvaluationEqualFilter: true/false
            if (root.TryGetProperty("MinEvaluationEqualFilter", out var meqf) && (meqf.ValueKind == JsonValueKind.True || meqf.ValueKind == JsonValueKind.False))
                settings.MinEvaluationEqualFilter = meqf.GetBoolean();

            return (settings, errors);
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
                    { "MaxDisplayCount", settings.MaxDisplayCount },
                    { "MinEvaluation", settings.MinEvaluation },
                    { "LastRootFolder", settings.LastRootFolder },
                    { "DisplayCount", settings.DisplayCount },
                    { "NormalModeImageAreaPercent", settings.NormalModeImageAreaPercent },
                    { "FullScreenModeImageAreaPercent", settings.FullScreenModeImageAreaPercent },
                    { "MinEvaluationFilterEnabled", settings.MinEvaluationFilterEnabled },
                    { "MinEvaluationEqualFilter", settings.MinEvaluationEqualFilter }
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
            var (settings, _) = LoadWithValidation();
            settings.LastRootFolder = rootPath;
            Save(settings);
        }
    }
}