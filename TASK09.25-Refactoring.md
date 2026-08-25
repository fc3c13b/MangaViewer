# TASK09.25 – Form1.cs リファクタリング計画（600行以下化）

## 概要
- 対象: `Form1.cs`（現在約 1462 行）
- ゴール:
  - Form1 を **600 行以内**に抑える
  - 既存のキー操作・動作は完全維持
  - ログ/残骸を整理し、可読性と保守性を向上

## 基本方針
- Form1 は「UI と配線」に特化させる。
- ビジネスロジック・フィルタリング・ナビゲーション計算を別ファイルへ分離。
- 新しいクラスは既存の動作と互換を保ちつつ、Form1 から委譲する形にする。

## 1. ログ・残骸の削減（全体）

### やること
- `error.log` / `build.log` / `LoadLogFile` の冗長なデバッグログを整理:
  - 開発用残骸的な出力は削除または `#if DEBUG` で囲む
  - Form1 / DisplayManager / KeyboardInputHandler に散在する重複ログパターンを共通化
- ただし:
  - クラッシュ調査に有用な本質的なエラーログは維持

### 効果
- 全体で数十〜百行程度の削減と見通し改善。

## 2. CjService.cs（新規）

### 目的
- CJ（Collection JSON）の読み込み・作成・適用に関するロジックを Form1 から分離。

### 移動対象（Form1 → CjService）
- `LoadCjForParent()` の実質処理:
  - CJ ファイルを検索し、DBList に適用するロジック
- `CreateCjForParent()` の実質処理:
  - 新しい CJ を作成・保存し、DBList に適用するロジック
- `_activeCjData` / `_activeDbFile` の管理を CjService が担う

### Form1 の残す範囲
- INavigationActions から呼ばれる薄いラッパーのみ:
  - `CreateCjForParent(string)` → `CjService.Create(...)`
  - `LoadCjForParent(string)` → `CjService.Load(...)`

## 3. DbListBuilder.cs（新規）

### 目的
- DBList のフィルタリング・再構築ロジックを Form1 から分離。

### 移動対象
- `BuildRankFilteredListFromActiveCj()`
- `BuildRankFilteredListFromCj(...)`
- リストの再構築と listBoxFolders の更新に関する一連の流れ

### 設計方針
- DbListBuilder は静的メソッドまたは小さなサービスとして:
  - 入力: CJデータ / フォルダリスト / 設定
  - 出力: フィルタリング済みフォルダリスト（_folderList）と listBoxFolders の表示用リスト
- Form1 では:
  - `DbListBuilder.Build(...)` の結果を適用するだけの薄いコード

## 4. NavigationHandler.cs（新規）

### 目的
- ページ送り・フォルダ移動・DisplayCount変更などの「ナビゲーション計算」を集約。

### 移動対象
- `NavigateForward(int pageCount)` の中核ロジック
- `NavigateBackward(int pageCount)` の中核ロジック
- `NavigateFolderBy(int delta)` / `NavigateFolders(int delta)` の共通ロジック
- `SetDisplayCount(int count)` で行われる状態変更・ログ処理の大部分

### 設計方針
- NavigationHandler は:
  - Form1 から必要な状態（ImageCount, DisplayCount, FolderListCount など）を受け取り、または INavigationActions を通じて利用する。
- Form1 では:
  - INavigationActions の実装を「NavigationHandler に委譲する薄いラッパー」に整理

## 5. StartupHandler.cs（新規）

### 目的
- `Form1_Load()` の長い初期化フローを整理し、Form1 を短く保つ。

### 移動対象
- `Form1_Load()` 内の:
  - Settings / FolderService / CbzManager / DisplayManager / ImageService の初期化順序
  - EnsureBasicLayout() と関連のレイアウト補正ロジック
  - バックグラウンドスレッドでの初期処理フロー

### StartupHandler.cs の役割
- `StartupHandler.Initialize(Form1, ...)` で:
  - 必要な依存関係を構築し、Form1 に注入する
  - エラーハンドリングとフォールバックの構造を整理
- Form1 では:
  - `Form1_Load()` が StartupHandler を呼び出すだけのシンプルな形

## 6. Form1.cs の最終的な役割

### Form1 で残すもの（約 500〜600 行）
- UI コントロールの生成・配置 (`InitializeComponent`, `UpdateLayout`)
- イベントハンドラ:
  - キー入力 → KeyboardInputHandler
  - フォルダ選択 → DBListBuilder / CjService 経由
- INavigationActions の実装:
  - NavigationHandler / CjService / DbListBuilder への委譲

### Form1 で外すもの
- ビジネスロジック（CJの読み書き・DBフィルタリング）
- ナビゲーション計算の詳細
- 冗長なログ出力と初期化フローの詳細

## 7. 新しいファイル一覧

- `CjService.cs`
- `DbListBuilder.cs`
- `NavigationHandler.cs`
- `StartupHandler.cs`

## 8. 品質・保守性への影響

- Form1 が短くなり、責務が明確になる。
- テスト容易性が向上（ロジックが独立したクラスに分離される）。
- キー操作や既存の動作は維持し、実装の詳細だけを移動する形にする。
