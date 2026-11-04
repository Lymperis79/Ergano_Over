using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Graphics;
using Android.Provider;
using ZXing;
using ZXing.Common;
using Avalonia.Android;
using System.Runtime.Versioning;
using ActivityResult = global::Android.App.Result;

namespace ErganiManager.Android;

[SupportedOSPlatform("android23.0")]
[Activity(
    Label = "Ergani Manager",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity
{
    private const int NotificationPermissionRequestCode = 4102;
    private const int BarcodeCaptureRequestCode = 4103;
    private global::Android.Net.Uri? _barcodeCaptureUri;
    public static event Action<string?>? BarcodeCaptured;

    public static MainActivity? Current { get; private set; }
    public static bool IsAppInForeground { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
        AndroidNotificationService.CreateChannel(this);
    }

    protected override void OnResume()
    {
        base.OnResume();
        IsAppInForeground = true;
    }

    protected override void OnPause()
    {
        IsAppInForeground = false;
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        IsAppInForeground = false;
        base.OnDestroy();
    }

    public void StartBarcodeCapture()
    {
        try
        {
            var intent = new Intent(MediaStore.ActionImageCapture);
            if (PackageManager is null || intent.ResolveActivity(PackageManager) is null)
            {
                BarcodeCaptured?.Invoke(null);
                return;
            }

            // Ask the camera app to save the full-resolution photo to MediaStore.
            // The thumbnail returned in Intent extras is usually too small for reliable
            // barcode decoding, especially for the QR payloads used by Ergani.
            var values = new ContentValues();
            values.Put(MediaStore.Images.Media.InterfaceConsts.DisplayName,
                "ergani-scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jpg");
            values.Put(MediaStore.Images.Media.InterfaceConsts.MimeType, "image/jpeg");
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                values.Put(MediaStore.Images.Media.InterfaceConsts.RelativePath, "Pictures/ErganiManager");

            _barcodeCaptureUri = ContentResolver?.Insert(MediaStore.Images.Media.ExternalContentUri!, values);
            if (_barcodeCaptureUri != null)
            {
                intent.PutExtra(MediaStore.ExtraOutput, _barcodeCaptureUri);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }

            StartActivityForResult(intent, BarcodeCaptureRequestCode);
        }
        catch
        {
            BarcodeCaptured?.Invoke(null);
        }
    }

    protected override void OnActivityResult(int requestCode, ActivityResult resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != BarcodeCaptureRequestCode) return;

        Bitmap? bitmap = null;
        try
        {
            if (resultCode == (ActivityResult)(-1) && _barcodeCaptureUri != null)
            {
                using var stream = ContentResolver?.OpenInputStream(_barcodeCaptureUri);
                if (stream != null)
                    bitmap = BitmapFactory.DecodeStream(stream);
            }

            // Some camera apps ignore EXTRA_OUTPUT and return only a thumbnail.
            bitmap ??= resultCode == global::Android.App.Result.Ok
                ? data?.Extras?.Get("data") as Bitmap
                : null;

            if (bitmap is null)
            {
                BarcodeCaptured?.Invoke(null);
                return;
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = new int[width * height];
            bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);
            var rgb = new byte[pixels.Length * 3];
            for (var i = 0; i < pixels.Length; i++)
            {
                rgb[i * 3] = (byte)((pixels[i] >> 16) & 0xff);
                rgb[i * 3 + 1] = (byte)((pixels[i] >> 8) & 0xff);
                rgb[i * 3 + 2] = (byte)(pixels[i] & 0xff);
            }
            var source = new RGBLuminanceSource(rgb, width, height);
            var binary = new BinaryBitmap(new HybridBinarizer(source));
            var reader = new MultiFormatReader();
            var hints = new System.Collections.Generic.Dictionary<DecodeHintType, object>
            {
                [DecodeHintType.POSSIBLE_FORMATS] = new[]
                {
                    BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39,
                    BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.DATA_MATRIX,
                    BarcodeFormat.PDF_417
                }
            };
            var decoded = reader.decode(binary, hints);
            BarcodeCaptured?.Invoke(decoded?.Text);
        }
        catch (ReaderException)
        {
            BarcodeCaptured?.Invoke(null);
        }
        catch (Exception)
        {
            BarcodeCaptured?.Invoke(null);
        }
        finally
        {
            bitmap?.Recycle();
            if (_barcodeCaptureUri != null)
            {
                try { ContentResolver?.Delete(_barcodeCaptureUri, null, null); } catch { }
                _barcodeCaptureUri = null;
            }
        }
    }

    public void RequestNotificationPermission()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
            return;

        if (CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            RequestPermissions(
                new[] { global::Android.Manifest.Permission.PostNotifications },
                NotificationPermissionRequestCode);
        }
    }
}

[SupportedOSPlatform("android23.0")]
internal static class AndroidNotificationService
{
    private const string ChannelId = "ergani_alerts";

    public static void CreateChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
            return;

        var channel = new NotificationChannel(
            ChannelId,
            "Ergani Manager alerts",
            NotificationImportance.High)
        {
            Description = "Important attendance, work card and Ergani alerts"
        };
        channel.EnableVibration(true);
        manager.CreateNotificationChannel(channel);
    }

    public static void Show(Context context, MobileNotification item)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
            context.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            return;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager is null)
            return;

        var requestCode = unchecked((int)(item.Id % int.MaxValue));
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingIntent = PendingIntent.GetActivity(
            context,
            requestCode,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(context, ChannelId)
            : new Notification.Builder(context);

        builder.SetSmallIcon(Resource.Drawable.notification_icon)
            .SetContentTitle(string.IsNullOrWhiteSpace(item.Title) ? "Ergani Manager" : item.Title)
            .SetContentText(item.Message)
            .SetStyle(new Notification.BigTextStyle().BigText(item.Message))
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true)
            .SetCategory(Notification.CategoryMessage);

        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            builder!.SetPriority((int)NotificationPriority.High)
                .SetDefaults(NotificationDefaults.All);

        manager.Notify(requestCode, builder.Build());
    }
}
