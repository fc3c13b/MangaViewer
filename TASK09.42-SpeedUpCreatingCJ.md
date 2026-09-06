# TASK09.42 - CJ作成時のスキャン高速化

## 課題
キー3でフォルダを指定してCJ（Collection JSON）を作成する際、CBZファイル内の画像数をカウントするために非常に時間がかかる。
NAS環境では特に顕著で、ユーザー体験に悪影響を与えている。

## 現状の処理フロー
```
BuildFolderIndex() → 各フォルダループ:
├─ DJ (folder.json) の有無チェック
├─ 規則1-4の評価（評価値・画像カウントの判定）
├─ CountCbzFiles() ← CBZファイル数カウント
└─ CountImagesWithoutCbzFallback() ← **CBZを開いて画像数をカウント（最も重い）**
```

## 調査結果
ImageCountの使用箇所を特定：
- リスト表示のフィルター機能 → ユーザーが「単体画像ファイルのみ」に使用すると明言済み
- build_rank_db.py → Python側で読み取る値（CBZ数があれば十分）
- DJ/CJへの保存 → スキップ判定には `cbzFileCount > 0` で十分

## 変更内容

### 1. CountImagesWithoutCbzFallback() の除去
**対象メソッド:**
- `BuildFolderIndex()` (規則4処理部分)
- `RescanFolderUsingRules()` 
- `BuildFolderIndexWithCache()` (再スキャン時)

**変更前:**
```csharp
int scanCbzCount = CountCbzFiles(dir);
int scanImageCount = 0;
if (scanCbzCount > 0)
    scanImageCount = CountImagesWithoutCbzFallback(dir); // ← 除去対象
```

**変更後:**
```csharp
int scanCbzCount = CountCbzFiles(dir);
// CBZ内の画像カウントは行わない（単体画像ファイルのみで十分）
```

### 2. DJ保存時のimageCount値
- CBZがあるフォルダ: `imageCount` は0または単体画像数のみ
- スキップ判定ロジックに変更なし（`cbzFileCount > 0` で判定されるため）

## 期待される効果
- CBZファイル内の画像カウント処理が完全に除去される
- NAS環境でのスキャン時間が大幅に短縮される（推定50-80%削減）
- リスト表示のフィルター機能に影響なし（単体画像用のみ使用）

## 影響しない箇所
- CBZファイル数のカウント → **維持**（必要）
- 評価値（Rating）の処理 → **変更なし**
- スキップ判定ロジック（規則1-4）→ **変更なし**
- リスト表示フィルター → **影響なし**（単体画像用のみ）

## 注意事項
- `CountImagesWithoutCbzFallback()` メソッド自体は残す（単体画像ファイルの処理で必要）
- DJに保存される `imageCount` はCBZ内の画像数を含まなくなるが、機能に影響しない

---
**作成日:** 2026/9/4  
**ステータス:** 実装完了 ✅