# TASK: Shared CJ Vol2

## 目的

- Vol1の方針を実装に落とすため、行数が多い箇所から段階的に機能分離する。
- 振る舞いを維持したまま、変更耐性とデバッグ性を上げる。

## 優先順位（行数ベース）

1. `CbzManager.cs`（1434行）
2. `Form1.cs`（1355行）
3. `FolderService.cs`（987行）
4. `SettingsDialog.cs`（713行）
5. `RatingService.cs`（505行）
6. `FormNavigator.cs`（482行）
7. `CjService.cs`（436行）

## フェーズ計画

### Phase A: CbzManager分離（最優先）

- 目的: キャッシュ・展開・先読み・通知を責務分離。
- 分割先候補:
  - `CbzCacheStore`
  - `CbzExtractor`
  - `CbzPreloadScheduler`
  - `CbzStatusNotifier`
- 完了条件:
  - 既存ログタグ維持
  - 公開API互換維持
  - 起動/表示/先読みの挙動差分なし

### Phase A 実装結果（更新）

- 実装済み:
  - `CbzRuntimeState`（状態保持）
  - `CbzCacheCoordinator`（キャッシュ保持/trim/保護）
  - `CbzPreloadCoordinator`（先読みスケジューリング）
  - `CbzRecoveryHelper`（再試行/フォールバック判定）
- 補助実装:
  - `CbzCacheHelper`, `CbzStatusHelper`, `CbzVolumeComparer`
- 状態:
  - 「キャッシュ/先読み/回復」の責務分離は当初案より細かく実施済み。

### Phase B: Form1分離

- 目的: UIエントリを薄くし、処理責務を外出し。
- 分割先候補:
  - `FolderSelectionController`
  - `InfoLabelPresenter`
  - `CjLoadCoordinator`
- 完了条件:
  - 上下キー最優先動作維持
  - `KEY-NAV` / `INFO-LABEL` ログ維持

### Phase B 実装結果（更新）

- 実装済み:
  - `UiStateService`, `UiBehaviorService`, `UiLayoutService`, `PlaybackStateService`
  - `CbzFormBridge`, `CbzUiBridge`, `CbzSelectionBridge`, `CbzDialogLayout`
- 反映内容:
  - `Form1` の CBZ 遷移ロジックを `CbzFormBridge` 経由へ移行。
  - `_cbzLoadGeneration` は `Form1` 側でカプセル化し、Bridge は最小 API のみ利用。
  - 表示終端近傍の先読み起点を `CbzFormBridge.TriggerPreloadNearEnd` に集約。
- 状態:
  - 当初候補（`FolderSelectionController` など）とは別名/別粒度で同目的を達成。

### Phase B 追加要件（正式）: 境界遷移の非同期完了条件

- 対象:
  - 左右キーの巻境界遷移（次巻/前巻）
  - 明示的 CBZ 切替（Alt+Left/Alt+Right）
- 完了条件:
  - 境界遷移で重い File I/O を UI スレッドで実行しない。
  - 境界遷移は非同期コマンドとして実行し、UI スレッドは入力を継続可能である。
  - 同時遷移は直列化され、`ActiveCbxIndex` を競合更新しない。
  - 最新要求のみ UI 反映し、旧要求結果は破棄する（世代制御）。
  - 失敗時は現ページを維持し、異常終了しない。
  - ログで `start/cancel/apply/discard/fail` を遷移単位で追跡できる。

### Phase B 検証条件（追加）

- 連続キー入力（左右連打）時に UI が固まらない。
- キャッシュミス巻への遷移中でもキー入力を受け付ける。
- NAS 遅延・共有アクセス遅延時も UI ループが停止しない。

### Phase C: FolderService分離

- 目的: スキャン・メタIO・インデックス構築を分離。
- 分割先候補:
  - `FolderScanService`
  - `FolderMetaRepository`
  - `CjIndexBuilder`
- 完了条件:
  - `FolderService.cs` を 700行前後まで圧縮
  - 既存フィルタ結果一致
  - 4拡張子1回列挙最適化を維持

### Phase D: Settings/Rating周辺分離

- 目的: 設定UIと評価永続化を分離。
- 対象:
  - `SettingsDialog.cs`
  - `RatingService.cs`
  - `FormNavigator.cs`
- 完了条件:
  - 設定変更副作用の互換維持
  - 評価読み書き互換維持

### Phase E: CJ同期モジュール仕上げ

- 目的: 同期処理を専用モジュールへ整理。
- 分割先候補:
  - `CjSyncService`
  - `CjStorageService`
- 完了条件:
  - 疑似障害テスト（ローカル破損/共有破損/競合）成功
  - `[CJ-SYNC]` ログで追跡可能

### Phase E 補足（現状）

- `[CJ-SYNC] read/winner/repair` ログは運用ログ上で追跡可能。
- 共有失敗時ローカル継続の方針は維持。
- ただし Android 側接続運用と長期バックアップ方針は未完了のため、Vol1 Phase 3 項目として継続管理する。

## 実装がVol2記載を超えた点（明示）

- 画面レイヤの詳細分離（CBZ ダイアログ配置・選択連携・UI同期）まで実施。
- 上下キーのデバウンス + 世代キャンセルによる stale 読み込み破棄を実装し、`KEY-NAV` ログで実運用シナリオ検証を実施。
- 起動直後キー入力、フォルダ切替、CBZ 移動の一連をログ証跡で確認済み。

## 実施ルール

1. 1フェーズを複数小PR/小コミットに分割する。
2. 1コミットごとにビルド確認する。
3. 1フェーズ完了ごとに起動確認する。
4. 破壊的変更は避け、互換レイヤを先に置く。

## ロールバック方針

- フェーズ内で挙動差分が出た場合は、そのフェーズ開始前のコミットまで戻せる粒度で作業する。
- 共有同期系は常に「共有失敗時ローカル継続」を守る。

## チェックポイント

### ビルド

- `dotnet build MangaViewer.sln --configuration Debug` 成功

### 起動

- 起動後に例外停止しない
- 既存のDB読込とフォルダ表示が維持される

### UI応答性

- 左右キーの巻境界遷移で UI スレッドが停止しない。
- 巻境界遷移中に別入力を受け付け、旧要求結果は破棄される。
- 境界遷移失敗時も表示状態が破綻しない（現ページ維持）。

### 同期

- `CJ-SYNC` の read/winner/repair/write が連続して追える
- 共有不可時はローカル継続

## 相談が必要なポイント

1. Android実装技術の最終確定（Flutter前提で進めるか）
2. SMB接続情報の保存方式（暗号化有無）
3. 長期運用時のバックアップ削除ポリシー（`.bak`, `.conflict`）
