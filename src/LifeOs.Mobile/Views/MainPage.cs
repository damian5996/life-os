using LifeOs.Mobile.Services;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace LifeOs.Mobile.Views;

public sealed class MainPage : Microsoft.Maui.Controls.TabbedPage
{
    private readonly RecordingPage recording;
    private readonly CaptureSession session;

    public MainPage(NotebookPage notebook, RecordingPage recording, CaptureSession session)
    {
        this.recording = recording;
        this.session = session;
        Title = "Life OS";
        BarBackgroundColor = Color.FromArgb("F6F7F2");
        SelectedTabColor = Color.FromArgb("285E49");
        UnselectedTabColor = Color.FromArgb("65756D");
        Children.Add(notebook);
        Children.Add(recording);
        On<Microsoft.Maui.Controls.PlatformConfiguration.Android>().SetToolbarPlacement(ToolbarPlacement.Bottom);
        // Opening the notification or a retained recording should surface its controls.
        if (session.IsRecording || session.CanRetry || session.Busy) CurrentPage = recording;
    }

    public void ShowRecordingIfActive()
    {
        if (session.IsRecording || session.Busy) CurrentPage = recording;
    }

    public async Task HandleStartRecordingIntentAsync()
    {
        CurrentPage = recording;
        await recording.HandleStartRecordingIntentAsync();
    }
}
