# TASK: Shared CJ Vol1

## 目的

- WindowsアプリとAndroidアプリでCJを共有し、どちらか片方が利用不可でも運用を継続できる基準を定義する。
- 強固さを上げつつ、実装と運用の複雑化を避ける。

## 全体構成

### コンポーネント

- Windows App
  - ローカルCJを必ず読み書き
  - 共有CJはベストエフォートで利用
- Android App
  - ローカルCJを必ず読み書き
  - SMB経由で共有CJを利用
- Shared CJ Storage
  - Windows: `Z:/MangaViewerCache`
  - Android: SMB URI（Tailscale経由）
- Local CJ Storage
  - 各端末のローカル保存領域

### データ構造（CJトップレベル）

- `parentFolder`
- `folders`
- `cjVersion`（共有系バージョン）
- `localVersion`（共有不可時のローカル加算）
- `lastWriter`（固定端末ID）
- `savedAtUtcMs`

### 同期の基本ルール

1. 読み込み時は local / shared の両方を試す。
2. 勝者判定は `cjVersion -> localVersion -> savedAtUtcMs -> lastWriter`。
3. 勝者を正として古い側を修復同期する。
4. 共有不可時はローカル運用を継続する。

### UIスレッド契約（正式）

- UIスレッドで実行してよい処理:
  - 画面描画反映（最新状態の反映のみ）
  - 軽量なインデックス計算・境界判定
  - ユーザー入力受付とキャンセル指示
- UIスレッドで実行してはいけない処理:
  - CBZ/ZIP 展開
  - ディレクトリ再帰列挙・大容量ファイル列挙
  - キャッシュ作成・削除・trim の実処理
  - ロック待機を伴う処理
  - ネットワークI/O（共有ストレージ読書き）
- UI応答性目標（SLO）:
  - キー入力（左右）から「画面反応開始」まで 100ms 以内を目標とする。
  - キャッシュミス時でも UI 入力ループを停止させない。

## 全体計画

### Phase 1

- メタデータ導入（`cjVersion`, `localVersion`, `lastWriter`, `savedAtUtcMs`）
- winner判定導入
- 共有不可時フォールバック導入

### Phase 2

- 原子的保存（tmp -> rename）
- `.bak` 1世代バックアップ
- 破損/競合時の修復導線（`.conflict` 1世代）

### Phase 3

- Android側接続設定（SMB/Tailscale）
- 接続テストと運用ログ
- 共有停止時フォールバック運用の検証

## 実装反映（Vol2計画を超えて先行した内容）

- リファクタの主軸が CJ 同期のみでなく、WinForms 側の責務分離まで拡張された。
- `Form1` と `CbzManager` の直接依存をさらに薄くするため、画面側ブリッジ層を追加した。
- 起動直後キー操作とフォルダ切替の実運用ログ検証（`KEY-NAV`）を継続的に実施し、世代キャンセル方式で stale 読み込み破棄を確認した。

### 追加された責務分離レイヤ（抜粋）

- `CbzFormBridge`: Form 側の CBZ 切替・先読み起点を一元化。
- `CbzUiBridge`: Manager 状態から UI 表示状態への同期を集約。
- `CbzSelectionBridge`: CBZ 選択ダイアログからのロード連携を分離。
- `CbzDialogLayout`: CBZ 選択ダイアログのレイアウト/描画を分離。
- `UiStateService` / `UiBehaviorService` / `UiLayoutService` / `PlaybackStateService`: UI 状態・挙動・レイアウト・再生位置の分離を先行実装。

### 現時点の位置づけ

- Vol1 の「共有不可でも継続」「勝者判定で収束」の方針は維持。
- ただし実装は、同期基盤に加えて「UI と状態管理の境界整備」まで進んでいる。

## 運用方針

- 共有障害はログのみ通知（デバッグ用途）。
- 共有不可であってもアプリ動作継続を最優先にする。
- 同期失敗で処理全体を停止しない。

## 完了基準（全体）

- 共有停止中でもローカルのみで閲覧・保存を継続できる。
- 再接続後、起動または保存イベントで収束できる。
- 片系破損時に健全側から自動回復できる。
- 保存中断時に本体CJが壊れにくい。
