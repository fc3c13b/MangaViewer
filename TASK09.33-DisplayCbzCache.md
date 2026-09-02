# TASK09.33: タイトルバーにCBZキャッシュ一覧を表示（展開日時順）

## 概要
アプリタイトル → VER → CacheJsonファイル名の後ろに、"/"区切りでCBZキャッシュのファイル名を全て表示する。
ソートは「CBZを展開した日時（＝キャッシュフォルダ作成日時）」の古い順とする。

## 修正箇所
- `Form1.cs` の `UpdateWindowTitle()` メソッド

## 現在の動作
```
"MangaViewer - DBList [ratings_cache_O_NEW3.json]"
```

## 変更後の動作例
```
"MangaViewer - DBList [ratings_cache_O_NEW3.json] / vol1.cbz / vol2.cbz"
```

## アルゴリズム（UpdateWindowTitle内）

1. `_activeDbFile`がある場合、dbName = `Path.GetFileName(_activeDbFile)` を取得
2. 基本タイトル: `"{AppTitle} - DBList [{dbName}]"`
3. CBZリスト構築:
   - `_currentFolder` がnullまたは空なら終了
   - `Directory.GetFiles(_currentFolder, "*.cbz")` でCBZファイルを列挙
   - もしCBZファイルが0件なら終了
4. 各CBZについて、対応するキャッシュフォルダの作成日時を取得:
   - キャッシュルート: `%LOCALAPPDATA%\MangaViewer\CBZCache`
   - ハッシュ名: CBZフルパスのMD5（CbzManager.GetCacheDirectoryと同じロジック）
   - `cacheDir = Path.Combine(cacheRoot, md5Hash)`
   - そのディレクトリの `CreationTime` をソートキーとする
5. キャッシュフォルダが存在するCBZのみを対象に、CreationTimeで昇順ソート（古い順＝最初に展開したものが先頭）
6. ソート後のCBZファイル名を"/"区切りで連結（長すぎる場合は短縮表示）:
   - 各CBZについて、拡張子を除いた名前部分の文字数を確認:
     - 10文字以下 → そのまま `.cbz` をつけて使用
       - 例: `vol1.cbz`
     - 11文字以上 → 先頭5文字 + 末尾5文字（計10文字）にして、`.cbz` をつける
       - 例: `ウルトラショック 第01巻.cbz` → `ウルトラシ...第01巻.cbz`
   - 最終的な連結例:
     - `cbzList = " / ウルトラシ...第01巻.cbz / ウルトラシ...第02巻.cbz / ウルトラシ...第03巻.cbz"`
7. タイトル設定:
   - `this.Text = $"{baseTitle} {cbzList}"`

## 注意点
- キャッシュフォルダが存在しないCBZはリストに含めない（未展開＝まだ見ていない）
- パフォーマンスのため、ファイルシステムアクセスは軽量に保つ（GetFiles + GetCreationTimeのみ）
- エラー処理: ディレクトリ読み取り失敗時はcbzListを空でフォールバック
