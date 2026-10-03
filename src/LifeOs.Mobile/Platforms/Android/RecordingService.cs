using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace LifeOs.Mobile.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMicrophone)]
public sealed class RecordingService : Service
{
    private const string Channel = "lifeos-recording";
    private AudioRecorder Recorder => IPlatformApplication.Current!.Services.GetRequiredService<AudioRecorder>();
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            manager.CreateNotificationChannel(new NotificationChannel(Channel, "Nagrywanie", NotificationImportance.Low));
            var open = new Intent(this, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            var pending = PendingIntent.GetActivity(this, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var notification = new Notification.Builder(this, Channel)
                .SetContentTitle("Life OS — nagrywanie")
                .SetContentText("Dotknij, aby wrócić i zatrzymać nagrywanie.")
                .SetSmallIcon(Resource.Drawable.recording_notification)
                .SetContentIntent(pending).SetOngoing(true).Build();
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
                StartForeground(1, notification, ForegroundService.TypeMicrophone);
            else StartForeground(1, notification);
            Recorder.BeginInForeground();
        }
        catch (Exception error)
        {
            Recorder.FailedToStart(error);
            StopSelf();
        }
        // Never restart the microphone following process death without user action.
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        Recorder.ServiceDestroyed();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }
}
