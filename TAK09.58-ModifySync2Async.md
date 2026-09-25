# TAK09.58 - Sync to Async 改修メモ

## 目的
- アプリ操作中に UI が止まる問題を減らす
- フォルダ移動、CBZ 巻移動、起動時初期化の同期待ちを減らす
- 画像表示が途中で破綻しても、リスト移動を継続できるようにする

## 実施した修正

### 1. 起動時の同期待ちを削除
- [StartupHandler.cs] の初期化フローで、`Task.Run(...).Wait()` による待ちをやめた
- `LoadAndSortImagesAsync()` を経由して、初期ロードをバックグラウンド実行する形に変更した
- 起動直後の「固まったように見える」時間を減らした

### 2. フォルダ移動を非同期ロード化
- [FormNavigator.cs] のフォルダ移動処理を、`LoadAndSortImages()` の同期呼び出しから非同期呼び出しへ変更した
- [Form1.cs] に `LoadAndSortImagesAsync()` を追加し、世代番号で古いロード結果を破棄するようにした
- これにより、フォルダを連続移動しても古い読み込み結果が後から画面を書き戻しにくくした

### 3. CBZ 巻移動を非同期化
- [FormNavigator.cs] の `NavigateCbzNext` / `NavigateCbzPrev` を非同期化した
- [Form1.cs] に `SwitchToNextCbxAsync()` / `SwitchToPreviousCbxAsync()` を追加した
- キャッシュ未作成や遅いストレージでの待ちが、UI を直接止めにくくなった

## 調査で分かった主因
- ログ上、MAO フォルダでは [CbzManager.cs] の初回 CBZ スキャンが約 1400ms かかっていた
- つまり、描画よりも「フォルダ走査」と「キャッシュ展開待ち」がフリーズの中心だった
- そのため、描画キャンセルよりも、読み込みと展開の同期処理を外す方が効果が大きい

## まだ残るリスク
- [CbzManager.cs] の `InitializeForFolder()` は、CBZ ファイル列挙とソートをまだ同期実行している
- [CbzManager.cs] の `EnsureCacheExtracted()` は、`SemaphoreSlim.Wait()` による待ちが残っている
- [Form1.cs] のタイトル更新や CBZ 一覧確認も、フォルダによっては小さな引っかかりを作る可能性がある

## 今後の方針
- 初回 CBZ スキャンの分離
- キャッシュ展開待ちのさらに細かい非同期化
- 必要最小限の UI 更新だけを先に返す構成へ寄せる

## デグレ時の戻し対象
1. [StartupHandler.cs] の初期化を非同期化した変更
	- `LoadAndSortImagesAsync()` を使うようにした部分を戻す
	- ただし、`Task.Run(...).Wait()` の復活は UI 停止を再発させやすいため、戻す場合は慎重に確認する

2. [FormNavigator.cs] のフォルダ移動を非同期化した変更
	- `NavigateFolderBy()` と `NavigateToNextUnrated()` で `LoadAndSortImagesAsync()` を呼ぶようにした部分を戻す
	- 併せて [Form1.cs] の `LoadAndSortImagesAsync()` と世代番号チェックを確認する

3. [FormNavigator.cs] の CBZ 巻移動を非同期化した変更
	- `NavigateCbzNext()` / `NavigateCbzPrev()` で `SwitchToNextCbxAsync()` / `SwitchToPreviousCbxAsync()` を呼ぶ部分を戻す
	- [Form1.cs] の `SwitchToNextCbxAsync()` / `SwitchToPreviousCbxAsync()` も合わせて確認する

4. [CbzManager.cs] の重い同期処理が残っている点
	- `InitializeForFolder()` の CBZ 列挙・ソート
	- `EnsureCacheExtracted()` の `SemaphoreSlim.Wait()` と展開待ち
	- ここは今回の非同期化で完全には外していないため、再発時の主原因候補として扱う

## デグレ確認の観点
- 起動直後に UI が固まらないか
- フォルダ上下移動で選択が追従し、古い読み込み結果が後から上書きしないか
- Alt+左右で CBZ 巻移動したときに、表示が止まり続けないか
- MAO のような巻数の多いフォルダで、初回読み込みが明らかに遅くなっていないか

## 前回コミットからの変更点
- 起動時初期化で、DB 読み込み後の画像読み込みを非同期化した
- フォルダ移動と CBZ 巻移動を非同期化し、古いロード結果を世代番号で破棄するようにした
- CBZ キャッシュ作成や展開待ちの影響を減らすため、キャッシュ通知とタイトル更新を遅延実行に寄せた
- フリーズ原因の切り分けとして、MAO の初回 CBZ スキャンが重いことをログから確認した
