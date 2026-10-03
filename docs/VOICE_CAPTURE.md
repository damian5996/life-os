# Voice capture v0.1

## Architecture

The backend remains .NET 10 Minimal API + EF Core 10 / Npgsql / PostgreSQL.
`CaptureCreationService.CreateFromTextAsync` contains the existing raw-first save,
classification, title, summary, tags, metadata and final persistence behavior.
Both text and audio endpoints use it. No model/schema migration is needed.

`POST /api/captures/audio` receives one multipart `file`, validates it, calls
`IAudioTranscriptionService`, then creates a capture with `Source=VOICE` and
`RawText` exactly equal to the transcription. The response is the same 201 Capture
as text creation. `CapturedAt` defaults to server time.

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

The adapter uses JSON responses and a short Polish prompt preserving terms such as
.NET, Azure, backend and API. It does not translate or summarize the transcription.
Use a model supporting the `language`, `prompt` and `response_format=json` fields.
See the [OpenAI transcription API](https://developers.openai.com/api/reference/cli/resources/audio/subresources/transcriptions/methods/create).
No OpenAI key or database credentials are included in the Android project.

## Failure contract

- 400: missing/empty/wrong field, multiple files or malformed multipart.
- 413: file or request too large. A malformed/oversized multipart section can be 400.
- 415: unsupported extension/MIME or request content type.
- 502: transcription unavailable, timed out, malformed response or blank text. No capture created.
- 201 + `processingState=FAILED`: transcription saved; classification failed.
- 503: database failure. If the raw insert succeeded but enrichment persistence failed,
  the existing `rawCaptureSaved=true` and `captureId` fields are returned.

A network failure can occur after a server commit. Check the server before retrying;
v0.1 has no idempotency. Existing history shows only COMPLETED notes, so checking a
FAILED/PENDING note may require the database. Reverse proxies can impose smaller
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
Views/MainPage.cs           Polish recording screen and permission flow
Services/CaptureSession.cs  singleton workflow, one retained pending file
Services/IAudioRecorder.cs  recording boundary
Services/LifeOsApiClient.cs multipart HttpClient, typed Capture confirmation
Models/CaptureResponse.cs   minimal response fields
Platforms/Android/         activity, recorder, microphone foreground service, widget
```

The app records ordinary audio using native `MediaRecorder`: MPEG-4 container,
`.m4a`, AAC, mono, 44.1 kHz, 64 kbit/s (approximately 0.5 MB/minute). There is no
SpeechRecognizer, silence detector, or pause-based stop. Only Stop finalizes the
normal recording. OS/process failure can still interrupt it.

The foreground service starts from the visible activity after permission is granted,
then keeps recording while the page is recreated or the phone is briefly locked.
It is non-sticky: Android must not silently restart the microphone after killing the
process. A persistent notification returns to the app to stop recording.
See [Android microphone foreground services](https://developer.android.com/develop/background-work/services/fgs/service-types#microphone).

Manifest permissions: INTERNET, RECORD_AUDIO (requested at runtime), FOREGROUND_SERVICE,
FOREGROUND_SERVICE_MICROPHONE. No storage, contacts or location permissions.
The permission-denied screen explains how to enable microphone access and opens app
settings, including when Android no longer displays a permission prompt. No notification
permission prompt is needed to run the foreground service; notification visibility
can depend on Android settings.

Recordings live in app-private `Files/recordings` (MAUI AppDataDirectory), excluded
from Android backup. The file is deleted only after a valid 201 Capture confirmation,
including FAILED classification (the raw text is saved). Upload failures retain the
file and the pending path across app restarts. Manual retry warns about duplicates.
“Zachowaj plik i nagraj nowe” frees the pending UI slot while preserving the old file.
There is no recordings list or automatic retry. Uninstalling/clearing app data deletes
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

- Fresh install: grant, deny, deny permanently, return from microphone settings.
- Start normally, talk 1–3 minutes, pause 5 and 15 seconds, resume talking, Stop.
- Lock/unlock while recording; rotate, leave/reopen app, return from notification.
- Cold and warm widget taps start once; repeated taps leave current recording running.
- Stop shows Wysyłanie… → Transkrypcja i analiza… → ✓ Zapisano.
- Validate Polish/English terminology, VOICE, RawText and AI fields in PostgreSQL.
- Confirm successful upload removes local m4a; FAILED classification reports saved original.
- Disable network: recording remains; restart app, retry manually after checking server.
- Force-stop during recording: no microphone restart; retained file may be incomplete.
- Verify foreground-service behavior on the actual device/OEM and target Android version.

Debug builds allow private file inspection with `adb shell run-as pl.lifeos.mobile ls files/recordings`.
Do not print or share personal recordings in logs.

## v0.1 limits and next steps

The upload is synchronous; the app receives no server-side stage progress. The phase
changes after file bytes reach the HTTP transport, then waits for the final response.
No upload queue, offline DB, automatic cleanup of failed files, chunks, background
upload guarantee, auth, note UI, iOS or store publishing. Very long recordings can
exceed 25 MB. Android force-stop, calls, microphone contention and OEM battery rules
can interrupt recording; a foreground service cannot guarantee survival of process death.

Next sensible improvements after real use: idempotency for uncertain uploads, a small
manual recovery/export action for retained files, and device-specific lifecycle fixes.
Keep normal capture as tap → talk → stop → saved.

## Verification in this workspace

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
