using System;
using System.Threading.Tasks;

namespace MangaViewer
{
    internal static class CbzFormBridge
    {
        internal static bool CanNavigate(Form1 form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            return form._cbzManager != null && NavigationHandler.CanNavigateCbx(form._cbzManager.CbxFiles.Count);
        }

        internal static async Task<bool> SwitchToNextAsync(Form1 form)
        {
            if (!CanNavigate(form))
                return false;

            int loadGeneration = form.BeginCbzLoadGeneration();

            form.RememberCurrentPlaybackPosition();

            await form.VolumeTransitionGate.WaitAsync().ConfigureAwait(false);
            string? nextCbx;
            try
            {
                if (!form.IsLatestCbzLoadGeneration(loadGeneration))
                    return false;

                nextCbx = await Task.Run(() => form._cbzManager!.SwitchToNextCbx()).ConfigureAwait(false);
            }
            finally
            {
                form.VolumeTransitionGate.Release();
            }

            if (!form.IsLatestCbzLoadGeneration(loadGeneration))
                return false;

            if (nextCbx == null)
                return false;

            CbzUiBridge.RefreshFromManager(form, form._cbzManager!);
            return true;
        }

        internal static async Task<bool> SwitchToPreviousAsync(Form1 form)
        {
            if (!CanNavigate(form))
                return false;

            int loadGeneration = form.BeginCbzLoadGeneration();

            form.RememberCurrentPlaybackPosition();

            await form.VolumeTransitionGate.WaitAsync().ConfigureAwait(false);
            string? prevCbx;
            try
            {
                if (!form.IsLatestCbzLoadGeneration(loadGeneration))
                    return false;

                prevCbx = await Task.Run(() => form._cbzManager!.SwitchToPreviousCbx()).ConfigureAwait(false);
            }
            finally
            {
                form.VolumeTransitionGate.Release();
            }

            if (!form.IsLatestCbzLoadGeneration(loadGeneration))
                return false;

            if (prevCbx == null)
                return false;

            CbzUiBridge.RefreshFromManager(form, form._cbzManager!);
            return true;
        }

        internal static void ScheduleNavigationPreload(Form1 form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (form._cbzManager == null) return;
            if (form._folderList == null || form._folderList.Count == 0) return;
            if (form._currentFolderIndex < 0 || form._currentFolderIndex >= form._folderList.Count) return;

            string currentFolder = form._folderList[form._currentFolderIndex];
            string? nextFolder = (form._currentFolderIndex + 1 < form._folderList.Count)
                ? form._folderList[form._currentFolderIndex + 1]
                : null;
            string? nextNextFolder = (form._currentFolderIndex + 2 < form._folderList.Count)
                ? form._folderList[form._currentFolderIndex + 2]
                : null;

            form._cbzManager.PreloadForNavigationContext(currentFolder, 3, nextFolder);

            if (!string.IsNullOrWhiteSpace(nextNextFolder))
                form._cbzManager.PreloadFirstCbxForFolder(nextNextFolder);
        }

        internal static void TriggerPreloadNearEnd(Form1 form, int startIndex)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (form._cbzManager == null || form._imagePaths.Count == 0)
                return;

            int remainingPages = form._imagePaths.Count - startIndex;
            if (remainingPages <= 16)
            {
                _ = Task.Run(() => form._cbzManager.PreloadNextCbx());
            }
        }
    }
}
