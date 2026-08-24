# TASK09.24 - DeleteFolderList (フォルダリストモード削除・DBList専用に整理)

## 概要
これまでの変更で、アプリは「CJ（アプリキャッシュJSON）に基づく DBList モード」を基本とする方針に確定した。
それに伴い、従来からの「フォルダ階層順リストモード」とキー7によるモード切替を削除し、
起動時から常に DBList モードとして動作するように仕様を整理する。

## 背景（合意済み）
- Q1: スタートアップは「前回使用したフォルダ＋その CJ を自動使用」。
- Q2: キー3の動作：
  - フォルダ選択ダイアログを表示
  - 選択フォルダをスキャンし、CJ を新規作成または更新
  - その CJ を使って DBList モードでリスト表示
- Q3: rank_display_db.json は不使用。今後は各ルートフォルダごとの CJ ファイルのみを使用。

## 変更方針（全体）
1. フォルダリストモードとモード切替キー（7）を削除し、DBList モード専用に固定する。
2. 起動時は前回使用したフォルダの CJ を自動読み込み。
3. キー3で「フォルダ選択＋CJ の生成/更新＋DBList に切り替え」を行う。
4. rank_display_db.json は参照せず、CJ ベースのロジックのみを使用する。

## 修正対象ファイル
- Form1.cs
- KeyboardInputHandler.cs
- INavigationActions.cs / Form1.cs（実装側）
- Settings.cs / SettingsManager.cs（LastRootFolder の追加）
- 必要に応じて CJManager.cs の呼び出し箇所

## 詳細仕様

### 1. モード切替の削除（キー7）
- KeyboardInputHandler.cs:
  - D7 キーで ToggleDisplayMode() を呼び出している処理を削除。
- Form1.cs:
  - ToggleDisplayMode() の実装を削除するか、空実装にする。
  - IsRankDisplayMode が存在する場合：
    - 常に true とみなすか、またはモード判定ロジックを DBList ベースに一本化する。
      （内部で必要なら残してもよいが、外部からモード切替できないようにする。）

### 2. 起動時の動作（DBList 自動読み込み）
- Settings.cs:
  - LastRootFolder { get; set; } = ""; を追加。
- Form1.cs（起動処理 / Load または Shown イベント）:
  - Settings から LastRootFolder を読み込む。
  - LastRootFolder が設定されており、かつそのフォルダの CJ が存在する場合：
    - その CJ を使って DBList モードでリスト表示を行う。
      （既存の LoadCjForParent または同等の処理を使用。）
  - それ以外の場合：
    - リストを空にするか、安全な初期状態とする（エラーにならないこと）。

### 3. キー3 の動作（フォルダ選択＋CJ生成/更新）
- KeyboardInputHandler.cs:
  - D3 キーの処理はそのまま維持。
  - DBList モードでは FolderBrowserDialog を表示し、選択後に CJ ロジックを呼び出す。
- Form1.cs / INavigationActions 実装:
  - CreateCjForParent(parent):
    - フォルダスキャンを行い、該当フォルダの CJ を新規作成または更新する処理を行う。
    - スキャン完了後、その CJ から DBList に再構築する。
  - LoadCjForParent(parent):
    - 既存 CJ を読み込み、DBList に反映する。
  - 最後に使用したフォルダ:
    - キー3で選択・適用したフォルダを Settings.LastRootFolder に保存する。

### 4. rank_display_db.json の不使用化
- Form1.cs および関連箇所:
  - rank_display_db.json を参照しているコードがあれば削除または無効化する。
  - リストの生成・ソートはすべて CjManager ベース（GetFoldersFromCj など）に統一する。
- CJManager.cs:
  - rank_display_db.json に依存する処理がある場合は、CJ のみに変更する。

### 5. フォルダリストモードの削除
- Form1.cs:
  - listBoxFolders が「フォルダ階層順モード」と「DBList モード」で分岐しているロジックを、
    DBList モードの動作に一本化する。
  - フォルダ階層順モード固有のフラグや処理は削除または無効化。
- KeyboardInputHandler.cs:
  - フォルダリストモード用の特別なキー割り当てがあれば削除する。

## テスト項目（簡易）
- アプリ起動時：
  - 前回使用したフォルダの CJ が正しく読み込まれ、DBList で表示されるか。
- キー3:
  - 新しいフォルダを選択するとスキャンされ、CJ が作成/更新されて DBList に反映されるか。
  - LastRootFolder が保存され、次回起動時に自動使用されるか。
- キー7:
  - モード切替が起きず、常に DBList の動作のみとなることを確認。
