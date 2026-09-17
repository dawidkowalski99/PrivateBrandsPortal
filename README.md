# Private Brands Portal

Wewnętrzny portal działu Marek Prywatnych: ASP.NET Core MVC, Razor Views,
EF Core i SQL Server, Windows Authentication, docelowo IIS w sieci firmowej.
Interfejs jest po angielsku zgodnie z nazwami ekranów i pól w wymaganiach.

## Stan: ETAP 2 — model i migracja gotowe, baza oczekuje konfiguracji

Gotowe: solution, projekt MVC .NET 10, ApplicationDbContext, konfiguracja SQL,
Windows SSO, polityki autoryzacji, responsywny sidebar/topbar, Dashboard oraz
informacyjne strony Projects, Approvals i Administration. Zasoby Bootstrap
są lokalne; aplikacja nie potrzebuje CDN. Własny CSS nie wymaga procesu npm.

Dodano siedem encji biznesowych, osobne konfiguracje EF, migrację
InitialBusinessSchema, deterministyczny seed słowników i generator numerów.
Nie ma jeszcze formularzy, operacji administracyjnych ani workflow.
Kreski na Dashboardzie oznaczają brak danych, a nie rzeczywiste liczniki.
Brak connection stringa nadal nie blokuje stron informacyjnych.
Nie wykonujemy migracji ani połączeń SQL automatycznie podczas startu.
PrivateBrandsPortal_DEV NIE została utworzona: brakuje potwierdzonego endpointu
SQL (instancji lub portu) i ConnectionStrings:DefaultConnection.

## Wymagania i środowisko

- Windows, konto domenowe do rzeczywistego SSO, Git.
- SDK .NET 10 (global.json wskazuje 10.0.401, dopuszczając nowsze patche).
- Dostęp do nuget.org podczas pierwszego restore.
- SQL Server 2022 Express dopiero od etapu z encjami; serwer: 192.168.1.170.
- Produkcja: IIS, Windows Authentication, ASP.NET Core Hosting Bundle 10 x64.

Na komputerze początkowo były runtime'y, ale nie było SDK.
Oficjalne SDK 10.0.401 zainstalowano lokalnie w .tools/dotnet, bez zmiany
instalacji systemowej. Katalog .tools jest ignorowany przez Git.
Po sklonowaniu na inną maszynę zainstaluj SDK ze strony Microsoft:
https://dotnet.microsoft.com/download/dotnet/10.0

Skrypt dotnet.ps1 wybiera lokalne SDK, a gdy go nie ma, używa systemowego.
Ustawia DOTNET_ROOT i katalogi cache dla bieżącego procesu.
Można zamiast niego używać zwykłego dotnet, jeśli SDK jest zainstalowane systemowo.

## Uruchomienie development

PowerShell, z katalogu C:\Dev\PrivateBrandsPortal:

```powershell
.\dotnet.ps1 --info
.\dotnet.ps1 restore PrivateBrandsPortal.sln --configfile NuGet.Config
.\dotnet.ps1 tool restore --configfile NuGet.Config
.\dotnet.ps1 build PrivateBrandsPortal.sln --no-restore
.\dotnet.ps1 test PrivateBrandsPortal.sln --no-build
.\dotnet.ps1 run --project src/PrivateBrandsPortal.Web --launch-profile http
```

Adres: http://localhost:5080. Zatrzymanie: Ctrl+C w terminalu aplikacji.
HTTP służy wyłącznie lokalnemu development na loopback.
Produkcja wymaga HTTPS. Alternatywnie profil IIS Express ma Windows
Authentication włączone, Anonymous wyłączone, port HTTPS 44380.

Otwórz portal w przeglądarce obsługującej zintegrowane logowanie Windows,
np. firmowym Edge. Użytkownik jest odczytywany z User.Identity.Name.
Aplikacja nie ma formularza logowania i nie przechowuje haseł AD.
Jeśli przeglądarka pokaże monit o hasło, anuluj go i sprawdź z IT ustawienia
strefy Local Intranet / polityki automatycznego logowania. Bez odpowiedniej
konfiguracji przeglądarki i domeny sam kod nie zagwarantuje bezmonitowego SSO.

Środowisko izolowane może blokować profil Windows, Data Protection, dziennik
zdarzeń albo Negotiate. Uruchamiaj wtedy komendy w zwykłym terminalu użytkownika;
nie wyłączaj uwierzytelniania w kodzie. Zwykłe uruchomienie nie wymaga konta admina.

## Konfiguracja bazy i sekretów

Klucz: ConnectionStrings:DefaultConnection. W appsettings.json celowo jest pusty.
Nie potwierdzono nazwy instancji ani portu SQL — NIE zakładamy SQLEXPRESS.
Przed pierwszym połączeniem ustal z IT dokładny endpoint, sposób
uwierzytelniania i certyfikat SQL. Dla certyfikatu należy użyć zgodnej nazwy DNS.

Baza development: PrivateBrandsPortal_DEV. Produkcja: PrivateBrandsPortal.
Poniższy endpoint jest placeholderem, który trzeba zastąpić:

```powershell
.\dotnet.ps1 user-secrets set "ConnectionStrings:DefaultConnection" "Server=<POTWIERDZONY_ENDPOINT_SQL>;Database=PrivateBrandsPortal_DEV;Integrated Security=True;Encrypt=True;TrustServerCertificate=False" --project src/PrivateBrandsPortal.Web
```

UserSecretsId jest już skonfigurowane, więc user-secrets init nie jest potrzebne.
User Secrets znajdują się w profilu Windows poza repozytorium; nie są szyfrowanym
sejfem. Nie wklejaj haseł SQL do kodu, README, poleceń zachowywanych w historii
ani plików wersjonowanych. Preferuj zintegrowane uwierzytelnianie.
Nie włączono EnableSensitiveDataLogging.

Na IIS użyj bezpiecznej konfiguracji wdrożeniowej, np. zmiennej środowiskowej
ConnectionStrings__DefaultConnection. ASPNETCORE_ENVIRONMENT=Production.
Połączenie SQL korzysta z tożsamości procesu / puli IIS, a nie automatycznie
z konta użytkownika przeglądarki. Uprawnienia SQL dla konta usługi konfiguruje IT.
Nie wprowadzamy impersonacji ani delegacji kont użytkowników do SQL.

## Model danych

| Encja | Przeznaczenie |
| --- | --- |
| AppUser | Profil domenowy, rola aplikacyjna, aktywność; bez haseł |
| Country | Słownik krajów z kodem i kolejnością |
| ProductType | Rozszerzalny słownik typów produktów, bez enuma |
| Project | Brief klienta, numer, kraj, PM, status i daty |
| ProjectProduct | Produkt, SKU, ilość, estymacje i bieżący status review |
| ProductReview | Oddzielny historyczny zapis decyzji i jej autora |
| AuditLog | Historyczne stare/nowe wartości pól i autor zmiany |

Relacje 1:N:
- Country → Projects; AppUser → Projects (ProjectManager).
- Project → ProjectProducts; ProductType → ProjectProducts.
- ProjectProduct → ProductReviews; AppUser → ProductReviews (Reviewer).
- AppUser → AuditLogs (ChangedByUser).

Wszystkie FK mają DeleteBehavior.NoAction. Usuwanie powiązanych użytkowników,
słowników, projektów i produktów jest blokowane, zamiast usuwać historię.
Słowniki i użytkownicy powinny być dezaktywowane przez IsActive.
AuditLog.EntityType + EntityId to celowo logiczne odniesienie do różnych tabel,
bez polimorficznego FK. Nie wdrożono jeszcze automatycznego audytowania.

Unikalne indeksy: DomainLogin (bez rozróżniania wielkości liter), ProjectNumber,
Country.Code oraz ProductType.Name. Quantity to int (liczba sztuk), musi być > 0.
EstimatedValue to decimal(18,2), >= 0; EstimatedMargin to decimal(5,2), 0–100%.
Marża 28% jest zapisywana jako 28.00. SKU ma limit 100 znaków, Customer 200,
komentarze review 2000. Odrzucenie wymaga niepustego komentarza także w SQL.
Ograniczenia SQL uzupełnią walidację przyszłych ViewModels.

Enumy mają jawne wartości, Display Names i są zapisywane jako nazwy tekstowe:
AppRole, ProjectStatus, FormulaStatus, ProductReviewStatus, ReviewDecision,
AuditChangeType. Nowy projekt ma Draft, produkt Pending (również default SQL).
Nowe stany wymagają migracji ograniczeń CHECK; logika przejść powstanie
w serwisach kolejnych etapów. Nie należy zmieniać nazw istniejących enumów
bez migracji danych.

Wszystkie timestampy używają DateTimeOffset i kolumn datetimeoffset.
Konwerter EF normalizuje wartości do UTC, także SubmittedAtUtc nullable.
Pola mają początkową wartość UTC; przyszłe serwisy aktualizujące encje muszą
ustawiać UpdatedAtUtc. Nie ma jeszcze automatycznej historii ani serwisu edycji.

## Numeracja projektów

IProjectNumberGenerator / ProjectNumberGenerator pobiera atomowo NEXT VALUE FOR
dbo.ProjectNumberSequence (bigint, bez cyklu), z CancellationToken.
Rok pochodzi z TimeProvider w UTC; format to PB-2026-0001.
Po 9999 liczba rośnie bez obcinania cyfr. Licznik jest globalny i NIE resetuje
się z początkiem roku. Wycofanie transakcji lub rezerwacja bez zapisu może
pozostawić lukę — numeracja nie jest ciągłym rejestrem księgowym.
Unikalny indeks ProjectNumber stanowi dodatkowe zabezpieczenie.
W ETAPIE 3 serwis tworzenia projektu wywoła generator i przypisze wynik;
samo new Project ani Add nie rezerwuje numeru. Nie używamy MAX(Id)+1.
Testy sprawdzają format i konfigurację sekwencji; nie wykonano jeszcze
testu współbieżności na fizycznym SQL Server.
[Zasady sekwencji SQL Server](https://learn.microsoft.com/sql/relational-databases/sequence-numbers/sequence-numbers?view=sql-server-ver17).

## Seed słowników

HasData w konfiguracjach: Countries ID 1–5: Poland/PL, Germany/DE, France/FR,
Sweden/SE, United Kingdom/GB. ProductTypes ID 1–4: Shampoo, Shower Gel,
Body Lotion, Conditioner. DisplayOrder wynosi 10, 20 itd.; IsActive=true.
Daty seedowanych typów to stałe 2026-01-01T00:00:00Z.
Seed wykonuje migracja tylko raz, a nie każdy start aplikacji.
HasData opisuje dane początkowe; nie należy później zmieniać tych samych
rekordów w seedzie, jeśli administrator zarządza już nimi w bazie.
Nie seedujemy użytkowników ani uprawnień.

## EF Core migrations i tworzenie bazy

Narzędzie dotnet-ef 10.0.12 jest przypięte w .config/dotnet-tools.json.
Migracja 20260917070205_InitialBusinessSchema jest już w Data/Migrations.
Nie generuj jej ponownie. Poniższa kontrola i eksport SQL działają offline:

```powershell
.\dotnet.ps1 ef migrations has-pending-model-changes --project src/PrivateBrandsPortal.Web
.\dotnet.ps1 ef migrations script --idempotent --project src/PrivateBrandsPortal.Web --output artifacts/InitialBusinessSchema.sql
```

Po potwierdzeniu dokładnego Server, uprawnień Windows, certyfikatu SQL
i zapisaniu connection stringa w User Secrets można wykonać:

```powershell
.\dotnet.ps1 ef database update --project src/PrivateBrandsPortal.Web --startup-project src/PrivateBrandsPortal.Web
```

database update utworzy bazę, jeśli konto ma uprawnienia; alternatywnie bazę
tworzy administrator i nadaje odpowiednie uprawnienia. Przed komendą zweryfikuj,
że connection string wskazuje PrivateBrandsPortal_DEV.
W produkcji migracje stosuje się kontrolowanie po przeglądzie i backupie,
odrębnym kontem wdrożeniowym, a nie automatycznie z konta aplikacji.
Po aktualizacji uruchom w SSMS scripts/verify-dev-schema.sql, aby sprawdzić
tabele, wpis migracji, 5 krajów, 4 typy produktów, sekwencję i FK.
Na razie tych operacji na serwerze nie wykonano. Nie używamy EnsureCreated,
SQLite ani automatycznego update podczas startu.

## Pierwszy Admin development

Po utworzeniu bazy administrator SQL może uruchomić w SSMS lokalną kopię
scripts/bootstrap-dev-admin.sql, zastępując dwa placeholdery zatwierdzonym
kontem DOMAIN\username i nazwą wyświetlaną. Skrypt wymaga bazy
PrivateBrandsPortal_DEV, używa transakcji i odmawia zmian, jeśli istnieje już
wskazane konto lub dowolny Admin. Nie podnosi uprawnień istniejącego użytkownika.
Nie zapisuj rzeczywistego loginu w repozytorium. Skrypt nie został wykonany
i nie jest mechanizmem automatycznego tworzenia użytkowników produkcyjnych.
Utworzenie profilu nie włącza jeszcze ekranów administracyjnych: mapowanie
aktywnego AppUser na claim roli zostanie podłączone przed operacjami biznesowymi.

## Uwierzytelnianie i autoryzacja

Negotiate działa z Kestrelem; pod IIS wykorzystuje uwierzytelnianie serwera.
Fallback policy wymaga zalogowania na wszystkich stronach. Wyjątkiem jest
bezpieczna strona błędu; pliki statyczne nie zawierają danych użytkowników.

PortalAuthorization definiuje ReviewProjects (Manager, Admin) i
AdministerPortal (Admin), wykorzystując oddzielny claim privatebrands:role.
Nie przypisujemy ról automatycznie i nie utożsamiamy grup AD z rolami aplikacji.
Dlatego obecnie obok konta widnieje Role not assigned.
CurrentUserService centralizuje DomainLogin i IsAuthenticated; odczyt tożsamości
nie wymaga zapytania SQL. Model AppUser jest gotowy, ale ETAP 2 nie podłącza
jeszcze mapowania kont do uprawnień i nie nadaje claimów automatycznie.
W kolejnych etapach aktywny AppUser będzie źródłem uprawnień; nieaktywne
i niezarejestrowane konta nie mogą uzyskać dostępu do operacji biznesowych.

Informacyjne szkielety Approvals i Administration są teraz dostępne wszystkim
uwierzytelnionym. Przed dodaniem danych i operacji należy nałożyć odpowiednie
polityki na endpointy i kontrolować widoczność menu. Ukrycie linku nie stanowi
autoryzacji. Nie istnieje development bypass ani testowy login w projekcie Web.

MVC globalnie sprawdza antiforgery dla metod modyfikujących dane.
Przyszłe formularze Razor muszą korzystać z Form Tag Helper / AntiForgeryToken,
ViewModels i walidacji po stronie serwera.
Globalna obsługa wyjątków pokazuje jedynie komunikat i identyfikator zgłoszenia;
własny ILogger zapisuje typ błędu i identyfikator, bez treści requestu,
wyjątku czy SQL. Surowe logowanie middleware wyjątków wyłączono celowo.

## Struktura

```text
PrivateBrandsPortal.sln
global.json
NuGet.Config
dotnet.ps1
.config/dotnet-tools.json
src/PrivateBrandsPortal.Web/
  Configuration/     konfiguracja EF i polityk
  Controllers/       HTTP i wybór widoków
  Data/              ApplicationDbContext i konwerter UTC
    Configurations/  siedem konfiguracji IEntityTypeConfiguration
    Migrations/      InitialBusinessSchema i snapshot
  Interfaces/        kontrakty serwisów
  Models/
    Entities/        siedem encji biznesowych
    Enums/           role, statusy i rodzaje zmian
  Services/          użytkownik, numeracja i obsługa błędów
  ViewModels/        modele widoków i przyszłych formularzy
  Views/             Razor i wspólny layout
  wwwroot/           lokalny Bootstrap, CSS i zasoby
  Properties/        profile uruchamiania
  web.config         Windows Authentication dla IIS
tests/PrivateBrandsPortal.Tests/
  FoundationTests.cs
  BusinessModelTests.cs
scripts/
  bootstrap-dev-admin.sql
  verify-dev-schema.sql
```

Bez Repository Pattern, osobnego API, SPA i dodatkowych bibliotek projektowych.
W kolejnych etapach logika tworzenia, submit, review, audit i wyliczania statusu
trafi do serwisów. Daty biznesowe będą przechowywane w UTC.

## Testy

38 testów: 14 testów fundamentu oraz 24 testy modelu i usług.
Zachowano odrzucanie anonimowych żądań, renderowanie czterech stron bez bazy,
provider SQL Server, polityki ról (w tym brak uprawnień wynikających z grupy
Windows o nazwie Admin), bezpieczną i niecache'owaną stronę błędu.
Test modelu z ETAPU 1 sprawdza teraz obecność 7 encji zamiast pustego schematu.
Nowe testy obejmują FK i brak cascade delete, relację Project → Products,
status domyślny, indeksy, precyzję decimal, deterministyczny seed, sekwencję,
format i granice numeracji, UTC, CurrentUserService oraz nazwy statusów.
TestServer nie obsługuje kontekstu połączeń Negotiate, więc tylko projekt
testowy zastępuje uwierzytelnianie handlerem testowym.
Rzeczywiste SSO wymaga dodatkowego testu na Kestrelu / IIS z kontem domenowym.

```powershell
.\dotnet.ps1 test PrivateBrandsPortal.sln --logger "trx;LogFileName=stage2.trx" --results-directory artifacts/TestResults
```

Weryfikacja ETAPU 1: build bez błędów i ostrzeżeń; 14/14 testów;
Kestrel wystartował, anonimowy HTTP 401 + Negotiate, Windows SSO HTTP 200.
Dashboard sprawdzono w przeglądarce desktop i przy szerokości 390 px.
Nie testowano połączenia SQL ani wdrożenia na docelowym IIS.

## Przygotowanie IIS

1. IT instaluje IIS z rolą Windows Authentication oraz Hosting Bundle .NET 10 x64.
   Po instalacji Hosting Bundle należy ponownie uruchomić IIS.
2. Przygotuj odrębną pulę aplikacji (No Managed Code, 64-bit) i konto usługi
   z minimalnymi uprawnieniami do plików oraz docelowej bazy.
   Włącz Load User Profile i zapewnij trwałe, chronione klucze Data Protection.
   Nie umieszczaj ich w folderze nadpisywanym podczas publikowania.
3. Publikuj lokalnie:

   ```powershell
   .\dotnet.ps1 publish src/PrivateBrandsPortal.Web -c Release -o artifacts/publish
   ```

4. Przenieś zawartość artifacts/publish do folderu witryny na 192.168.1.170.
   SDK generuje konfigurację ASP.NET Core Module w publikowanym web.config.
   Ustaw Production oraz bezpieczny connection string produkcyjny.
5. Windows Authentication: Enabled. Anonymous Authentication: Disabled.
   Projekt zawiera te ustawienia w web.config. Jeśli IIS zgłosi 500.19 z powodu
   zablokowanej sekcji, administrator musi dopuścić konfigurację dla tej witryny.
6. Utwórz wewnętrzny rekord DNS portalmp wskazujący 192.168.1.170 i binding HTTPS
   na porcie 443 dla portalmp. Certyfikat z firmowego CA musi obejmować tę nazwę
   i być zaufany na komputerach pracowników. AllowedHosts zawiera portalmp;
   przy innej nazwie / FQDN zaktualizuj listę.
7. IT konfiguruje Local Intranet / politykę SSO przeglądarek i w razie potrzeby
   poprawny, unikalny SPN HTTP dla konta obsługującego witrynę. Nie rejestruj
   SPN bez sprawdzenia właściciela i istniejących wpisów.
8. Ogranicz dostęp zaporą do sieci firmowej. Zweryfikuj z komputera domenowego
   https://portalmp/, brak monitu logowania, tożsamość użytkownika oraz blokadę
   anonimowych żądań. SQL Express musi mieć skonfigurowany właściwy endpoint
   TCP i reguły zapory; nie otwieraj portów na podstawie założonej nazwy instancji.

Na tym etapie nie wykonano zmian DNS, IIS ani SQL na serwerze firmowym.

Dokumentacja Microsoft:
- [Windows Authentication](https://learn.microsoft.com/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0)
- [IIS hosting](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
- [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)

## Następny etap — wyłącznie po poleceniu

ETAP 3: wizard Brief → Products → Summary, ViewModels z walidacją,
serwis tworzenia projektu z generatorem numeru i Save Draft.
Przed operacjami biznesowymi trzeba uruchomić bazę i podłączyć kontrolowane
mapowanie aktywnego AppUser do ról. Submit i decyzje Managera pozostają
w późniejszych etapach. ETAPU 3 jeszcze nie rozpoczęto.

