# TASK09.28 - ModifyRatingCheck

## 概要
DB評価値フィルタの動作を修正し、rating=-1 のフォルダがすべて消える問題を解消する。
また、「以上チェック（DbFilterGreaterOrEqual）」ON/OFF で振る舞いを切り替えられるようにする。

## 1. テスト用 DB リスト作成アプリの削除

- TestDbLoader/ フォルダ全体を削除対象とする。
- MangaViewer.sln から TestDbLoader の参照も削除する。

## 2. DbListBuilder.cs の修正

### 設定項目のマッピング
- DB評価値 → Settings.DbMinEvaluation（既存）。
- 以上チェック → Settings.DbFilterGreaterOrEqual。

### 新しい判定ルール

minEvaluation = settings.DbMinEvaluation  
filterOn = settings.DbFilterGreaterOrEqual

- minEvaluation <= 0:
  - フィルタ無効（常に OK）。
- rating == -1:
  - 未評価扱いで必ずリスト表示（OK）。
- filterOn ON (DbFilterGreaterOrEqual=true):
  - rating >= minEvaluation → OK。
- filterOn OFF (DbFilterGreaterOrEqual=false):
  - rating == minEvaluation → OK。

### ShouldIncludeFolder の変更

Before:

private static bool ShouldIncludeFolder(
    int rating,
    int imageCount,
    int minEvaluation,
    int dbMinImageCount,
    int dbMaxImageCount)
{
    // 評価値フィルタ（minEvaluation が設定されている場合）
    if (rating < minEvaluation && minEvaluation > 0)
        return false;

    // ImageCount フィルタ
    if (dbMinImageCount > 0 && imageCount < dbMinImageCount)
        return false;

    if (dbMaxImageCount > 0 && imageCount > dbMaxImageCount)
        return false;

    return true;
}

After:

private static bool ShouldIncludeFolder(
    int rating,
    int imageCount,
    int minEvaluation,
    bool filterGreaterOrEqual,
    int dbMinImageCount,
    int dbMaxImageCount)
{
    // 評価値フィルタ（DB評価値が設定されている場合）
    if (minEvaluation > 0)
    {
        bool allowedByRating;

        if (rating == -1)
            allowedByRating = true;
        else if (filterGreaterOrEqual)
            allowedByRating = rating >= minEvaluation;
        else
            allowedByRating = rating == minEvaluation;

        if (!allowedByRating)
            return false;
    }

    // ImageCount フィルタ
    if (dbMinImageCount > 0 && imageCount < dbMinImageCount)
        return false;

    if (dbMaxImageCount > 0 && imageCount > dbMaxImageCount)
        return false;

    return true;
}

### BuildFromCj の呼び出し変更

Before:

int minEvaluation = settings.DbMinEvaluation;
// ...
Where(f => ShouldIncludeFolder(r, img, minEvaluation, dbMinImageCount, dbMaxImageCount))

After:

bool filterGreaterOrEqual = settings.DbFilterGreaterOrEqual;
// ...
Where(f => ShouldIncludeFolder(
    r, img, minEvaluation, filterGreaterOrEqual, dbMinImageCount, dbMaxImageCount))

### GetExclusionReason の整合

Before:

GetExclusionReason(rating, imageCount, minEvaluation, dbMinImageCount, dbMaxImageCount)

After:

GetExclusionReason(
    rating, imageCount, minEvaluation, filterGreaterOrEqual, dbMinImageCount, dbMaxImageCount)

- 内部ロジックは ShouldIncludeFolder と同じ条件で理由文字列を生成する。

## 3. 効果

- rating=-1 のフォルダがすべて消える問題を解消。
- 「以上チェック」ON/OFF で DB評価値のフィルタ挙動を切り替え可能にする。
