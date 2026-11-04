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

    public void StartBarcodeCapture()\n    {\n        try\n        {\n            var intent = new Intent(MediaStore.ActionImageCapture);\n            if (intent.ResolveActivity(PackageManager) is null)\n            {\n                BarcodeCaptured?.Invoke(null);\n                return;\n            }\n            StartActivityForResult(intent, BarcodeCaptureRequestCode);\n        }\n        catch\n        {\n            BarcodeCaptured?.Invoke(null);\n        }\n    }\n\n    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)\n    {\n        base.OnActivityResult(requestCode, resultCode, data);\n        if (requestCode != BarcodeCaptureRequestCode) return;\n        if (resultCode != Result.Ok || data?.Extras?.Get("data") is not Bitmap bitmap)\n        {\n            BarcodeCaptured?.Invoke(null);\n            return;\n        }\n\n        try\n        {\n            var width = bitmap.Width;\n            var height = bitmap.Height;\n            var pixels = new int[width * height];\n            bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);\n            var source = new RGBLuminanceSource(pixels, width, height);\n            var binary = new BinaryBitmap(new HybridBinarizer(source));\n            var reader = new MultiFormatReader();\n            var hints = new System.Collections.Generic.Dictionary<DecodeHintType, object>\n            {\n                [DecodeHintType.POSSIBLE_FORMATS] = new[] { BarcodeFormat.QR_CODE, BarcodeFormat.CODE_128, BarcodeFormat.CODE_39, BarcodeFormat.EAN_13, BarcodeFormat.EAN_8, BarcodeFormat.DATA_MATRIX, BarcodeFormat.PDF_417 }\n            };\n            var decoded = reader.Decode(binary, hints);\n            BarcodeCaptured?.Invoke(decoded?.Text);\n        }\n        catch (ReaderException)\n        {\n            BarcodeCaptured?.Invoke(null);\n        }\n        catch (Exception)\n        {\n            BarcodeCaptured?.Invoke(null);\n        }\n    }\n\n    public void RequestNotificationPermission()
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
