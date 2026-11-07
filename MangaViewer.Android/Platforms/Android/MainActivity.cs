using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;

namespace MangaViewer.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Landscape, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	private const int FolderPickerRequestCode = 7401;
	private static TaskCompletionSource<string?>? _folderPickerTcs;
	private static MainActivity? _lastInstance;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);
		_lastInstance = this;
	}

	public static Task<string?> PickFolderAsync()
	{
		if (_lastInstance == null)
			return Task.FromResult<string?>(null);

		if (_folderPickerTcs != null && !_folderPickerTcs.Task.IsCompleted)
			return _folderPickerTcs.Task;

		_folderPickerTcs = new TaskCompletionSource<string?>();

		var intent = new Intent(Intent.ActionOpenDocumentTree);
		intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			intent.PutExtra(DocumentsContract.ExtraInitialUri, global::Android.Net.Uri.Parse("content://com.android.externalstorage.documents/tree/primary%3A"));
		}

		_lastInstance.StartActivityForResult(intent, FolderPickerRequestCode);
		return _folderPickerTcs.Task;
	}

	protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
	{
		base.OnActivityResult(requestCode, resultCode, data);

		if (requestCode != FolderPickerRequestCode)
			return;

		var tcs = _folderPickerTcs;
		_folderPickerTcs = null;
		if (tcs == null)
			return;

		if (resultCode != Result.Ok || data?.Data == null)
		{
			tcs.TrySetResult(null);
			return;
		}

		var uri = data.Data;
		var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
		try
		{
			ContentResolver?.TakePersistableUriPermission(uri, flags);
		}
		catch
		{
			// Persistable grant may fail on some providers; continue with current session grant.
		}

		tcs.TrySetResult(uri.ToString());
	}
}
