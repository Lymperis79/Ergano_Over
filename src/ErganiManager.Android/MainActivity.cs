using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using System.Runtime.Versioning;

namespace ErganiManager.Android;

// This file is Android-specific. Runtime SDK checks below guard APIs introduced after Android 6.
#pragma warning disable CA1416, CA1422
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

    public void StartBarcodeCapture()
    {
        try
        {
            StartActivityForResult(
                new Intent(this, typeof(BarcodeScannerActivity)),
                BarcodeCaptureRequestCode);
        }
        catch
        {
            BarcodeCaptured?.Invoke(null);
        }
    }

    protected override void OnActivityResult(int requestCode, global::Android.App.Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != BarcodeCaptureRequestCode) return;

        if (resultCode == global::Android.App.Result.Ok)
            BarcodeCaptured?.Invoke(data?.GetStringExtra("barcode"));
        // A user cancellation is not a failed scan; leave the manual-entry field untouched.
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
        {
            // Legacy priority/defaults are required only before notification channels exist.
            builder!.SetPriority((int)NotificationPriority.High);
            builder!.SetDefaults(NotificationDefaults.All);
        }

        manager.Notify(requestCode, builder!.Build());
    }
}

#pragma warning restore CA1416, CA1422
