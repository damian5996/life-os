# Life OS v0.1

Mały eksperyment: **mam myśl → szybko ją zapisuję → Life OS pamięta i porządkuje**.
ASP.NET Core Minimal API / .NET 10, EF Core, PostgreSQL i API LLM zgodne z OpenAI Chat Completions.
Minimalny interfejs Blazor po polsku. Bez kont użytkowników, transkrypcji audio, harmonogramu ani dodatkowych integracji.

## Uruchomienie (PowerShell)

Wymagania: SDK .NET 10, PostgreSQL (np. Docker Desktop) i model/API obsługujące tekstowy
Chat Completions oraz structured outputs lub JSON mode. Polecenia wykonuj w katalogu rozwiązania.

```powershell
dotnet restore
dotnet tool restore

# Wybierz własne hasło; nie zapisuj sekretów w repozytorium.
$env:POSTGRES_PASSWORD = '<lokalne-hasło>'
docker compose up -d --wait
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5435;Database=lifeos;Username=lifeos;Password=$env:POSTGRES_PASSWORD"
$env:Llm__ApiKey = '<klucz-api>'
$env:Llm__Model = '<nazwa-modelu-u-dostawcy>'
$env:Llm__BaseUrl = 'https://api.openai.com/v1/'

dotnet ef database update --project src/LifeOs.Api
dotnet run --project src/LifeOs.Api --no-launch-profile --urls http://localhost:5080
```

Interfejs: [http://localhost:5080/](http://localhost:5080/).
Swagger: [http://localhost:5080/swagger](http://localhost:5080/swagger).
OpenAPI: [http://localhost:5080/swagger/v1/swagger.json](http://localhost:5080/swagger/v1/swagger.json).
Migracja początkowa jest w repozytorium; aplikacja nie migruje bazy automatycznie.

`docker compose stop` zatrzymuje PostgreSQL, zachowując dane w wolumenie `lifeos-data`.
Port hosta to **5435**, port kontenera to 5432. Możesz zamiast Dockera użyć istniejącej bazy
PostgreSQL lub Supabase i zmienić connection string (w tym SSL zgodnie z wymaganiami hosta).
Plik `.env` może dostarczyć hasło do Compose, ale aplikacja .NET **nie wczytuje go automatycznie**.

## Konfiguracja

### Supabase PostgreSQL

Aplikacja używa istniejącego `LifeOsDbContext` i `UseNpgsql`: .NET 10, EF Core 10.0.9
oraz `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0. Nie wymaga klienta Supabase ani
dodatkowych pakietów konfiguracji JSON.

Parametry połączenia: host `aws-1-eu-west-1.pooler.supabase.com`, port `5432`, baza
`postgres`, użytkownik `postgres.epysgbumwglrgtbwczqr`, `SSL Mode=Require`.
Hasło i pełny connection string przechowuj poza repozytorium.

Lokalnie zapisz pełny connection string w .NET User Secrets pod kluczem
`ConnectionStrings:DefaultConnection` (projekt ma już `UserSecretsId`). Poniższy kod
pyta o connection string bez wyświetlania go i przekazuje go do CLI przez stdin:

```powershell
$connectionInput = Read-Host 'Pełny connection string Supabase' -AsSecureString
$connectionValue = [System.Net.NetworkCredential]::new('', $connectionInput).Password
@{ ConnectionStrings = @{ DefaultConnection = $connectionValue } } | ConvertTo-Json -Compress | dotnet user-secrets set --project src/LifeOs.Api
Remove-Variable connectionValue, connectionInput
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/LifeOs.Api
dotnet ef database update --project src/LifeOs.Api
dotnet run --project src/LifeOs.Api --no-launch-profile --urls http://localhost:5080
```

User Secrets są wczytywane w środowisku Development. Zmienna środowiskowa
`ConnectionStrings__DefaultConnection` ma pierwszeństwo; usuń jej lokalną wartość,
jeśli wcześniej wskazywała na Docker. W Azure App Service ustaw tę samą zmienną
w konfiguracji aplikacji, z pełnym connection stringiem i hasłem. Nie zapisuj jej
w `appsettings.json`, profilu publikowania ani innym pliku repozytorium.

Migracja `InitialCreate` tworzy tabelę `Captures` i indeks `CapturedAt`; używa typów
PostgreSQL (`uuid`, `timestamp with time zone`, `text[]`, `jsonb`). Aplikacja nie
uruchamia migracji automatycznie. `dotnet ef database update` stosuje brakujące
migracje i wymaga dostępu do bazy oraz uprawnień do tworzenia tabel.

### Zmienne środowiskowe

| Zmienna środowiskowa | Znaczenie |
| --- | --- |
| `ConnectionStrings__DefaultConnection` | Wymagany connection string PostgreSQL; lokalnie także User Secrets `ConnectionStrings:DefaultConnection` |
| `Llm__ApiKey` | Klucz dostawcy; może być pusty dla lokalnego API bez klucza |
| `Llm__Model` | Nazwa modelu/deploymentu u dostawcy, bez domyślnego wyboru |
| `Llm__BaseUrl` | Baza API, domyślnie `https://api.openai.com/v1/`; dodawane jest `chat/completions` |
| `Llm__UseJsonSchema` | Domyślnie `true`; ustaw `false` dla API obsługujących tylko JSON mode |
| `Llm__TimeoutSeconds` | Domyślnie 60, zakres 1–300 |

Brak modelu lub niedostępny dostawca nie blokuje zapisu oryginału: notatka otrzymuje `FAILED`.
Provider musi przyjmować powyższy endpoint, Bearer auth (jeśli podano klucz) i wybrany format JSON.
Nie ma automatycznego przełączania formatu ani ponawiania żądań. W JSON mode odpowiedź nadal
jest deserializowana i walidowana jako silnie typowany obiekt. Nie parsujemy prozy ani bloków Markdown.

Schemat generujemy z rekordów C#, wszystkie pola są wymagane, a nieznane dane oznaczane `null`.
To odpowiada regułom [OpenAI Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs).
Połączenie z rzeczywistym dostawcą wymaga Twojej konfiguracji; testy nie zużywają tokenów LLM.

## Architektura i struktura

```text
LifeOs.slnx
src/LifeOs.Api/
  Program.cs                  konfiguracja DI, Swagger, prosta obsługa błędów
  Endpoints/LifeOsEndpoints.cs trzy operacje API
  Data/LifeOsDbContext.cs      mapowanie PostgreSQL
  Data/Migrations/            początkowa migracja i snapshot
  Models/                     Capture, enumy i silnie typowane odpowiedzi
  Llm/                        ILifeOsLlmService, klient HTTP, JSON/schema
  Prompts/                    polskie instrukcje klasyfikacji i przeglądu
  Components/                 strony i komponenty Blazor
  Ui/                         klient HTTP istniejącego API i polskie etykiety
  wwwroot/app.css             responsywne style
tests/LifeOs.Api.Tests/        testy API, kontraktu LLM i PostgreSQL
compose.yaml                  wyłącznie lokalny PostgreSQL
```

Endpointy używają bezpośrednio DbContext i `ILifeOsLlmService`. Brak repozytoriów, mediatora,
warstw domenowych, background workers i infrastruktury do hipotetycznej skali.
`Source` opisuje kanał wejścia i **nie jest przekazywany do klasyfikacji semantycznej**.
`RawText` zapisujemy dokładnie jako otrzymaną wartość JSON `text`, wraz ze spacjami, literówkami
i nowymi liniami. Nie wykonujemy `Trim`, tłumaczenia ani nadpisania oryginału przez AI.

## Zapis i awarie

1. Sprawdzamy tekst oraz źródło. Zapisujemy rekord `PENDING` osobną, zakończoną operacją DB.
2. Dopiero po zapisie wołamy LLM. Udana odpowiedź daje pola AI i `COMPLETED`.
3. Awaria, timeout, odmowa modelu lub wadliwy JSON daje `FAILED` i polski `processingError`.
4. Jeśli zapis stanu końcowego się powiedzie, zwracamy **201**, także dla `FAILED`:
   oznacza to zapis notatki, nie gwarancję ukończenia AI. Klient powinien odczytać `processingState`.

Przerwanie procesu po pierwszym zapisie pozostawia w bazie `PENDING`. Nie ma automatycznych
ponowień. Anulowanie połączenia klienta nie anuluje końcowego zapisu stanu (osobny timeout 10 s).
Jeśli aktualizacja bazy po klasyfikacji zawiedzie, zwracamy **503** z `rawCaptureSaved=true`
i `captureId`; oryginał pozostaje w bazie jako `PENDING`. Jeśli zawiedzie pierwszy zapis,
nie wołamy LLM i nie potwierdzamy sukcesu. Nie da się zagwarantować zapisu przy niedostępnej DB.
Po błędzie sieci sprawdź bazę (np. w pgAdmin) przed ponownym wysłaniem: historia API pokazuje
tylko `COMPLETED`. v0.1 nie deduplikuje żądań,
a zerwane połączenie może uniemożliwić klientowi poznanie wyniku zakończonej transakcji.

Logi zawierają identyfikatory, stan operacji i rodzaj błędu, bez RawText, kluczy ani treści
odpowiedzi dostawcy. Szczegółowe logi EF są wyłączone, aby nie ujawniać danych przy błędach.
Notatki są wysyłane do skonfigurowanego dostawcy LLM podczas klasyfikacji i przeglądu.

## Baza danych

Jedna tabela `Captures` (plus standardowa `__EFMigrationsHistory`):

| Kolumny | Typ PostgreSQL |
| --- | --- |
| `Id` | `uuid`, klucz główny |
| `CreatedAt`, `CapturedAt` | `timestamp with time zone`, UTC |
| `RawText`, `Source`, `ProcessingState` | `text`, wymagane |
| `Type`, `Title`, `Summary`, `ProcessingError` | `text`, nullable |
| `Tags` | `text[]`, domyślnie pusta tablica w modelu |
| `ActionRequired` | `boolean`, nullable do czasu klasyfikacji |
| `MetadataJson` | `jsonb`, początkowo `{}` |

Indeks na `CapturedAt`. Enumy zapisujemy jako czytelny tekst.
Tablica `text[]` jest najprostszym natywnym zapisem listy tagów w PostgreSQL.
JSONB pozwala rozszerzać metadane bez kolejnej tabeli. v0.1 wyodrębnia `activity`, `energy`,
`satisfaction`, `location`; pola bez dowodów są pomijane w zapisanym JSON. Liczb nie zgadujemy.
Odpowiedź API zwraca `metadataJson` jako ciąg JSON, zgodnie z nazwą właściwości modelu.

`CreatedAt` to czas zapisu na serwerze. `CapturedAt` to czas myśli; brak wartości oznacza teraz.
Offset wejściowy jest normalizowany do UTC, z zachowaniem chwili. Historia i przegląd korzystają
z `CapturedAt`, nie z czasu importu. Przyszłe daty nie wchodzą do przeglądu.

Nowa migracja po zmianach modelu:

```powershell
dotnet ef migrations add NazwaZmiany --project src/LifeOs.Api --output-dir Data/Migrations
dotnet ef database update --project src/LifeOs.Api
```

## API i przykłady

`POST /api/captures`, `Content-Type: application/json`:

```json
{
  "text": "Dzisiaj po tenisie miałem dużo energii mimo że wcześniej nie chciało mi się iść.",
  "capturedAt": "2026-09-22T18:20:00+02:00",
  "source": "VOICE"
}
```

`source` jest wymagane: `VOICE`, `TEXT`, `OTHER`. `text` nie może być pusty ani składać się
wyłącznie z białych znaków. `capturedAt` jest opcjonalne; używaj ISO 8601 z offsetem lub `Z`.
Nieprawidłowy JSON, data lub enum zwraca 400. Kategorie: `FIELD_NOTE`, `TASK`, `CONTENT_IDEA`, `IDEA`, `OTHER`.

Przykłady do wklejenia w Swagger (bez daty, aby od razu trafiały do bieżącego przeglądu):

```json
{"text":"Medytacja dzisiaj była trudna. Po 15 minutach spokojniej. Satysfakcja 7 na 10.","source":"VOICE"}
```
```json
{"text":"Nie chciało mi się iść na tenis, ale po treningu miałem bardzo dużo energii.","source":"VOICE"}
```
```json
{"text":"Jutro muszę zadzwonić do dentysty.","source":"TEXT"}
```
```json
{"text":"Ludzie często próbują optymalizować swoje życie, zanim zdecydują jakiego życia chcą.","source":"TEXT"}
```
```json
{"text":"Zastanawiam się czy automatyzowanie procesów biznesowych mogłoby być dla mnie ciekawym kierunkiem zawodowym.","source":"TEXT"}
```
```json
{"text":"dzisiaj medytacja byla nawet spoko na poczatku duzo myslalem ale potem jakos po 15 minutach sie uspokoilem dalbym 7 na 10","source":"VOICE"}
```

Oczekiwane kategorie kolejno: FIELD_NOTE, FIELD_NOTE, TASK, CONTENT_IDEA, IDEA, FIELD_NOTE.
W ostatnim przykładzie znaczenie oceny może być niejednoznaczne; model powinien być ostrożny.

Przykład curl (Bash):

```bash
curl -i http://localhost:5080/api/captures \
  -H 'Content-Type: application/json' \
  --data-raw '{"text":"Jutro muszę zadzwonić do dentysty.","source":"TEXT"}'
curl 'http://localhost:5080/api/captures?days=7&limit=100'
curl -X POST http://localhost:5080/api/weekly-review
```

PowerShell (UTF-8):

```powershell
$body = @{ text = 'Jutro muszę zadzwonić do dentysty.'; source = 'TEXT' } | ConvertTo-Json
Invoke-RestMethod http://localhost:5080/api/captures -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

`GET /api/captures?days=7&type=TASK&source=VOICE&limit=100`:
wszystkie filtry opcjonalne, `days` 1–36500, `limit` 1–500 (domyślnie 100), najnowsze
`CapturedAt` pierwsze. Bez `days` historia nie ma ograniczenia czasowego. Zwracane są wyłącznie
rekordy `COMPLETED`. Rekordy `FAILED` i `PENDING` pozostają w bazie, ale są pomijane przez API historii.

`POST /api/weekly-review` bez body: bierze wyłącznie notatki `COMPLETED` z ostatnich 7 × 24 godzin.
Notatki `FAILED` i `PENDING` nie są wysyłane do LLM. Zwraca `periodStart`, `periodEnd`, `keyObservations`,
`recurringThemes`, `energyGivers`, `energyDrainers`, `openTasks`, `bestContentIdeas`,
`patterns`, `suggestedExperiment`. Wszystkie treści generowane są po polsku.
Daty ustala backend. Brak notatek `COMPLETED` w tym okresie daje puste listy i null bez wywołania LLM.
Awaria LLM daje 502; awaria bazy 503. Przegląd nie jest zapisywany.
Fakty, wzorce wsparte wieloma notatkami i hipotezy rozróżnia prompt. Bez dowodów sekcja
pozostaje pusta. To wskazówki dla modelu, nie gwarancja trafności semantycznej.

## Testy

```powershell
dotnet test
```

Testy API używają WebApplicationFactory, EF InMemory, stałego zegara i podstawionego
`ILifeOsLlmService`. Obejmują walidację, zapis przed AI, zachowanie oryginału i stanów przy
awariach, filtry, tygodniowy zakres i Swagger. Testy klienta HTTP sprawdzają strukturę JSON,
schematy, polskie znaki, błędy dostawcy, odmowy i ucięte odpowiedzi.

Opcjonalny test PostgreSQL stosuje migrację i sprawdza rzeczywisty zapis, JSONB, tablice,
daty oraz przepływ po awarii AI. **Użyj osobnej, pustej, tymczasowej bazy**; test dopisuje notatki.

```powershell
$env:LIFEOS_TEST_POSTGRES = '<connection string tymczasowej bazy>'
dotnet test
Remove-Item Env:LIFEOS_TEST_POSTGRES
```

Bez tej zmiennej test PostgreSQL jest jawnie pomijany. Automatyczne testy nie sprawdzają
jakości klasyfikacji prawdziwego modelu; użyj przykładów w Swagger po konfiguracji klucza.

## Android / MacroDroid — tylko plan integracji

1. Widget uruchamia rozpoznawanie mowy po polsku.
2. **Android/MacroDroid wykonuje speech-to-text**, backend nie dostaje audio.
3. MacroDroid wysyła UTF-8 JSON na `POST /api/captures`, z rozpoznanym `text`, `source=VOICE`
   i opcjonalnym czasem ISO 8601. Tekst musi być poprawnie escaped jako JSON (cudzysłowy, nowe linie).
4. Po 201 wyświetla „Zapisano”; przy `FAILED` może dodać „Klasyfikacja AI niedostępna”.
5. Inny widget lub klient może wysłać ten sam JSON z `source=TEXT`.

Telefon musi mieć dostęp do adresu komputera/serwera; `localhost` w telefonie oznacza telefon.
Dla zaufanej sieci lokalnej uruchom API z `--urls http://0.0.0.0:5080` i użyj adresu LAN komputera.
v0.1 celowo nie ma uwierzytelniania — używaj lokalnie lub przez prywatny dostęp, nie wystawiaj
otwartego portu w Internecie. Czas oczekiwania klienta ustaw dłuższy niż timeout LLM.
Żadna integracja Android nie jest zaimplementowana w tym repozytorium.

## Świadome ograniczenia i kandydaci na v0.2

Jedno synchroniczne wywołanie AI na zapis; brak retry, deduplikacji, edycji i ponownej
klasyfikacji przez API. Brak ukończeń zadań: przegląd pokazuje zamiary bez potwierdzonego
wykonania. Brak utrwalania przeglądów. Duża ilość tekstu może przekroczyć kontekst modelu;
nie stosujemy cichego obcinania notatek. Brak wyszukiwania, natywnej aplikacji mobilnej,
audio, RAG, embeddingów, kalendarza, powiadomień, workerów i harmonogramu.

Po sprawdzeniu użyteczności: ręczne ponowienie FAILED/PENDING, klucz idempotencji klienta,
prosty status ukończenia zadania i dopracowany widget MacroDroid. Wybierz wyłącznie to,
co rozwiąże problem zaobserwowany podczas rzeczywistego korzystania.

## Interfejs webowy — Notatki i Tydzień

Jedna aplikacja ASP.NET Core hostuje API i Blazor Interactive Server. Nie potrzeba Node.js,
oddzielnego hostingu ani dodatkowej migracji. UI korzysta z istniejących trzech endpointów przez
klienta HTTP `LifeOsApiClient`; nie odczytuje bezpośrednio bazy i nie duplikuje klasyfikacji.
Klient serwerowy korzysta z adresu otwartej aplikacji, więc ten adres musi być również osiągalny
z serwera (istotne przy późniejszym wdrożeniu za reverse proxy).

- `/` — maksymalnie 100 najnowszych ukończonych notatek, filtry typów, szczegóły i dodawanie tekstu.
- `/tydzien` — przegląd generowany wyłącznie przyciskiem, z ośmioma polskimi sekcjami.
- `/swagger` — dotychczasowe testowanie API.

Lista nie wyświetla pełnego oryginału. Kliknięcie karty pokazuje niezmieniony tekst obok
podsumowania AI, typ, źródło, datę, tagi, stan przetwarzania i potrzebę działania.
Daty wyświetlane są w strefie Europe/Warsaw. Formularz wysyła `text` i `source=TEXT`, bez
ręcznego wyboru typu. Podczas zapisu przycisk jest wyłączony, a po błędzie API tekst pozostaje
w formularzu. Odpowiedź 201 z FAILED pokazuje zapisany oryginał i informację o błędzie AI;
ten rekord nadal nie pojawia się w historii ani przeglądzie, zgodnie z regułą COMPLETED-only.

Lokalnie uruchom aplikację zwykłym poleceniem z początku README i otwórz
http://localhost:5080/. Dla telefonu w tej samej **zaufanej sieci Wi-Fi**, po zatrzymaniu
lokalnego procesu, możesz świadomie udostępnić aplikację w LAN:

```powershell
dotnet run --project src/LifeOs.Api --no-launch-profile --urls http://0.0.0.0:5080
```

Na Androidzie otwórz `http://ADRES-IP-KOMPUTERA:5080/` (adres IPv4 aktywnej karty znajdziesz
przez `ipconfig`). Jeśli zapora blokuje dostęp, zezwól na aplikację wyłącznie w sieci prywatnej.
Aplikacja nie ma logowania; osoby mające dostęp do portu mogą czytać i dodawać notatki oraz
uruchamiać płatne wywołania LLM. Nie otwieraj tego portu publicznie.

Blazor Server wymaga ciągłego połączenia z serwerem. Brak działania offline, PWA, nagrywania,
edycji, usuwania, wyszukiwania i trwałych linków do pojedynczych notatek. Szczegóły otwierają
się z już pobranej listy, bez nowego endpointu. Formularz i wygenerowany przegląd są stanem
bieżącego widoku; odświeżenie lub przejście na inną stronę może je wyczyścić. UI nie zapisuje
przeglądów. Testy bUnit sprawdzają renderowanie listy i szczegółów, mapowanie filtrów, żądanie
TEXT, obsługę błędu zapisu, stany puste oraz ręczne generowanie przeglądu. Nie wywołują LLM.
