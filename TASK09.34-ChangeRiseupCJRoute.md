# TASK09.34 – 起動時の CJ ルート変更（LoadCjForParent不使用）

## 目的

アプリ起動時に `LoadCjForParent` を経由せず、最後に使用した CJ ファイルをそのまま読み込み、フィルタリングしてリストと画像表示を行う。

- これにより、起動フローが安定し、不要な再構築・再検索を抑える。
- キー3（フォルダ変更）などの運用では引き続き `LoadCjForParent` を使用する。

## 概要

- アプリ起動時：
  - 最後に使用した CJ ファイルを復元
  - そのデータをフィルタリングして `_folderList` を構築
  - 通常通り画像表示を行う
- フォルダ変更（キー3など）時：
  - これまでの `LoadCjForParent` ベースのフローを引き続き利用

## 用語・役割

- `_activeCjData`: 最後に読み込んだ CJ(JSON) のデータをメモリ上で保持するフィールド。親フォルダ情報＋各フォルダのメタデータ（rating, CbzZipCount, imageCount, updatedAt など）。
- `BuildRankFilteredListFromActiveCj()`: `_activeCjData` をもとに、DB/設定条件でフィルタ・ソートし、表示用リスト `_folderList` を構築する処理。

## 変更範囲（設計）

### 1. Settings に LastCjFile を追加

- ファイル: `Settings.cs`
  - プロパティを追加:
    - `public string LastCjFile { get; set; } = "";`
- ファイル: `SettingsManager.cs`
  - 読み込み処理:
    - "LastCjFile" を文字列として復元（なければ空）。
  - 保存処理:
    - "LastCjFile" を JSON に出力。

### 2. アプリ起動フロー：LoadCjForParent を使わないように変更

- ファイル: `StartupHandler.cs`（RunBackgroundInit など）

#### 現在の動作（変更対象）

- LastRootFolder をもとに LoadCjForParent/CreateCjForParent を呼び出し、CJ を再構築・検索している。

#### 新しい動作

起動時フロー：

1) Settings から `LastCjFile` を取得
2) ファイルが存在するか確認:
   - 存在しない or パス不正 → キャッシュ復元フォールバックへ
3) `CjService.LoadCjFromFile(LastCjFile)` で CJ を読み込み
4) Form1（UI スレッド）で：
   - `_activeCjData = cjData`
   - `_activeDbFile = LastCjFile`
   - `_activeCjParentFolder = cjData.ParentFolder`
   - `BuildRankFilteredListFromActiveCj()` を呼び出し、リストを構築
5) `EvaluateLoadResult(...)` で妥当性を確認:
   - 成功 → `_dbLoadedOnce = true`（起動成功として確定）
6) 失敗または LastCjFile が無効な場合：
   - TryRestoreFromLatestCache（既存のキャッシュ復元ロジック）を試す

#### 制約

- この変更では、起動フローから LoadCjForParent の呼び出しを削除する。
- キー3などでのフォルダ切替処理はそのまま残し、LoadCjForParent を使い続ける。

### 3. LastCjFile の保存タイミング

- ファイル: `Form1.cs`（および必要に応じて関連箇所）

ルール：

- 実際に CJ を使用した際（_activeDbFile が設定・更新された際）に、そのパスを Settings.LastCjFile に反映し、SaveSettings する。
- 具体的には以下を含む：
  - キー3でフォルダを選択して新しい CJ を作成した場合
  - キャッシュ復元で有効な CJ ファイルを採用した場合

これにより次回起動時に同じ CJ が優先的に使われる。

## データフロー（起動時）

1) App startup:
   - Settings.Load → LastCjFile を取得
2) StartupHandler (BackgroundInit):
   - LastCjFile が有効なら LoadCjFromFile
   - 失敗なら TryRestoreFromLatestCache
3) Form1 (UI スレッド):
   - _activeCjData にセット
   - BuildRankFilteredListFromActiveCj() でフィルタ＆ソート → _folderList を構築
4) リスト表示・画像表示を通常フローで実行

## 影響範囲（確認事項）

- StartupHandler.cs:
  - 起動時の LoadCjForParent の呼び出しを削除または条件変更。
- Form1.cs:
  - LastCjFile の保存ロジック追加・調整。
- Settings.cs / SettingsManager.cs:
  - LastCjFile の永続化対応。

## 成功基準

- アプリ起動時：
  - 最後に使用した CJ ファイルがそのまま使われる（LoadCjForParent不使用）。
  - フィルタリングされたリストと画像表示が正常に行われる。
- キー3によるフォルダ変更：
  - LoadCjForParent を使い続け、新しい CJ が正しく作成・反映される。
  - 新しい CJ のパスが LastCjFile に保存され、次回起動で優先される。
