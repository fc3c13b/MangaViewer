$src = [System.IO.File]::ReadAllText("Form1.cs", [System.Text.UTF8Encoding]::new($false))

# 1) Ensure CbzManager field exists (add after FolderService field if missing)
$fieldLine = "        private CbzManager? _cbzManager;"
if (-not ($src.Contains("_cbzManager"))) {
    $src = $src.Replace(
        "private FolderService? _folderService;",
        ("private FolderService? _folderService;" + "`n" + "        " + $fieldLine)
    )
}

# 2) Replace current LoadAndSortImages body (the patched but broken one).
$oldBlock = @'
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
'@

$newBlock = @'
            // If no images found, try CBZ via CbzManager as fallback
            if ((_imagePaths == null || _imagePaths.Count == 0) && Directory.Exists(folderPath))
            {
                var cbzFiles = System.IO.Directory.GetFiles(folderPath, "*.cbz", System.IO.SearchOption.TopDirectoryOnly);
                if (cbzFiles.Length > 0)
                {
                    if (_cbzManager == null) _cbzManager = new CbzManager();
                    bool ok = _cbzManager.InitializeForFolder(folderPath);
                    if (ok && _cbzManager.CurrentImagePaths != null && _cbzManager.CurrentImagePaths.Count > 0)
                    {
                        _imagePaths = _cbzManager.CurrentImagePaths;
                    }
                }
            }

            _displayManager.ImagePaths = _imagePaths;
'@

$src = $src.Replace($oldBlock, $newBlock)

[System.IO.File]::WriteAllText("Form1.cs", $src, [System.Text.UTF8Encoding]::new($false))
Write-Host "OK: LoadAndSortImages patched v2."