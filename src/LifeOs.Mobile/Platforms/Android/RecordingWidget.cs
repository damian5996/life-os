using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;

namespace LifeOs.Mobile.Platforms.Android;

[BroadcastReceiver(Label = "Life OS – Nagraj", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData("android.appwidget.provider", Resource = "@xml/recording_widget_info")]
public sealed class RecordingWidget : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null) return;
        foreach (var id in appWidgetIds)
        {
            var intent = new Intent(context, typeof(MainActivity))
                .SetAction(MainActivity.StartRecordingAction)
                .SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var pending = PendingIntent.GetActivity(context, id, intent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            var views = new RemoteViews(context.PackageName, Resource.Layout.recording_widget);
            views.SetOnClickPendingIntent(Resource.Id.widget_record, pending);
            appWidgetManager.UpdateAppWidget(id, views);
        }
    }
}
