using System.Collections.Generic;

namespace MangaViewer
{
    internal sealed class CbzRuntimeState
    {
        internal List<string> CbxFiles { get; set; } = new();
        internal int ActiveCbxIndex { get; set; }
        internal List<string> CurrentImagePaths { get; set; } = new();
        internal string? CurrentFolder { get; set; }
        internal string? LoadedCbzFile { get; set; }

        internal void Reset()
        {
            CurrentFolder = null;
            CbxFiles.Clear();
            ActiveCbxIndex = 0;
            CurrentImagePaths = new List<string>();
            LoadedCbzFile = null;
        }

        internal void SetCurrentImagePaths(string cbzFile, List<string> imagePaths)
        {
            CurrentImagePaths = imagePaths;
            LoadedCbzFile = cbzFile;
        }
    }
}
