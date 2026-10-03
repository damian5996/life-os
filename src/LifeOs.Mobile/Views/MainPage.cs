using LifeOs.Mobile.Services;

namespace LifeOs.Mobile.Views;

public sealed class MainPage : ContentPage
{
    private readonly CaptureSession session;
    private readonly Button record = new() { Text = "🎙️ Nagraj", FontSize = 28, HeightRequest = 180, WidthRequest = 260,
        CornerRadius = 70, BackgroundColor = Color.FromArgb("173C35"), TextColor = Colors.White };
    private readonly Label duration = new() { Text = "00:00", FontSize = 40, HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.Black };
    private readonly Label status = new() { HorizontalTextAlignment = TextAlignment.Center, FontSize = 17, TextColor = Colors.Black };
    private readonly Button settings = new() { Text = "Otwórz ustawienia mikrofonu", IsVisible = false };
    private readonly Button fresh = new() { Text = "Zachowaj plik i nagraj nowe", IsVisible = false };
    private readonly IDispatcherTimer timer;
    private readonly TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool requestingPermission;

    public MainPage(CaptureSession session)
    {
        this.session = session;
        Title = "Life OS";
        BackgroundColor = Color.FromArgb("F4F5EF");
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(28, 48), Spacing = 28, VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = "Life OS", FontSize = 36, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("173C35") },
                record, duration, status, settings, fresh }
        }};
        record.HorizontalOptions = LayoutOptions.Center;
        record.Clicked += async (_, _) =>
        {
            if (session.IsRecording) await session.StopAndUploadAsync();
            else if (session.CanRetry)
            {
                if (await DisplayAlertAsync("Ponowić wysyłanie?", "Sprawdź najpierw, czy notatka jest już na serwerze. Ponowienie może utworzyć duplikat.", "Wyślij", "Anuluj"))
                    await session.RetryAsync();
            }
            else await StartRecordingAsync();
        };
        settings.Clicked += (_, _) => AppInfo.Current.ShowSettingsUI();
        fresh.Clicked += async (_, _) => { session.KeepFileAndStartFresh(); await StartRecordingAsync(); };
        session.Changed += Refresh;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => loaded.TrySetResult();
        Refresh();
    }

    public async Task HandleStartRecordingIntentAsync()
    {
        await loaded.Task;
        // A widget never stops a recording, retries an upload, or overwrites a retained file.
        if (!session.IsRecording && !session.Busy && !session.CanRetry) await StartRecordingAsync();
        Refresh();
    }

    private async Task StartRecordingAsync()
    {
        if (requestingPermission || session.Busy || session.IsRecording) return;
        requestingPermission = true;
        record.IsEnabled = false;
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.Microphone>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.Microphone>();
            if (permission != PermissionStatus.Granted)
            {
                status.Text = "Brak dostępu do mikrofonu. Zezwól na nagrywanie, aby zapisać notatkę. " +
                    "Jeśli system nie pyta ponownie, włącz mikrofon w ustawieniach aplikacji.";
                settings.IsVisible = true;
                return;
            }
            settings.IsVisible = false;
            await session.StartAsync();
        }
        catch { status.Text = "Nie udało się uzyskać dostępu do mikrofonu. Spróbuj ponownie."; }
        finally { requestingPermission = false; record.IsEnabled = !session.Busy; }
    }

    protected override void OnAppearing() { base.OnAppearing(); timer.Start(); Refresh(); }
    protected override void OnDisappearing() { timer.Stop(); base.OnDisappearing(); }
    private void Refresh()
    {
        record.Text = session.IsRecording ? "⏹ Zatrzymaj" : session.CanRetry ? "Ponów wysyłanie" : "🎙️ Nagraj";
        record.IsEnabled = !session.Busy && !requestingPermission;
        fresh.IsVisible = session.CanRetry;
        duration.Text = $"{(int)session.Elapsed.TotalMinutes:00}:{session.Elapsed.Seconds:00}";
        if (!settings.IsVisible && !requestingPermission) status.Text = session.Status;
    }
}
