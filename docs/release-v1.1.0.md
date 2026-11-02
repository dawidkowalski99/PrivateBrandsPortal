# PrivateBrandsPortal v1.1.0 — kontrolowane wdrożenie aktualizacji

STATUS: przygotowano lokalnie. NIE wykonano deploymentu, migracji PROD, połączenia z bazą PROD,
operacji IIS/Entra/Exchange ani wysyłki poczty. Poniższe kroki wymagają osobnego polecenia właściciela.

## Identyfikacja i weryfikacja

- Źródła: main, commit `0532655f6b7ba3c59574f3541a1ce95daeebcba2`.
- Początkowy Git: clean. Po przygotowaniu pozostają niecommitowane dokumenty i SQL release.
- Restore: powodzenie. Build Release: 0 błędów / 0 ostrzeżeń.
- Pełne testy Release: 385/385; testy SQL wyłącznie PrivateBrandsPortal_DEV, Graph jest atrapą.
- `dotnet list package --vulnerable --include-transitive`: brak znanych podatności w obu projektach.
- Publish: 264 pliki; DLL zgodna SHA-256 z buildem Release; brak PFX/P12/KEY/secrets.json.
- Publish framework-dependent .NET 10: `C:\Deploy\PrivateBrandsPortal_v1.1.0`.
- Manifest wszystkich plików, rozmiarów i SHA-256: `C:\Deploy\PrivateBrandsPortal_v1.1.0.manifest.sha256.txt`.
- Skrypt: `docs/sql/release-v1.1.0-WorkflowNotificationOutbox.sql` (przygotowany, NIEWYKONANY).
- Cel: IIS Site/AppPool PrivateBrandsPortal, `E:\Sites\PrivateBrandsPortal` na 192.168.1.170.
- W publish są: PrivateBrandsPortal.Web.dll/exe, deps/runtimeconfig/staticwebassets, web.config,
  appsettings.json, appsettings.Development.json, biblioteki zależne, runtimes/ i wwwroot/.
- **W publish NIE MA appsettings.Production.json.** Jeśli ktoś doda go do paczki później,
  również NIE WOLNO go kopiować na produkcyjny plik. Nie zmieniać konfiguracji mailowej.
- Ta aktualizacja wymaga jednej migracji, GraphEmailSender nie wymaga kolejnej.

## Co robi SQL

Bezpośredni poprzednik: `20261007180815_ProductCustomerRejection`.
Cel: `20261030144145_WorkflowNotificationOutbox`.

Wygenerowano offline:

```powershell
.\dotnet.ps1 ef migrations list --no-connect --project src/PrivateBrandsPortal.Web --configuration Release --no-build
.\dotnet.ps1 ef migrations script 20261007180815_ProductCustomerRejection 20261030144145_WorkflowNotificationOutbox --idempotent --project src/PrivateBrandsPortal.Web --configuration Release --no-build --output artifacts/release-1.1.0-ef-generated.sql
```

Wersja release kwalifikuje tabele schematem dbo i zawiera dodatkowe zabezpieczenia: SQLCMD stop-on-error, jawny USE PrivateBrandsPortal,
weryfikację 10 wcześniejszych wpisów historii, blokadę nieznanych/nowszych migracji, XACT_ABORT,
transakcyjną blokadę aplikacyjną, kontrolę częściowo utworzonego schematu i weryfikację po wykonaniu.
Powtórne wykonanie na poprawnej bazie z docelową migracją nie zmienia danych/schematu.
Nie jest to skrypt inicjalizacji pustej bazy ani uzupełnienia starszych brakujących migracji.
Historia jest wypisywana przed i po; nie należy dopisywać wpisu historii ręcznie.

Migracja dodaje nullable AppUsers.NotificationApproverId, indeks i FK NO ACTION oraz tabelę
NotificationOutbox i jej indeksy (w tym unikalny EventKey). Nie zasila tabel ani nie zmienia
istniejących projektów. Skrypt nie był wykonywany, również na DEV; oceniono jego treść offline.

## Ręczna procedura — dopiero po osobnej zgodzie

### 1. Przygotowanie okna i planu odzyskania

- [ ] Uzyskać zgodę, wyznaczyć okno serwisowe, operatora SQL/IIS i warunek wycofania.
- [ ] Przenieść paczkę do katalogu staging POZA aktywnym katalogiem aplikacji. Zweryfikować SHA-256
      wszystkich plików według manifestu; nie uruchamiać aplikacji ze staging.
- [ ] Potwierdzić poprawny serwer/instancję SQL i bazę PrivateBrandsPortal z administratorem;
      adres SQL nie jest zgadywany z samego adresu serwera IIS.
- [ ] Odczytać historię migracji PROD i porównać ze skryptem. Jeśli brakuje poprzednika lub innej
      wcześniejszej migracji, albo są nowsze/nieznane wpisy — STOP, przygotować osobny plan.
- [ ] Potwierdzić kompatybilny, aktualny Hosting Bundle/ASP.NET Core Runtime .NET 10 x64 i ANCM V2.
      Publish nie zawiera runtime .NET. Nie aktualizować runtime/IIS bez oddzielnego uzgodnienia.
- [ ] Zweryfikować Windows Authentication, Anonymous off, właściwą tożsamość puli i jej prawa do
      SQL oraz udziału załączników. Porównać opublikowany web.config z lokalnymi wymaganiami IIS.
- [ ] Potwierdzić środowisko Production i efektywne Notifications:WorkerEnabled=false,
      również w zmiennych środowiska/puli, które mogą nadpisać plik JSON. Nie uruchamiać workera.
- [ ] Potwierdzić przygotowane wymagane ustawienia Graph (bez ich ujawniania w logach).
      Graph wymaga poprawnej konfiguracji przy starcie, nawet przy WorkerEnabled=false.
      Wyłączony worker NIE weryfikuje jeszcze dostępu do certyfikatu ani połączenia z Graph.
- [ ] Zrobić pełny backup DB PrivateBrandsPortal z CHECKSUM zgodnie z polityką SQL, do unikalnego
      pliku .bak (nie nadpisywać poprzednich backupów); wykonać RESTORE VERIFYONLY WITH CHECKSUM.
      Zachować potwierdzoną procedurę próbnego restore poza produkcją; VERIFYONLY nie zastępuje
      testu odtworzenia. Skopiować backup poza katalog aplikacji i zapewnić do niego dostęp operatorowi.
- [ ] Zrobić pełny backup E:\Sites\PrivateBrandsPortal wraz z konfiguracją, web.config, ACL i
      manifestem/hashami do oddzielnej lokalizacji, nie pod wwwroot ani pod katalogiem aplikacji.
      Chronić backup appsettings.Production.json jak konfigurację zawierającą dane środowiska.
- [ ] Zachować kopię konfiguracji IIS (site/pool/bindings/authentication/environment) do odtworzenia.
- [ ] Załączniki pozostają na zewnętrznym udziale: NIE kopiować, przenosić ani usuwać ich w tej
      aktualizacji. Potwierdzić istniejący backup/snapshot udziału i zgodność z punktem backupu DB.

### 2. Zatrzymanie zapisów i zabezpieczenie konfiguracji

- [ ] Wstrzymać pracę użytkowników i inne instancje zapisujące do tej bazy. Zatrzymać AppPool
      PrivateBrandsPortal i potwierdzić jego zatrzymanie przed podmianą plików.
- [ ] Jeśli pierwszy backup DB był online i od tego czasu mogły wystąpić zapisy, wykonać drugi,
      finalny backup po zatrzymaniu zapisów. To jest spójny punkt rollbacku; odnotować czas.
- [ ] Zapisać SHA-256 produkcyjnego E:\Sites\PrivateBrandsPortal\appsettings.Production.json
      i potwierdzić jego bezpieczną kopię. Plik ma pozostać w miejscu, bez zmiany zawartości.

### 3. Podmiana aplikacji (bez konfiguracji produkcyjnej)

- [ ] Używać tylko zatwierdzonego staging. NIE stosować robocopy /MIR, /PURGE ani usuwania całego
      E:\Sites\PrivateBrandsPortal — usunęłoby to produkcyjną konfigurację.
- [ ] Zachować appsettings.Production.json. Jawnie wykluczyć go ze wszystkich operacji kopiowania
      i usuwania niezależnie od tego, czy paczka aktualnie zawiera taki plik.
- [ ] Dla czystej podmiany przenieść stare PLIKI APLIKACJI do osobnego rollback/quarantine poza
      katalogiem aplikacji, pozostawiając produkcyjny JSON oraz zidentyfikowane pliki środowiskowe,
      logi, ACL i ewentualny trwały katalog Data Protection. Najpierw zatwierdzić listę przenoszonych
      plików; nie usuwać nieznanych katalogów automatycznie. Nie dotykać udziału załączników.
- [ ] Skopiować zawartość publish (łącznie z wwwroot/ i runtimes/) do aktywnego katalogu,
      z jawnym wykluczeniem appsettings.Production.json. Unikać mieszanki starych DLL i nowych plików.
      Nie zmieniać tożsamości puli ani ACL potrzebnych do odczytu konfiguracji/kluczy/załączników.
- [ ] Porównać pliki release z manifestem. Ponownie sprawdzić hash produkcyjnego JSON — musi być
      identyczny. Jeśli różni się, STOP i przywrócić właściwy plik z backupu przed uruchomieniem.

### 4. Migracja — nadal zatrzymana aplikacja

- [ ] Administrator SQL wybiera świadomie właściwy serwer oraz PrivateBrandsPortal.
- [ ] Wykonać wyłącznie zatwierdzony release SQL, w SQLCMD Mode (SSMS) lub sqlcmd z `-b`.
      Skrypt ma `:On Error exit`; nie usuwać tej dyrektywy i nie kontynuować po błędzie.
      Nie uruchamiać dotnet ef database update z przypadkowym connection stringiem.
- [ ] Zachować wynik i obie listy __EFMigrationsHistory. Potwierdzić wpis
      20261030144145_WorkflowNotificationOutbox, tabelę NotificationOutbox, kolumnę
      AppUsers.NotificationApproverId, jej FK i indeksy. Przy błędzie NIE uruchamiać nowego kodu.
- [ ] Nie traktować migracji jako konfiguracji odbiorców: nowe pole pozostaje NULL dla istniejących
      użytkowników; brak przypisania/e-maila daje Blocked i nie blokuje akcji biznesowych.

### 5. Start i smoke test — bez e-maili

- [ ] Potwierdzić ponownie efektywny WorkerEnabled=false i uruchomić AppPool PrivateBrandsPortal.
- [ ] Sprawdzić https://portalmp.forte.local/ pod kontem domenowym: SSO, Dashboard, Projects,
      istniejący Project Details, Approvals/Implementation, Reports, Archive i Users zgodnie z rolą.
- [ ] Sprawdzić odczyt istniejącego załącznika z udziału; nie zmieniać danych ani dokumentów użytkowników.
- [ ] Sprawdzić logi aplikacji/IIS/Event Viewer: brak błędów startu, EF i autoryzacji.
- [ ] Odczytać NotificationOutbox (Id, EventType, Status, Attempts, LastError, CreatedAtUtc,
      NextAttemptAtUtc, SentAtUtc); NIE resetować rekordów ani nie wysyłać ich ręcznie.
- [ ] Opcjonalny test zapisu workflow tylko na wcześniej uzgodnionym projekcie testowym PROD i po
      zgodzie na taki zapis: potwierdzić Pending/Blocked oraz Attempts=0 i brak SentAtUtc.
      Bez osobnej zgody smoke test pozostaje tylko odczytowy.
- [ ] Dopiero po pozytywnym smoke teście dopuścić użytkowników i zamknąć okno serwisowe.
      WorkerEnabled nadal false. Obserwować logi; nie usuwać backupów.

## Rollback kodu (preferowany przy błędzie aplikacji po migracji)

1. Zatrzymać AppPool i wszystkie zapisy; ustalić punkt/czas oraz przyczynę rollbacku.
2. Zachować nieudany pakiet i logi diagnostyczne poza aktywnym katalogiem.
3. Przywrócić poprzednie pliki aplikacji z backupu, usuwając nowy payload według jego manifestu;
   nie usuwać appsettings.Production.json ani udziału załączników. Porównać hash zachowanego JSON.
4. Migracja jest addytywna: starszy kod powinien ignorować nową tabelę/nullable kolumnę.
   Preferować pozostawienie nowego schematu zamiast destrukcyjnego Down. Zweryfikować zgodność
   faktycznego backupu kodu; nie zakładać jego wersji na podstawie historii repozytorium.
5. Potwierdzić WorkerEnabled=false, przywrócić ewentualne uzgodnione ustawienia IIS, uruchomić
   pulę i wykonać odczytowy smoke test. Zachować kolejkę Outbox do późniejszej analizy.

## Rollback bazy — osobna decyzja administratora

- NIE wykonywać automatycznie `dotnet ef database update 20261007180815_ProductCustomerRejection`.
  Down usuwa NotificationOutbox i NotificationApproverId, a więc traci wiadomości, stan prób
  i przypisania. W repo nie przygotowano automatycznego destrukcyjnego rollbacku.
- Podstawową metodą odzyskania jest zweryfikowany backup DB sprzed zmian, przy zatrzymanych
  wszystkich zapisach. Administrator uzgadnia utratę zapisów od punktu backupu oraz sposób
  odtworzenia (ewentualne point-in-time tylko jeśli recovery model i łańcuch logów to umożliwiają).
- Wykonać backup stanu awaryjnego/eksport potrzebnej diagnostyki przed restore, o ile jest możliwy.
  Odtworzyć backup zgodnie z procedurą DBA, a następnie zgodny poprzedni kod i konfigurację.
- Skoordynować stan załączników z odtworzonym DB: nie usuwać automatycznie plików powstałych
  później; ustalić odzyskanie/reconciliation z administratorem udziału. W oknie bez zapisów
  załączników ten release sam ich nie zmienia.
- Zweryfikować historię migracji, logowanie, projekty i dostęp do plików. Nie dopisywać historii
  ręcznie i nie odtwarzać ponownie już wysłanych maili. Worker ma pozostać wyłączony.

## Pierwszy Graph test jest odrębnym etapem

Nie włączać WorkerEnabled w ramach tego release. Osobno przejrzeć backlog i odbiorców,
zweryfikować certyfikat/tożsamość puli/Graph/Exchange, uzyskać zgodę na konkretną wiadomość
oraz kontrolowany sposób jej wysłania. Samo włączenie workera przetwarza WSZYSTKIE należne
rekordy. Szczegóły: [graph-email.md](graph-email.md). Nie resetować Sent/Simulated/Failed hurtem.

## Wyniki przygotowania lokalnego

Logi są w artifacts/release-1.1.0-{restore,build,tests,vulnerabilities,publish}.log.
TestResults/release-1.1.0.trx znajduje się pod projektem testowym.
Brak zmian workflow, GraphEmailSender, Outboxa, konfiguracji mailowej i produkcji.
Dokumenty/skrypt pozostawiono do przeglądu; nie wykonano commitu ani push.

SHA-256 skryptu SQL: 8794BA609876C286E4F70684C0D0B6354A2A9B29628BFBED19C22FC0D77C8938
SHA-256 manifestu publish: 33AB1F26C42C11DE65CBCE239E3FF5056EC48DD21C71C3F3114CF6A2EE8B27FD
