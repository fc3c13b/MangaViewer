
## Plan: CBZキャッシュの同期処理問題の修正

現在のCBZ表示時に次のCBZも同期的に展開しているため、画像表示が遅くなっています。

**Steps**
1. `PreloadNextCbzIfAvailable()`を非同期化
   - `Task.Run()`でバックグラウンド実行
   - 現在のCBZ表示をブロックしない
2. デバッグ用ログ追加（オプション）
   - キャッシュヒット/ミスを記録

**Relevant files**
- [CbzManager.cs](CbzManager.cs#L343-L374) — `PreloadNextCbzIfAvailable()`を`Task.Run()`で非同期化
- [CbzManager.cs](CbzManager.cs#L256-L342) — `RefreshCurrentImagePaths()`からfire-and-forget呼び出し

**Verification**
1. ALT+左矢印でCBZ切り替え時の応答速度確認
2. キャッシュフォルダ（`%LOCALAPPDATA%\MangaViewer\CBZCache`）にハッシュ名フォルダが作成されているか確認
3. 2回目のCBZアクセス時にキャッシュヒットして即座に表示されるか確認

**Decisions**
- プリロードはfire-and-forget方式（完了を待たない）
- 現在のCBZ展開は同期のまま（キャッシュヒット時は高速なので問題なし）

**Further Considerations**
現在のCBZ展開も非同期化すべきか？
- Option A: 現在のまま（キャッシュヒット時は高速） — **推奨**
- Option B: 完全非同期化（ローディング表示が必要になる）

上記の内容を TASK09.34-ModifyCache-2.md として作成してください。ファイル編集ツールがないため、コンテンツを提示しました。## Plan: CBZキャッシュの同期処理問題の修正

現在のCBZ表示時に次のCBZも同期的に展開しているため、画像表示が遅くなっています。

**Steps**
1. `PreloadNextCbzIfAvailable()`を非同期化
   - `Task.Run()`でバックグラウンド実行
   - 現在のCBZ表示をブロックしない
2. デバッグ用ログ追加（オプション）
   - キャッシュヒット/ミスを記録

**Relevant files**
- [CbzManager.cs](CbzManager.cs#L343-L374) — `PreloadNextCbzIfAvailable()`を`Task.Run()`で非同期化
- [CbzManager.cs](CbzManager.cs#L256-L342) — `RefreshCurrentImagePaths()`からfire-and-forget呼び出し

**Verification**
1. ALT+左矢印でCBZ切り替え時の応答速度確認
2. キャッシュフォルダ（`%LOCALAPPDATA%\MangaViewer\CBZCache`）にハッシュ名フォルダが作成されているか確認
3. 2回目のCBZアクセス時にキャッシュヒットして即座に表示されるか確認

**Decisions**
- プリロードはfire-and-forget方式（完了を待たない）
- 現在のCBZ展開は同期のまま（キャッシュヒット時は高速なので問題なし）

**Further Considerations**
現在のCBZ展開も非同期化すべきか？
- Option A: 現在のまま（キャッシュヒット時は高速） — **推奨**
- Option B: 完全非同期化（ローディング表示が必要になる）

