# Kontrola jakości polskiej transkrypcji

Aktualizacja 2026-11-14: poprzednia kontrola obejmowała tylko Hangul i przepuszczała
cyrylicę. LifeOS obsługuje polskie nagrania; każde żądanie transkrypcji wysyła teraz
stałe `language=pl`, niezależnie od dawnych ustawień `Llm:TranscriptionLanguage`.
Nie jest to tłumaczenie tekstu ani automatyczny wybór języka.

1. Domyślny model pierwszej próby to `gpt-4o-transcribe` zamiast wersji mini.
   Wdrożenie ustawia również model w konfiguracji Azure, aby stara wartość go nie
   nadpisywała. Pełny model może zwiększyć koszt i czas transkrypcji.
2. Litery spoza obsługiwanych bloków alfabetu łacińskiego (m.in. cyrylica, Hangul,
   grecki, arabski, chiński) powodują jedną ponowną próbę z oryginalnymi bajtami audio.
   Używany jest `Llm:TranscriptionFallbackModel`, domyślnie również `gpt-4o-transcribe`.
3. Nie dodajemy promptu, który wcześniej był powtarzany przy ciszy. Nie usuwamy
   obcych liter i nie tłumaczymy podejrzanego wyniku. Polski, angielskie terminy,
   cyfry, interpunkcja i emoji pozostają bez zmian.
4. Drugi podejrzany wynik lub błąd dostawcy kończy się HTTP 502 bez utworzenia
   notatki i bez klasyfikacji. Telefon zachowuje nagranie do ponownej synchronizacji.
   Pusta transkrypcja nadal zwraca HTTP 422, również bez notatki.
5. Obie próby mają wspólny limit czasu. W logach nie ma treści nagrania ani transkrypcji.

Parametr języka pomaga modelowi, ale nie gwarantuje poprawności każdego słowa.
Kontrola alfabetu nie wykryje błędnych słów zapisanych literami łacińskimi.
Zmiana nie przerabia starych notatek; serwer nie przechowuje ich oryginalnego audio.

Testy obejmują cyrylicę i Hangul ze zgłoszeń, inne alfabety (także poza BMP), ponowne
wysłanie identycznego audio, strumienie bez przewijania, brak zapisu i klasyfikacji
po powtórnym błędzie, ciszę oraz zachowanie polskich znaków i angielskich terminów.
Test ustawień sprawdza, że dawne wartości `auto`, `ru`, `ko` i pusty język nie
zmieniają `language=pl`. Pełna ocena dokładności konkretnego nagrania wymaga audio.

Parametry: [oficjalna dokumentacja OpenAI](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create).
