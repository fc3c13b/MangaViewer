using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MangaViewer
{
    internal static class CbzRecoveryHelper
    {
        internal static bool TryRecoverToPlayableCbx(CbzManager manager, bool preferNext)
        {
            if (manager.CbxFiles.Count <= 1)
                return false;

            int originalIndex = manager.ActiveCbxIndex;
            IEnumerable<int> candidateIndexes = BuildCandidateIndexes(manager.CbxFiles.Count, originalIndex, preferNext);

            foreach (int candidateIndex in candidateIndexes)
            {
                if (candidateIndex < 0 || candidateIndex >= manager.CbxFiles.Count)
                    continue;

                manager.ActiveCbxIndex = candidateIndex;
                manager.RefreshCurrentImagePaths(extractEvenIfEmpty: true, preloadNext: false, showWarnings: false);
                if (manager.CurrentImagePaths.Count > 0)
                {
                    StartupHandler.WriteStartupLog($"[CBZ] recovery skip from={originalIndex} to={candidateIndex} file={Path.GetFileName(manager.CbxFiles[candidateIndex])} images={manager.CurrentImagePaths.Count}");
                    return true;
                }
            }

            manager.ActiveCbxIndex = originalIndex;
            manager.RefreshCurrentImagePaths(extractEvenIfEmpty: false, preloadNext: false, showWarnings: false);
            return false;
        }

        private static IEnumerable<int> BuildCandidateIndexes(int count, int originalIndex, bool preferNext)
        {
            if (preferNext)
            {
                return Enumerable.Range(originalIndex + 1, count - originalIndex - 1)
                    .Concat(Enumerable.Range(0, originalIndex).Reverse());
            }

            return Enumerable.Range(0, originalIndex).Reverse()
                .Concat(Enumerable.Range(originalIndex + 1, count - originalIndex - 1));
        }
    }
}
