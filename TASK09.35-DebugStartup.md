# TASK09.35 - 起動遅延の調査結果と対策 (Debug Startup)

## 1. ログ概要（実測値）

該当ログ抜粋（要約）:

- LoadCjFromFile: 3ms
- BuildRankFiltered: 159ms, folderCount=385
- TryRestoreFromLatestCache 全体: 330ms
- CbzManager.InitializeForFolder: 8887ms (cbzCount=12)
- ImageLoader.Load:
  - baseImgs=1 (+170ms)
  - cbzImgs=1619 (+115763ms)
  - total = 115933ms
- T4 LoadAndSortImages: +115934ms (delta_loadImgs)
- T5 DisplayImages(0): +694ms
- RunBackgroundInit 全体: ~117,230ms

## 2. ステップ別評価（基準 vs 実測）

1) ratings_cache / CJ JSON の読み込み
   - 実測: 3ms
   - 判定: OK

2) メモリ展開・内部構造構築
   - 実測: 数ms〜数十ms程度と推定
   - 判定: OK

3) フィルタリング（画像数/CBZ数、評価値など）
   - 実測: 159ms (folderCount=385)
   - 判定: OK

4) リスト表示（ListBox の反映）
   - 直接計測なしだが、UI応答は正常範囲と推定。
   - 判定: とりあえずOK

5) CBZ/画像の表示準備（キャッシュ利用含む）
   - CbzManager.InitializeForFolder: 8,887ms (12CBZ) → 遅い
   - ImageLoader.Load(cbzImgs): +115,763ms (1,619枚) → 極めて重い
   - DisplayImages(0): 694ms → やや重い範囲だが、二次的な影響と考えられる
   - 判定: 明確に問題あり（ボトルネック確定）

## 3. CBZ キャッシュが「生かされていない」原因の詳細

結論：キャッシュ自体は存在するが、呼び出しパターンと実装のせいで事実上毎回再スキャンしている。

CbzManager.cs の該当箇所（抜粋要約）:

- GetCurrentImagePaths():
  - 常に RefreshCurrentImagePaths(extractEvenIfEmpty: false) を呼ぶ。

- RefreshCurrentImagePaths():
  - キャッシュディレクトリが存在しても、毎回：
    - Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories)
      - Where で拡張子フィルタ
      - OrderBy でソート
  - つまり「キャッシュ Hit」でも、重いファイルスキャン＋フィルタを都度実行。

- ImageLoader.Load の動作（推定）:
  - CBZ が多数ある場合、GetCurrentImagePaths() を複数回呼び出している可能性が高い。
  - その結果：
    - 同じ CBZ キャッシュディレクトリに対して GetFiles(AllDirectories) を何十〜何百回も実行。
    - これが cbzImgs=1619 で +115,763ms の原因になっていると解釈できる。

つまり：
- ディスク上のキャッシュは使われているが、
- 「一度取得した画像パスリストを再利用していない」ため、
- キャッシュがあるのに「毎回再展開されたかのように重い」となる。

## 4. 特定された問題点（修正前）

- メインボトルネックは「ImageLoader.Load + CbzManager の CBZ 展開・キャッシュ処理」
  - cbzImgs=1619 を取得するまでに約 115 秒かかっている。
  - これは以下のような原因が疑われる:
    - GetCurrentImagePaths() やキャッシュディレクトリの再スキャンを画像数回分呼んでいる可能性が高い。
    - 同じ CBZ のキャッシュ一覧やファイルリストを都度再計算している。
    - キャッシュ Hit でも、キャッシュ内の全ファイルを毎回 GetFiles で走査し、それを多数のフォルダ/画像で繰り返している。

- CbzManager.InitializeForFolder も重い:
  - 12CBZ で約 9 秒。
  - RefreshCurrentImagePaths やキャッシュディレクトリ再構築処理が非効率な可能性あり。

## 5. 対策候補（実装用）

以下の対策を、後続の TASK で段階的に実施する。

- (A) GetCurrentImagePaths の結果をメモ化
  - 同じ CBZ/同じフォルダに対して GetCurrentImagePaths() を複数回呼ばないよう、
    ImageLoader または CbzManager で in-memory キャッシュ（Dictionary）を持つ。
  - これにより「1,619 枚 × ディレクトリスキャン」→「1 回のスキャン＋キャッシュ参照」に削減する。

- (B) LoadAndSortImages の CBZ 処理をバッチ化
  - ImageLoader.Load で:
    - CBZ ファイルごとに同じキャッシュディレクトリを何度も再スキャンしない。
    - フォルダ単位で一度だけキャッシュ一覧を取得し、それを共有して cbzImgs を構築する。

- (C) CbzManager.InitializeForFolder の軽量化
  - RefreshCurrentImagePaths で:
    - キャッシュディレクトリの GetFiles を最小限に抑える（必要な拡張子のみ）。
    - 同じ CBZ のキャッシュを再計算しないよう、既存リストと差分だけ更新する方向へ改善。

- (D) 計測ログの追加・整理
  - StartupHandler.cs に追加済みの [PROF] T0〜T5 を維持。
  - ImageLoader / CbzManager で:
    - GetCurrentImagePaths の呼出回数と所要時間をサンプリングで記録し、
      「同じパスを何度も再スキャンしている」箇所を特定する。

## 6. 次のステップ

- TASK09.36（仮）として:
  - ImageLoader.Load と CbzManager に (A)〜(C) の最適化を実装。
  - 同じ環境で再度起動し、error.log から:
    - LoadAndSortImages が数秒以内になるか確認する。
