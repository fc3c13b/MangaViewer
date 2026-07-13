# MangaViewer

## 開発概要

MangaViewer は、Windows Forms をを使用して開発されたマンガ（コミック）閲覧アプリケーションです。フォルダ内に格納された画像ファイルをページ順に読み込み、左右2枚同時に表示するスプリットビュー方式でマンガを読むことができます。

- **フレームワーク**: .NET 8.0 (Windows Forms)
- **言語**: C#
- **IDE**: Visual Studio 2022
- **ターゲットプラットフォーム**: Windows

## ファイル構成

```
MangaViewer/
├── MangaViewer.sln          # Visual Studio ソリューションファイル
├── MangaViewer.csproj       # プロジェクト設定ファイル
├── Program.cs               # アプリケーションエントリポイント
├── Form1.cs                 # メインフォーム（UI・ビジネスロジック）
├── Form1.Designer.cs        # フォームデザイナー生成ファイル
├── .editorconfig            # エディタ設定ファイル
├── README.md                # このファイル
├── SOFTSPEC.md              # 機能仕様書
├── bin/                     # ビルド出力ディレクトリ
│   ├── Debug/               # Debug ビルド出力
│   └── Release/             # Release ビルド出力
└── obj/                     # 中間ビルドファイル
```

## ファイル毎の目的・機能

| ファイル | 目的 | 概要 |
|---------|------|------|
| `MangaViewer.sln` | ソリューション定義 | Visual Studio のソリューションファイル。プロジェクトの構成（Debug/Release）を定義する。 |
| `MangaViewer.csproj` | プロジェクト設定 | .NET 8.0-windows ターゲット、Windows Forms 有効化、nullable 参照型を有効にするなどのプロジェクト設定を管理する。 |
| `Program.cs` | アプリケーションエントリポイント | `Main()` メソッドからアプリケーションを起動。例外発生時は `error_log.txt` にログを出力するエラーハンドリングを実装する。 |
| `Form1.cs` | メインフォーム | 画面描画・キーボード操作・画像読み込み・フォルダ navigation の全ロジックを担うコアファイル。 |
| `Form1.Designer.cs` | UI デザイナーファイル | Visual Studio Designer により自動生成されるフォーム関連のコード。 |

## ビルド方法

```bash
# Debug ビルド
dotnet build -c Debug

# Release ビルド
dotnet build -c Release
```

## 実行方法

```bash
dotnet run
```

またはビルド後の実行ファイルを直接起動してください。

## ショートカットキー一覧

| キー | 機能 |
|------|------|
| `1` (キー1) | フォルダ選択ダイアログを開く |
| `→` (右矢印) | 前の2ページに戻る（数字の小さい方へ） |
| `←` (左矢印) | 次の2ページに進む（数字の大きい方へ） |
| `↑` (上矢印) | 上位のフォルダ（前の話）へ移動 |
| `↓` (下矢印) | 下位のフォルダ（次の話）へ移動 |

## 対応画像形式

- JPG / JPEG
- PNG
- WebP

## ライセンス

本ライセンスに関する記載はありません。