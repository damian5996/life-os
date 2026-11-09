using LifeOs.Mobile.Models;
using LifeOs.Mobile.Services;

namespace LifeOs.Mobile.Tests;

public sealed class OfflineTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "lifeos-queue-test-" + Guid.NewGuid());
    private readonly Clock clock = new();
    private readonly Api api = new();
    private bool online;
    private CaptureSession Session(RecordingQueue queue) => new(new Recorder(), api, queue, () => online, a => a(), clock);

    [Fact]
    public async Task MultipleOfflineRecordingsSurviveRestartAndOnlySyncOnClickWithOriginalDates()
    {
        var queue = new RecordingQueue(folder);
        var session = Session(queue);
        var firstDate = clock.Now;
        await session.StartAsync(); await session.StopAndUploadAsync();
        clock.Now = clock.Now.AddDays(1);
        await session.StartAsync(); await session.StopAndUploadAsync();
        Assert.Equal(2, session.Pending.Count);
        Assert.Empty(api.Calls);
        online = true;
        clock.Now = clock.Now.AddDays(5);
        session = Session(new RecordingQueue(folder));
        Assert.Empty(api.Calls);
        await session.RetryAsync();
        Assert.Equal(new[] { firstDate, firstDate.AddDays(1) }, api.Calls.Select(x => x.Date));
        Assert.Empty(session.Pending);
        Assert.Empty(new RecordingQueue(folder).Pending);
        Assert.Empty(Directory.GetFiles(folder, "*.m4a"));
    }
    [Fact]
    public async Task FailedUploadRetainsFileAndSameIdAndDateAcrossRestartAndRetry()
    {
        var queue = new RecordingQueue(folder);
        var session = Session(queue);
        await session.StartAsync(); await session.StopAndUploadAsync();
        var item = Assert.Single(session.Pending);
        online = true; api.Fail = true;
        await session.RetryAsync();
        Assert.True(File.Exists(queue.AudioPath(item)));
        Assert.NotNull(Assert.Single(session.Pending).Error);
        session = Session(new RecordingQueue(folder));
        api.Fail = false; clock.Now = clock.Now.AddMonths(1);
        await session.RetryAsync();
        Assert.Equal(2, api.Calls.Count);
        Assert.All(api.Calls, x => { Assert.Equal(item.Id, x.Id); Assert.Equal(item.CapturedAt, x.Date); });
        Assert.Empty(session.Pending);
    }
    [Fact]
    public async Task OnlineNewRecordingDoesNotAutomaticallySendOldQueue()
    {
        var session = Session(new RecordingQueue(folder));
        await session.StartAsync(); await session.StopAndUploadAsync();
        var old = Assert.Single(session.Pending);
        online = true;
        await session.StartAsync(); await session.StopAndUploadAsync();
        Assert.Equal(old.Id, Assert.Single(session.Pending).Id);
        Assert.NotEqual(old.Id, Assert.Single(api.Calls).Id);
    }
    [Fact]
    public void InterruptedRecordingIsRecoveredWithOriginalTime()
    {
        var queue = new RecordingQueue(folder);
        var item = queue.Begin(clock.Now);
        File.WriteAllBytes(queue.AudioPath(item), [1, 2]);
        var recovered = Assert.Single(new RecordingQueue(folder).Pending);
        Assert.Equal(item.CapturedAt, recovered.CapturedAt);
        Assert.Equal("ready", recovered.State);
        Assert.Contains("przerwane", recovered.Error);
    }
    [Fact]
    public void LegacyFileIsRecoveredWithoutDeletion()
    {
        Directory.CreateDirectory(folder);
        var id = Guid.NewGuid();
        var path = Path.Combine(folder, id.ToString("N") + ".m4a");
        File.WriteAllBytes(path, [1]);
        var queue = new RecordingQueue(folder);
        Assert.Equal(id, Assert.Single(queue.Pending).Id);
        Assert.True(File.Exists(path));
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T12:00:00+01:00");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Recorder : IAudioRecorder
    {
        public bool IsRecording { get; private set; }
        public TimeSpan Elapsed => TimeSpan.Zero;
        public event Action<string>? Interrupted { add { } remove { } }
        public Task StartAsync(string path) { File.WriteAllBytes(path, [1, 2, 3]); IsRecording = true; return Task.CompletedTask; }
        public Task StopAsync() { IsRecording = false; return Task.CompletedTask; }
    }
    private sealed class Api : ILifeOsApiClient
    {
        public bool Fail;
        public List<(Guid Id, DateTimeOffset Date)> Calls = [];
        public Task<CaptureResponse> UploadAudioAsync(string path, Action uploaded, CancellationToken ct) => throw new NotSupportedException();
        public Task<CaptureResponse> UploadAudioAsync(string path, Guid id, DateTimeOffset date, Action uploaded, CancellationToken ct)
        {
            Calls.Add((id, date));
            if (Fail) throw new HttpRequestException();
            return Task.FromResult(new CaptureResponse(id, "COMPLETED"));
        }
    }
}
