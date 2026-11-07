using MangaViewer.Android.Models;
using MangaViewer.Android.Services;
using System.Diagnostics;

namespace MangaViewer.Android.Pages;

public partial class SambaFolderBrowserPage : ContentPage
{
    private readonly SambaBrowserService _browser;
    private readonly SambaConnectionConfig _config;
    private readonly TaskCompletionSource<string?> _tcs;

    private string _currentPath;
    private BrowserItem? _selected;

    public SambaFolderBrowserPage(SambaBrowserService browser, SambaConnectionConfig config, string initialPath, TaskCompletionSource<string?> tcs)
    {
        InitializeComponent();
        _browser = browser;
        _config = config;
        _tcs = tcs;
        _currentPath = initialPath;
        Title = $"SAMBA: {config.DisplayName}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync(_currentPath);
    }

    private async Task LoadAsync(string path)
    {
        try
        {
            PathLabel.Text = path;
            StatusLabel.Text = "読込中...";
            var children = await _browser.ListChildrenAsync(_config, path);
            FolderList.ItemsSource = children;
            _selected = null;
            _currentPath = path;
            StatusLabel.Text = $"{children.Count} 件";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "読込失敗";
            Debug.WriteLine($"[SambaFolderBrowserPage.LoadAsync] host={_config.Host}, share={_config.ShareName}, path={path}, ex={ex}");

            string message = ex.Message;
            if (string.IsNullOrWhiteSpace(_config.ShareName) &&
                message.Contains("network name", StringComparison.OrdinalIgnoreCase))
            {
                message =
                    "共有一覧ブラウズがサーバー側で許可されていない可能性があります。\n" +
                    "SAMBA設定で共有名を明示入力してください（例: manga）。\n\n" +
                    $"host={_config.Host}\npath={path}\n\n" +
                    $"detail: {ex.Message}";
            }

            await DisplayAlertAsync("SAMBAブラウザ", message, "OK");
        }
    }

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        if (_selected == null)
            return;

        if (!_selected.IsDirectory)
            return;

        await LoadAsync(_selected.Path);
    }

    private async void OnSelectClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(_currentPath);
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        await Navigation.PopModalAsync();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selected = e.CurrentSelection.FirstOrDefault() as BrowserItem;
    }
}
