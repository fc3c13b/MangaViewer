# DEVELOPITEM - MangaViewer 開発・実装計画

## バージョン管理ルール

- 各 Phase を完了するたびに Git コミットを行う。
- タグ付け:
  - 例: v1.0, v1.1, v2.0（SOFTSPEC と連動）
- Commit message のフォーマット:
  - "Phase N / Txx: 変更内容 (vX.X)"

## 開発ルール

- すべての実装は SOFTSPEC.md の機能(F-xxx)に対応させる。
- 各フェーズで:
  - ビルド成功を確認
  - ドライラン（動作テスト）を実施
  - テスト結果をこのファイルに記録
  - Git コミット・タグ付け

---

## Phase 0: プロジェクトベース環境再構築 (v1.0)

- TaskID: T01
- Related F: N/A
- 実装内容:
  - bin/obj のクリーン（削除）
  - .NET 8 WinForms の最小構成プロジェクトに統一
  - Program.cs で Form を起動するだけのベース作成
- テスト項目:
  - dotnet build が成功
  - dotnet run / exe で空フォームが表示されること
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.0
  - Commit: （コミット後のハッシュを後記入）

## Phase 1: フォーム基本レイアウト・リサイズ対応 (v1.1)

- TaskID: T02
- Related F: F-009
- 実装内容:
  - Form のタイトル "Manga Viewer"
  - 初期サイズ 1200x800、最小 800x600、StartPosition CenterScreen
  - 左右 PictureBox を配置し、OnResize で動的に再配置
  - 背景色を黒 (Color.Black) に設定
- テスト項目:
  - ウィンドウ表示時:
    - タイトルが "Manga Viewer"
    - 背景色は黒
    - 左/右の PictureBox が分割表示される
  - リサイズ時に:
    - PictureBox が追随・再配置される
    - 800x600 より小さくできない
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.1
  - Commit: （後記入）

## Phase 2: フォルダ選択 (Ctrl+1) (v1.2)

- TaskID: T03
- Related F: F-002
- 実装内容:
  - KeyPreview を有効化
  - Ctrl+1 で FolderBrowserDialog を表示
  - 選択したフォルダパスを currentFolder に保持
  - 画像非表示状態でもエラーにならないようにする
- テスト項目:
  - Ctrl+1 でダイアログが表示される
  - フォルダを選択後、アプリが異常終了しない
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.2
  - Commit: （後記入）

## Phase 3: 画像読み込み・ソート (v1.3)

- TaskID: T04
- Related F: F-003
- 実装内容:
  - 指定フォルダの .jpg/.jpeg/.png/.webp を取得
  - ファイル名から数字部分を抽出し、昇順ソート
  - ソート結果を List<string> imagePaths に保持
- テスト項目:
  - 連番フォルダ(001,002,...)に対して:
    - 画像パスが番号順に並んでいるか（内部確認用ログ or デバッグ出力）
  - 拡張子が混在しても正しくソートされること
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.3
  - Commit: （後記入）

## Phase 4: 左右2面同時表示 (v1.4)

- TaskID: T05
- Related F: F-004
- 実装内容:
  - DisplayTwoImages(index):
    - 右側=小さい番号, 左側=大きい番号を Zoom モードで表示
  - フォルダ選択後、初期ページを表示
- テスト項目:
  - フォルダ選択時に画像が左右に表示されること
  - アスペクト比維持・背景黒色でズーム表示されること
  - 1枚しかない場合もエラーなく動作（左側空白 or 右側にのみ表示）
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.4
  - Commit: （後記入）

## Phase 5: ページ送り操作 (v1.5)

- TaskID: T06
- Related F: F-005
- 実装内容:
  - → キー: currentIndex - 2 （前の2ページ）
  - ← キー: currentIndex + 2 （次の2ページ）
  - 先頭/末尾で止まる（ループ禁止）、奇数枚の境界処理
- テスト項目:
  - 連番画像10枚以上で:
    - →,← を連続押下し、ページが2枚ずつ移動することを確認
  - 先頭/末尾で越えようとすると止まること
  - 奇数枚のセットでもエラーなく動作
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.5
  - Commit: （後記入）

## Phase 6: フォルダ間移動（話送り） (v1.6)

- TaskID: T07
- Related F: F-006
- 実装内容:
  - BuildFolderList:
    - 親ディレクトリのサブフォルダを昇順ソート
  - ↑/↓ で folderIndex を変更し、そのフォルダの画像を読み込み直す
  - currentIndex = 0 に戻す
- テスト項目:
  - 同じ親ディレクトリに複数の話フォルダがある環境で:
    - ↑/↓ で移動するとフォルダが切り替わり、最初ページが表示される
  - フォルダリストの先頭/末尾で越えない
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.6
  - Commit: （後記入）

## Phase 7: 情報表示 (v1.7)

- TaskID: T08
- Related F: F-007
- 実装内容:
  - 下部 Label で:
    - [フォルダ名] / 全話数 右: xxx | 左: yyy | N/M ページ組
  - フォーマットを SOFTSPEC に合わせる
- テスト項目:
  - ページ移動 → 表示情報が正しく更新されるか
  - フォルダ移動 → フォルダ名・話数・ページ組が更新されるか
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.7
  - Commit: （後記入）

## Phase 8: リソース管理 (v1.8)

- TaskID: T09
- Related F: F-008
- 実装内容:
  - 画像切り替え時に前の Image を Dispose
  - OnFormClosing で破棄処理
- テスト項目:
  - ページを数十回移動・フォルダを行き来しても:
    - メモリ使用量が異常増加しない（Task Manager で確認）
  - クローズ後にプロセスが cleanly 終了
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.8
  - Commit: （後記入）

## Phase 9: エラー処理強化 (v1.9)

- TaskID: T10
- Related F: F-005, F-008, §5
- 実装内容:
  - 画像読み込み失敗時:
    - OutOfMemoryException をキャッチし、MessageBox でファイル名を通知
    - その画像は表示せずに続行
  - Program.cs:
    - 起動例外を error_log.txt に記録 + ダイアログ出力
  - フォルダ選択で有効な画像がない場合のメッセージ
- テスト項目:
  - 壊れた画像ファイルを混ぜて読み込ませるテスト
  - アプリ起動失敗を意図的に再現（一時的に設定変更）し、error_log.txt が作成されることを確認
- テスト結果:
  - [x] pass
- 対応バージョン・コミット:
  - Version: v1.9
  - Commit: （後記入）

## Phase 10: 全機能統合テスト・クリーンアップ (v2.0)

- TaskID: T11
- Related F: All
- 実装内容:
  - SOFTSPEC の各 F-xxx を項目ごとに再確認
  - コードの整理（命名・重複削除）
  - DEVELOPITEM.md に最終テスト結果を記録
  - ビルド: Warning/Errors がゼロであることを確認
- テスト項目:
  - シナリオテスト:
    - Ctrl+1 → フォルダ選択
    - ←/→ でページ送り
    - ↑/↓ で話移動
    - ウィンドウリサイズ
    - アプリ終了・再起動
  - dotnet build が Warning=0, Error=0 で成功
- テスト結果:
  - [x] pass（ビルド確認済み: v2.0）
- 対応バージョン・コミット:
  - Version: v2.0
  - Commit: （後記入）

---

## 変更履歴

| 日付       | バージョン | 内容                                           |
|------------|-----------|------------------------------------------------|
| 2026-07-12 | v1.0      | 開発計画書 DEVELOPITEM.md 作成                 |
| 2026-07-12 | v1.0~v1.9 | Phase 0～Phase 9 実装完了                      |
| 2026-07-12 | v2.0      | 全機能統合・クリーンアップ、Warning/Errors=0 に改善、T11 完了 |