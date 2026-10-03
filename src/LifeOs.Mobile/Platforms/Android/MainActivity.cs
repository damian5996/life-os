using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using LifeOs.Mobile.Views;

namespace LifeOs.Mobile;

[Activity(Name = "pl.lifeos.mobile.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
        ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    public const string StartRecordingAction = "pl.lifeos.mobile.StartRecording";
    private bool startRequested;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Saved activity restoration must not replay an already-consumed widget tap.
        if (savedInstanceState is null) ConsumeIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ConsumeIntent(intent);
    }

    private void ConsumeIntent(Intent? intent)
    {
        if (intent?.Action != StartRecordingAction) return;
        startRequested = true;
        intent.SetAction(Intent.ActionMain);
    }

    protected override void OnPostResume()
    {
        base.OnPostResume();
        if (!startRequested) return;
        startRequested = false;
        // Start only from a resumed, visible activity (Android microphone FGS restriction).
        _ = StartFromWidgetAsync();
    }

    private async Task StartFromWidgetAsync()
    {
        try
        {
            var page = IPlatformApplication.Current!.Services.GetRequiredService<MainPage>();
            await page.HandleStartRecordingIntentAsync();
        }
        catch
        {
            Android.Widget.Toast.MakeText(this, "Nie udało się rozpocząć nagrywania. Otwórz aplikację i spróbuj ponownie.",
                Android.Widget.ToastLength.Long)?.Show();
        }
    }
}
