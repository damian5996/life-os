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
        // A slightly diagonal vertical swipe must stay with the WebView. Otherwise
        // the tab pager sends touchcancel and cuts off the native scroll momentum.
        On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
            .SetToolbarPlacement(ToolbarPlacement.Bottom)
            .SetIsSwipePagingEnabled(false);
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
