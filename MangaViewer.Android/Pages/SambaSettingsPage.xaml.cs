using MangaViewer.Android.Models;
using MangaViewer.Android.Services;

namespace MangaViewer.Android.Pages;

public partial class SambaSettingsPage : ContentPage
{
    private readonly AppSettingsService _settingsService;
    private AppSettings _settings = new();
    private SambaConnectionConfig? _selected;

    public SambaSettingsPage(AppSettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _settings = await _settingsService.LoadAsync();
        ConnectionList.ItemsSource = null;
        ConnectionList.ItemsSource = _settings.SambaConnections
            .Select((connection, index) => new SambaConnectionListItem
            {
                Number = index + 1,
                FolderName = string.IsNullOrWhiteSpace(connection.DisplayName) ? "(未設定)" : connection.DisplayName,
                Host = connection.Host,
                ServerRoot = string.IsNullOrWhiteSpace(connection.RootPath) ? "/" : connection.RootPath,
                Connection = connection
            })
            .ToList();
    }

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        SambaConnectionConfig? config = await PromptConnectionAsync(new SambaConnectionConfig());
        if (config == null)
            return;

        _settings.SambaConnections.Add(config);
        await SaveCredentialFlowAsync(config.Id);
        await _settingsService.SaveAsync(_settings);
        await ReloadAsync();
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (_selected == null)
        {
                        await DisplayAlertAsync("SAMBA設定", "編集対象を選択してください。", "OK");
            return;
        }

        SambaConnectionConfig? config = await PromptConnectionAsync(_selected);
        if (config == null)
            return;

        int idx = _settings.SambaConnections.FindIndex(x => x.Id == _selected.Id);
        if (idx < 0)
            return;

        config.Id = _selected.Id;
        _settings.SambaConnections[idx] = config;

        bool updateCred = await DisplayAlertAsync("SAMBA設定", "認証情報を更新しますか？", "はい", "いいえ");
        if (updateCred)
            await SaveCredentialFlowAsync(config.Id);

        await _settingsService.SaveAsync(_settings);
        await ReloadAsync();
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (_selected == null)
        {
                        await DisplayAlertAsync("SAMBA設定", "削除対象を選択してください。", "OK");
            return;
        }

                bool yes = await DisplayAlertAsync("SAMBA設定", $"{_selected.DisplayName} を削除しますか？", "削除", "キャンセル");
        if (!yes)
            return;

        _settings.SambaConnections.RemoveAll(x => x.Id == _selected.Id);
        if (_settings.DefaultSambaConnectionId == _selected.Id)
            _settings.DefaultSambaConnectionId = null;

        await _settingsService.DeleteCredentialAsync(_selected.Id);
        await _settingsService.SaveAsync(_settings);
        _selected = null;
        await ReloadAsync();
    }

    private async void OnTestClicked(object? sender, EventArgs e)
    {
        if (_selected == null)
        {
                        await DisplayAlertAsync("SAMBA設定", "テスト対象を選択してください。", "OK");
            return;
        }

        bool ok = await _settingsService.TestConnectionAsync(_selected);
        string suffix = string.IsNullOrWhiteSpace(_selected.ShareName)
            ? "\n注意: このテストはTCP:445到達のみです。共有名未指定だとSAMBAブラウズ時に失敗するサーバーがあります。"
            : string.Empty;
        await DisplayAlertAsync("SAMBA接続テスト", (ok ? "TCP:445 接続OK" : "接続失敗（ホスト名/ポート/ネットワークを確認）") + suffix, "OK");
    }

    private async void OnSetDefaultClicked(object? sender, EventArgs e)
    {
        if (_selected == null)
        {
                        await DisplayAlertAsync("SAMBA設定", "既定化する接続を選択してください。", "OK");
            return;
        }

        _settings.DefaultSambaConnectionId = _selected.Id;
        await _settingsService.SaveAsync(_settings);
                await DisplayAlertAsync("SAMBA設定", "既定接続に設定しました。", "OK");
        await ReloadAsync();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selected = (e.CurrentSelection.FirstOrDefault() as SambaConnectionListItem)?.Connection;
    }

    private async Task<SambaConnectionConfig?> PromptConnectionAsync(SambaConnectionConfig source)
    {
        string? displayName = await DisplayPromptAsync("SAMBA設定", "表示名", initialValue: source.DisplayName);
        if (displayName == null)
            return null;

        string? host = await DisplayPromptAsync("SAMBA設定", "サーバー名/IP", initialValue: source.Host);
        if (host == null)
            return null;

        string? share = await DisplayPromptAsync("SAMBA設定", "共有名（推奨: 明示入力。例: manga）", initialValue: source.ShareName);
        if (share == null)
            return null;

        string? root = await DisplayPromptAsync("SAMBA設定", "ルートパス（任意）", initialValue: source.RootPath);
        if (root == null)
            return null;

        string? timeoutRaw = await DisplayPromptAsync("SAMBA設定", "タイムアウト秒", initialValue: source.TimeoutSeconds.ToString());
        if (timeoutRaw == null)
            return null;

        if (!int.TryParse(timeoutRaw, out int timeoutSeconds) || timeoutSeconds <= 0)
            timeoutSeconds = source.TimeoutSeconds <= 0 ? 5 : source.TimeoutSeconds;

        return new SambaConnectionConfig
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
            DisplayName = displayName.Trim(),
            Host = host.Trim(),
            ShareName = share.Trim(),
            RootPath = root.Trim(),
            TimeoutSeconds = timeoutSeconds
        };
    }

    private async Task SaveCredentialFlowAsync(string connectionId)
    {
        string? user = await DisplayPromptAsync("SAMBA認証", "ユーザー名（空可）", initialValue: string.Empty);
        if (user == null)
            return;

        string? pass = await DisplayPromptAsync("SAMBA認証", "パスワード（空可）", initialValue: string.Empty);
        if (pass == null)
            return;

        await _settingsService.SaveCredentialAsync(connectionId, user, pass);
    }
}
