# Kontrola jakości polskiej transkrypcji

Poprawka z 2026-11-13 obsługuje zaobserwowane koreańskie znaki (Hangul) przy
zawahaniach i mruczeniu w polskich nagraniach. Parametr `language=pl` był już
wysyłany; sam nie gwarantuje polskiego zapisu każdego dźwięku.

1. Zwykłe nagranie korzysta z `Llm:TranscriptionModel` tak jak dotychczas.
2. Jeśli polski wynik zawiera Hangul, oryginalne bajty audio są wysyłane jeszcze raz
   do `Llm:TranscriptionFallbackModel` (domyślnie `gpt-4o-transcribe`).
3. Nie dodajemy promptu, nie przekazujemy błędnego tekstu do poprawiania i nie usuwamy
   znaków z transkrypcji. Krótka polska mowa i angielskie terminy pozostają bez zmian.
4. Drugi podejrzany wynik lub błąd dostawcy kończy się HTTP 502 bez utworzenia
   notatki i bez klasyfikacji. Telefon zachowuje nagranie do ponowienia synchronizacji.
   Pusta transkrypcja nadal zwraca HTTP 422, również bez notatki.
5. Maksymalnie jedno ponowienie mieści się we wspólnym limicie czasu transkrypcji.
   Dodatkowe wywołanie AI występuje tylko dla podejrzanego wyniku i może zwiększyć koszt.

Kontrola jest celowo ograniczona do Hangul przy języku `pl`: nie gwarantuje wykrycia
każdego błędnego słowa ani halucynacji zapisanej alfabetem łacińskim. Przy jawnym
wyborze języka koreańskiego nie blokuje koreańskiej transkrypcji. Zmiana nie przerabia
starych notatek; serwer nie przechowuje ich oryginalnego audio.

Parametry sprawdzono w [oficjalnej dokumentacji transkrypcji OpenAI](https://developers.openai.com/api/reference/resources/audio/subresources/transcriptions/methods/create).
Testy obejmują fragmenty Hangul ze zgłoszenia, ponowne wysłanie identycznego audio,
strumień bez przewijania, brak zapisu przy powtórnym błędzie, ciszę i zachowanie
krótkiej mowy. Ocena dokładności na konkretnym nagraniu wymaga jego audio; same zrzuty
ekranu nie pozwalają odtworzyć oryginalnego dźwięku.
