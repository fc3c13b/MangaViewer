# TASK v0.9.13 - 表示モードと現在パスのタイトルバー表示

## 概要

ウィンドウタイトルバーに「現在のリスト表示モード」と「対象パス／DB名」を表示し、ユーザーがどのデータ源を見ているかを一目で確認できるようにする。

## バージョン

- csproj の Version を `0.9.13` に更新

## 要件

### 1. タイトルバーの構成

現在のタイトル例:
- Manga Viewer v0.9.12

変更後の形式:
- [FolderList] O:\NEW
- [DBList] rank_display_db.json

ルール:
- アプリ名＋バージョンはそのまま維持
- その後にモードとパス／DB名を区切り記号で追加
- 例:
  - Manga Viewer v0.9.13 - FolderList [O:\NEW]
  - Manga Viewer v0.9.13 - DBList [rank_display_db.json]

### 2. モード判定ロジック

既存の `_displayMode` を利用:
- 0 → "FolderList"（フォルダー階層順）
- 1 → "DBList"（評価値ランク順）

更新タイミング:
- ToggleDisplayMode() でモード切替時
- フォルダリスト再構築時（ルート変更・再検索など）
- DBリスト表示に切り替え時

### 3. パス／DB名の取得

- FolderList モード:
  - 現在のルートフォルダーパスを表示
  - 例: O:\NEW / F:\MangaDL\wnacg\...
- DBList モード:
  - 使用しているDBファイル名のみ表示（フルパス不要）
  - 例: rank_display_db.json

### 4. 実装方針

- Form1.cs のタイトル設定箇所を一元管理するメソッドにまとめる:
  - UpdateWindowTitle()
- このメソッドから:
  - _displayMode でモード判定
  - 現在のルートパスまたはDBファイル名を取得
  - タイトル文字列を組み立てて this.Text に設定

## 変更対象ファイル（予定）

| ファイル | 内容 |
|----------|------|
| MangaViewer.csproj | Version を 0.9.13 に更新 |
| Form1.cs | UpdateWindowTitle() を追加し、モード・パスに応じたタイトル表示を実装。既存の this.Text 設定箇所から呼び出しに統合 |

## 完了条件

- [ ] バージョンが 0.9.13 になっている
- [ ] FolderList モードでルートフォルダパスが [] で表示される
- [ ] DBList モードで DB ファイル名が [] で表示される
- [ ] モード切替時にタイトルが正しく更新される
