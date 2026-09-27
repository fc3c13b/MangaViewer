using System;

namespace MangaViewer
{
    internal static class CbzUiBridge
    {
        internal static void SyncDisplayState(Form1 form, CbzManager manager, int targetIndex = 0)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (manager == null) throw new ArgumentNullException(nameof(manager));

            form._imagePaths = manager.CurrentImagePaths;
            form._currentIndex = targetIndex;
            form._displayManager.ImagePaths = form._imagePaths;
        }

        internal static void RefreshFromManager(Form1 form, CbzManager manager, bool resetIndex = true)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            if (manager == null) throw new ArgumentNullException(nameof(manager));

            form._imagePaths = manager.CurrentImagePaths;
            if (resetIndex)
                form._currentIndex = 0;
            form._displayManager.ImagePaths = form._imagePaths;
        }
    }
}
