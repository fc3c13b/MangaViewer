# TASK v0.9.14 - DBList を CJ ベースに再設計（CreateCJ-2）

## 概要

DBリスト表示モードのデータ源を「rank_display_db.json のみ」から
「キー3で選択した親フォルダーに対応する CJ ファイル」ベースに変更し、
タイトルバーにも現在アクティブな DB/CJ を明示する。

rank_display_db.json は過渡期用のダミーとして一時的に残し、
CJ が安定運用できたら削除する。

## 用語定義

- DJ（Data JSON）: データフォルダー内のコンテンツ毎の JSON。
- CJ（Cache JSON）: %APPDATA%\MangaViewer\cache に保存される親フォルダーごとのキャッシュJSON。
  - ファイル名例: ratings_cache_O__NEW.json, ratings_cache_F__MangaDL_wnacg_同人誌.json

## DBList モードのデータ源（変更）

### 基本ルール

- DBList モードでは、キー3で親フォルダーを選択した際：
  - その親フォルダーに対応する CJ ファイルを「DB」として使用する。
  - まだ存在しない場合は新規作成（CJ生成ロジックに従う）。
- DBList で表示するフォルダーリストは、アクティブな CJ の folders から
  評価値順に取得・フィルタリングする。

### アクティブな DB/CJ が未選択時の挙動

- DBList モードだが有効な CJ/DB が未選択の場合：
  - ダミーとして rank_display_db.json の内容を一時的に表示し続ける（現状維持）。
  - これは過渡期用；CJ安定後、rank_display_db.json は削除予定。

## キー3の動作（DBList モード時）

- DBList モードでキー3を押した場合：
  - FolderBrowserDialog で親フォルダーを選択。
  - その親フォルダーに対応する CJ ファイルを DB と見なす。
    - 存在する場合: その CJ をアクティブ DB に設定。
    - 未作成の場合: CJ を新規作成後、それをアクティブ DB に設定。
  - アクティブ DB が確定したら、その CJ の folders から評価値順でリストを更新。

## タイトルバー表記（DBList モード）

- DBList モードでは、現在アクティブな DB/CJ のファイル名をタイトルに表示:
  - 例 (CJ): Manga Viewer v0.9.13 - DBList [ratings_cache_O__NEW.json]
  - 例 (ダミー): Manga Viewer v0.9.13 - DBList [rank_display_db.json]

- FolderList モードは従来通り:
  - 例: Manga Viewer v0.9.13 - FolderList [O:\NEW]

## rank_display_db.json の扱い

- 現状:
  - DBリスト表示のテスト用DBとして使用。
- これから:
  - アクティブな CJ が未選択時のダミーデータ源として一時的に残す。
  - CJ ベースの運用が安定したら、rank_display_db.json とその参照コードを削除する（別タスクで実施可）。

## 完了条件

- [ ] DBList モードでキー3を押すと、親フォルダー選択後に対応する CJ をアクティブ DB に使用する。
- [ ] アクティブな CJ が存在する場合、DBList はその CJ の folders から評価値順に表示する。
- [ ] アクティブな CJ が未選択の場合、rank_display_db.json の内容を一時的に表示し続ける。
- [ ] タイトルバーで DBList モード時はアクティブな DB/CJ ファイル名を表示する（上記形式）。