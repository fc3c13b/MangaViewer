# TASK09.29 – DBリスト読み込みを「起動時1回のみ」に限定する

## 目的
- アプリ起動時に「1つのDB（JSON）」を読み込み、DBリストを作成・更新し ListBox に表示。
- その後、同じ DB の再読込・監視・自動更新は一切行わない（一度きりの静的読み込み）。

## 要件（確認済み）
- A: 既存 CJ システムで自動探される JSON をそのまま使う。
- B: 「DBの読み込み処理は終わる」の意味：
  - アプリ起動時に DB(JSON) を1回読む → リストに設定 → その後、同じ DB の再読込・監視・自動更新は一切しない（静的な読み込み）。

## 対象箇所
- StartupHandler.cs
- Form1.cs
- DbListBuilder.cs
- CjService.cs（必要に応じてガード追加）

## 実装計画

### 1. StartupHandler.Initialize を「DB読み込み＝ここで終わり」に整理
- RootFolder の決定ロジックは現状維持。
- StartupHandler.Initialize で：
  - Form1.LoadCjForParentInternal(rootFolder, autoCreate: true) を呼び、DB(JSON) を1回だけ読み込む。
    - ここで _activeCjData に保持。
  - 次に BuildRankFiltered(this) を呼び：
    - DbListBuilder.BuildFromActiveCj(_activeCjData, ...) でフォルダリストを構築
    - listBoxFolders に設定・表示
- ここまでで「DBの読み込み処理は完了」とみなし、以降同じ DB の再読込を行わない。

### 2. LoadCjForParentInternal を「1回だけ」に制限（Form1.cs）
- 同じ parentFolder で _activeCjData が既にセット済みなら早期リターンするガードを強化：
  - if (_activeCjParentFolder == parentFolder && _activeCjData != null) → return;
- CjService.FindAndLoadCj(parentFolder) は起動時の最初の呼び出しでしか DB(JSON) を実際に読む処理を行わない。
- BuildRankFiltered(this) で _activeCjData からリスト構築するだけ（ファイル I/O なし）。

### 3. DbListBuilder の役割を「メモリ内操作のみ」に固定
- DbListBuilder.BuildFromActiveCj は：
  - 引数で渡された CjRoot または既に読み込んだ DB(JSON) を使う。
  - ここで新たな JSON ファイルの再読込を行わない。

### 4. 他の箇所での不要な再読込を排除
- リサイズ時・モード切替時・スクロール時などに DB(JSON) を再度読む処理があれば：
  - 削除するか、_activeCjData が既にある場合は何もしないガードを追加。
- これにより「起動時の1回だけ」が保証される。

## 期待動作
- アプリ起動:
  - RootFolder を決定
  - DB(JSON) を1回読み込み → _activeCjData に保持
  - BuildRankFiltered で ListBox にリスト表示
- その後:
  - 同じ DB の再読込・監視・自動更新は一切行わない
