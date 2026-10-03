using System.Diagnostics;
using Android.Content;
using Android.Media;
using LifeOs.Mobile.Services;

namespace LifeOs.Mobile.Platforms.Android;

// DI singleton; the foreground service owns its recording lifetime, not the page.
public sealed class AudioRecorder : IAudioRecorder
{
    private MediaRecorder? recorder;
    private TaskCompletionSource? starting;
    private readonly Stopwatch elapsed = new();
    public bool IsRecording { get; private set; }
    public TimeSpan Elapsed => elapsed.Elapsed;
    public event Action<string>? Interrupted;
    private string? path;

    public async Task StartAsync(string outputPath)
    {
        if (IsRecording || starting is not null) return;
        path = outputPath;
        starting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var context = global::Android.App.Application.Context;
            context.StartForegroundService(new Intent(context, typeof(RecordingService)));
            await starting.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch
        {
            global::Android.App.Application.Context.StopService(new Intent(global::Android.App.Application.Context, typeof(RecordingService)));
            Release();
            throw;
        }
        finally { starting = null; }
    }

    internal void BeginInForeground()
    {
        if (IsRecording) return;
        if (starting is null || path is null) throw new InvalidOperationException("Brak żądania nagrania.");
        try
        {
            recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new MediaRecorder(global::Android.App.Application.Context) : new MediaRecorder();
            recorder.SetAudioSource(AudioSource.Mic);
            recorder.SetOutputFormat(OutputFormat.Mpeg4);
            recorder.SetAudioEncoder(AudioEncoder.Aac);
            recorder.SetAudioChannels(1);
            recorder.SetAudioSamplingRate(44100);
            recorder.SetAudioEncodingBitRate(64000);
            recorder.SetOutputFile(path);
            recorder.Error += OnRecorderError;
            recorder.Prepare();
            recorder.Start();
            IsRecording = true;
            elapsed.Restart();
            starting.TrySetResult();
        }
        catch (Exception error) { Release(); starting.TrySetException(error); throw; }
    }

    internal void FailedToStart(Exception error) => starting?.TrySetException(error);

    public Task StopAsync()
    {
        try
        {
            if (!IsRecording || recorder is null) throw new InvalidOperationException("Nagrywanie zostało przerwane.");
            recorder.Stop(); // Finalizes the MPEG-4 container; upload only after this succeeds.
            return Task.CompletedTask;
        }
        finally
        {
            Release();
            global::Android.App.Application.Context.StopService(new Intent(global::Android.App.Application.Context, typeof(RecordingService)));
        }
    }

    private void OnRecorderError(object? sender, MediaRecorder.ErrorEventArgs e)
    {
        ServiceDestroyed();
        global::Android.App.Application.Context.StopService(new Intent(global::Android.App.Application.Context, typeof(RecordingService)));
    }

    internal void ServiceDestroyed()
    {
        if (!IsRecording) return;
        try { recorder?.Stop(); } catch { /* Retain even an incomplete file for recovery. */ }
        Release();
        Interrupted?.Invoke("Nagrywanie zostało przerwane przez system. Plik pozostał na telefonie i może być niepełny.");
    }

    private void Release()
    {
        IsRecording = false;
        elapsed.Stop();
        if (recorder is null) return;
        recorder.Error -= OnRecorderError;
        try { recorder.Release(); }
        finally { recorder.Dispose(); recorder = null; }
    }
}
