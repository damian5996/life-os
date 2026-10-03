using System.Text.Json;
using LifeOs.Mobile.Services;
using LifeOs.Mobile.Views;
using LifeOs.Mobile.Platforms.Android;

namespace LifeOs.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<App>();
        using var stream = typeof(MauiProgram).Assembly.GetManifestResourceStream("LifeOs.Mobile.appsettings.json")!;
        var config = JsonSerializer.Deserialize(stream, MobileJsonContext.Default.ApiConfiguration)!;
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(11) });
        builder.Services.AddSingleton<ILifeOsApiClient, LifeOsApiClient>();
        builder.Services.AddSingleton<AudioRecorder>();
        builder.Services.AddSingleton<IAudioRecorder>(sp => sp.GetRequiredService<AudioRecorder>());
        builder.Services.AddSingleton<CaptureSession>();
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
