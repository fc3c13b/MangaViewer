$content = Get-Content Form1.cs -Raw
# Add _cbzManager field after _imagePaths
$content = $content -replace 'internal List<string> _imagePaths = new List<string>();', 
    "internal List<string> _imagePaths = new List<string>();`n        internal CbzManager? _cbzManager;"
# Replace GetInfoText string param with List<FolderEntry> 
$content = $content -replace 'GetInfoText\(_currentFolder, _currentFolderIndex, _folderList\)', 
    'GetInfoText(_currentFolder, _currentFolderIndex, [ref]_folderService)'
# Add status callback in BuildSubfolderList
$content = $content -replace 'var entries = _folderService.BuildFolderIndex\(rootPath\);', 
    '_folderService.StatusCallback = (msg) => { labelInfo.Text = msg; Application.DoEvents(); };`n                var entries = _folderService.BuildFolderIndex(rootPath);'
Set-Content Form1.cs -Value $content -Encoding UTF8