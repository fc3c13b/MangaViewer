# TASK 09.22 – CJ ロード時の差分更新ロジック変更

## 目的

既存の CJ（キャッシュJSON）をロードする際、子フォルダの有効性を確認し、
必要に応じて再スキャンしてデータを最新化する。
不要なエントリは削除し、新規フォルダは自動的に追加する。

## 新しい動作仕様

親フォルダ指定時に CJ を作成・更新する処理（FolderService.BuildFolderIndexWithCj など）を以下のように変更する：

1. CJ のメモリコピー
   - 既存の CJ ファイルがあれば読み込み、メモリ上に CjRoot を構築。
   - なければ空の CjRoot を新規作成。

2. 実際に存在する子フォルダごとに処理（上から順に）
   - ディスク上の親フォルダ直下の全サブフォルダを取得。
   - それぞれについて：

     A) CJ にその子フォルダのエントリが存在する場合
        - 比較基準: Directory.GetLastWriteTime(subfolder) と cjEntry.updatedAt を比較。
          - updatedAt が新しい（または等しい）場合:
            - そのまま使用（変更なし）。
          - updatedAt が古い場合:
            - その子フォルダを再スキャンする。
              - DJ（フォルダ内 .json）が存在する場合：既存の BuildFolderIndex と同じ「DJ ベースの特殊ルール」に従って imageCount / cbzFileCount / rating を決定し、CJ エントリを更新。
              - DJ が存在しない場合：画像数・CBZ/ZIP数を直接カウントして更新。
            - updatedAt を現在時刻に更新。

     B) CJ にその子フォルダのエントリが存在しない場合（新規追加）
        - その場でスキャンして CjFolderEntry を作成し、CjRoot.Folders に追加：
          - DJ が存在する場合：既存の BuildFolderIndex と同じ「DJ ベースの特殊ルール」に従って imageCount / cbzFileCount / rating を決定。
          - DJ が存在しない場合：画像数・CBZ/ZIP数を直接カウント（rating は未設定）。

3. CJ にあるが実際に存在しないフォルダの処理
   - 全サブフォルダ処理終了後、ディスク上にディレクトリが存在しないエントリを CJ から削除。

4. 全子フォルダの処理終了後
   - 更新された CjRoot をディスクに保存（上書き）。

## スキャンに関する注意点

- 「ディレクトリスキャンは一度だけ行い結果を再利用する」という記述は誤り。
- ディスク上のサブフォルダ一覧は一度取得するが、各子フォルダに対しては必要に応じて個別にスキャン・更新を行う。
  - つまり：updatedAt が古ければそのフォルダのみ再スキャンし、DJ ベースのルールを適用可能。

## DJ ベースの特殊ルール（CJ作成時に利用）

- CJ を新規作成または再スキャンする際、該当フォルダに DJ（.json）が存在すれば、
  BuildFolderIndex() で使用されている既存のロジック（規則1〜4など）に従って
  imageCount / cbzFileCount / rating を決定する。
- これにより、CJ と既存動作との整合性が保たれる。

## 影響箇所

- FolderService.cs:
  - BuildFolderIndexWithCj() のロジックを書き換え、上記フローに従う。
  - CJ 新規作成・再スキャン時は DJ ベースの特殊ルールを適用するよう修正。
- CJManager.cs:
  - 必要に応じて LoadCj / SaveCj の呼び出し箇所を調整（構造自体は維持）。

## 制約・注意点

- タイムアウト機構（30秒）はそのまま維持。
- UnauthorizedAccessException やその他の例外で個々のフォルダが失敗しても、全体処理を継続。
- フィルタ条件（MinDisplayCount / MaxDisplayCount / MinEvaluation など）は既存と同じルールを使用。

## 完了条件

- BuildFolderIndexWithCj() が上記フローで動作することを確認。
- アプリ起動時およびフォルダ切替時に CJ の差分更新が正しく行われることを確認。