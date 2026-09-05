# TASK09.42 - LogWriter モジュール (ログファイル分割)

## 1. 目的
- 既存の StartupHandler.Log() は File.AppendAllText を直接呼び出し
- 各ログ書き出しが独立 → ファイル分割の管理が分散
- バッファリングなし → 200回の CBZ 展開で 200回ファイル I/O が発生
- AI が読めるのは **50行 ＝ 約2,000文字** のファイルのみ

## 2. 検討事項
### 2.1 バッファ設計
- **8KB バッファ** → 4KB を超えたら行単位で切り出し
- AI のトークン制限（32,768）を超えるファイルが生成されない

### 2.2 RingBuffer
- .NET に標準のリングバッファはない → NuGet パッケージを使用
  - [RingBuffer 1.5.1](https://www.nuget.org/packages/RingBuffer/) (48K ダウンロード)
- byte ベースの高性能バッファ、非同期 I/O 対応

## 3. 実装計画
1. LogWriter.cs (新規) → Init, Log, Shutdown API
2. StartupHandler.Log() → LogWriter.Log() に置き換え
3. Form1_FormClosing で LogWriter.Shutdown() を呼び出し
4. ログディレクトリ以下に、作成ログを配置、ファイル分割: app-live.{YYYY-MM-DD-HH-MM-SS}.log (日付別)
5. 最新ログのみコピーして app-live.log として保存

## 4. AI が読めるファイル
- @app-live → 最新ログ