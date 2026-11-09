using LifeOs.Mobile.Services;

namespace LifeOs.Mobile.Views;

public sealed class RecordingPage : ContentPage
{
    private readonly CaptureSession session;
    private readonly Button record = new() { Text = "🎙️ Nagraj", FontSize = 28, HeightRequest = 180, WidthRequest = 260,
        CornerRadius = 70, BackgroundColor = Color.FromArgb("173C35"), TextColor = Colors.White };
    private readonly Label duration = new() { Text = "00:00", FontSize = 40, HorizontalTextAlignment = TextAlignment.Center, TextColor = Colors.Black };
    private readonly Label status = new() { HorizontalTextAlignment = TextAlignment.Center, FontSize = 17, TextColor = Colors.Black };
    private readonly Button settings = new() { Text = "Otwórz ustawienia mikrofonu", IsVisible = false };
    private readonly Button sync = new() { Text = "Synchronizuj" };
    private readonly Label connection = new() { TextColor = Color.FromArgb("65756D") };
    private readonly VerticalStackLayout pending = new() { Spacing = 16 };
    private string? renderedQueue;
    private readonly IDispatcherTimer timer;
    private readonly TaskCompletionSource loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool requestingPermission;

    public RecordingPage(CaptureSession session)
    {
        this.session = session;
        Title = "Nagraj";
        SafeAreaEdges = SafeAreaEdges.All;
        BackgroundColor = Color.FromArgb("F6F7F2");
        Content = new ScrollView { Content = new VerticalStackLayout
        {
            Padding = new Thickness(28, 48), Spacing = 28, VerticalOptions = LayoutOptions.Center,
            Children = { new Label { Text = "ZATRZYMAJ MYŚL", FontSize = 12, TextColor = Color.FromArgb("285E49"), HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "Notatka głosowa", FontSize = 32, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("173C35") },
                new Label { Text = "Powiedz, co chcesz zapamiętać. Life OS uporządkuje Twoją notatkę.",
                    TextColor = Color.FromArgb("65756D"), HorizontalTextAlignment = TextAlignment.Center },
                record, duration, status, settings, connection, sync, pending }
        }};
        record.HorizontalOptions = LayoutOptions.Center;
        record.Clicked += async (_, _) =>
        {
            if (session.IsRecording) await session.StopAndUploadAsync();
            else await StartRecordingAsync();
        };
        settings.Clicked += (_, _) => AppInfo.Current.ShowSettingsUI();
        sync.Clicked += async (_, _) => await session.RetryAsync();
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
        if (!session.IsRecording && !session.Busy) await StartRecordingAsync();
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
        record.Text = session.IsRecording ? "⏹ Zatrzymaj" : "🎙️ Nagraj";
        record.IsEnabled = !session.Busy && !requestingPermission;
        var items = session.Pending;
        sync.IsVisible = items.Count > 0;
        sync.Text = $"Synchronizuj ({items.Count})";
        sync.IsEnabled = session.CanRetry && session.IsOnline;
        connection.Text = session.IsOnline ? "Internet dostępny. Zaległe nagrania wyślesz przyciskiem „Synchronizuj”." :
            "Tryb offline — nagrania pozostaną na telefonie do synchronizacji.";
        var fingerprint = string.Join("|", items.Select(x => $"{x.Id}:{x.Error}")) + session.Busy + session.IsRecording;
        if (fingerprint != renderedQueue)
        {
            renderedQueue = fingerprint;
            pending.Children.Clear();
            foreach (var item in items)
            {
                pending.Children.Add(new Label { Text = LifeOs.Api.Ui.PolishLabels.Date(item.CapturedAt) + " · czeka na wysłanie",
                    FontAttributes = FontAttributes.Bold, TextColor = Colors.Black });
                if (item.Error is not null) pending.Children.Add(new Label { Text = item.Error, TextColor = Color.FromArgb("8B4513") });
                var remove = new Button { Text = "Usuń nagranie z telefonu", IsEnabled = !session.Busy && !session.IsRecording };
                remove.Clicked += async (_, _) =>
                {
                    if (await DisplayAlertAsync("Usunąć nagranie?", "Lokalny plik zostanie trwale usunięty. Tej operacji nie można cofnąć.", "Usuń", "Anuluj"))
                        session.Remove(item);
                };
                pending.Children.Add(remove);
            }
        }
        duration.Text = $"{(int)session.Elapsed.TotalMinutes:00}:{session.Elapsed.Seconds:00}";
        if (!settings.IsVisible && !requestingPermission) status.Text = session.Status;
    }
}
