$src = [System.IO.File]::ReadAllText("Form1.cs", [System.Text.UTF8Encoding]::new($false))

$oldBlock = @'
        internal void LoadAndSortImages(string folderPath)
        {
            _currentFolder = folderPath;
            if (_folderService == null)
                _folderService = new FolderService(_settings);
            _imagePaths = _folderService.LoadAndSortImages(folderPath);
            _displayManager.ImagePaths = _imagePaths;
        }
'@

$newBlock = @'
        internal void LoadAndSortImages(string folderPath)
        {
            _currentFolder = folderPath;
            if (_folderService == null)
                _folderService = new FolderService(_settings);
            _imagePaths = _folderService.LoadAndSortImages(folderPath);

            // If no images found, try CBZ via CbzManager as fallback
            if ((_imagePaths == null || _imagePaths.Count == 0) && Directory.Exists(folderPath))
            {
                var cbzFiles = (System.IO.Directory.GetFiles(folderPath, "*.cbz", System.IO.SearchOption.TopDirectoryOnly)).ToList();
                if (cbzFiles.Count > 0)
                {
                    CbzManager.InitializeForFolder(cbzFiles);
                    _imagePaths = CbzManager.GetCurrentImagePaths() ?? new List<string>();
                }
            }

            _displayManager.ImagePaths = _imagePaths;
        }
'@

if ($src.Contains($oldBlock)) {
    $src = $src.Replace($oldBlock, $newBlock)
    [System.IO.File]::WriteAllText("Form1.cs", $src, [System.Text.UTF8Encoding]::new($false))
    Write-Host "OK: LoadAndSortImages patched."
} else {
    Write-Host "FAIL: old block not found in Form1.cs"
}