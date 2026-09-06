# TASK09.40 – CBZキャッシュの非同期化とタイトルバー表示

## 概要

TASK09.32-ModifyCache-2 と TASK09.33 の未実装部分をまとめて修正する。

---

## 1) プリロードの非同期化（TASK09.32-ModifyCache-2）

### 問題点
現在のCBZ表示時に次のCBZも同期的に展開しているため、画像表示が遅くなる。
CbzManager.cs L373 で `PreloadNextCbzIfAvailable()` が同期的に呼び出されている。

### 修正箇所
- **CbzManager.cs**
  - `PreloadNextCbzIfAvailable()` を非同期メソッドに変更（async Task）
  - `RefreshCurrentImagePaths()` から fire-and-forget で呼び出す
  - `Task.Run()` でバックグラウンド実行し、現在のCBZ表示をブロックしない

### 実装方針
- プリロードは fire-and-forget 方式（完了を待たない）
- 現在のCBZ展開は同期のまま（キャッシュヒット時は高速なので問題なし）

---

## 2) タイトルバーにCBZキャッシュ一覧を表示（TASK09.33）

### 修正箇所
- **Form1.cs** に `UpdateWindowTitle()` メソッドを新規追加

### アルゴリズム
1. `_activeDbFile`がある場合、`dbName = Path.GetFileName(_activeDbFile)` を取得
2. 基本タイトル: `{AppTitle} - DBList [{dbName}]`
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
6. ソート後のCBZファイル名を"/"区切りで連結:
   - 10文字以下 → そのまま `.cbz` をつけて使用
     - 例: `vol1.cbz`
   - 11文字以上 → 先頭5文字 + "..." + 末尾5文字（計10文字）にして、`.cbz` をつける
     - 例: `ウルトラショック 第01巻.cbz` → `ウルトラシ...第01巻.cbz`
7. タイトル設定:
   - `this.Text = $"{baseTitle}{cbzList}"`

### 注意点
- キャッシュフォルダが存在しないCBZはリストに含めない（未展開＝まだ見ていない）
- パフォーマンスのため、ファイルシステムアクセスは軽量に保つ（GetFiles + GetCreationTimeのみ）
- エラー処理: ディレクトリ読み取り失敗時はcbzListを空でフォールバック

---

## 実装ステップ（推奨：2分割）

両タスクは相互に独立しているが、リスク管理のため以下の順序で実施する。

### Step 1: タイトルバーへのCBZキャッシュ一覧表示（TASK09.33）
- **リスク**: 低（読み取り専用処理、既存機能に影響なし）
- **実装箇所**: Form1.cs に `UpdateWindowTitle()` を新規追加
- **検証ポイント**:
  - タイトルバーにCBZキャッシュ一覧が表示されるか確認
  - ファイル名短縮ロジックが正しく動作するか（10文字以下はそのまま、11文字以上は先頭5+"..."+末尾5）
  - キャッシュフォルダが存在しないCBZはリストに含まれていないか
  - ソート順序が古い順（最初に展開したものが先頭）になっているか
- **ロールバック**: UpdateWindowTitle() メソッドの削除のみで完了

### Step 2: プリロードの非同期化（TASK09.32-ModifyCache-2）
- **リスク**: 中（非同期化により潜在的な競合状態の可能性がある）
- **実装箇所**: CbzManager.cs の `PreloadNextCbzIfAvailable()` を async Task に変更
- **検証ポイント**:
  - CBZ切り替え時の応答速度が改善されているか（ALT+左矢印で確認）
  - キャッシュ展開処理がバックグラウンドで動作しているか
  - プリロード完了後のキャッシュヒット確認
  - UIスレッドとの競合状態がないか（特にCacheChangedイベント呼び出し時）
- **ロールバック**: PreloadNextCbzIfAvailable() を同期メソッドに戻す

---

## 影響箇所

| ファイル | 変更内容 |
|----------|----------|
| `CbzManager.cs` | PreloadNextCbzIfAvailable() を async Task に変更、Task.Run で fire-and-forget |
| `Form1.cs` | UpdateWindowTitle() メソッドを新規追加、CacheChanged イベントで呼び出し |

---

## 検証方法

1. ALT+左矢印でCBZ切り替え時の応答速度確認（プリロードが同期ではなくなっているか）
2. キャッシュフォルダ（`%LOCALAPPDATA%\MangaViewer\CBZCache`）にハッシュ名フォルダが作成されているか確認
3. 2回目のCBZアクセス時にキャッシュヒットして即座に表示されるか確認
4. タイトルバーにCBZキャッシュ一覧が表示されているか確認（展開日時順、長いファイル名は短縮表示）
