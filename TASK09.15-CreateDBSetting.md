# TASK v0.9.15 - DBList 用の表示・評価フィルタ設定を追加（CreateDBSetting）

## 概要

フォルダーリスト表示と DBList 表示で使用する「最小/最大表示枚数」「最小評価値」を分離し、
DBList モード専用のフィルタパラメータを導入する。

- フォルダーリストモード:
  - 既存設定のみを使用。
- DBList モード:
  - 新しい DB 専用設定を使用。

## 用語定義

- フォルダーリストモード: キー3などで選択した親フォルダー配下のフォルダを階層順にリスト表示するモード（従来）。
- DBList モード: CJ/DB ベースで評価値順にフォルダをリスト表示するモード。

## 既存設定の扱い変更

以下の設定は「フォルダーリストモード専用」として扱う：

- MinDisplayCountEnabled
  - フォルダーリストモードでのみ有効。
- MinDisplayCount
  - フォルダーリストモードで使用する最小表示枚数。
- MaxDisplayCount
  - フォルダーリストモードで使用する最大表示枚数。
- MinEvaluation
  - フォルダーリストモードで使用する最小評価値（フィルタ基準）。

これらは DBList モードでは使用しない。

## DBList 用新設定の追加

Settings に以下のプロパティを追加し、DBList モードでのみ適用する：

- DbMinDisplayCount:
  - DBList で表示されるフォルダの「最小画像数」基準。
  - デフォルト: 20（後で調整可能）
- DbMaxDisplayCount:
  - DBList で表示されるフォルダの「最大画像数」基準。
  - デフォルト: 1000
- DbMinEvaluation:
  - DBList で表示されるフォルダの「最小評価値」フィルタ。
  - デフォルト: 8（後で調整可能）

これらは既存の MinDisplayCount / MaxDisplayCount / MinEvaluation とは独立に管理する。

## UI（SettingsDialog）の変更

設定ダイアログで以下のように整理：

- フォルダーリストモード用セクション:
  - 「最小表示枚数 (フォルダーリスト)」
  - 「最大表示枚数 (フォルダーリスト)」
  - 「最小評価値 (フォルダーリスト)」
- DBList モード用セクション（新規）:
  - 「DB最小表示数」
  - 「DB最大表示数」
  - 「DB最小評価値」

既存の動作を壊さず、ラベルや説明文は必要に応じて補足。

## フィルタロジックの変更（Form1 など）

- フォルダーリストモード:
  - MinDisplayCount, MaxDisplayCount, MinEvaluation を使用（従来通り）。
- DBList モード:
  - DbMinDisplayCount, DbMaxDisplayCount, DbMinEvaluation を使用するよう修正。
  - DBList のフィルタ処理箇所では、既存のフォルダーリスト用パラメータを参照しないように分離する。

## 完了条件

- [ ] Settings に DbMinDisplayCount / DbMaxDisplayCount / DbMinEvaluation が追加されている。
- [ ] SettingsDialog で DBList 用の3項目が編集可能になっている。
- [ ] フォルダーリストモードでは既存設定のみを使用している。
- [ ] DBList モードでは新しい DB 用設定のみを使用している（既存設定と分離）。
