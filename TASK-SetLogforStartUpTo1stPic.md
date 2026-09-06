 # TASK: 起動～初回画像表示までのログ追加
 
 ## 目的
 `TASK-SetStartupLog.md` で既に CBZ 展開までのログ (#1〜#6) を実装済み。
 **CBZ キャッシュの読み込みから、最初の画像が PictureBox に表示されるまで** のログを追加し、
 「なぜ画像が表示されない」問題の切り分けを可能にする。
 
 ## 既存ログ (#1〜#6) の補完
 | # | 内容 | ファイル | ステータス |
 |---|------|----------|------------|
 | 1 | CBZ キャッシュ HIT | CbzManager.cs | ✅ 完了 |
 | 2 | CBZ キャッシュ MISS | CbzManager.cs | ✅ 完了 |
 | 3 | RefreshCurrentImagePaths 結果 | CbzManager.cs | ✅ 完了 |
 | 4 | ExtractToDirectory 開始 | CbzManager.cs | ✅ 完了 |
 | 5 | ImageLoader.Load エントリ | ImageLoader.cs | ✅ 完了 |
 | 6 | CBZ コレクション完了 | ImageLoader.cs | ✅ 完了 |
 
 ## 追加するログ (#7〜#9)
 
 | # | 位置 | ファイル | 内容 |
 |---|------|----------|------|
 | **7** | `DisplayManager.DisplayImages()` エントリ | DisplayManager.cs | `[DISPLAY] Start: startIndex={N}, DisplayCount={C}, ImagePaths.Count={M}` |
 | **8** | 初回画像ロード完了 | DisplayManager.cs (DisplayImages ループ内) | `[DISPLAY] First image loaded: {path}` |
 | **9** | `RunBackgroundInit()` 最終完了 | StartupHandler.cs | `[BG-FINAL] Images loaded={N}, firstImage={path}` |
 
 ## 診断フロー（新規）
 ```
 RunBackgroundInit() (UI スレッド外)
     └─→ DBロード完了 → InvokeIfSafe(form, () => { ... })  // UI スレッド
         ├─→ LoadAndSortImages(selectedFolder)   ← #5,#6 (既存)
         └─→ DisplayImages(0)                    ← #7
             └─→ LoadImageIntoPictureBox(pb, imagePath)  ← #8 (i=0 の初回画像のみ)
         └─→ labelInfo.Text = "初期化完了"
     └─→ [BG-FINAL]                            ← #9 (InvokeIfSafe 内)
 ```
 
 ## スタートアップログの出力先について
 - **既存**: `StartupHandler.StartupLog()` → `startup-{yyyyMMdd_HHmmss}.log`
 - **注意**: 既に DisplayManager.cs で `StartupHandler.LogError()` が使われているが、
   これは `error.log` に出力されるため **新ログには `StartupLog()` を使う**
 - DisplayImages は UI スレッド上から呼ばれるため、`StartupLog()` をそのまま呼び出せる
 
 ## 実装詳細
 
 ### #7: DisplayImages() エントリ
 **ファイル**: `DisplayManager.cs` L88
 
 ```csharp
 public void DisplayImages(int startIndex)
 {
     StartupHandler.StartupLog($"[DISPLAY] Start: startIndex={startIndex}, DisplayCount={DisplayCount}, ImagePaths.Count={ImagePaths.Count}");
     // ... 既存コード
 }
 ```
 
 ### #8: LoadImageIntoPictureBox() 初回画像完了
 **ファイル**: `DisplayManager.cs` L169
 
 DisplayImages() ループ内で i==0 の画像がロード成功した時点でログ出力:
 ```csharp
 // DisplayImages() 内のループ (L109〜)
 for (int i = 0; i < count; i++)
 {
     // ... 既存コード
     LoadImageIntoPictureBox(pictureBoxes[i], ref currentImages![i], imagePath);
 
     if (i == 0 && pictureBoxes[i].Image != null)
         StartupHandler.StartupLog($"[DISPLAY] First image loaded: {Path.GetFileName(imagePath)}");
 }
 ```
 
 ### #9: RunBackgroundInit() 最終完了
 **ファイル**: `StartupHandler.cs` L142〜159 (InvokeIfSafe 内)
 
 ```csharp
 InvokeIfSafe(form, () =>
 {
     string selectedFolder = EnsureValidFolderIndex(form);
     form.LoadAndSortImages(selectedFolder);
     form._currentIndex = 0;
     form._displayManager.ImagePaths = form._imagePaths;
     form._displayManager.InitializePictureBoxes();
 
     form.PerformLayout();
     form.UpdateLayout();
 
     form._displayManager.DisplayImages(0);  // #7 がここで呼ばれる
     if (listBoxFoldersSafe(form, out var lb))
         lb.SelectedIndex = form._currentFolderIndex;
 
     // 初回画像の情報を取得
     string? firstImage = form._imagePaths.Count > 0 ? form._imagePaths[0] : null;
 
     form.labelInfo.Text = "初期化完了";
     StartupLog("[STATUS] labelInfo set to '初期化完了'");
     form.UpdateWindowTitle();
 
     // #9: 最終サマリー
     StartupLog($"[BG-FINAL] Images loaded={form._imagePaths.Count}, firstImage={(string.IsNullOrEmpty(firstImage) ? "(none)" : Path.GetFileName(firstImage))}");
 });
 ```
 
 ## 注意事項
 - `StartupHandler.Log()` は obsolete なので使わない（#401）
 - 既存の `StartupHandler.LogError()` は error.log に出力するため、新ログには使わない
 - DisplayManager.cs 内の既存の `AppendAllText("error.log")` はそのまま（後で purge 対象）
 - ログ出力は try-catch で囲み、失敗しても UI ブロックしないようにする
 
 ## 既存の error.log ログとの区別
 DisplayImages() 内には既に大量の `AppendAllText("error.log")` が存在する（L90, L97, L108, L116, L171）。
 これらは起動時のデバッグ用だが error.log に混入するため、新ログは `StartupLog()` を使う。
 #9 完了後、既存の error.log ログは削除対象とする。
 
 ## 完成イメージ
 ```
 [2026-09-04 15:30:01.234] [BG-FINAL] Images loaded=1619, firstImage=001.png
 ```
 この 1 行があれば、CBZ 展開完了 (#6) から画像表示 (#8) までの「見えない部分」で
 何が起こっているかが把握できる。
 
 ## 関連ファイル
 - `TASK-SetStartupLog.md` - 既存ログ (#1〜#6) の仕様
 - `TASK09.35-DebugStartup.md` - 起動パフォーマンスの分析結果