# Android 版 MangaViewer 開発計画 (androidMangaViewer.md)

- 概要:
  - Windows 版 (WinForms) を壊さずに、Android タブレット向けに .NET MAUI で別途実装
  - 同じリポジトリ内・別のソリューションとし、ロジックは最小限共有する設計

- 方針:
  - Windows 版: そのまま維持（初期段階は一切変更なし）
  - Android 版: 独立プロジェクトとして開発後、安定してから共通化を進める

## 1. プロジェクト構成案

- 構成 (同じリポジトリ内):

  - MangaViewer/                 ← 既存の Windows Forms アプリ（初期は不変）
  - MangaViewer.Android.sln      ← Android / MAUI 用ソリューション
  - MangaViewer.Android/         ← .NET MAUI プロジェクト
  - MangaViewer.Core/            ← プラットフォーム非依存のコアロジック

- 参照関係 (フェーズ2以降):

  - MangaViewer.Android → MangaViewer.Core
  - MangaViewer (Windows) は初期は Core を使わずそのまま維持

## 2. 共通コア設計 (MangaViewer.Core)

- 目的:
  - Android と将来の Windows 改修で同じロジックを使い、保守コストを下げる
  - ただし初期は「Android のみ利用」し、Windows 版に影響しないようにする

- 含めるもの (プラットフォーム非依存):
  - Settings モデル
    - MinDisplayCountEnabled, MinDisplayCount, MinEvaluation, DisplayCount など
  - RatingService のコアロジック
    - rating/imageCount の読み書きロジック（ファイルI/O自体はインターフェース経由）
  - FolderService のコアロジック
    - フォルダ一覧取得・フィルタ・ソートなどのアルゴリズム部分
  - ImageService / DisplayManager のロジック的核
    - ページペア計算、表示インデックスの管理など

- 含めないもの:
  - WinForms (System.Windows.Forms) に依存するコード
  - Android / MAUI 固有の UI・イベント処理コード

## 3. Android 用プロジェクト設計 (MangaViewer.Android)

- 技術スタック:
  - .NET 8 + .NET MAUI
  - Android タブレットを主眼（大画面・タッチ操作最適化）

- UI:
  - XAML またはコードベースで以下の主要画面を実装:
    - メイン閲覧画面 (2ページ同時表示)
    - フォルダ一覧画面（リスト型UI）
    - 設定ダイアログ
    - 評価入力用オーバーレイ

## 4. タッチ操作設計

- ページ送り:
  - 右スワイプ → 前ページ (currentIndex - 2)
  - 左スワイプ → 次ページ (currentIndex + 2)
  - スワイプ判定は SwipeGestureRecognizer またはタッチイベント実装

- フォルダ（リスト）操作:
  - リスト画面での上下スワイプ → リストスクロール
  - リスト項目タップ → そのフォルダをダイレクト開く
