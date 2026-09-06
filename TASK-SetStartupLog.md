# TASK: 起動時CBZ表示問題のログ診断機能

## 目的
アプリ起動時に CBZ ファイルが表示されない問題の原因を特定するために、
CBZ 展開・読み込みプロセスの各段階で診断ログを出力する。

## ログファイル
- **パス**: `startup-{yyyyMMdd_HHmmss}.log` (AppDomain.CurrentDomain.BaseDirectory 配下)
- **フォーマット**: `[HH:mm:ss.fff] {メッセージ}`
- **出力元**: `StartupHandler.StartupLog()` メソッド

## 実装計画（6つのログポイント）

| # | 位置 | ファイル | 内容 | ステータス |
|---|------|----------|------|------------|
| 1 | `_imagePathsByCbz.TryGetValue` HIT | CbzManager.cs | `[CBZ-STARTUP] Cache HIT: {cbzFile}` | ✅ 完了 |
| 2 | `_imagePathsByCbz.TryGetValue` MISS | CbzManager.cs | `[CBZ-STARTUP] Cache MISS: {cbzFile}` | ✅ 完了 |
| 3 | `RefreshCurrentImagePaths()` 結果 | CbzManager.cs | `[CBZ-STARTUP] Refresh result: count={N}, cacheDir={path}` | ✅ 完了 |
| 4 | `ExtractToDirectory` 呼び出し前 | CbzManager.cs | `[CBZ-STARTUP] Extracting: {cbzFile} → {cacheDir}` | ✅ 完了 |
| 5 | ImageLoader.Load() エントリ | ImageLoader.cs | `[CBZ] Loading CBZ images for: {folderPath}` | ✅ 完了 |
| 6 | ImageLoader 結果サマリー | ImageLoader.cs | `[CBZ] Collection complete: {N} images from {M} CBZ files` | ✅ 完了 |

## 診断フロー
```
ImageLoader.Load()
    └─→ [Point #5] フォルダパスログ
        └─→ CbzManager.InitializeForFolder()
            └─→ RefreshCurrentImagePaths()
                ├─→ [Point #1/2] キャッシュ HIT/MISS
                └─→ MISS 時: ExtractToDirectory()
                    ├─→ [Point #4] 展開開始ログ
                    └─→ [Point #3] 展開結果（画像数）
        └─→ [Point #6] 合計サマリー
```

## トラブルシューティングガイド

### CBZ が表示されない場合のチェックリスト
1. **Point #5** でフォルダパスが正しいか確認
2. **Point #1/2** でキャッシュ HIT か MISS か確認
3. **MISS の場合**: Point #4 で展開先ディレクトリを確認
4. **Point #3** で画像数が 0 なら展開失敗の可能性
5. **Point #6** で最終的な画像数が期待通りか確認

## 注意事項
- ログは起動時（`StartupHandler.RunBackgroundInit()`）にのみ出力される
- `error.log` とは別のファイルに出力される
- 起動時ごとに新しいログファイルが作成される（日時を含むため、同一日内の複数起動でも別ファイル）
