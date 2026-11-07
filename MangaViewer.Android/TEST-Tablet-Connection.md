# MangaViewer Android: Windows PC から Tablet 接続テスト手順

## 1. 事前準備

- Android タブレットで開発者向けオプションを有効化
- USB デバッグを ON
- Windows 側に Android SDK / platform-tools を導入
- 同一LANで NAS (SAMBA) に到達できることを確認

## 2. 接続確認

```powershell
adb devices
```

- 端末が `device` として表示されること
- `unauthorized` の場合は端末側で許可ダイアログを承認

## 3. ビルド

```powershell
cd c:\source\repos\MangaViewer\MangaViewer.Android
dotnet build MangaViewer.Android.csproj -f net10.0-android
```

## 4. 実機起動（USB接続）

```powershell
cd c:\source\repos\MangaViewer\MangaViewer.Android
dotnet build MangaViewer.Android.csproj -t:Run -f net10.0-android
```

- 複数端末接続時は `-p:AndroidDeviceSerial=<serial>` を指定

## 5. 動作確認シナリオ

1. 起動後、右側リスト下に仮想キー 20個が表示される
2. `3` キーで「閲覧ソースを選択」モーダルが開く
3. `0` キーで SAMBA 設定画面が開く
4. SAMBA 接続先を追加して「接続テスト」を実行
5. `A` 押下後に `←/→/↑/↓` で ALT 代用動作になる
6. `L` で右側リスト＋キー領域の表示切替ができる
7. `CTRL` + 数字で評価値が更新される

## 6. ログ確認

```powershell
adb logcat | findstr MangaViewer
```

## 7. よくある問題

- 端末が見えない: USBケーブル交換、`adb kill-server` → `adb start-server`
- SAMBAテスト失敗: ホスト名解決不可の可能性。IP指定で再試行
- 445接続不可: NAS/Windows ファイアウォール、SMB設定（SMB2/3）を確認
