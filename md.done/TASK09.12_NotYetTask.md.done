# TASK v0.9.12 未実装・不備箇所メモ（更新版）

前提と方針
- キー7: 通常表示モード ⇔ DB表示モード の切り替え（ToggleDisplayMode と整合）。
- D3 (通常モード): ルートフォルダ変更＋階層順リスト再構築（既存動作を明確化・維持）。
- D3 (DBモード): FolderBrowserDialog で親フォルダ指定 → CJ作成 → _dbFilterRating によるフィルタで DB リスト再構築。
- フィルタ条件:
  - デフォルト:_dbFilterRating = 8
  - Settings に「以上/一致」切替フラグを追加し、設定画面から制御可能とする。

## ✅ 完了済み

- [x] CJのJSON構造が規格通り定義されている
  - CjManager.cs: LoadCache / SaveCache で folders(parentFolder) マップ形式で読み書き
  - AppPaths.CacheDir = %APPDATA%\MangaViewer\cache を使用
  - GetCacheFilePath で親フォルダごとにユニークなファイル名を生成（EscapeForFileName）
- [x] ratings_cache.json は維持されている
  - Form1.cs: LoadRatingsCache() / SaveRatingsCache() が残っており、DBリスト表示のデータソースとして利用

## ❌ まだ未実装・修正必要な箇所

### 1. キー3（D3）の動作分岐を実装

- 要求:
  - 通常モード (IsRankDisplayMode == false):
    - D3 → ルートフォルダ変更ダイアログ表示 → 選択後、ChangeRootFolder で階層順リスト再構築。
  - DBモード (IsRankDisplayMode == true):
    - D3 → FolderBrowserDialog で親フォルダ指定 → CreateCjForParent(parent) を経由して CJ 作成
      → _dbFilterRating とフィルタ設定に従って DB リストを再構築。
- 現状:
  - KeyboardInputHandler.cs の D3 キー処理は HandleRootFolderChange(nav) のみ。
  - モードに応じた分岐がない。
- 必要な実装:
  - INavigationActions:
    - IsRankDisplayMode を参照可能な形で確保（既存 OK）。
  - KeyboardInputHandler:
    - D3 キーで:
      - if (nav.IsRankDisplayMode) → DBモードフロー (CreateCjForParent + フィルタ再構築)
      - else → 通常ルート変更フロー (ShowRootFolderDialog / ChangeRootFolder)

### 2. HandleRootFolderChange の分離

- 要求:
  - 「物理ルート変更用」と「CJ作成用」のフローを明確に分離。
- 現状:
  - KeyboardInputHandler.HandleRootFolderChange() が単一メソッドで nav.ShowRootFolderDialog() を呼び出すのみ。
- 必要な実装:
  - INavigationActions に別々のアクションを追加（例）:
    - ShowRootFolderDialog(); // 物理ルート変更用
    - CreateCjForParent(string parentFolder); // DBモード・CJ作成用
  - KeyboardInputHandler:
    - D3 キーでモードごとに異なる呼び出しを行う。
  - Form1:
    - ShowRootFolderDialog → ChangeRootFolder (階層順)
    - CreateCjForParent → CJ 保存 + DB リスト再構築

### 3. DBフィルター評価値 (_dbFilterRating) を定義・適用

- 要求:
  - DBリスト表示（評価値順）時のフィルター評価値は別変数で定義、デフォルト=8。
- 現状:
  - Form1.cs に _dbFilterRating のような専用変数が確認できていない。
- 必要な実装:
  - Form1.cs:
    - private int _dbFilterRating = 8; を追加。
  - DBリスト表示時 (BuildRankFilteredList / BuildRankFilteredListFromCj):
    - _dbFilterRating とフィルタモード（一致/以上）に従ってフォルダをフィルタリングする。

### 4. フィルタ条件切替（一致 ⇔ 以上）の設定化

- 要求:
  - 「_dbFilterRating と一致」か「_dbFilterRating 以上」かを設定画面で切り替えられるようにする。
- 必要な実装:
  - Settings.cs:
    - public bool DbFilterGreaterOrEqual { get; set; } = true; // デフォルト「以上」
  - SettingsDialog.cs:
    - チェックボックス追加: 「DB表示：評価値を『以上』でフィルタする」
      - ON → ≥ _dbFilterRating
      - OFF → == _dbFilterRating（一致のみ）
  - Form1.cs (BuildRankFilteredList / BuildRankFilteredListFromCj):
    - if (_settings.DbFilterGreaterOrEqual)
        条件 = entry.Rating >= targetRating;
      else
        条件 = entry.Rating == targetRating;

### 5. CJ作成＋評価値フィルターでのリスト更新フロー（DBモード）

- 要求:
  - DBリスト表示中にキー3 → 親フォルダ指定 → CJ作成 → その評価値でフィルターしたリストに更新。
- 必要な実装:
  - Form1.CreateCjForParent(parentFolder):
    - 同じ親フォルダの CJ が既にある場合は重複メッセージを表示（既存ロジック維持）。
    - サブフォルダ走査して CjFolderEntry を作成し、CjManager.SaveCj で保存。
    - IsRankDisplayMode == true の場合:
      - BuildRankFilteredListFromCj(folders) を呼び出し、_dbFilterRating とフィルタ設定に従ってリスト再構築。

## 📌 まとめ（次の実装タスク用）

- D3 キーの動作をモードごとに分岐させるロジックを追加
  - 通常: ルート変更＋階層順再構築
  - DB: CJ作成＋_dbFilterRating によるフィルタリング
- HandleRootFolderChange を「物理ルート変更」と「CJ作成」に分離
- _dbFilterRating を導入し、DBリスト表示時に適用
- フィルタ条件（一致/以上）を Settings から制御可能にする
- DBモードでの D3 キー処理で CJ 生成＋評価値フィルタリングによるリスト更新を実装
