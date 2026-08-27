# TASK 09.27 – DBList が空になる問題を修正（起動時 CJ ロード改善）

## 概要

起動時に「DBList が空です。キー3でフォルダを選択してください。」と表示され、既存の ratings_cache_*.json が使われない問題を修正する。

## 原因分析

現在の StartupHandler.cs の動作フロー:

1. `LastRootFolder` が設定されていればそれを元に CJ を読み込み
2. DBList が空の場合に `TryRestoreFromCache()` で ratings_cache_*.json から復元を試みる
3. ここで **重大な欠陥** がある:

   - `InferParentFolderFromCacheName()` がキャッシュファイル名から親フォルダを推測しようとしている
     - 例: "O__NEW3" → "O:\NEW\3" のように `_` を `\` に単純置換
   - しかし実際のパスは複雑な場合が多く、この推測ロジックはほぼ失敗する
   - その結果、有効なキャッシュファイルがあっても `Directory.Exists(reconstructedPath)` が false になり、復元されない

**本質的な問題:**
- ratings_cache_*.json の中身には正しい `parentFolder` フィールドが含まれているにもかかわらず、それを無視してファイル名からの推測に頼っている。

## 修正方針（Plan1）

### 変更対象: StartupHandler.cs

#### (1) TryRestoreFromCache() を修正

- キャッシュファイルを CjService.LoadCjFromFile() で読み込んだ後、
  ファイル名の推測ではなく **JSON に保存された parentFolder フィールド** を使う。

Before（概念的）:
```
var cjData = CjService.LoadCjFromFile(cacheFile);
if (cjData?.Folders != null && cjData.Folders.Count > 0) {
    string? inferredRoot = InferParentFolderFromCacheName(fileName); // ← ここで推測
    if (!string.IsNullOrEmpty(inferredRoot) && Directory.Exists(inferredRoot)) {
        form._activeCjData = cjData;
        form._activeDbFile = cacheFile;
        ...
    }
}
```

After:
```
var cjData = CjService.LoadCjFromFile(cacheFile);
if (cjData?.Folders != null && cjData.Folders.Count > 0) {
    // JSON に保存された parentFolder を優先使用
    string? restoredRoot = cjData.ParentFolder;
    
    // ParentFolder が空または無効な場合は、ファイル名からの推測にフォールバック
    if (string.IsNullOrEmpty(restoredRoot) || !Directory.Exists(restoredRoot)) {
        restoredRoot = InferParentFolderFromCacheName(fileName);
    }

    if (!string.IsNullOrEmpty(restoredRoot) && Directory.Exists(restoredRoot)) {
        form._activeCjData = cjData;
        form._activeDbFile = cacheFile;
        ...
    }
}
```

#### (2) InferParentFolderFromCacheName() の扱い

- 完全削除はせず、フォールバック用に残す。
- ただし優先度を下げる: JSON の parentFolder が正しければそちらを使うため、推測ロジックの失敗が全体に影響しない。

### 期待される動作（修正後）

1. アプリ起動 → LastRootFolder から CJ ロードを試みる
2. DBList が空 or LastRootFolder が無効なら:
   - ratings_cache_*.json を最新順に確認
   - JSON の parentFolder フィールドを使って直接復元
3. 有効なキャッシュがあれば、DBList にフォルダ一覧が正しく表示される

## テスト方法

1. アプリ起動 → DBList に過去のフォルダ一覧が表示されることを確認
2. LastRootFolder を削除または無効化した場合でも、ratings_cache_*.json から復元されることを確認
3. 複数のキャッシュファイルがある場合、最新のものから順に復元されることを確認
