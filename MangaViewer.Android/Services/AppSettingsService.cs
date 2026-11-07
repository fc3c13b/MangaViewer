using System.Net.Sockets;
using System.Text.Json;
using MangaViewer.Android.Models;

namespace MangaViewer.Android.Services;

public sealed class AppSettingsService
{
    private const string SettingsPreferenceKey = "mv_android_settings_v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public Task<AppSettings> LoadAsync()
    {
        string raw = Preferences.Default.Get(SettingsPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return Task.FromResult(new AppSettings());

        try
        {
            AppSettings? parsed = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
            return Task.FromResult(parsed ?? new AppSettings());
        }
        catch
        {
            return Task.FromResult(new AppSettings());
        }
    }

    public Task SaveAsync(AppSettings settings)
    {
        string raw = JsonSerializer.Serialize(settings, JsonOptions);
        Preferences.Default.Set(SettingsPreferenceKey, raw);
        return Task.CompletedTask;
    }

    public async Task SaveCredentialAsync(string connectionId, string userName, string password)
    {
        string key = BuildCredentialKey(connectionId);
        string json = JsonSerializer.Serialize(new CredentialPayload(userName, password), JsonOptions);
        await SecureStorage.Default.SetAsync(key, json);
    }

    public async Task<(string userName, string password)?> GetCredentialAsync(string connectionId)
    {
        try
        {
            string? raw = await SecureStorage.Default.GetAsync(BuildCredentialKey(connectionId));
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            CredentialPayload? payload = JsonSerializer.Deserialize<CredentialPayload>(raw, JsonOptions);
            if (payload == null)
                return null;

            return (payload.UserName, payload.Password);
        }
        catch
        {
            return null;
        }
    }

    public Task DeleteCredentialAsync(string connectionId)
    {
        SecureStorage.Default.Remove(BuildCredentialKey(connectionId));
        return Task.CompletedTask;
    }

    public async Task<bool> TestConnectionAsync(SambaConnectionConfig config, CancellationToken ct = default)
    {
        using var tcp = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds)));
        try
        {
            await tcp.ConnectAsync(config.Host, 445, timeoutCts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCredentialKey(string connectionId) => $"mv_android_smb_cred_{connectionId}";

    private sealed record CredentialPayload(string UserName, string Password);
}
