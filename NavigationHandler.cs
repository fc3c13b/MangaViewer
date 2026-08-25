using System;
using System.Collections.Generic;

namespace MangaViewer
{
    /// <summary>
    /// ページ送り・フォルダ移動などの「ナビゲーション計算」を一元管理。
    /// Form1 から状態を受け取り、次のインデックスや動作指示を返す。
    /// </summary>
    public static class NavigationHandler
    {
        // ====== 画像ページ送り（NavigateForward / NavigateBackward）======

        /// <summary>
        /// Forward: Nページ分移動した結果の (newIndex, needWrapToMax) を計算。
        /// CBZ切替ロジックは Form1 で行い、ここでは数値計算のみ行う。
        /// </summary>
        public static int ComputeForwardIndex(int currentIndex, int pageCount, int imageCount, int displayCount)
        {
            if (imageCount == 0 || pageCount <= 0) return currentIndex;

            int next = currentIndex + pageCount;
            int maxIndex = ComputeMaxPageIndex(imageCount, displayCount);

            if (next > maxIndex)
                next = maxIndex;

            return next;
        }

        /// <summary>
        /// Backward: Nページ分戻った結果の newIndex を計算。
        /// </summary>
        public static int ComputeBackwardIndex(int currentIndex, int pageCount, int imageCount, int displayCount)
        {
            if (imageCount == 0 || pageCount <= 0) return currentIndex;

            int prev = currentIndex - pageCount;
            if (prev < 0)
                prev = 0;

            return prev;
        }

        /// <summary>
        /// CBZ末尾判定用：現在のインデックスが「最後のページブロック」を超えているか。
        /// </summary>
        public static bool IsPastEnd(int currentIndex, int imageCount, int displayCount)
        {
            if (imageCount == 0) return true;
            int maxIndex = ComputeMaxPageIndex(imageCount, displayCount);
            return currentIndex > maxIndex;
        }

        /// <summary>
        /// CBZ先頭判定用：現在のインデックスが先頭を超えているか。
        /// </summary>
        public static bool IsBeforeStart(int currentIndex)
        {
            return currentIndex < 0;
        }

        /// <summary>
        /// CBZ切替後の newIndex を計算するヘルパー。
        /// - moveToNext=true: 次のCBZへ進む場合（先頭にリセット）
        /// - moveToNext=false: 前のCBZへ戻る場合（末尾ブロックに合わせる）
        /// </summary>
        public static int ComputeIndexAfterCbxSwitch(bool moveToNext, int imageCount, int displayCount)
        {
            if (moveToNext)
                return 0;

            // 前の CBZ に戻った場合は最後のページブロックに合わせる
            return ComputeMaxPageIndex(imageCount, displayCount);
        }

        // ====== フォルダ移動（NavigateFolderBy / NavigateFolders）======

        /// <summary>
        /// フォルダインデックスを delta 分移動した結果の newIndex を計算。
        /// </summary>
        public static int ComputeNewFolderIndex(int currentIndex, int delta, int folderCount)
        {
            if (folderCount == 0) return -1;

            int next = currentIndex + delta;
            if (next < 0) next = 0;
            if (next >= folderCount) next = folderCount - 1;
            return next;
        }

        // ====== DisplayCount / MaxIndex 計算 ======

        /// <summary>
        /// 表示可能な最大ページインデックス（最後のブロックの開始位置）。
        /// </summary>
        public static int ComputeMaxPageIndex(int imageCount, int displayCount)
        {
            if (imageCount == 0 || displayCount <= 0) return 0;

            int maxIndex = Math.Max(0,
                imageCount - (imageCount % displayCount == 0
                    ? displayCount
                    : 1));
            return maxIndex;
        }

        // ====== SetDisplayCount 関連の計算ヘルパー ======

        /// <summary>
        /// DisplayCount を変更する際に、現在の currentIndex が有効な範囲内にあるか確認。
        /// </summary>
        public static bool IsCurrentIndexValid(int currentIndex, int imageCount, int newDisplayCount)
        {
            if (imageCount == 0) return false;
            int max = ComputeMaxPageIndex(imageCount, newDisplayCount);
            return currentIndex <= max;
        }

        /// <summary>
        /// 無効な currentIndex を新しい DisplayCount に合わせて修正。
        /// </summary>
        public static int ClampIndexForDisplayCount(int currentIndex, int imageCount, int displayCount)
        {
            if (imageCount == 0) return 0;
            int max = ComputeMaxPageIndex(imageCount, displayCount);
            if (currentIndex > max) currentIndex = max;
            if (currentIndex < 0) currentIndex = 0;
            return currentIndex;
        }

        // ====== DBList / CJ 用フィルタリング計算ヘルパー ======

        /// <summary>
        /// CJ の FolderEntry から、評価値と DisplayCount でフィルタリングする条件判定。
        /// </summary>
        public static bool ShouldIncludeFolder(
            int rating,
            int displayCount,
            int minEvaluation,
            int dbMinDisplayCount,
            int dbMaxDisplayCount)
        {
            // 評価値フィルタ（minEvaluation が設定されている場合）
            if (rating < minEvaluation && minEvaluation > 0)
                return false;

            // DisplayCount フィルタ（DbMin/DbMax が設定されている場合）
            if (dbMinDisplayCount > 0 && displayCount < dbMinDisplayCount)
                return false;

            if (dbMaxDisplayCount > 0 && displayCount > dbMaxDisplayCount)
                return false;

            return true;
        }

        /// <summary>
        /// フォルダリストのソートキーを生成（評価値優先、次に DisplayCount）。
        /// </summary>
        public static (int rating, int displayCount) MakeSortKey(int rating, int displayCount)
        {
            return (rating, displayCount);
        }

        // ====== CBZ ナビゲーション判定ヘルパー ======

        /// <summary>
        /// CBZ 間移動が有効か（2個以上存在する場合のみ）。
        /// </summary>
        public static bool CanNavigateCbx(int cbzFileCount)
        {
            return cbzFileCount >= 2;
        }
    }
}
