using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace ErganiManager.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        => base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .LogToTrace();
}
