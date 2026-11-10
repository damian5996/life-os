# Voice capture v0.1

## Architecture

The backend remains .NET 10 Minimal API + EF Core 10 / Npgsql / PostgreSQL.
`CaptureCreationService.CreateFromTextAsync` contains the existing raw-first save,
classification, title, summary, tags, metadata and final persistence behavior.
Both text and audio endpoints use it. No model/schema migration is needed.

`POST /api/captures/audio` receives one multipart `file`, validates it, calls
`IAudioTranscriptionService`, then creates a capture with `Source=VOICE` and
`RawText` exactly equal to the transcription. The response is the same 201 Capture
as text creation. New clients send `recordingId` (UUID) and `capturedAt` (ISO 8601
round-trip timestamp with offset) together. `CapturedAt` is the recording start time;
`CreatedAt` is server insertion time. Legacy uploads without both fields use server time.

Allowed extensions: m4a, mp4, mp3, wav, webm, ogg, flac (case insensitive).
Maximum audio size: **25,000,000 bytes**. The request has an additional 64 KiB
multipart allowance. Matching common MIME types are accepted; missing MIME or
`application/octet-stream` is accepted when the extension is allowed. Validation
is metadata based; the transcription provider validates the audio encoding.

No permanent server audio storage. ASP.NET may spool multipart input to its own
request-scoped temporary files; these are disposed on request completion, including
failure. Only the resulting text Capture remains in PostgreSQL. Audio is sent to
the configured transcription provider. Logs do not contain text, original filenames,
audio, provider response bodies, API keys or connection strings.

## Backend configuration

Reuse the existing `Llm` configuration section and `Llm:ApiKey` / `Llm:BaseUrl`.
The provider must expose `audio/transcriptions` in addition to `chat/completions`.
A text-only OpenAI-compatible provider is not necessarily a transcription provider.

| Key | Default | Azure App Service setting |
| --- | --- | --- |
| `Llm:TranscriptionModel` | `gpt-4o-mini-transcribe` | `Llm__TranscriptionModel` |
| `Llm:TranscriptionLanguage` | `pl` | `Llm__TranscriptionLanguage` |
| `Llm:TranscriptionTimeoutSeconds` | `180` | `Llm__TranscriptionTimeoutSeconds` |

The adapter uses JSON responses, Polish language and `chunking_strategy=auto` for
provider-side voice activity detection. It sends no prompt: the previous instruction
was echoed as invented speech on a silent recording. It does not translate or summarize
the transcription. The provider must support language, JSON responses and automatic VAD.
See the [OpenAI transcription API](https://developers.openai.com/api/reference/cli/resources/audio/subresources/transcriptions/methods/create).
No OpenAI key or database credentials are included in the Android project.

## Failure contract

- 400: missing/empty/wrong field, multiple files or malformed multipart.
- 413: file or request too large. A malformed/oversized multipart section can be 400.
- 415: unsupported extension/MIME or request content type.
- 422: no speech was recognized (blank transcription). No capture or AI classification.
- 502: transcription unavailable, timed out or malformed response. No capture created.
- 201 + `processingState=FAILED`: transcription saved; classification failed.
- 503: database failure. If the raw insert succeeded but enrichment persistence failed,
  the existing `rawCaptureSaved=true` and `captureId` fields are returned.

A network failure can occur after a server commit. The recording UUID is the capture
primary key: retries return the existing capture, including FAILED/PENDING or trashed
captures, without transcribing again or restoring it. Conflicting metadata returns 409.
The database primary key also prevents duplicates from concurrent requests. Legacy
uploads without a recording UUID do not have this guarantee. Reverse proxies can impose smaller
body limits or shorter response timeouts than this application.

## Manual audio request

Start the backend using the existing secret/database configuration. In PowerShell:

```powershell
dotnet run --project src/LifeOs.Api --no-launch-profile --urls http://localhost:5080
# In another terminal, use an actual recording:
curl.exe --fail-with-body -i http://localhost:5080/api/captures/audio -F 'file=@C:/recordings/notatka.m4a;type=audio/mp4'
```

Confirm HTTP 201, `source=VOICE`, accurate Polish `rawText`, and
`processingState=COMPLETED`. Inspect PostgreSQL to confirm the same capture ID and
text were saved. Try an empty file, an unsupported .3gp file and a file over 25 MB.

Backend-only commands do not require MAUI:

```powershell
dotnet restore LifeOs.Backend.slnx
dotnet test LifeOs.Backend.slnx
# Optional: disposable test database, never a production database.
$env:LIFEOS_TEST_POSTGRES = '<connection string to an empty disposable PostgreSQL database>'
dotnet test tests/LifeOs.Api.Tests
```

## Mobile project

`src/LifeOs.Mobile` targets `net10.0-android`, Android 8 / API 26 or newer.

```text
App.cs / MauiProgram.cs      one window, DI services and public URL configuration
Views/MainPage.cs           native tabs: shared notes UI and recording
Views/NotebookPage.cs       BlazorWebView hosting shared Razor routes
Views/RecordingPage.cs      Polish recording screen and permission flow
Services/CaptureSession.cs  singleton workflow and manual queue synchronization
Services/RecordingQueue.cs durable per-recording manifests with original timestamps
Services/IAudioRecorder.cs  recording boundary
Services/LifeOsApiClient.cs multipart HttpClient, typed Capture confirmation
Services/NativeExportDestination.cs app-private export file + Android share sheet
Models/CaptureResponse.cs   minimal response fields
Platforms/Android/         activity, recorder, microphone foreground service, widget
```

### Shared Web and Android interface (v0.2)

`src/LifeOs.Shared` is a Razor class library used by both the API host and Android.
It owns the Notes and Reviews pages, list/details components, layout, Polish labels,
CSS, API contracts and the text/history/review/export HTTP client. Existing
`LifeOs.Api.*` namespaces are retained to preserve the EF model identity and avoid
an unnecessary database migration. The library has no EF, database or LLM dependency.

Android runs these components locally in MAUI Blazor Hybrid; it does not load the
server's website or need a Blazor Server connection. The Web still uses Interactive
Server. Both load `_content/LifeOs.Shared/app.css` and call the same REST API.
The phone's API base URL comes from `appsettings.json`, never the WebView's local origin.
The shared client uses generated JSON metadata for trimmed Android Release builds.

The **Notatki** tab provides history, type filters, details, text entry, trash/restore,
reviews for a week/month/six months, and Markdown/JSON export. Native export downloads
the selected scope into a private cache directory and opens Android's share sheet;
the user chooses a destination. It needs no broad storage permission. Export files
remain in app cache so the receiving application can read them; the OS may evict them.
The **Nagraj** tab keeps the original native recorder, foreground service and retry flow.
Switch native tabs using the bottom buttons. Horizontal swipe paging is disabled so
slightly diagonal vertical gestures remain with the WebView and retain scroll momentum.
Widget launches select that tab before requesting recording. Returning from recording
refreshes the notes list; an open text draft or details view is preserved. There is
also an explicit refresh button on the shared notes page.

The phone needs a reachable backend with the trash, reviews and export endpoints from
this repository, and the `AddCaptureTrash` migration. Deploy the matching backend before
using these features against Azure; building an APK does not deploy the API. No additional
database migration is introduced by sharing the interface. Notes and reviews require
network access; there is still no offline notes cache.

The app records ordinary audio using native `MediaRecorder`: MPEG-4 container,
`.m4a`, AAC, mono, 44.1 kHz, 64 kbit/s (approximately 0.5 MB/minute). There is no
SpeechRecognizer, silence detector, or pause-based stop. Only Stop finalizes the
normal recording. OS/process failure can still interrupt it.

The foreground service starts from the visible activity after permission is granted,
then keeps recording while the page is recreated or the phone is briefly locked.
It is non-sticky: Android must not silently restart the microphone after killing the
process. A persistent notification returns to the app to stop recording.
See [Android microphone foreground services](https://developer.android.com/develop/background-work/services/fgs/service-types#microphone).

Manifest permissions: INTERNET, ACCESS_NETWORK_STATE, RECORD_AUDIO (requested at runtime), FOREGROUND_SERVICE,
FOREGROUND_SERVICE_MICROPHONE. No storage, contacts or location permissions.
The permission-denied screen explains how to enable microphone access and opens app
settings, including when Android no longer displays a permission prompt. No notification
permission prompt is needed to run the foreground service; notification visibility
can depend on Android settings.

Recordings live in app-private `Files/recordings` (MAUI AppDataDirectory), excluded
from Android backup. The file is deleted only after a valid 201 Capture confirmation,
including FAILED classification (the raw text is saved). Upload failures retain the
file and its UUID/timestamp across app restarts. Multiple recordings can wait in the
visible queue. Offline Stop saves locally; reconnecting does not send queued files.
“Synchronizuj” uploads them sequentially; failures stay visible and can be retried or
explicitly deleted. Online Stop uploads only the newly recorded file. Confirmation is
persisted atomically before deleting audio. Legacy retained files are recovered using
file creation time (their exact recording timestamp was not previously stored).
Uninstalling/clearing app data deletes
these files; process death may leave an unfinalized, unusable m4a container.

## Backend URL

Edit **`src/LifeOs.Mobile/appsettings.json`**, then rebuild/reinstall:

```json
{"ApiBaseUrl":"https://YOUR-LIFE-OS-API/"}
```

It contains only a public API URL, not backend settings or secrets. The current URL is
`https://life-os-eeg7cpfyf6ctb9ff.westeurope-01.azurewebsites.net/` for the deployed Life OS backend.
For local USB testing use `http://127.0.0.1:5080/` with reverse forwarding; for an emulator
use `http://10.0.2.2:5080/`. Debug permits
cleartext HTTP for local development; Release requires HTTPS and does not enable
cleartext traffic. There is deliberately no authentication in this iteration.

## Build and install on a physical phone

Install .NET 10, the `maui-android` workload, Android SDK and Microsoft JDK 21
(or Visual Studio's .NET MAUI workload). Official dependency setup:
[InstallAndroidDependencies](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies).

```powershell
dotnet workload install maui-android
dotnet build src/LifeOs.Mobile -f net10.0-android -c Debug
```

Enable Android developer options and USB debugging, connect a data-capable USB cable,
and accept the computer authorization prompt. With Android platform-tools on PATH:

```powershell
adb devices
# Optional local backend via USB: set mobile ApiBaseUrl to http://127.0.0.1:5080/ first.
adb reverse tcp:5080 tcp:5080
dotnet build src/LifeOs.Mobile -t:Install -f net10.0-android -c Debug
# Or install a built APK:
adb install -r src/LifeOs.Mobile/bin/Debug/net10.0-android/pl.lifeos.mobile-Signed.apk
```

Launch Life OS once. Record/stop normally and grant microphone permission when asked.
Long-press the launcher home screen → Widgets → **Life OS – Nagraj**, and add it.

## Widget launch

`RecordingWidget` is a native AppWidgetProvider with a one-button RemoteViews layout.
An immutable activity PendingIntent targets MainActivity explicitly, with action
`pl.lifeos.mobile.StartRecording`. MainActivity consumes the action once, waits for
OnPostResume and the page to load, then invokes `HandleStartRecordingIntentAsync`.

A cold or warm launch starts recording immediately after any required permission
prompt. Repeated widget taps never stop an active recording or create another recorder.
If an upload is active or a failed file needs attention, the app shows that state and
does not overwrite it. Notification taps only reopen the app; they do not start recording.
No scheduled widget updates, history, statistics or background receiver microphone starts.
An optional launcher shortcut is not included in v0.1.

## Device acceptance checklist (requires a real microphone/phone)

- Open Notatki and switch filters; compare the same records and appearance with Web.
- Open details, create a text note, move it to trash and restore it.
- Generate week/month/six-month reviews; changing the period clears the previous result.
- Share Markdown and JSON from active notes and trash; inspect the received file.
- Switch to Nagraj, save audio, return to Notatki and confirm the refreshed list.
- Leave a text draft, switch tabs, then return and confirm the draft is retained.
- Fresh install: grant, deny, deny permanently, return from microphone settings.
- Start normally, talk 1–3 minutes, pause 5 and 15 seconds, resume talking, Stop.
- Lock/unlock while recording; rotate, leave/reopen app, return from notification.
- Cold and warm widget taps start once; repeated taps leave current recording running.
- Stop shows Wysyłanie… → Transkrypcja i analiza… → ✓ Zapisano.
- Validate Polish/English terminology, VOICE, RawText and AI fields in PostgreSQL.
- Confirm successful upload removes local m4a; FAILED classification reports saved original.
- Disable network: record multiple notes, restart the app, reconnect and press Synchronizuj.
  Confirm no automatic upload on reconnect, original dates and no duplicate after retry.
- Force-stop during recording: no microphone restart; retained file may be incomplete.
- Verify foreground-service behavior on the actual device/OEM and target Android version.

Debug builds allow private file inspection with `adb shell run-as pl.lifeos.mobile ls files/recordings`.
Do not print or share personal recordings in logs.

## v0.1 limits and next steps

The upload is synchronous; the app receives no server-side stage progress. The phase
changes after file bytes reach the HTTP transport, then waits for the final response.
No offline notes cache, automatic cleanup of failed files, chunks, background
upload guarantee, auth, iOS or store publishing. Very long recordings can
exceed 25 MB. Android force-stop, calls, microphone contention and OEM battery rules
can interrupt recording; a foreground service cannot guarantee survival of process death.

Next sensible improvements after real use: manual recovery/export of retained audio
and device-specific lifecycle fixes.
Keep normal capture as tap → talk → stop → saved.

### Priority defect reported 2026-11-09: silence becomes a fabricated note

The user recorded about one second of silence. The saved transcription reproduced the
entire transcription prompt from `OpenAiAudioTranscriptionService`, and classification
then marked that invented content COMPLETED. Current validation only rejects an empty
file or blank transcription; neither establishes that the audio contains speech.
This defect was confirmed by comparing the screenshot with the source prompt.
The existing note should not be treated as evidence of anything the user said.

The correction removes the instruction-style prompt, enables provider-side VAD, and
rejects blank transcription with 422 and a clear no-speech message before saving a note.
No minimum recording length or automatic stop during pauses is imposed. Uploaded audio
remains on the phone after rejection, following the existing recovery policy.

Verified on the deployed API on 2026-11-09: 1 s and 5 s digital silence and 3 s low-level
white noise each returned 422 without a capture. A short Polish phrase, the same phrase
at 3% amplitude, and speech separated by a 3 s pause returned 201/COMPLETED with the
expected words. These are synthetic regression samples, not a guarantee for every
microphone, background sound or speech level. Three generated test captures were moved
to trash; existing notes were not modified.

Android 0.2.2 recognizes 422 separately from network failures and displays the no-speech
message with instructions to keep the file and record again. It does not discard audio.
The backend/shared tests passed (107; one optional database test skipped), as did all
8 mobile upload tests, including retained-file/no-speech feedback.

The classification and review prompts now use natural Polish word order and short,
lowercase pronouns only when needed, instead of demonstrating sentences starting with
„Tobie”. A fresh six-month review contained 43 items and no „Tobie” sentence starts.
Existing capture summaries are not rewritten; reviews are regenerated on request.

## Verification in this workspace

### Android scroll gestures — 2026-11-09

- On Samsung SM-S938B, a mostly vertical 110 ms swipe with 150 px of horizontal drift
  triggered `touchcancel` in the original TabbedPage. The page moved only 71 CSS px,
  with no momentum after release. A straight vertical swipe already worked correctly.
- Disabled Android swipe paging; navigation through the bottom tab buttons is retained.
  The identical diagonal gesture now ends with `touchend`, moves 1288 CSS px and continues
  for about 1091 ms after release. Straight up/down gestures retain approximately 1070 ms
  of momentum. The maximum sampled animation frame interval was 17 ms in these runs.
- Measurements used the Debug WebView tools on the physical phone, without reading note
  content. WebView developer tools are registered only in Debug builds.
- Debug and Release builds passed without warnings/errors. Release 0.2.1 (version code 3)
  was installed with existing app data retained and cold-launched without runtime errors.
  Both bottom tab buttons were verified on the device. The diagnostic ADB forwarding was
  removed, and the installed Release package is not debuggable.

### Deployed shared interface — 2026-11-08

- 103 API/shared UI tests and 7 mobile upload tests passed. The PostgreSQL integration
  test also passed against a separate disposable database: 111 passing tests in total.
- Android Release built with zero warnings/errors. Version 0.2 (version code 2) was
  installed with `adb install -r` on Samsung SM-S938B, retaining existing app data.
  Notes, Reviews and Recording screens opened successfully; no Android runtime errors
  were observed. No USB reverse forwarding is configured; the app uses Azure HTTPS.
- Application commit `30bb24f` was published through GitHub Actions. Deployment follow-up
  `24b6593` adds an explicit restart and public readiness checks for the current API,
  database-backed history and shared stylesheet. Verified deployment:
  https://github.com/damian5996/life-os/actions/runs/42484088152
- The existing configured database already had `AddCaptureTrash`; EF found no pending
  model changes. The published package also passed local Production-mode checks.
- Live Azure checks passed for history, JSON/Markdown export, shared web assets, text
  creation with real AI classification (`COMPLETED`), trash, restore and weekly review.
  Empty multipart audio returned 400. The single deployment test note was left in trash;
  existing notes were not modified.
- Azure briefly served the old process against replaced assemblies (`BadImageFormatException`)
  and took several minutes to restart. The final public readiness check passed. Approved
  manual runtime diagnostics confirmed .NET 10, the expected startup command and successful
  startup; configuration values and complete application logs were not printed.
- The user confirmed a fresh microphone recording was saved and appeared in Notes.
  The broader device acceptance checklist above was not repeated in full.

### Shared interface — 2026-10-20

- 103 API/shared UI tests passed; the optional PostgreSQL integration test was skipped.
- 7 native audio upload tests passed.
- Android Debug and Release APK builds completed with no warnings/errors. The Release
  APK contains the shared stylesheet, host HTML and Blazor WebView script.
- Backend publish includes `LifeOs.Shared.dll` and its static assets. EF reports no
  model changes after the existing migrations.
- No phone/emulator was connected for this change; the device checklist above remains
  to be run for the new UI, tab lifecycle, microphone and native file sharing.
- A read-only check of the configured Azure server's Swagger schema showed the older
  API without export, trash/restore or `/api/review`. The matching backend must be
  deployed; it was not deployed as part of this local implementation.
- Installable APK: `src/LifeOs.Mobile/bin/Release/net10.0-android/pl.lifeos.mobile-Signed.apk`.
  Locally prepared backend package: `.tools/deploy/mobile-parity` (both build outputs are ignored).

### Previous recording-only verification

- Backend: 75 tests passed, including an actual disposable PostgreSQL database round-trip
  for the audio route; transcription and classification providers were mocked.
- Mobile HTTP client: 7 tests passed (multipart field/MIME, success/FAILED confirmations,
  invalid responses, retained input file, empty input).
- Android Debug and Release builds passed with zero warnings/errors. Debug APK embeds
  its assemblies and is ready for direct installation. Runtime/device behavior is not
  established by compilation.
- On 2026-10-03, installed and launched the Debug APK on Samsung SM-S938B / Android 16.
  USB reverse forwarding connects the phone to the local API on port 5080.
- A real microphone recording completed the live transcription/classification pipeline.
  The user confirmed `✓ Zapisano`; PostgreSQL confirmed VOICE/COMPLETED, a nonempty
  transcription, title and tags. The phone's recordings directory was empty afterward.
- These device-test notes use the separate `lifeos-device-test` PostgreSQL container
  (database `lifeos_device_test`, localhost port 55436), not the existing capture database.
  The container is retained so test notes are not discarded at the end of the session.
- The user confirmed the device checks and transcription quality on 2026-10-03.
  Widget launch works; a dynamic Record/Stop widget is a possible later improvement.
- On 2026-10-03, backend commit `41f90c3` was deployed successfully through GitHub
  Actions to the existing Azure App Service. The HTTPS OpenAPI document includes the
  audio route, empty multipart input returns 400, and database-backed history returns 200.
  The deployed backend uses the existing App Service configuration, not the local
  `lifeos-device-test` database override. Android configuration now points at its HTTPS URL.
- The HTTPS-configured Release APK was installed on the connected Samsung phone.
  The USB reverse forwarding was removed. A final user recording without the cable
  remains to verify the deployed capture workflow independently of the local computer.

This machine has a workspace-local SDK/toolchain under the ignored `.tools` directory.
To build with it without changing the system-wide .NET installation:

```powershell
& ./.tools/dotnet/dotnet.exe build src/LifeOs.Mobile -c Debug -f net10.0-android -p:AndroidSdkDirectory=C:/repos/my-apps/LifeOS/.tools/android-sdk -p:JavaSdkDirectory=C:/repos/my-apps/LifeOS/.tools/jdk
& ./.tools/android-sdk/platform-tools/adb.exe devices
```

The APK supports ARM64 phones and x64 emulators; it embeds assemblies for direct
`adb install` use. Older 32-bit ARM devices are not included in this MVP build.

### Offline queue and custom review dates — verified 2026-11-09

- Custom review dates include both selected Polish calendar days, with Europe/Warsaw
  daylight-saving transitions covered by tests.
- All 119 backend/shared UI tests passed with a disposable PostgreSQL database,
  including concurrent inserts with the same recording UUID. All 14 mobile tests passed.
- Android Release 0.3.0 (version code 5) built with zero warnings/errors and was installed
  on Samsung SM-S938B. The network-state permission and recording screen were verified.
- Backend commit `dd53ba0` deployed successfully in GitHub Actions run `42647326630`.
  A synthetic speech upload to Azure retained its original recording date; retry returned
  the same UUID, and a custom review of that Polish calendar day included the note.
  Synthetic test notes were moved to trash. Timestamp assertions account for PostgreSQL's
  microsecond precision.
- The user confirmed that two offline recordings survived reopening the app, then
  synchronized successfully after reconnecting and pressing Synchronizuj. Both appeared
  in notes with their recording dates and disappeared from the queue.
- A third retained recording from the old version was recovered. Legacy files use file
  creation time; their prior server-save status cannot be inferred, so users should check
  for an existing note before sending them again. New recordings have stable UUIDs.
