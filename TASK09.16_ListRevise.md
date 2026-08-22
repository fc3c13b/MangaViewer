# TASK v0.9.16 - 設定変更後のリスト再フィルタ対応（フォルダーリスト / DBList）

## 概要

SettingsDialog で評価値関連の設定を変更して OK を押した際、
現在の表示モードに応じてフォルダリストを即時に再構築するよう改善する。

- フォルダーリスト表示中：
  - 最小評価値 (MinEvaluation) などを変更 → その場でフォルダーリストを更新
- DBList 表示中：
  - DB最小評価値 (DbMinEvaluation) などを変更 → その場で DB リストを更新

## 背景（現状の問題）

現在の実装（ShowSettingsDialog）では、設定変更後の挙動が不完全：

- フォルダーリストモード：
  - MinEvaluation/MinDisplayCount/MaxDisplayCount が変わった場合、
    BuildSubfolderList() を呼び出す処理は存在するが、
    「現在の表示モードを正しく判定していない」ため、DBList モード中でも同じロジックが動いてしまう。
- DBList モード：
  - DbMinEvaluation / DbMinDisplayCount / DbMaxDisplayCount の変更を検知して
    BuildRankFilteredListFromActiveCj()（または CJManager）を再実行する処理がない。

結果として、DBList でフィルタ値を変えてもリストが更新されない問題が発生する。

## 目標

- フォルダーリストモード：
  - MinEvaluation / MinDisplayCount / MaxDisplayCount の変更時に、
    BuildSubfolderList() でフォルダーリストを再構築し、表示を更新する。
- DBList モード：
  - DbMinEvaluation / DbMinDisplayCount / DbMaxDisplayCount の変更時に、
    DBList 用のフィルタロジック（BuildRankFilteredListFromActiveCj など）で再構築し、
    表示を更新する。

## 実装計画

### 1. 表示モードの判別方法を確認・補強

Form1 にて、現在のリスト取得元を区別できる手段を確認：

- フォルダーリストモード（BuildSubfolderList ベース）
- DBList モード（BuildRankFilteredListFromActiveCj / CJManager ベース）

対応方針：
- 既に _displayMode や同等のフラグがある場合 → それを使用。
- まだない場合 → bool IsDbListMode() のような判定メソッドを追加し、
  「DBList モードか否か」を ShowSettingsDialog から利用できるようにする。

### 2. ShowSettingsDialog の修正（Form1）

ShowSettingsDialog で、設定変更前後の値を比較した際、モードごとに別処理を行う：

- ダイアログ表示前に保存する値（追加・整理）：
  - prevMinEvaluation
  - prevDbMinEvaluation
  - prevMinDisplayCount, prevMaxDisplayCount
  - prevDbMinDisplayCount, prevDbMaxDisplayCount

- SettingsDialog.ShowDialog() が OK で戻った後：
  - (_settings, _) = SettingsManager.LoadWithValidation(); を実行（既実装）。

- フォルダーリストモードの場合（IsDbListMode == false）：
  - filterChanged の判定条件：
    - MinEvaluation / MinDisplayCount / MaxDisplayCount のいずれかが変更。
  - 変更があった場合：
    - BuildSubfolderList(rootPath) を呼び出してフォルダーリストを再構築。
    - 現在のフォルダ位置を復元（既存ロジックと同等）。

- DBList モードの場合（IsDbListMode == true）：
  - dbFilterChanged の判定条件：
    - DbMinEvaluation / DbMinDisplayCount / DbMaxDisplayCount のいずれかが変更。
  - 変更があった場合：
    - BuildRankFilteredListFromActiveCj()（または CJManager を通じた同等処理）を再実行し、
      DBList を再構築。
    - リスト選択位置の復元・表示更新を行う（既存の DBList 側ロジックに合わせる）。

### 3. フィルタ条件の分離確保

- フォルダーリストモード：
  - MinEvaluation / MinDisplayCount / MaxDisplayCount のみを使用。
- DBList モード：
  - DbMinEvaluation / DbMinDisplayCount / DbMaxDisplayCount のみを使用。
- ShowSettingsDialog の再ビルド処理でも、この分離を維持し、
  「DBList でフォルダーリスト用パラメータを使う」状態にならないようにする。

### 4. 影響範囲（修正対象ファイル）

主に以下のファイルを修正：

- Form1.cs:
  - ShowSettingsDialog():
    - DBList モード判定の追加・補強。
    - DbMinEvaluation などの変更検知と、DBList の再構築呼び出しを追加。
  - 必要に応じて IsDbListMode() や同等の判定ロジックを追加。

他は本タスクでは原則変更しない（Settings.cs / CJManager.cs は既存設計に従う）。

## 完了条件

- [ ] フォルダーリスト表示中に MinEvaluation などを変更し OK → リストが再フィルタされる。
- [ ] DBList 表示中に DbMinEvaluation などを変更し OK → DB リストが再フィルタされる。
- [ ] モードごとに使用する設定パラメータが混在していない（分離維持）。
