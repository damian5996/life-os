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
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif
        using var stream = typeof(MauiProgram).Assembly.GetManifestResourceStream("LifeOs.Mobile.appsettings.json")!;
        var config = JsonSerializer.Deserialize(stream, MobileJsonContext.Default.ApiConfiguration)!;
        builder.Services.AddSingleton(config);
        if (!Uri.TryCreate(config.ApiBaseUrl, UriKind.Absolute, out var apiBaseUrl) || apiBaseUrl.Scheme is not ("https" or "http"))
            throw new InvalidOperationException("Nieprawidłowy adres API w konfiguracji aplikacji.");
#if !DEBUG
        if (apiBaseUrl.Scheme != "https") throw new InvalidOperationException("Wersja Release wymaga HTTPS.");
#endif
        builder.Services.AddSingleton(new LifeOs.Api.Ui.LifeOsClientOptions(apiBaseUrl));
        builder.Services.AddHttpClient("LifeOsUi", http => http.Timeout = TimeSpan.FromMinutes(6));
        builder.Services.AddScoped<LifeOs.Api.Ui.LifeOsApiClient>();
        builder.Services.AddScoped<LifeOs.Api.Ui.CaptureChanges>();
        builder.Services.AddSingleton<LifeOs.Api.Ui.IExportDestination, NativeExportDestination>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(11) });
        builder.Services.AddSingleton<ILifeOsApiClient, LifeOsApiClient>();
        builder.Services.AddSingleton<AudioRecorder>();
        builder.Services.AddSingleton<IAudioRecorder>(sp => sp.GetRequiredService<AudioRecorder>());
        builder.Services.AddSingleton(_ => new RecordingQueue(Path.Combine(FileSystem.AppDataDirectory, "recordings")));
        builder.Services.AddSingleton(sp => new CaptureSession(sp.GetRequiredService<IAudioRecorder>(),
            sp.GetRequiredService<ILifeOsApiClient>(), sp.GetRequiredService<RecordingQueue>(),
            () => Connectivity.Current.NetworkAccess == NetworkAccess.Internet,
            action => MainThread.BeginInvokeOnMainThread(action), TimeProvider.System));
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<NotebookPage>();
        builder.Services.AddSingleton<RecordingPage>();
        return builder.Build();
    }
}
