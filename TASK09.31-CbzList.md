# TASK09.31 – CBZ/ZIP ファイル選択ダイアログ（キー L）

## 概要

- キー **L** で、現在フォルダ内の `.cbz` / `.zip` ファイル一覧をダイアログ表示。
- ダイアログはトグル動作：L をもう一度押下または Esc で閉じる。
- リストからファイルを選択し Enter または OK ボタンで、その CBZ/ZIP を開き画像表示に切り替える。

## 詳細仕様

### トリガー（キー L）

- 対象箇所：Form1 の KeyDown イベント（KeyboardInputHandler と連携）。
- 動作：
  - ダイアログ非表示 → L で表示（ShowCbzSelectDialog）。
  - ダイアログ表示中 → L または Esc で閉じる（HideCbzSelectDialog）。

### ダイアログ UI

- Form1 の画像表示の上にオーバーレイで表示するパネル：
  - Panel: panelCbzListOverlay
    - BackColor: 半透明黒（例：ControlPaint.Dark(DarkGray) + Alpha）
  - Panel: panelCbzListContent
    - Label: lblTitle → 「CBZ / ZIP ファイルを選択」
    - ListBox: listBoxCbzFiles → 一覧表示
    - Button: btnOk, btnCancel

- listBoxCbzFiles：
  - Items に現在フォルダ（_currentFolder）内の .cbz / .zip ファイル名を昇順で設定。
  - SelectedIndex は初期値 0（先頭選択）。
  - 上下キーで移動可能（ListBox デフォルト動作）。

### ダイアログ表示処理（ShowCbzSelectDialog）

- panelCbzListOverlay を作成・配置：
  - Form1 の ClientRectangle に重ねる。
- listBoxCbzFiles.Items に：
  - Directory.GetFiles(_currentFolder, "*.cbz", TopDirectoryOnly)
  - Directory.GetFiles(_currentFolder, "*.zip", TopDirectoryOnly)
  - 拡張名無視でソート（ファイル名順）。
- ダイアログ表示時：
  - panelCbzListOverlay.Visible = true;
  - listBoxCbzFiles.Focus();

### ダイアログ非表示処理（HideCbzSelectDialog）

- L または Esc で呼ばれる。
- panelCbzListOverlay.Visible = false;
- フォーカスを戻す（Form1 に戻す）。

### ファイル選択＋OK（Enter / OK ボタン）

- listBoxCbzFiles の SelectedIndex が有効な場合：
  - 対応するファイルパス cbzPath を取得。
  - CbzManager でその CBZ/ZIP を開く処理を行う：
    - _cbzManager.InitializeForFolder(cbzPath) または既存ロジックに合わせて、
      そのファイルを現在の画像ソースとして読み込む。
  - ダイアログを閉じる（HideCbzSelectDialog）。

### 実装箇所（概要）

- Form1.cs：
  - panelCbzListOverlay / listBoxCbzFiles / ボタン等の作成・配置。
  - ShowCbzSelectDialog() / HideCbzSelectDialog() の実装。
  - KeyDown で L キーをハンドリングし、表示/非表示をトグル。
- KeyboardInputHandler.cs（必要に応じて）：
  - L キーの処理を Form1 から呼び出すように統合。
