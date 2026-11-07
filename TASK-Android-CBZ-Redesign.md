# TASK - Android CBZ表示 再設計（Windows知見反映）

作成日: 2026-09-28
対象: MangaViewer.Android
目的: 「起動後に画像が表示されない」問題を、Windows版で有効だった設計原則をAndroidへ移植して根本解決する。

## 1. 背景と現状

### 1.1 実測で確認した現象
- 起動後ログは以下まで進む。
  - [MainPage] LoadImagesForSelection start source=samba ...
  - [MainPage] direct samba images=0
  - [MainPage] samba archives=1
- その後、描画開始ログが出ない。
  - [MainPage] samba archive extracted images=...
  - [MainPage] render start ...
  - [MainPage] no images branch ...
- 端末内キャッシュは `.extracting` のまま残り、`.complete` が作られない。
- `.step` は `copying-archive` で停滞するケースがある。

結論: 「通常画像0件→CBZフォールバック開始」までは通るが、SMB経由CBZのコピー/展開待ちでUIフローが復帰せず、描画分岐まで到達していない。

### 1.2 Windows側からの既知知見
- UI同期経路に重いI/Oを置くと、体感フリーズになる（Ver09.63 NG）。
- 先読みは fire-and-forget、現在表示の完了待ちに混ぜない（TASK09.32-ModifyCache-2, TASK09.40）。
- 「キャッシュ取得の唯一入口」「.complete で正当性判定」「世代制御で旧要求破棄」が有効（CbzManager, CbzCacheHelper, Vol2）。
- ログで起動→初回画像表示までの各段を可視化しないと原因特定できない（TASK-SetLogforStartUpTo1stPic）。

## 2. 再設計の原則

### 2.1 UIスレッド禁止事項（Windows Vol1準拠）
- SMBオープン
- CBZダウンロードコピー
- Zip列挙/展開
- ロック待ち
- 大量ファイル列挙

### 2.2 UIスレッド許可事項
- 最新状態の反映（ImageSource差し替え）
- 軽量インデックス計算
- 操作受付・キャンセル要求

### 2.3 応答性SLO
- ユーザー操作から視覚応答開始まで 100ms 以内を目標。
- キャッシュミス/NAS遅延時でもUI入力ループを止めない。

## 3. Android向け新アーキテクチャ

## 3.1 役割分離
- MainPage
  - 画面反映のみ
  - 「要求ID（generation）」を採番し、最新要求のみ反映
- ArchivePipelineService（新規）
  - 画像候補解決のオーケストレーション
  - direct image → archive image の順に非同期処理
- ArchiveFetchService（新規）
  - SMB/LocalからCBZバイト列を取得
  - タイムアウト、再試行、キャンセルを管理
- ArchiveExtractService（新規）
  - `.extracting` への展開、`.complete` 確定、原子的リネーム
- ArchiveIndexCache（新規）
  - archiveKey -> extracted image list をメモリ保持
  - 同一要求の再列挙を回避

## 3.2 フロー（起動時/フォルダ選択時）
1. MainPage が generation を進める。
2. direct画像を非同期取得。
3. directが0件なら archive候補を取得。
4. archive先頭1件のみを current 優先で処理。
5. 取得成功した最初の画像集合を即UI反映。
6. 残りarchiveは低優先度で先読み（fire-and-forget）。
7. generation不一致の結果は破棄。

## 3.3 重要ルール
- current と preload の優先度を分ける（current最優先）。
- 「先読み完了待ち」で current 表示を遅らせない。
- `.extracting` が残っていても一定時間で破棄再試行できるようにする。
- 失敗時は no-image 分岐へ必ず復帰する（無限待ち禁止）。

## 4. 現行実装とのギャップ

- 現行 `ArchiveImageCacheService` は、呼び出し元が結果待ちのため、SMBコピー遅延時に描画経路を占有しやすい。
- `.extracting` 停滞時の「UI復帰保証」が弱い。
- direct→archive→render/no-image の最終分岐ログは追加済みだが、フェイルファスト条件が不足。

## 5. 実装計画（段階導入）

### Phase A: UI復帰保証（最優先）
- MainPage側で「表示待ち上限時間」を設ける。
- 上限超過で no-image 分岐へ復帰し、再試行ボタンを提示。
- generationキャンセルを徹底し、旧タスクの完了結果を捨てる。

完了条件:
- 描画ログが必ず `render start` または `no images branch` のどちらかへ到達。
- `.extracting` 停滞時でも画面が固まらない。

### Phase B: パイプライン分離
- `ArchiveImageCacheService` を fetch/extract/index の3層へ分離。
- current処理は単一CBZ先頭成功まで、残りは非同期先読みへ。

完了条件:
- 先頭画像表示までの時間が、CBZ個数に比例して悪化しない。

### Phase C: キャッシュ健全化
- `.extracting` に startedAt を記録し、TTL超過でクリーンアップ。
- `.complete` と画像実体が不一致なら再構築。
- メモリインデックスキャッシュを導入して再列挙削減。

完了条件:
- 2回目アクセスで `.complete` ヒット時に即表示。

### Phase D: 運用ログ標準化
- [PIPE] request/start/cancel/apply/discard/fail
- [FETCH] open/copy/timeout/retry
- [EXTRACT] begin/entry/end/marker/rename
- [RENDER] begin/success/empty

完了条件:
- 起動から初回表示までの経路が1本のトレースで追える。

## 6. 検証シナリオ

1. direct画像ありフォルダ
- archiveへ行かず `render start` 到達。

2. directなし・CBZあり（SMB高速）
- archive経由で `render start` 到達。

3. directなし・CBZあり（SMB遅延）
- UIは応答維持。
- 一定時間内に no-image 分岐 or 部分表示へ復帰。

4. directなし・CBZなし
- `no images branch` 到達。

5. 連打/フォルダ切替
- generationにより旧結果が discard され、最新のみ適用。

## 7. 実装着手順

1. Phase A を先に実装（UI復帰保証）。
2. ログで `render start` / `no images branch` 到達率を確認。
3. その後に Phase B/C の分離・最適化を導入。

## 8. 参照元（Windows知見）
- TASK09.32-ModifyCbzCache.md
- TASK09.32-ModifyCache-2.md
- TASK09.40-ModifyCache-3.md
- TASK09.35-DebugStartup.md
- Ver09.63_Vol1+2_NG_Result.md
- TASK-SharedCJ-Vol1.md
- TASK-SharedCJ-Vol2.md
- TASK-SetLogforStartUpTo1stPic.md

## 9. Ver09.63 NG指摘との対応表

### 9.1 UI同期経路に重いI/Oが混在
- 対応:
  - MainPageはUI反映のみ。
  - SMB open/copy, zip extract, file enumerateを非UIサービス層へ移動。
  - current表示とpreloadを分離し、current完了をpreload待ちにしない。

### 9.2 境界遷移・入力遅延
- 対応:
  - generation採番で最新要求のみ適用（旧要求discard）。
  - 同時要求の直列化契約を導入（current単位の排他）。
  - 失敗/遅延時はUI復帰（render or no-image）を保証。

### 9.3 ログI/O負荷
- 対応:
  - 高頻度ログは Debug.WriteLine 中心、同期ファイル追記を避ける。
  - 必須トレースのみ固定タグ化（[PIPE]/[FETCH]/[EXTRACT]/[RENDER]）。
  - 起動〜初回描画のトレースは段階ログで最小本数に抑制。

### 9.4 完了条件不足（Vol2指摘）
- 対応:
  - Phaseごとに完了条件を定義（A: UI復帰保証、B:分離、C:健全化、D:運用ログ）。
  - 検証シナリオに連打/キャッシュミス/NAS遅延を明示。
