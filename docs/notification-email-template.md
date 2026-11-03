# Powiadomienia HTML po polsku

Centralny `NotificationEmailRenderer` generuje temat, HTML oraz tekst. HTML używa tabel, stylów inline i kontenera 600 px (także tabeli warunkowej dla Outlooka). Nie ma obrazów, JavaScript ani zewnętrznych CSS. Wszystkie wartości trafiające do HTML są kodowane. Komentarze zachowują podział na wiersze, a puste pola są pomijane.

## Zapis i zgodność

Nie jest potrzebna migracja. Istniejące pole `NotificationOutbox.Body` przechowuje teraz wersjonowany snapshot JSON poprzedzony `notification-email:v1`. Zawiera numer projektu, klienta, kraj, PM, autora, SKU, kategorię, podkategorię i komentarz z chwili zdarzenia. Czas pochodzi z istniejącego `CreatedAtUtc`. Pozostałe pola Outboxa, odbiorcy, klucze zdarzeń, transakcje i retry nie zmieniły się.

Renderer obsługuje także stare tekstowe wpisy kolejki. Nie przepisuje ich i nie odczytuje aktualnych danych projektu zamiast historycznych. Stare wiadomości nie mają kraju, PM, kategorii ani podkategorii, dlatego te pola są w nich pomijane.

`Notifications:TimeZone` domyślnie ma wartość `Central European Standard Time` (Polska, automatyczny czas letni/zimowy). Format daty używa `pl-PL`. Błędna strefa jest zgłaszana podczas walidacji konfiguracji. Nie zmieniono pliku produkcyjnego.

Graph otrzymuje `contentType: HTML`, jeśli `EmailMessage.HtmlBody` istnieje. `TextBody` pozostaje dostępny w kontrakcie i testach, a wiadomości bez HTML nadal mogą być wysyłane jako tekst. JSON Graph sendMail obsługuje jedno body: nie wysyłamy MIME multipart/alternative. Autoryzacja Graph i certyfikaty nie zostały zmienione.

## Podgląd bez wysyłki

Uruchom:

```powershell
.\dotnet.ps1 test -c Release --filter FullyQualifiedName~NotificationEmailRendererTests
```

Test symulacji zapisuje wyłącznie fikcyjne przykłady do:

- `tests/PrivateBrandsPortal.Tests/bin/Release/net10.0/email-previews/manager.html`
- `tests/PrivateBrandsPortal.Tests/bin/Release/net10.0/email-previews/manager.txt`
- `tests/PrivateBrandsPortal.Tests/bin/Release/net10.0/email-previews/pm.html`
- `tests/PrivateBrandsPortal.Tests/bin/Release/net10.0/email-previews/pm.txt`

Pliki TXT zawierają także temat wiadomości. HTML można otworzyć lokalnie w przeglądarce. Tryb Simulated nadal używa istniejącego `LoggingEmailSender`; nie zapisuje danych klientów ani treści maili do logów. Nie trzeba włączać workera, konfigurować Graph ani wykonywać prawdziwej wysyłki, aby obejrzeć te przykłady. Ostateczny wygląd w Outlooku wymaga osobnego testu w kliencie pocztowym; nie wykonywano wysyłki.

## Nazwy zdarzeń

| EventType | Tytuł |
|---|---|
| ProjectSubmitted / AwaitingManagerReview | Projekt oczekuje na akceptację |
| ProjectApprovalBypassed | Projekt przekazano do realizacji bez dodatkowej akceptacji |
| ProductApproved | Produkt został zatwierdzony |
| ProductRejected | Produkt został odrzucony |
| ProductEditedAndApproved / EditedAndApproved | Produkt został zmieniony i zatwierdzony |
| ImplementationRequested | Produkt oczekuje na akceptację wdrożenia |
| ImplementationResubmitted | Produkt został ponownie przekazany do akceptacji wdrożenia |
| ImplementationApproved | Wdrożenie produktu zostało zatwierdzone |
| ImplementationRejected | Wdrożenie produktu zostało odrzucone |
| ProductRejectedByCustomer | Produkt został odrzucony przez klienta |
| ProjectManagerChanged | Zmieniono Project Managera projektu |
| CommercialStatus:PriceOfferSubmitted | Oferta cenowa została przekazana klientowi |
| CommercialStatus:OfferUnderNegotiation | Oferta jest w trakcie negocjacji |
| CommercialStatus:CustomerApprovedOrder | Klient zatwierdził projekt lub zamówienie |
| CommercialStatus:CustomerNotApproved | Produkt został odrzucony przez klienta |
| CommercialStatus:ImplementationIntoProduction | Produkt przekazano do wdrożenia do produkcji |
| CommercialStatus:SalesAndDelivery | Produkt przeszedł do sprzedaży i dostawy |

Mapowania obejmują wszystkie aktualnie generowane zdarzenia, wszystkie wartości statusów komercyjnych i dwa aliasy opisane w wymaganiach. Nie dodają nowych zdarzeń workflow. Nieznany przyszły EventType ma bezpieczną nazwę „Projekt został zaktualizowany”.

## Zakres sprawdzeń

Testy renderera sprawdzają polski temat i datę, DST, dane projektu/produktu, ukrywanie pustych pól, komentarze, kodowanie HTML, adres ekranu, stare wpisy kolejki, mapowania i symulację. Test transportu sprawdza HTML oraz tekst na fałszywym handlerze HTTP. Istniejące testy Outboxa nadal sprawdzają odbiorców, transakcje, retry i workflow. Nie wykonujemy testu na produkcji ani prawdziwej wysyłki.

Wcześniej przygotowany publish v1.1.0 nie jest aktualizowany przez tę zmianę. Przed późniejszym wdrożeniem należy ponownie przygotować i zweryfikować paczkę. Rollback do starego kodu przy nowych wpisach snapshot JSON wymaga pozostawienia workera wyłączonego: stary renderer nie rozumie nowego formatu Body.
