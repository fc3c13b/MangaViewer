# TASK09.23 - ModifyListDisplay (フォルダリスト表示後に応答なしの修正)

## 現象
- フォルダリストは正常に表示されるが、画像が表示されず「応答なし」になる。

## 原因
Form1.cs の `ListBoxFolders_DrawItem` および `ResetScrollAnimation` で、
`Graphics.FromHwnd(IntPtr.Zero)` を使用している箇所があり、
これが UI スレッドで重い処理として動作し、大量の描画イベント時にフリーズを引き起こす。

該当箇所:
- `ListBoxFolders_DrawItem` (DrawItem イベント)
  - MeasureString に Graphics.FromHwnd(IntPtr.Zero) を使用
- `ResetScrollAnimation`
  - MeasureString に Graphics.FromHwnd(IntPtr.Zero) を使用

## 修正案

1. **DrawItem で e.Graphics を使用する**
   - ListBoxFolders_DrawItem では、MeasureString やテキスト描画に引数の `e.Graphics` を使い、
     `Graphics.FromHwnd(IntPtr.Zero)` の呼び出しを削除する。

2. **ResetScrollAnimation でも安全な Graphics を使用する**
   - ResetScrollAnimation 内で MeasureString に使用している
     `Graphics.FromHwnd(IntPtr.Zero)` を、
     listBoxFolders.CreateGraphics() で取得し、using で確実に Disposeするように変更する。

3. **Font の再利用（任意だが推奨）**
   - DrawItem やアニメーション関連で都度 new Font("Meiryo UI", 9f) をしている箇所を、
     フィールドにキャッシュして使い回すようにする（GC負荷・描画コストの軽減）。

## 修正対象ファイル
- Form1.cs
