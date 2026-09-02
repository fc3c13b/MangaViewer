# TASK09.38 - SettingsDialog 設定構造の修正計画

## 1. 概要と目的

### 背景
SettingsDialog.cs に存在する obsolete な設定項目を削除し、新しいフィルタリング仕様に対応させる。

### 削除対象（obsolete理由）
| 設定キー | 理由 |
|---------|------|
| DisplayCount | 非推奨。Min/Maxの範囲指定に統合済み |
| DbMinDisplayCount | DBList用として分離されたが不要 |
| DbMaxDisplayCount | DBList用として分離されたが不要 |
| DbMinEvaluation | MinEvaluation と統合される |
| RatingOneKeywords | 評価1キーワード機能は廃止 |

### 追加対象（新フィルタ仕様）
- **MinEvaluationFilterEnabled**: チェックボックス（評価値フィルタ有効/無効）
- **MinEvaluationEqualFilter**: ラジオボタンまたはチェックボックス（「以上」vs「等しい」）

---

## 2. Legacy Dialog Controls インベントリ

### 削除対象の行番号一覧表

| 設定項目 | フィールド定義 | UI要素 | 保存ロジック | 対応行番号 |
|---------|---------------|--------|-------------|-----------|
| DisplayCount | - | cbDisplay2, cbDisplay8 | 行882 | 削除対象 |
| DbMinDisplayCount | NumericUpDown | numDbMinEvaluation? | 行889 | 削除対象 |
| DbMaxDisplayCount | NumericUpDown | - | 行890 | 削除対象 |
| DbMinEvaluation | NumericUpDown (numMinEvaluation) | labelMinEvaluation | 行891 | 統合対象 |
| RatingOneKeywords | TextBox (txtRatingOneKeywords) | txtRatingOneKeywords | 行885 | 削除対象 |

---

## 3. 変更サマリー

### Before/After の比較表

| カテゴリ | Before (削除) | After (追加) |
|--------|---------------|-------------|
| フィールド定義 | numMinEvaluation, txtRatingOneKeywords | cbMinEvalFilterEnabled, rbMinEvalEqual |
| SaveSettingsToFile パラメータ | displayCount, ratingOneKeywords等 | minEvaluationFilterEnabled, minEvaluationEqualFilter |
| JSONキー | DisplayCount, DbMinDisplayCount等 | MinEvaluationFilterEnabled, MinEvaluationEqualFilter |

### 行数推定
- **削除**: ~300行（フィールド定義、InitializeComponent内UI生成）
- **追加**: ~50行（新フィルタUI、イベントハンドラ）
- **純減**: ~250行
