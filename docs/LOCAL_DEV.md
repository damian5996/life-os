# LifeOS Dev na komputerze i telefonie

Produkcja pozostaje w Azure, a jej pakiet Android to `pl.lifeos.mobile`.
Lokalny Dev ma własną bazę, fioletową ikonę, nazwę **LifeOS Dev** i pakiet
`pl.lifeos.mobile.dev`. Android przechowuje jego ustawienia i nagrania oddzielnie.
Dane produkcyjne nie są automatycznie kopiowane do Dev.

## Codzienne uruchamianie

W PowerShell, w katalogu repozytorium, przy uruchomionym Docker Desktop:

```powershell
.\scripts\start-dev.ps1
```

Przeglądarka: <http://127.0.0.1:5081>. Administracja:
<http://127.0.0.1:5081/administracja>.

Po podłączeniu telefonu przez USB i zaakceptowaniu debugowania:

```powershell
.\scripts\connect-dev-phone.ps1
```

Otwórz fioletową aplikację **LifeOS Dev**. Po ponownym podłączeniu USB może być
potrzebne powtórzenie polecenia. Przy wielu urządzeniach podaj `-Serial <identyfikator>`.
Po zmianach aplikacji, w tym wspólnego interfejsu Razor:

```powershell
.\scripts\install-dev-phone.ps1
```

Skrypt buduje i aktualizuje tylko Dev. Nie usuwa danych ani nie nadpisuje produkcyjnego
pakietu. Zatrzymaj nagrywanie w Dev przed aktualizacją. Po pracy:

```powershell
.\scripts\stop-dev.ps1
```

Zatrzymanie zachowuje bazę i nagrania. Backend Dev wymaga działającego komputera;
bez połączenia można nagrywać offline, ale przeglądanie i synchronizacja poczekają.

## Klucz AI — opcjonalny na etapie przygotowania

Bez klucza działają interfejs, kategorie, edycja i aktywacja ustawień. Nie działają
prawdziwe podglądy AI, klasyfikacja ani transkrypcja. Próba AI zwraca komunikat braku
klucza bez żądania do dostawcy. Nagrania po błędzie wysyłania pozostają w kolejce.
Tekstowa notatka zachowuje oryginał ze stanem FAILED.

Gdy zechcesz włączyć AI:

```powershell
.\scripts\set-dev-key.ps1
.\scripts\stop-dev.ps1
.\scripts\start-dev.ps1
```

Klucz jest wpisywany w ukrytym polu i zapisywany w osobnym magazynie .NET User Secrets
`lifeos-local-dev`, poza repozytorium i APK. Nie wklejaj go do czatu ani plików Git.
Dev nie dziedziczy klucza z User Secrets projektu produkcyjnego. Wywołania AI mogą
generować koszty API; lokalny backend nie wymaga hostingu Azure.

## Administracja

- Kategorie: nazwa, instrukcja wyboru przez AI i przełącznik aktywności. Aktywowane
  wcześniej kategorie można wyłączyć, zachowując historię.
- Osobne instrukcje dla notatki i podsumowania okresu. Format danych i lista sekcji
  wynikowych pozostają ustalone przez aplikację; nie są dowolnym szablonem raportu.
- **Zapisz szkic** nie zmienia aktywnych ustawień.
- **Sprawdź na przykładzie** używa bieżącego szkicu, bez zapisu notatki lub aktywacji.
  Wymaga klucza AI. Podgląd podsumowania traktuje przykład jako jedną notatkę.
- **Zapisz i aktywuj** włącza ustawienia tylko w Dev. Notatka zapamiętuje identyfikator
  i nazwę kategorii oraz numer aktywnej wersji. Starsze notatki nie są przetwarzane ponownie.
- Nieaktualna edycja z drugiego okna zwraca konflikt zamiast nadpisywać nowsze zmiany.
  Można wczytać aktywne lub domyślne ustawienia do szkicu. Pełna historia wcześniejszych
  promptów nie jest jeszcze archiwizowana.

## Jak testować transkrypcję na telefonie

1. Uruchom Docker Desktop. W PowerShell przejdź do `C:\repos\my-apps\LifeOS`.
2. Jeśli klucz nie jest jeszcze skonfigurowany, uruchom `scripts/set-dev-key.ps1`.
   Możesz użyć klucza z własnego konta OpenAI; magazyn Dev jest oddzielny od produkcji.
   Bez klucza nagrywanie działa, ale transkrypcja nie będzie możliwa.
3. Po zmianie klucza zrestartuj Dev: `scripts/stop-dev.ps1`, a następnie
   `scripts/start-dev.ps1`. Skrypt startu buduje aktualny backend. Przy zwykłym starcie
   wystarczy `scripts/start-dev.ps1`.
4. Podłącz i odblokuj telefon, zaakceptuj debugowanie USB i uruchom
   `scripts/connect-dev-phone.ps1`.
5. Otwórz **LifeOS Dev** z fioletową ikoną → **Nagraj**. Zwykły LifeOS nadal wysyła do Azure.
6. Nagraj 20–30 sekund: pełne polskie zdanie, „hmm”, „yyy”, „mhm”, pauzę około 5 sekund,
   kolejne zdanie i terminy „.NET, Azure”. Naciśnij Stop. Przy działającym połączeniu
   świeże nagranie wysyła się automatycznie; zaległe wyślesz przyciskiem Synchronizuj.
7. Otwórz szczegóły w Notatkach i sprawdź **Oryginalną notatkę**, czyli transkrypcję,
   nie tylko podsumowanie AI. Porównaj słowa, przerwy i brak koreańskich wstawek.
   Osobno sprawdź krótką wypowiedź, cichą mowę oraz sekundę samej ciszy.

Testuj na gałęzi `codex/dev-admin`. Poprawka transkrypcji jest wspólna z produkcją;
panel kategorii/promptów zmienia późniejszą analizę tekstu, nie rozpoznawanie audio.
Zgłaszając problem, podaj godzinę testu i czy użyto Dev, czy produkcji. Nie trzeba
wklejać prywatnej treści. Do oceny konkretnego błędu potrzebny jest oryginalny dźwięk;
same zrzuty nie pozwalają go odtworzyć. Udane nagrania są usuwane z telefonu po zapisie,
więc do powtarzalnych prób najlepiej używać znanego, krótkiego tekstu testowego.

W Dev sprawdź też wyłączenie internetu, ponowne uruchomienie aplikacji i ręczną
synchronizację po odzyskaniu połączenia. Data notatki ma pozostać datą nagrania.

## Izolacja i publikacja

- Compose: projekt `lifeos-local-dev`, wolumin `lifeos-local-dev_lifeos-dev-data`,
  PostgreSQL `127.0.0.1:55439/lifeos_dev`.
- Backend słucha tylko `127.0.0.1:5081`. Tryb `LifeOs:LocalDev=true` odmawia startu
  z innym hostem, portem, użytkownikiem lub nazwą bazy oraz poza Development.
  Profile uruchamiania mają tę blokadę; używaj skryptu dostarczającego lokalne hasło.
- Dev automatycznie stosuje migracje wyłącznie do lokalnej bazy. Produkcyjna baza
  nie otrzymała nowej migracji.
- Pakiet Dev odrzuca zdalny adres API; HTTP jest dopuszczone tylko dla localhost.
  Produkcyjne ustawienia i wymaganie HTTPS pozostają oddzielne.
- Pliki Androida są rozdzielone: `obj/Dev`, `bin/Dev` oraz produkcyjne `obj`, `bin`.
- Logi i dane pomocnicze są w ignorowanym katalogu `.tools/dev`. Hasło lokalnej bazy
  jest w `database.json`; nie usuwaj go, zachowując wolumin.
- Administracja jest obecnie dostępna tylko w lokalnym Dev. Przed udostępnieniem
  w chmurze potrzebne są logowanie/uprawnienia, plan migracji i kopia bazy.
- Push i pull request uruchamiają testy, nie wdrożenie. Produkcyjny workflow wymaga
  ręcznego uruchomienia z `main` i wpisania `DEPLOY PRODUCTION` po akceptacji.
  Gałąź `codex/dev-admin` służy testom i nie została wdrożona do Azure.

## Weryfikacja 2026-11-10

Sprawdzono migrację na odrębnej bazie PostgreSQL, izolację szkicu, konflikty edycji,
dynamiczny schemat kategorii, podgląd bez zapisu, filtrowanie i eksport oraz brak
administracji w produkcyjnym trybie backendu. Próby AI używały kontrolowanych
odpowiedzi testowych — zgodnie z decyzją użytkownika Dev nie ma klucza.
Release APK zainstalowano obok LifeOS 0.3.0. Użytkownik potwierdził pasek Dev i zakładkę
Administracja na telefonie. Workflow zabezpieczający produkcję przeszedł testy,
a etap wdrożenia został pominięty.

### Domknięcie 2026-11-13

Zaległe zmiany zapisano na gałęzi `codex/dev-admin`. Dołączono poprawkę kontroli
polskiej transkrypcji z produkcyjnego commitu `46bcc0d`; opis w
[TRANSCRIPTION_QUALITY.md](TRANSCRIPTION_QUALITY.md). Przeszło 135 testów backendu/UI,
2 dodatkowe testy PostgreSQL oraz 14 testów mobilnych. Jednorazową bazę testową usunięto;
baza Dev zachowuje ustawienia. Klucz AI nadal pozostaje nieskonfigurowany zgodnie
z wcześniejszą decyzją użytkownika — testy z prawdziwym głosem wymagają jego dodania.
