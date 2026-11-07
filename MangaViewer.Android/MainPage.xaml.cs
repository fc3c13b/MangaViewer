using MangaViewer.Android.Models;
using MangaViewer.Android.Pages;
using MangaViewer.Android.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;

namespace MangaViewer.Android;

public partial class MainPage : ContentPage
{
	private readonly AppSettingsService _settingsService;
	private readonly LocalStorageBrowserService _localStorage;
	private readonly ArchiveImageCacheService _archiveImageCache;
	private readonly IServiceProvider _services;
	private SambaBrowserService? _sambaBrowser;
	private AppSettings _settings = new();

	private List<ViewerFolderItem> _folders = new();
	private List<ViewerImageItem> _imageItems = new();
	private List<string> _images = new();
	private SambaConnectionConfig? _activeSambaConfig;
	private bool _imageListMode;
	private bool _suppressSelectionLoad;

	private readonly ConcurrentDictionary<string, byte[]> _imageCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Queue<string> _imageCacheOrder = new();
	private readonly object _cacheLock = new();
	private CancellationTokenSource? _preloadCts;
	private const int MaxCachedImages = 24;
	private const int MinDisplayCountDefault = 20;
	private const int MaxDisplayCountDefault = 1000;
	private const int MinEvaluationDefault = 8;
	private const int NormalModeImageAreaPercentDefault = 72;
	private const int FullScreenModeImageAreaPercentDefault = 85;

	private int _folderIndex;
	private int _displayCount = 2;
	private int _pageIndex;
	private int _rating;
	private bool _isFullScreenMode;

	private bool _ctrlArmed;
	private bool _altArmed;
	private bool _listVisible = true;
	private int _folderLoadGeneration;
	private CancellationTokenSource? _folderLoadCts;
	private bool _archiveLoadingInProgress;

	private SambaBrowserService SambaBrowser =>
		_sambaBrowser ??= _services.GetRequiredService<SambaBrowserService>();

	public MainPage(AppSettingsService settingsService, LocalStorageBrowserService localStorage, ArchiveImageCacheService archiveImageCache, IServiceProvider services)
	{
		InitializeComponent();
		_settingsService = settingsService;
		_localStorage = localStorage;
		_archiveImageCache = archiveImageCache;
		_services = services;
		RefreshRightList();
		UpdateStatus("起動完了");
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		try
		{
			_settings = await _settingsService.LoadAsync();

			if (_settings.LastSourceType == "local" && !string.IsNullOrWhiteSpace(_settings.LocalRootUri))
			{
				await LoadLocalFoldersAsync(_settings.LocalRootUri!);
			}
			else if (_settings.LastSourceType == "samba" && !string.IsNullOrWhiteSpace(_settings.DefaultSambaConnectionId))
			{
				_activeSambaConfig = _settings.SambaConnections.FirstOrDefault(x => x.Id == _settings.DefaultSambaConnectionId);
				if (_activeSambaConfig != null)
				{
					string root = string.IsNullOrWhiteSpace(_settings.SambaRootPath)
						? SambaBrowser.BuildBasePath(_activeSambaConfig)
						: _settings.SambaRootPath!;
					await LoadSambaFoldersAsync(_activeSambaConfig, root);
				}
			}

			ApplyRuntimeSettings();

			UpdateStatus($"設定ロード完了 | 最終ソース={_settings.LastSourceType ?? "未設定"}");
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MainPage.OnAppearing] {ex}");
			UpdateStatus("起動時エラー");
			await DisplayAlertAsync("起動エラー", ex.Message, "OK");
		}
	}

	private async void OnVirtualKeyClicked(object? sender, EventArgs e)
	{
		if (sender is not Button button)
			return;

		string key = button.Text?.Trim().ToUpperInvariant() ?? string.Empty;
		if (string.IsNullOrEmpty(key))
			return;

		if (key is "CTRL" or "ALT" or "A")
		{
			ArmModifier(key);
			return;
		}

		if ((_folderLoadCts != null || _archiveLoadingInProgress) && key is not "L" and not "S" and not "0" and not "3")
		{
			UpdateStatus("読込中: 画像/CBZ展開完了後にページ移動します。");
			return;
		}

		bool ctrl = _ctrlArmed;
		bool alt = _altArmed;

		try
		{
			switch (key)
			{
				case "←":
					await NavigateHorizontalAsync(isForward: true, ctrl, alt);
					break;
				case "→":
					await NavigateHorizontalAsync(isForward: false, ctrl, alt);
					break;
				case "↑":
					await NavigateVerticalAsync(up: true, ctrl, alt);
					break;
				case "↓":
					await NavigateVerticalAsync(up: false, ctrl, alt);
					break;
				case "S":
					UpdateStatus("S: スライドショー切替");
					break;
				case "L":
					ToggleListPanel();
					break;
				case "+":
					if (ctrl)
					{
						_rating = Math.Min(9, _rating + 1);
						UpdateStatus($"CTRL+ + : 評価={_rating} 次へ移動");
						await NavigateVerticalAsync(up: false, ctrl: false, alt: false);
					}
					else
					{
						UpdateStatus("+ 単押し: 現在仕様では操作なし");
					}
					break;
				default:
					if (char.IsDigit(key, 0))
					{
						await HandleNumberKeyAsync(key[0], ctrl, alt);
					}
					break;
			}
		}
		finally
		{
			_ctrlArmed = false;
			_altArmed = false;
			UpdateModifierLabel();
		}
	}

	private void ArmModifier(string key)
	{
		switch (key)
		{
			case "CTRL":
				_ctrlArmed = true;
				UpdateStatus("CTRL待機");
				break;
			case "ALT":
			case "A":
				_altArmed = true;
				UpdateStatus($"{key}: ALT代用待機");
				break;
		}

		UpdateModifierLabel();
	}

	private async Task HandleNumberKeyAsync(char num, bool ctrl, bool alt)
	{
		if (ctrl)
		{
			_rating = num - '0';
			UpdateStatus($"CTRL+{num}: 評価={_rating}");
			return;
		}

		if (alt)
		{
			UpdateStatus($"ALT+{num}: 現在未割り当て");
			return;
		}

		switch (num)
		{
			case '0':
				await OpenSettingsMenuAsync();
				break;
			case '1':
				_displayCount = 1;
				UpdateStatus("1: DisplayCount=1");
				break;
			case '2':
				_displayCount = 2;
				UpdateStatus("2: DisplayCount=2");
				break;
			case '3':
				await OpenSourceSelectionAsync();
				break;
			case '4':
			case '5':
			case '6':
				UpdateStatus($"{num}: 未割り当て");
				break;
			case '7':
				ToggleDisplayMode();
				break;
			case '8':
				_displayCount = 8;
				UpdateStatus("8: DisplayCount=8");
				break;
			case '9':
				await ToggleFullScreenAsync();
				break;
		}
	}

	private async Task OpenSourceSelectionAsync()
	{
		string action = await DisplayActionSheetAsync(
			"閲覧ソースを選択",
			"キャンセル",
			null,
			"ローカルストレージ",
			"共有フォルダ（SAMBA）");

		if (action == "ローカルストレージ")
		{
			string? root = await _localStorage.PickFolderAsync();
			if (!string.IsNullOrWhiteSpace(root))
			{
				_settings.LocalRootUri = root.Trim();
				_settings.LastSourceType = "local";
				_settings.SambaRootPath = null;
				_activeSambaConfig = null;
				await _settingsService.SaveAsync(_settings);
				await LoadLocalFoldersAsync(_settings.LocalRootUri);
			}
			return;
		}

		if (action == "共有フォルダ（SAMBA）")
		{
			if (_settings.SambaConnections.Count == 0)
			{
				bool open = await DisplayAlertAsync("SAMBA", "接続先が未登録です。設定を開きますか？", "開く", "キャンセル");
				if (open)
					await OpenSettingsAsync();
				return;
			}

			List<string> items = _settings.SambaConnections.Select(x => x.DisplayName).ToList();
			items.Add("設定を開く");
			string pick = await DisplayActionSheetAsync("SAMBA接続先", "キャンセル", null, items.ToArray());
			if (pick == "設定を開く")
			{
				await OpenSettingsAsync();
				return;
			}

			SambaConnectionConfig? config = _settings.SambaConnections.FirstOrDefault(x => x.DisplayName == pick);
			if (config == null)
				return;

			string startPath = _settings.SambaRootPath ?? string.Empty;
			if (string.IsNullOrWhiteSpace(startPath))
				startPath = SambaBrowser.BuildBasePath(config);

			string? selectedRoot = await OpenSambaFolderBrowserAsync(config, startPath);
			if (string.IsNullOrWhiteSpace(selectedRoot))
				return;

			_settings.DefaultSambaConnectionId = config.Id;
			_settings.LastSourceType = "samba";
			_settings.SambaRootPath = selectedRoot;
			_activeSambaConfig = config;
			await _settingsService.SaveAsync(_settings);
			await LoadSambaFoldersAsync(config, selectedRoot);
		}
	}

	private async Task<string?> OpenSambaFolderBrowserAsync(SambaConnectionConfig config, string startPath)
	{
		var tcs = new TaskCompletionSource<string?>();
		var page = new SambaFolderBrowserPage(SambaBrowser, config, startPath, tcs);
		await Navigation.PushModalAsync(new NavigationPage(page));
		return await tcs.Task;
	}

	private async Task OpenSettingsAsync()
	{
		var page = Handler?.MauiContext?.Services.GetService<SambaSettingsPage>()
			?? new SambaSettingsPage(_settingsService);
		await Navigation.PushModalAsync(new NavigationPage(page));
	}

	private async Task OpenSettingsMenuAsync()
	{
		UpdateStatus("0: 設定メニューを表示");
		string currentCacheName = "未設定";
		if (!string.IsNullOrWhiteSpace(_settings.SharedCacheConnectionId))
		{
			SambaConnectionConfig? current = _settings.SambaConnections
				.FirstOrDefault(x => x.Id == _settings.SharedCacheConnectionId);
			if (current != null)
				currentCacheName = current.DisplayName;
		}

		string action = await DisplayActionSheetAsync(
			"設定メニュー",
			"キャンセル",
			null,
			"SAMBA登録を管理",
			$"共通Cache位置を選択（現在: {currentCacheName}）",
			$"最小表示枚数 ({_settings.MinDisplayCount})",
			$"最大表示画像数 ({_settings.MaxDisplayCount})",
			$"最小評価値 ({_settings.MinEvaluation})",
			$"ノーマル表示 画像領域幅% ({_settings.NormalModeImageAreaPercent})",
			$"全画面表示 画像領域幅% ({_settings.FullScreenModeImageAreaPercent})",
			$"読了位置から開始 ({(_settings.StartFromLastViewedPosition ? "ON" : "OFF")})",
			$"評価値フィルタ ({(_settings.MinEvaluationFilterEnabled ? "ON" : "OFF")})",
			$"評価値比較モード ({(_settings.MinEvaluationEqualFilter ? "等しい" : "以上")})");

		if (action == "SAMBA登録を管理")
		{
			UpdateStatus("設定メニュー: SAMBA登録を管理");
			await OpenSettingsAsync();
			return;
		}

		if (action.StartsWith("共通Cache位置を選択", StringComparison.Ordinal))
		{
			UpdateStatus("設定メニュー: 共通Cache位置を選択");
			await SelectSharedCacheLocationAsync();
			return;
		}

		if (action.StartsWith("最小表示枚数", StringComparison.Ordinal))
		{
			await ConfigureMinDisplayCountAsync();
			return;
		}

		if (action.StartsWith("最大表示画像数", StringComparison.Ordinal))
		{
			await ConfigureMaxDisplayCountAsync();
			return;
		}

		if (action.StartsWith("最小評価値", StringComparison.Ordinal))
		{
			await ConfigureMinEvaluationAsync();
			return;
		}

		if (action.StartsWith("ノーマル表示 画像領域幅%", StringComparison.Ordinal))
		{
			await ConfigureNormalLayoutPercentAsync();
			return;
		}

		if (action.StartsWith("全画面表示 画像領域幅%", StringComparison.Ordinal))
		{
			await ConfigureFullScreenLayoutPercentAsync();
			return;
		}

		if (action.StartsWith("読了位置から開始", StringComparison.Ordinal))
		{
			_settings.StartFromLastViewedPosition = !_settings.StartFromLastViewedPosition;
			await _settingsService.SaveAsync(_settings);
			UpdateStatus($"読了位置から開始: {(_settings.StartFromLastViewedPosition ? "ON" : "OFF")}");
			return;
		}

		if (action.StartsWith("評価値フィルタ", StringComparison.Ordinal))
		{
			_settings.MinEvaluationFilterEnabled = !_settings.MinEvaluationFilterEnabled;
			await _settingsService.SaveAsync(_settings);
			UpdateStatus($"評価値フィルタ: {(_settings.MinEvaluationFilterEnabled ? "ON" : "OFF")}");
			return;
		}

		if (action.StartsWith("評価値比較モード", StringComparison.Ordinal))
		{
			_settings.MinEvaluationEqualFilter = !_settings.MinEvaluationEqualFilter;
			await _settingsService.SaveAsync(_settings);
			UpdateStatus($"評価値比較: {(_settings.MinEvaluationEqualFilter ? "等しい" : "以上")}");
		}
	}

	private async Task ConfigureMinDisplayCountAsync()
	{
		int? value = await PromptIntAsync("設定", "最小表示枚数（2-99999）", _settings.MinDisplayCount, 2, 99999);
		if (value == null)
			return;

		_settings.MinDisplayCount = value.Value;
		if (_settings.MaxDisplayCount <= _settings.MinDisplayCount)
			_settings.MaxDisplayCount = _settings.MinDisplayCount + 1;
		await _settingsService.SaveAsync(_settings);
		UpdateStatus($"最小表示枚数: {_settings.MinDisplayCount}");
	}

	private async Task ConfigureMaxDisplayCountAsync()
	{
		int min = Math.Max(3, _settings.MinDisplayCount + 1);
		int current = Math.Max(min, _settings.MaxDisplayCount);
		int? value = await PromptIntAsync("設定", $"最大表示画像数（{min}-100000）", current, min, 100000);
		if (value == null)
			return;

		_settings.MaxDisplayCount = value.Value;
		await _settingsService.SaveAsync(_settings);
		UpdateStatus($"最大表示画像数: {_settings.MaxDisplayCount}");
	}

	private async Task ConfigureMinEvaluationAsync()
	{
		int? value = await PromptIntAsync("設定", "最小評価値（0-10）", _settings.MinEvaluation, 0, 10);
		if (value == null)
			return;

		_settings.MinEvaluation = value.Value;
		await _settingsService.SaveAsync(_settings);
		UpdateStatus($"最小評価値: {_settings.MinEvaluation}");
	}

	private async Task ConfigureNormalLayoutPercentAsync()
	{
		int? value = await PromptIntAsync("設定", "ノーマル表示 画像領域幅%（50-95）", _settings.NormalModeImageAreaPercent, 50, 95);
		if (value == null)
			return;

		_settings.NormalModeImageAreaPercent = value.Value;
		ApplyLayoutColumns();
		await _settingsService.SaveAsync(_settings);
		UpdateStatus($"ノーマル表示 画像領域幅: {_settings.NormalModeImageAreaPercent}%");
	}

	private async Task ConfigureFullScreenLayoutPercentAsync()
	{
		int? value = await PromptIntAsync("設定", "全画面表示 画像領域幅%（70-98）", _settings.FullScreenModeImageAreaPercent, 70, 98);
		if (value == null)
			return;

		_settings.FullScreenModeImageAreaPercent = value.Value;
		ApplyLayoutColumns();
		await _settingsService.SaveAsync(_settings);
		UpdateStatus($"全画面表示 画像領域幅: {_settings.FullScreenModeImageAreaPercent}%");
	}

	private async Task<int?> PromptIntAsync(string title, string message, int initialValue, int min, int max)
	{
		string? input = await DisplayPromptAsync(title, message, initialValue: initialValue.ToString(CultureInfo.InvariantCulture));
		if (input == null)
			return null;

		if (!int.TryParse(input.Trim(), out int parsed) || parsed < min || parsed > max)
		{
			await DisplayAlertAsync("入力エラー", $"{min} から {max} の整数を入力してください。", "OK");
			return null;
		}

		return parsed;
	}

	private async Task SelectSharedCacheLocationAsync()
	{
		if (_settings.SambaConnections.Count == 0)
		{
			bool open = await DisplayAlertAsync("設定", "SAMBA接続先が未登録です。先に登録画面を開きますか？", "開く", "キャンセル");
			if (open)
				await OpenSettingsAsync();
			return;
		}

		List<string> items = _settings.SambaConnections
			.Select((x, i) => $"No.{i + 1} {x.DisplayName}")
			.ToList();
		string pick = await DisplayActionSheetAsync("共通Cache位置（SAMBA登録済み）", "キャンセル", null, items.ToArray());
		if (pick == "キャンセル")
			return;

		int pickedIndex = items.FindIndex(x => x == pick);
		if (pickedIndex < 0 || pickedIndex >= _settings.SambaConnections.Count)
			return;

		SambaConnectionConfig selected = _settings.SambaConnections[pickedIndex];
		_settings.SharedCacheConnectionId = selected.Id;
		_settings.SharedCacheSambaRootPath = SambaBrowser.BuildBasePath(selected);
		await _settingsService.SaveAsync(_settings);
		await DisplayAlertAsync("設定", $"共通Cache位置を設定しました。\n{selected.DisplayName}\n{_settings.SharedCacheSambaRootPath}", "OK");
		UpdateStatus($"共通Cache設定: {selected.DisplayName}");
	}

	private async Task ToggleFullScreenAsync()
	{
		_isFullScreenMode = !_isFullScreenMode;
		ApplyLayoutColumns();
		await DisplayAlertAsync("表示", _isFullScreenMode ? "9: 全画面表示レイアウトON" : "9: 全画面表示レイアウトOFF", "OK");
		UpdateStatus(_isFullScreenMode ? "9: 全画面表示レイアウトON" : "9: 全画面表示レイアウトOFF");
	}

	private void ApplyRuntimeSettings()
	{
		if (_settings.MinDisplayCount < 2)
			_settings.MinDisplayCount = MinDisplayCountDefault;

		if (_settings.MaxDisplayCount <= _settings.MinDisplayCount)
			_settings.MaxDisplayCount = Math.Max(MaxDisplayCountDefault, _settings.MinDisplayCount + 1);

		if (_settings.MinEvaluation < 0 || _settings.MinEvaluation > 10)
			_settings.MinEvaluation = MinEvaluationDefault;

		if (_settings.DisplayCount is not (1 or 2 or 8))
			_settings.DisplayCount = 2;

		if (_settings.NormalModeImageAreaPercent < 50 || _settings.NormalModeImageAreaPercent > 95)
			_settings.NormalModeImageAreaPercent = NormalModeImageAreaPercentDefault;

		if (_settings.FullScreenModeImageAreaPercent < 70 || _settings.FullScreenModeImageAreaPercent > 98)
			_settings.FullScreenModeImageAreaPercent = FullScreenModeImageAreaPercentDefault;

		_displayCount = _settings.DisplayCount;
		ApplyLayoutColumns();
	}

	private void ApplyLayoutColumns()
	{
		double imagePercent = _isFullScreenMode
			? _settings.FullScreenModeImageAreaPercent
			: _settings.NormalModeImageAreaPercent;
		imagePercent = Math.Clamp(imagePercent, 50, 98);
		double listPercent = Math.Max(2, 100 - imagePercent);
		double eachImage = imagePercent / 2.0;

		MainGrid.ColumnDefinitions = new ColumnDefinitionCollection
		{
			new ColumnDefinition { Width = new GridLength(eachImage, GridUnitType.Star) },
			new ColumnDefinition { Width = new GridLength(eachImage, GridUnitType.Star) },
			new ColumnDefinition { Width = new GridLength(listPercent, GridUnitType.Star) }
		};
	}

	private async Task NavigateHorizontalAsync(bool isForward, bool ctrl, bool alt)
	{
		if (alt)
		{
			await MoveFolderIndexAsync(isForward ? 1 : -1);
			UpdateStatus(isForward ? "ALT+←: 次フォルダへ" : "ALT+→: 前フォルダへ");
			return;
		}

		int delta = ctrl ? _displayCount * 4 : _displayCount;
		_pageIndex += isForward ? delta : -delta;
		if (_pageIndex < 0)
			_pageIndex = 0;
		if (_pageIndex >= _images.Count)
			_pageIndex = Math.Max(0, _images.Count - 1);

		string op = ctrl ? "CTRL" : "NORMAL";
		UpdateStatus($"{op} {(isForward ? "←" : "→")}: pageIndex={_pageIndex}");
		RenderCurrentPagePair();
		await SaveViewingStateAsync();
	}

	private async Task NavigateVerticalAsync(bool up, bool ctrl, bool alt)
	{
		int delta;
		if (alt)
		{
			delta = up ? -10 : 10;
		}
		else if (ctrl)
		{
			delta = up ? -5 : 5;
		}
		else
		{
			delta = up ? -1 : 1;
		}

		if (_imageListMode)
		{
			if (_images.Count == 0)
				return;

			_pageIndex = Math.Clamp(_pageIndex + delta, 0, _images.Count - 1);
			RenderCurrentPagePair();
			RefreshRightListSelection();
			string mode = alt ? "ALT" : ctrl ? "CTRL" : "NORMAL";
			UpdateStatus($"{mode} {(up ? "↑" : "↓")}: imageIndex={_pageIndex + 1}/{_images.Count}");
			await SaveViewingStateAsync();
			return;
		}

		await MoveFolderIndexAsync(delta);
		string navMode = alt ? "ALT" : ctrl ? "CTRL" : "NORMAL";
		if (_folders.Count > 0)
			UpdateStatus($"{navMode} {(up ? "↑" : "↓")}: folder={_folders[_folderIndex].Name}");
	}

	private async Task MoveFolderIndexAsync(int delta)
	{
		if (_folders.Count == 0)
			return;

		_folderIndex += delta;
		if (_folderIndex < 0)
			_folderIndex = 0;
		if (_folderIndex >= _folders.Count)
			_folderIndex = _folders.Count - 1;

		_suppressSelectionLoad = true;
		FolderList.SelectedItem = _folders[_folderIndex];
		_suppressSelectionLoad = false;
		await LoadImagesForSelectionAsync(_folders[_folderIndex]);
		RefreshRightListSelection();
	}

	private void ToggleDisplayMode()
	{
		_imageListMode = !_imageListMode;
		ModeLabel.Text = _imageListMode ? "MODE: Image" : "MODE: Folder";
		RefreshRightList();
		RefreshRightListSelection();
		UpdateStatus(_imageListMode ? "7: 画像リストモード" : "7: フォルダリストモード");
	}

	private void ToggleListPanel()
	{
		_listVisible = !_listVisible;
		RightPanel.IsVisible = _listVisible;
		UpdateStatus($"L: リスト表示={(_listVisible ? "ON" : "OFF")}");
	}

	private void OnFolderSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_suppressSelectionLoad)
			return;

		if (e.CurrentSelection.FirstOrDefault() is ViewerFolderItem folderItem)
		{
			int idx = _folders.FindIndex(x => x.Location == folderItem.Location);
			if (idx >= 0)
				_folderIndex = idx;

			_ = LoadImagesForSelectionAsync(folderItem);
			UpdateStatus($"フォルダ選択: {folderItem.Name}");
			return;
		}

		if (e.CurrentSelection.FirstOrDefault() is not ViewerImageItem imageItem)
			return;

		_pageIndex = Math.Clamp(imageItem.Index, 0, Math.Max(0, _images.Count - 1));
		RenderCurrentPagePair();
		UpdateStatus($"画像選択: {imageItem.Name}");
		_ = SaveViewingStateAsync();
	}

	private void UpdateModifierLabel()
	{
		var flags = new List<string>();
		if (_ctrlArmed) flags.Add("CTRL");
		if (_altArmed) flags.Add("ALT");
		ModifierLabel.Text = flags.Count == 0 ? "MOD: -" : $"MOD: {string.Join("+", flags)}";
	}

	private void UpdateStatus(string text)
	{
		StatusLabel.Text = $"{DateTime.Now:HH:mm:ss} | {text}";
	}

	private void UpdateDownloadStatus(string text)
	{
		DownloadLabel.Text = string.IsNullOrWhiteSpace(text) ? "DL: 待機" : text;
	}

	private async Task SaveViewingStateAsync(string? folderLocation = null)
	{
		if (_images.Count == 0)
			return;

		int index = Math.Clamp(_pageIndex, 0, _images.Count - 1);
		string currentImagePath = _images[index];

		string? currentFolder = folderLocation;
		if (string.IsNullOrWhiteSpace(currentFolder) && _folders.Count > 0)
		{
			int folderIndex = Math.Clamp(_folderIndex, 0, _folders.Count - 1);
			currentFolder = _folders[folderIndex].Location;
		}

		if (string.IsNullOrWhiteSpace(currentFolder))
			return;

		_settings.LastViewedFolderLocation = currentFolder;
		_settings.LastViewedImagePath = currentImagePath;
		await _settingsService.SaveAsync(_settings);
	}

	private async Task LoadLocalFoldersAsync(string treeUri)
	{
		var folders = await _localStorage.ListSubfoldersAsync(treeUri);
		_folders = folders.ToList();
		RefreshRightList();
		if (_folders.Count == 0)
		{
			UpdateStatus("LOCAL: サブフォルダなし");
			return;
		}

		_folderIndex = 0;
		_suppressSelectionLoad = true;
		FolderList.SelectedItem = _folders[0];
		_suppressSelectionLoad = false;
		await LoadImagesForSelectionAsync(_folders[0]);
		UpdateStatus($"LOCAL: {_folders.Count} フォルダ読込");
	}

	private async Task LoadSambaFoldersAsync(SambaConnectionConfig config, string rootPath)
	{
		try
		{
			var children = await SambaBrowser.ListChildrenAsync(config, rootPath);
			var folders = children
				.Where(x => x.IsDirectory && !x.IsParentLink)
				.Select(x => new ViewerFolderItem
				{
					Name = x.Name,
					Location = x.Path,
					Source = "samba"
				})
				.ToList();

			if (folders.Count == 0)
			{
				folders.Add(new ViewerFolderItem
				{
					Name = new Uri(rootPath).Segments.LastOrDefault()?.TrimEnd('/') ?? "(current)",
					Location = rootPath,
					Source = "samba"
				});
			}

			_folders = folders;
			RefreshRightList();
			_folderIndex = 0;
			_suppressSelectionLoad = true;
			FolderList.SelectedItem = _folders[0];
			_suppressSelectionLoad = false;
			await LoadImagesForSelectionAsync(_folders[0]);
			UpdateStatus($"SAMBA: {_folders.Count} フォルダ読込");
		}
		catch (Exception ex)
		{
			await DisplayAlertAsync("SAMBA", ex.Message, "OK");
			UpdateStatus("SAMBA読込失敗");
		}
	}

	private async Task<IReadOnlyList<string>> TryLoadArchiveImagesWithRetryAsync(
		string source,
		Func<Task<IReadOnlyList<string>>> loader,
		int generation,
		CancellationToken cancellationToken,
		int maxAttempts,
		TimeSpan baseDelay)
	{
		for (int attempt = 1; attempt <= maxAttempts; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var images = await loader();
			if (images.Count > 0)
			{
				Debug.WriteLine($"[MainPage] archive retry success source={source} gen={generation} attempt={attempt} images={images.Count}");
				return images;
			}

			if (attempt < maxAttempts)
			{
				var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * attempt);
				Debug.WriteLine($"[MainPage] archive retry source={source} gen={generation} attempt={attempt}/{maxAttempts} delay={delay.TotalSeconds:F1}s");
				await Task.Delay(delay, cancellationToken);
			}
		}

		return Array.Empty<string>();
	}

	private async Task LoadImagesForSelectionAsync(ViewerFolderItem folder)
	{
		var localCts = new CancellationTokenSource();
		var previousCts = Interlocked.Exchange(ref _folderLoadCts, localCts);
		previousCts?.Cancel();
		previousCts?.Dispose();

		CancellationToken cancellationToken = localCts.Token;
		int generation = Interlocked.Increment(ref _folderLoadGeneration);
		_archiveLoadingInProgress = true;
		UpdateStatus($"画像読込開始: {folder.Name}");
		UpdateDownloadStatus("DL: 準備中");
		bool archiveWorkStarted = false;
		bool archiveWorkCompleted = false;
		try
		{
			Debug.WriteLine($"[MainPage] LoadImagesForSelection start gen={generation} source={folder.Source} location={folder.Location}");
			cancellationToken.ThrowIfCancellationRequested();
			if (folder.Source == "local")
			{
				_images = (await _localStorage.ListImageUrisAsync(folder.Location)).ToList();
				cancellationToken.ThrowIfCancellationRequested();
				Debug.WriteLine($"[MainPage] direct local images={_images.Count}");
				if (_images.Count == 0)
				{
					var archives = await _localStorage.ListArchiveUrisAsync(folder.Location);
					cancellationToken.ThrowIfCancellationRequested();
					Debug.WriteLine($"[MainPage] local archives={archives.Count}");
					if (archives.Count > 0)
					{
						archiveWorkStarted = true;
						UpdateDownloadStatus($"DL: {folder.Name} 読込中");
						try
						{
							_images = (await TryLoadArchiveImagesWithRetryAsync(
								"local",
								() =>
								{
									using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
									cts.CancelAfter(TimeSpan.FromSeconds(15));
									return _archiveImageCache
										.GetImagesFromLocalArchivesAsync(
											archives,
											_localStorage,
											cts.Token,
											message =>
											{
												if (!cancellationToken.IsCancellationRequested)
														MainThread.BeginInvokeOnMainThread(() => UpdateDownloadStatus(message));
											})
										.WaitAsync(TimeSpan.FromSeconds(16), cancellationToken);
								},
								generation,
								cancellationToken,
								maxAttempts: 3,
								baseDelay: TimeSpan.FromSeconds(1.5)))
								.ToList();
							archiveWorkCompleted = true;
						}
						catch (TimeoutException)
						{
							Debug.WriteLine($"[MainPage] local archive timeout gen={generation}");
							_images.Clear();
						}
						catch (OperationCanceledException)
						{
							Debug.WriteLine($"[MainPage] local archive canceled gen={generation}");
							_images.Clear();
						}
					}
					Debug.WriteLine($"[MainPage] local archive extracted images={_images.Count}");
				}
			}
			else
			{
				if (_activeSambaConfig == null)
					return;

				int timeoutSeconds = Math.Clamp(_activeSambaConfig.TimeoutSeconds <= 0 ? 300 : _activeSambaConfig.TimeoutSeconds, 5, 300);
				int uiTimeoutSeconds = timeoutSeconds;
				try
				{
					_images = (await SambaBrowser
						.ListImagePathsAsync(_activeSambaConfig, folder.Location)
						.WaitAsync(TimeSpan.FromSeconds(uiTimeoutSeconds), cancellationToken))
						.ToList();
				}
				catch (TimeoutException)
				{
					Debug.WriteLine($"[MainPage] samba direct image list timeout gen={generation} sec={uiTimeoutSeconds}");
					_images = new List<string>();
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[MainPage] samba direct image list failed gen={generation} {ex.GetType().Name}: {ex.Message}");
					_images = new List<string>();
				}

				Debug.WriteLine($"[MainPage] direct samba images={_images.Count}");
				if (_images.Count == 0)
				{
					IReadOnlyList<string> archives;
					try
					{
						archives = await SambaBrowser
							.ListArchivePathsAsync(_activeSambaConfig, folder.Location)
							.WaitAsync(TimeSpan.FromSeconds(uiTimeoutSeconds), cancellationToken);
					}
					catch (TimeoutException)
					{
						Debug.WriteLine($"[MainPage] samba archive list timeout gen={generation} sec={uiTimeoutSeconds}");
						archives = Array.Empty<string>();
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"[MainPage] samba archive list failed gen={generation} {ex.GetType().Name}: {ex.Message}");
						archives = Array.Empty<string>();
					}

					Debug.WriteLine($"[MainPage] samba archives={archives.Count}");
					if (archives.Count > 0)
					{
						archiveWorkStarted = true;
						UpdateDownloadStatus($"DL: {folder.Name} 読込中");
						try
						{
							_images = (await TryLoadArchiveImagesWithRetryAsync(
								"samba",
								() =>
								{
									return _archiveImageCache
										.GetImagesFromSambaArchivesAsync(
											archives,
											_activeSambaConfig,
											SambaBrowser,
											cancellationToken,
											message =>
											{
												if (cancellationToken.IsCancellationRequested || generation != Volatile.Read(ref _folderLoadGeneration))
													return;
													MainThread.BeginInvokeOnMainThread(() => UpdateDownloadStatus(message));
											})
										.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
								},
								generation,
								cancellationToken,
								maxAttempts: 3,
								baseDelay: TimeSpan.FromSeconds(2)))
								.ToList();
							archiveWorkCompleted = true;
						}
						catch (TimeoutException)
						{
							Debug.WriteLine($"[MainPage] samba archive timeout gen={generation} sec={uiTimeoutSeconds}");
							_images.Clear();
						}
						catch (OperationCanceledException)
						{
							Debug.WriteLine($"[MainPage] samba archive canceled gen={generation}");
							_images.Clear();
						}
					}
					Debug.WriteLine($"[MainPage] samba archive extracted images={_images.Count}");
				}
			}

			if (generation != Volatile.Read(ref _folderLoadGeneration))
			{
				Debug.WriteLine($"[MainPage] LoadImagesForSelection discarded stale gen={generation}");
				return;
			}

			ClearImageCache();
			_pageIndex = 0;
			if (_settings.StartFromLastViewedPosition &&
				string.Equals(_settings.LastViewedFolderLocation, folder.Location, StringComparison.OrdinalIgnoreCase) &&
				!string.IsNullOrWhiteSpace(_settings.LastViewedImagePath))
			{
				int restoredIndex = _images.FindIndex(x => string.Equals(x, _settings.LastViewedImagePath, StringComparison.OrdinalIgnoreCase));
				if (restoredIndex >= 0)
					_pageIndex = restoredIndex;
			}

			_imageItems = _images
				.Select((path, idx) => new ViewerImageItem
				{
					Index = idx,
					Name = $"{idx + 1:D4}: {Path.GetFileName(path)}",
					Path = path
				})
				.ToList();

			RefreshRightList();
			if (_images.Count == 0)
			{
				Debug.WriteLine($"[MainPage] no images branch folder={folder.Name}");
				LeftImage.Source = null;
				RightImage.Source = null;
				if (!_settings.CbzSupportHintShown)
				{
					await DisplayAlertAsync(
						"画像が見つかりません",
						"このフォルダーに JPG/JPEG/PNG/WEBP/CBZ/ZIP が見つかりません。",
						"OK");
					_settings.CbzSupportHintShown = true;
					await _settingsService.SaveAsync(_settings);
				}

				UpdateStatus($"画像0件: {folder.Name} (画像/CBZ/ZIPなし または 読込タイムアウト)");
				UpdateDownloadStatus(archiveWorkStarted ? "DL: 未完了" : "DL: 対象外");
				return;
			}

			RenderCurrentPagePair();
			_ = PreloadAroundAsync(_pageIndex);
			await SaveViewingStateAsync(folder.Location);
			Debug.WriteLine($"[MainPage] render start pageIndex={_pageIndex} images={_images.Count}");
			UpdateStatus($"画像ロード: {folder.Name} ({_images.Count}件)");
			UpdateDownloadStatus(archiveWorkStarted ? (archiveWorkCompleted ? "DL: 完了" : "DL: 未完了") : "DL: 対象外");
		}
		catch (OperationCanceledException)
		{
			Debug.WriteLine($"[MainPage] LoadImagesForSelection canceled gen={generation}");
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[MainPage] LoadImagesForSelection failed: {ex}");
			_images.Clear();
			LeftImage.Source = null;
			RightImage.Source = null;
			await DisplayAlertAsync("画像ロード", ex.Message, "OK");
			UpdateStatus("画像ロード失敗");
		}
		finally
		{
			_archiveLoadingInProgress = false;
			if (archiveWorkStarted && !archiveWorkCompleted)
				UpdateDownloadStatus("DL: 未完了");
			if (ReferenceEquals(_folderLoadCts, localCts))
			{
				Interlocked.CompareExchange(ref _folderLoadCts, null, localCts);
			}

			localCts.Dispose();
		}
	}

	private void RenderCurrentPagePair()
	{
		if (_images.Count == 0)
		{
			LeftImage.Source = null;
			RightImage.Source = null;
			return;
		}

		int leftIndex = Math.Clamp(_pageIndex, 0, _images.Count - 1);
		int rightIndex = leftIndex + 1;
		string leftPath = _images[leftIndex];
		string? rightPath = rightIndex < _images.Count ? _images[rightIndex] : null;

		LeftImage.Source = BuildImageSource(leftPath);
		RightImage.Source = rightPath != null ? BuildImageSource(rightPath) : null;
		_ = EnsureVisibleImagesLoadedAndRefreshAsync(leftPath, rightPath);
		_ = PreloadAroundAsync(leftIndex);
		RefreshRightListSelection();
	}

	private ImageSource BuildImageSource(string path)
	{
		if (_imageCache.TryGetValue(path, out var bytes))
		{
			return ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
		}

		if (File.Exists(path))
		{
			return ImageSource.FromStream(() => File.OpenRead(path));
		}

		if (_settings.LastSourceType == "samba" && _activeSambaConfig != null)
		{
			// Avoid opening SMB streams on the UI thread.
			return ImageSource.FromStream(() => Stream.Null);
		}

		return ImageSource.FromStream(() => _localStorage.OpenRead(path) ?? Stream.Null);
	}

	private async Task EnsureVisibleImagesLoadedAndRefreshAsync(string leftPath, string? rightPath)
	{
		if (_settings.LastSourceType != "samba" || _activeSambaConfig == null)
			return;

		try
		{
			await EnsureImageCachedAsync(
				leftPath,
				CancellationToken.None,
				message => MainThread.BeginInvokeOnMainThread(() => UpdateDownloadStatus(message)));
			if (!string.IsNullOrWhiteSpace(rightPath))
				await EnsureImageCachedAsync(
					rightPath,
					CancellationToken.None,
					message => MainThread.BeginInvokeOnMainThread(() => UpdateDownloadStatus(message)));

			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				if (_images.Count == 0)
					return;

				int currentLeftIndex = Math.Clamp(_pageIndex, 0, _images.Count - 1);
				string currentLeftPath = _images[currentLeftIndex];
				string? currentRightPath = currentLeftIndex + 1 < _images.Count ? _images[currentLeftIndex + 1] : null;

				if (!string.Equals(currentLeftPath, leftPath, StringComparison.OrdinalIgnoreCase))
					return;

				if (!string.Equals(currentRightPath ?? string.Empty, rightPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
					return;

				LeftImage.Source = BuildImageSource(leftPath);
				RightImage.Source = rightPath != null ? BuildImageSource(rightPath) : null;
			});
		}
		catch
		{
			// Visible-image preload is best-effort.
		}
	}

	private void RefreshRightList()
	{
		if (_imageListMode)
		{
			FolderList.ItemsSource = _imageItems;
		}
		else
		{
			FolderList.ItemsSource = _folders;
		}
	}

	private void RefreshRightListSelection()
	{
		_suppressSelectionLoad = true;
		if (_imageListMode)
		{
			if (_imageItems.Count > 0)
			{
				int index = Math.Clamp(_pageIndex, 0, _imageItems.Count - 1);
				FolderList.SelectedItem = _imageItems[index];
			}
			else
			{
				FolderList.SelectedItem = null;
			}
		}
		else
		{
			if (_folders.Count > 0)
			{
				int index = Math.Clamp(_folderIndex, 0, _folders.Count - 1);
				FolderList.SelectedItem = _folders[index];
			}
			else
			{
				FolderList.SelectedItem = null;
			}
		}
		_suppressSelectionLoad = false;
	}

	private void ClearImageCache()
	{
		_preloadCts?.Cancel();
		_imageCache.Clear();
		lock (_cacheLock)
		{
			_imageCacheOrder.Clear();
		}
	}

	private async Task PreloadAroundAsync(int centerIndex)
	{
		if (_images.Count == 0)
			return;

		var cts = new CancellationTokenSource();
		var previous = Interlocked.Exchange(ref _preloadCts, cts);
		previous?.Cancel();
		previous?.Dispose();

		CancellationToken token = cts.Token;
		int start = Math.Clamp(centerIndex - 1, 0, _images.Count - 1);
		int end = Math.Clamp(centerIndex + Math.Max(6, _displayCount * 3), 0, _images.Count - 1);

		for (int i = start; i <= end; i++)
		{
			if (token.IsCancellationRequested)
				return;

			string path = _images[i];
			if (_imageCache.ContainsKey(path))
				continue;

			try
			{
				await EnsureImageCachedAsync(
					path,
					token,
					message => MainThread.BeginInvokeOnMainThread(() => UpdateDownloadStatus(message)));
			}
			catch
			{
				// Preload failure must not break navigation.
			}
		}
	}

	private async Task EnsureImageCachedAsync(string path, CancellationToken token, Action<string>? statusReporter = null)
	{
		if (_imageCache.ContainsKey(path))
			return;

		byte[] bytes;
		if (File.Exists(path))
		{
			await using var source = File.OpenRead(path);
			await using var mem = new MemoryStream();
			await source.CopyToAsync(mem, token);
			bytes = mem.ToArray();
		}
		else if (_settings.LastSourceType == "samba" && _activeSambaConfig != null)
		{
			statusReporter?.Invoke($"DL: {Path.GetFileName(path)} 読込中");
			bytes = await Task.Run(() =>
			{
				using var source = SambaBrowser.OpenRead(_activeSambaConfig, path);
				using var mem = new MemoryStream();
				CopyStreamWithProgress(source, mem, token, statusReporter, Path.GetFileName(path));
				return mem.ToArray();
			}, token);
		}
		else
		{
			await using var source = OpenImageStream(path);
			if (source == null)
				return;

			await using var mem = new MemoryStream();
			await source.CopyToAsync(mem, token);
			bytes = mem.ToArray();
		}

		if (bytes.Length == 0)
			return;

		if (_imageCache.TryAdd(path, bytes))
		{
			lock (_cacheLock)
			{
				_imageCacheOrder.Enqueue(path);
				while (_imageCacheOrder.Count > MaxCachedImages)
				{
					if (_imageCacheOrder.TryDequeue(out string? oldKey))
					{
						_imageCache.TryRemove(oldKey, out _);
					}
				}
			}
		}
	}

	private static void CopyStreamWithProgress(Stream source, Stream destination, CancellationToken token, Action<string>? statusReporter, string displayName)
	{
		byte[] buffer = new byte[1024 * 1024];
		var stopwatch = Stopwatch.StartNew();
		TimeSpan lastReportAt = TimeSpan.Zero;
		long totalBytes = source.CanSeek ? source.Length : -1;
		long copiedBytes = 0;

		while (true)
		{
			token.ThrowIfCancellationRequested();
			int read = source.Read(buffer, 0, buffer.Length);
			if (read <= 0)
				break;

			destination.Write(buffer, 0, read);
			copiedBytes += read;

			if (statusReporter != null && stopwatch.Elapsed - lastReportAt >= TimeSpan.FromSeconds(1))
			{
				lastReportAt = stopwatch.Elapsed;
				double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
				double speed = elapsedSeconds > 0.1 ? copiedBytes / elapsedSeconds : 0;
				if (totalBytes > 0 && speed > 0)
				{
					double remainingSeconds = Math.Max(0, (totalBytes - copiedBytes) / speed);
					statusReporter($"DL: {displayName} | 残り約 {remainingSeconds:F0}秒");
				}
				else
				{
					statusReporter($"DL: {displayName} | {copiedBytes / 1024.0 / 1024.0:F1}MB");
				}
			}
		}
	}

	private Stream? OpenImageStream(string path)
	{
		if (File.Exists(path))
			return File.OpenRead(path);

		if (_settings.LastSourceType == "samba" && _activeSambaConfig != null)
		{
			try
			{
				return SambaBrowser.OpenRead(_activeSambaConfig, path);
			}
			catch
			{
				return null;
			}
		}

		return _localStorage.OpenRead(path);
	}
}
