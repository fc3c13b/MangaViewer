# TASK09.18_ModifyCJUsing-2 – DBList の CJ オンメモリ利用・フィルタ設計（実装計画）

## 概要

DBリスト表示時（DBListモード）において、CJ をオンメモリに保持し、
フィルタリング・ソート・Rating更新を可能な限りオンメモリで行う。
これにより：
- ディスクI/O の削減
- UI の応答性向上（固まりの改善）
- 今後の Rating 書き込み処理との整合性確保

## 基本方針

- DBListモードでアクティブな CJ は、一度読み込んでオンメモリに保持する。
- リスト表示・フィルタリングは、このオンメモリCJ を利用する。
- ディスクへの保存は「必要なタイミングのみ」に行う（初回作成時・Rating変更時など）。

## 1. オンメモリ CJ の保持構造（Form1）

Form1 に以下のフィールドを使用：
- private CjRoot? _activeCjData;
- private string? _activeCjParentFolder;

役割：
- _activeCjData: DBListモードで現在アクティブな CJ のデータ全体を保持。
- _activeCjParentFolder: その CJ が対応する親フォルダパス。

## 2. DBListモード切替時の処理（ToggleDisplayMode / キー3）

DBListモードに切り替える際、またはキー3で親フォルダを選択した際：
1) _activeCjParentFolder = parentFolder;
2) CJ が存在する場合:
   - _activeCjData = CjManager.LoadCj(parentFolder);
3) CJ が存在しない場合:
   - CreateCjForParent(parentFolder) で作成後、
     _activeCjData = CjManager.LoadCj(parentFolder);

これにより：
- DBリスト表示・フィルタリングは _activeCjData を使う。
- 同じ親フォルダに対して何度もディスク読み書きしない。

## 3. BuildRankFilteredListFromActiveCj の修正（オンメモリ利用）

BuildRankFilteredListFromActiveCj() は、_activeCjData を基に処理する：
- _activeCjData.Folders から必要なフォルダをフィルタ＋ソート。
- ディスクI/O は「初回ロード時」のみとし、リスト再構築時はオンメモリ演算とする。
- CjManager.GetFoldersFromCj() の利用は最小限にし、DBListでは _activeCjData を優先する形にする（必要に応じて内部メソッド化）。

## 4. CJ作成処理の高速化（応答なし対策）

現状：キー3の CreateCjForParent() は UI スレッドで重いスキャンをしている。
修正案：
- バックグラウンド実行化:
  - Task.Run でスキャン＋CJ生成を行う。
  - labelInfo に進捗メッセージを表示（Invoke）。
- ただし、既存ロジックは維持し、以下の点に限定して修正：
  - CreateCjForParent() の本体を非同期化するラッパーを追加。
  - UI は「作成中…」→完了後に BuildRankFilteredListFromActiveCj() を呼び出す。

## 5. Rating書き込み用インターフェースの準備（今後の拡張前提）

今後、Rating を書き込む処理を入れるため、以下のようなメソッドを Form1 に追加する設計：
- private void UpdateRatingInMemory(string folderPath, int newRating)
  - _activeCjData.Folders[folderPath].Rating = newRating;
  - CjManager.SaveCj(_activeCjParentFolder, _activeCjData);

これにより：
- Rating の変更は「オンメモリ更新 → まとめて保存」で統一。
- DBリスト再表示も _activeCjData を再利用するだけになる。

## 6. 修正対象ファイル

主に以下を修正：
- Form1.cs:
  - _activeCjData / _activeCjParentFolder の追加
  - ToggleDisplayMode() と キー3処理の修正（オンメモリ保持＋非同期スキャン）
  - BuildRankFilteredListFromActiveCj() を _activeCjData ベースに書き換え
- CjManager.cs:
  - GetFoldersFromCj() は残しつつ、DBListでは _activeCjData を優先する形にする（必要なら内部メソッド化）。

## 7. DBList の表示ルール（実装計画）

### 7.0 用語定義

- 「DB表示のチェック」：
  - Settings の「□ DB表示：評価値を『以上』でフィルタする（OFF=一致）」
  - ON: チェックON → Rating を基準にフィルタ有効
  - OFF: チェックOFF → Rating はフィルタせず、条件を満たせば表示

- CbzZipCount: フォルダ内の CBZ/ZIP の数。
- ImageCount: jpg/jpeg/webp/png の画像ファイル数。

### 7.1 基本方針

- DBList は CJ に記載された順序で表示し、フィルタリングのみを行う。
- Rating や CBZ/ImageCount で追加のソートは行わない。
- ディスクI/O を行わず、_activeCjData.Folders の情報だけで処理する。
- 表示条件は「CBZ/ZIPがある場合」と「画像ファイルだけの場合」で異なるが、どちらかの条件を満たせば表示する。

### 7.2 CBZ/ZIPありの場合（CbzZipCount >= 1）

A) DB表示のチェックが ON のとき：
- (Rating == -1) または (Rating >= DbMinEvaluation) を満たすフォルダを表示。

B) DB表示のチェックが OFF のとき：
- Rating でフィルタせず、CBZ/ZIP があれば表示。

### 7.3 CBZ/ZIP なし（CbzZipCount == 0）、画像ファイルのみ

この場合、ImageCount と DB最小・最大表示数も考慮する。

A) DB表示のチェックが ON のとき：
- (Rating == -1) または (Rating >= DbMinEvaluation)
- かつ (DbMinDisplayCount <= ImageCount)
- かつ (ImageCount <= DbMaxDisplayCount)
をすべて満たすフォルダを表示。

B) DB表示のチェックが OFF のとき：
- Rating はフィルタせず、
- (DbMinDisplayCount <= ImageCount)
- (ImageCount <= DbMaxDisplayCount)
を満たすフォルダを表示。

### 7.4 ソート順

- CJ に記載された順序（_activeCjData.Folders の挿入順）を尊重し、フィルタリングのみ行う。
- Rating や CBZ/ImageCount で追加のソートは行わない。

## 8. 期待される効果

- DBリスト表示時は CJ をオンメモリで保持。
- リストのフィルタ・ソート・Rating更新は可能な限りオンメモリで行う。
- UI の「固まる」問題は、スキャン部分をバックグラウンド化するだけで大幅に改善される。