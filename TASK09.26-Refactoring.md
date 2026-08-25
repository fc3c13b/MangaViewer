# TASK09.26 – Refactoring Phase 2 (Form1 659行 → 600行以下)

## 前提・現状
- TASK09.25 で以下の分離済み：
  - CjService, DbListBuilder, NavigationHandler, StartupHandler, FormNavigator, ListBoxScrollHelper
- 現在の `Form1.cs` は約 **659 行**。
- ゴール:
  - 既存の動作・キー操作を維持したまま、Form1 を **600 行以内**に抑える。
  - コードを「UI＋配線」に特化させ、ビジネスロジックは外部クラスへ集約する。

## 全体方針（2回に分ける）
- Phase A: 「表示・画像読み込み・フォルダリスト構築」のロジック分離
- Phase B: 「ナビゲーション・CJ操作・UI更新」のさらに薄いラッパー化＋冗長削除
- 両フェーズで：
  - 新しいクラスは既存の公開インターフェースと互換を保つ。
  - 動作変更なし（リファクタリングのみ）。

---

## Phase A: Display / Image / FolderList ロジック分離（TASK09.26-A）

### 目的
- Form1 から「画像読み込み」「フォルダリスト構築」「表示制御」の詳細を抜き出し、Form1 を短くする。

### やること

#### 1) `ImageLoader.cs`（新規）
Form1 の以下の処理を集約：
- `LoadAndSortImages(folderPath)`
  - FolderService と CbzManager を使い、画像リストを作成するロジック全体を移管。
- 変更点:
  - Form1 では:
    - `ImageLoader.Load(this, folderPath)` を呼び出すだけのラッパーにする。

#### 2) `FolderListBuilder.cs`（新規）
Form1 の以下の処理を集約：
- `BuildSubfolderList(rootPath)`
  - FolderService と RatingService を使ったフォルダ一覧＋リストボックス更新ロジック全体を移管。
- 変更点:
  - Form1 では:
    - `FolderListBuilder.Build(this, rootPath)` の呼び出しに置き換え、行数を削減。

#### 3) `DisplayController.cs`（新規）
Form1 の以下の処理を集約：
- `DisplayImages(startIndex)`
  - DisplayManager と labelInfo 更新＋CBZ preload ロジックを一元化。
- `UpdateInfoLabelAfterToggle()` / `BuildFolderDisplayWithCbz()` もここに統合。
- 変更点:
  - Form1 では:
    - `DisplayController.Display(this, startIndex)`
    - `DisplayController.UpdateInfoLabelForFullScreen(this)`
    - これらの呼び出しに置き換え、重複コードを削減。

### Phase A の効果（概算）
- Form1 から約 **80〜120 行**のロジックが外部へ移動。
- 結果: 659 → およそ 540〜570 行程度に低下し、600 行未満に近い状態になる。

---

## Phase B: INavigationActions / CJ / UI のさらに薄層化＋冗長削除（TASK09.26-B）

### 目的
- Form1 が INavigationActions を実装する部分と CJ/ルート変更関連の処理を、FormNavigator/CjService へ完全に委譲し、Form1 を最終的に **600 行以下**に安定させる。

### やること

#### 1) `FormNavigator.cs` の拡張
Phase A で作成したクラスと連携させつつ：
- Form1 に残る以下の処理をさらに薄く:
  - `NavigateBackwardTwoPages()`
    - FormNavigator にロジックを集約し、Form1 は呼び出しのみ。
  - `ShowRootFolderDialog()` / `ChangeRootFolder(rootPath)`
    - ルート変更＋DBList再構築フローを FormNavigator に一元化。
- これにより:
  - INavigationActions の実装が「FormNavigator.xxx(this, ...);」のラッパーに統一される。

#### 2) CJ/DB 関連処理の整理（CjService / DbListBuilder と連携）
- `LoadCjForParent()` / `CreateCjForParent()` はすでに CjService を利用しているが、Form1 内の冗長なエラーハンドリング・ログを整理:
  - エラーダイアログは最小限に。
  - BuildRankFilteredListFromActiveCj の呼び出しは DbListBuilder/FormNavigator と連携した形に統一。

#### 3) コメント・ログの削減（全体）
- Form1 および関連ファイルで：
  - 開発残骸的なコメントや冗長な説明を削除または短縮。
  - クラッシュ調査用以外の詳細ログは削る。
- これにより追加で数十行の削減が見込める。

### Phase B の効果（概算）
- Form1 からさらに約 **30〜60 行**削減。
- 最終的に:
  - Form1 が安定して **550〜590 行程度**に収まる。
  - 責務が明確：
    - UI コントロール管理
    - イベントハンドラ（キー入力・ListBox操作）
    - 外部サービス（FormNavigator / CjService / DbListBuilder / ImageLoader など）への委譲

---

## ファイル変更一覧（まとめ）

### Phase A で追加/修正
- 新規:
  - `ImageLoader.cs`
  - `FolderListBuilder.cs`
  - `DisplayController.cs`
- 修正:
  - `Form1.cs`: LoadAndSortImages / BuildSubfolderList / DisplayImages 関連を新しいクラスへ委譲し、行数削減。

### Phase B で追加/修正
- 新規: なし（既存の FormNavigator/CjService/DbListBuilder を強化）。
- 修正:
  - `Form1.cs`: INavigationActions の実装をさらに薄く、CJ/ルート変更フローを整理。
  - `FormNavigator.cs`: ナビゲーション＋ルート変更ロジックを集約・補完。

## 品質・保守性への影響
- Form1 が純粋な「UI＋配線」に近づく。
- テスト容易性が向上（ロジックが独立したクラスに分離される）。
- キー操作や既存の動作は維持し、実装の詳細だけを移動する形にする。
