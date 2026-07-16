# MangaViewer リファクタリング計画

## 概要

Form1.cs の God Class を分解し、保守性・拡張性を高める。
各 Phase を完了するたびに Git コミットを行う。
Commit message フォーマット: `refactor: Phase N - 変更内容 (vX.X)`

---

## Phase 1: 基本インフラ整理 (v2.1)

- **目標**: 構造を整え、後続の分割を安全に行えるようにする
- **実装内容**:
  - `AppPaths.cs`: SettingsFilePath など「固定パス」の管理
  - `Constants.cs`: RatioLeftImg / TotalRatio / MaxCacheSize などの一元化
- **やること**:
  - Form1 からハードコードされた数値・パスを Constants/AppPaths に移動
- **ステータス**: [ ] 未完了

---

## Phase 2: 設定周り分離 (v2.2)

- **目標**: setting.json の読み書きを一箇所に集中
- **実装内容**:
  - `Settings.cs`: 設定モデル（MinDisplayCountEnabled, MinDisplayCount, MinEvaluation, LastRootFolder など）
  - `SettingsManager.cs`: Load / Save を担当（JSON 処理はここと AppPaths のみ）
- **やること**:
  - Form1.LoadSettingsFromFile / SaveRootFolder を SettingsManager に移動
  - 戻り値を Settings オブジェクトに
- **ステータス**: [x] 完了 (v3.5.3)

---

## Phase 3: フォルダ管理分離 (v2.3)

- **目標**: フォルダリスト・話送り・JSON キャッシュ関連の整理
- **実装内容**:
  - `FolderService.cs`: BuildSubfolderList / CountImages / ReadFolderJson / SaveImageCountJson を移動
    - 最小表示枚数フィルタもここで担当
- **やること**:
  - Form1.BuildSubfolderList などフォルダ関連ロジックを FolderService に移す
  - Form1 は `FolderService.GetFilteredFolders(rootPath, settings)` の結果を使って ListBox にバインド
- **ステータス**: [x] 完了 (v3.5.3)

---

## Phase 4: ナビゲーション・状態管理 (v2.4) ※重要

- **目標**: KeyboardInputHandler が Form に依存するのを減らす
- **実装内容**:
  - `NavigationState.cs`: ImagePaths, CurrentIndex, CurrentFolder, FolderList, CurrentFolderIndex など
  - `INavigationActions.cs`:
    - NavigateForwardTwo()
    - NavigateBackwardTwo()
    - ChangeFolderUp()/Down()
    - SetRoot(rootPath)
- **やること**:
  - Form1 の内部状態を NavigationState に移動
  - KeyboardInputHandler は INavigationActions を使うように変更
  - これにより「Form1.cs の internal フィールドの増殖」を抑える
- **ステータス**: [x] 完了（KeyboardInputHandler.cs に分離済み、v3.5.2）

---

## Phase 5: 画像表示・キャッシュ分離 (v3.6.0)

- **目標**: 読み込み / キャッシュ / Dispose を一箇所に
- **実装内容**:
  - `ImageService.cs`: LoadOrGetCachedImage / ClearCache / Dispose(oldImage)
    - SkiaSharp を使う箇所は ImageService のみに限定
- **やること**:
  - Form1.DisplayTwoImages は「ImageService から画像を取得 → PictureBox にセット」に専念
- **ステータス**: [x] 完了 (v3.6.0)

---

## Phase 6: レーティング分離 (v2.6)

- **目標**: 評価値ロジックを Form から完全に切り離す
- **実装内容**:
  - `RatingService.cs`: SaveRating(folderPath, rating), ReadRating(folderPath)
- **やること**:
  - KeyboardInputHandler で直接 Form.SaveRatingToFolder を呼ぶのではなく、RatingService を経由
- **ステータス**: [ ] 未完了

---

## Phase 7: キー入力・DI 化 (v2.7)

- **目標**: KeyboardInputHandler の依存関係をクリーンに
- **実装内容**:
  - `KeyboardInputHandler`:
    - 引数: `(KeyEventArgs, INavigationActions, Func<DialogResult> showSettings, Func<string> selectRootFolder)` など
  - Form はインフラ（ダイアログ表示）を、ロジックは NavigationState / Services に分ける
- **やること**:
  - KeyboardInputHandler から Form1 の直接参照をなくす
- **ステータス**: [ ] 未完了

---

## Phase 8: 全機能統合テスト・クリーンアップ (v2.8)

- **目標**: リファクタリング後の整合性確保
- **やること**:
  - 動作確認（ページ送り / フォルダ移動 / 設定 / キャッシュ / レーティング）
  - `dotnet build` で Warning/Errors = 0
  - DEVELOPITEM.md に記録
- **ステータス**: [ ] 未完了

---

## 目指す構成

```
Form1 (UI 描画・イベント統合)
├── KeyboardInputHandler ── INavigationActions / NavigationState
├── SettingsManager ─── Settings.cs (model)
├── FolderService (フォルダリスト・フィルタ・JSON キャッシュ)
├── ImageService (画像読み込み・キャッシュ・SkiaSharp)
├── RatingService (評価値保存/読込)
├── Constants / AppPaths (定数・パス管理)
```




