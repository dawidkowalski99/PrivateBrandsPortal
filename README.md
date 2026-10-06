# Private Brands Portal

Wewnętrzny portal działu Marek Prywatnych: ASP.NET Core MVC, Razor Views,
EF Core i SQL Server, Windows Authentication, docelowo IIS w sieci firmowej.
Interfejs jest po angielsku zgodnie z nazwami ekranów i pól w wymaganiach.

## Stan: ETAP 4 — Manager Approval workflow

Gotowe: solution, projekt MVC .NET 10, ApplicationDbContext, konfiguracja SQL,
Windows SSO, polityki autoryzacji, responsywny sidebar/topbar, Dashboard oraz
moduły Projects i Approvals oraz szkielet Administration. Zasoby Bootstrap
są lokalne; aplikacja nie potrzebuje CDN. Własny CSS nie wymaga procesu npm.

Dodano siedem encji biznesowych, osobne konfiguracje EF, migrację
InitialBusinessSchema, deterministyczny seed słowników i generator numerów.
Projects obsługuje wizard Brief → Products → Summary → Save Draft,
szczegóły, listę własnych projektów, Edit Draft oraz Submit. Approvals obsługuje decyzje Managera i historię zmian. Nie ma jeszcze CRUD Administration. Kreski na Dashboardzie pozostają
informacyjnym szkieletem, a nie rzeczywistymi licznikami.
Nie wykonujemy migracji ani połączeń SQL automatycznie podczas startu.
PrivateBrandsPortal_DEV istnieje; zastosowano InitialBusinessSchema i zweryfikowano
5 krajów, 4 typy produktów oraz sekwencję numeracji. Połączenie pozostaje w
User Secrets, poza repozytorium. ETAP 3 nie zmienia schematu ani migracji.

## Wizard i zapis Draftu

WizardStore przechowuje w pamięci serwera wyłącznie DTO, bez encji EF.
Każdy wizard ma losowy token, właściciela AppUser i numer rewizji.
Oddzielne kreatory mają oddzielne tokeny; otwarcie tego samego tokenu w dwóch
kartach podlega kontroli rewizji i blokadzie operacji dla tego wizardu.
Token nie zastępuje autoryzacji: każde żądanie sprawdza właściciela.

Brief oraz Add/Edit/Remove produktu korzystają z POST + antiforgery
i przekierowania po sukcesie. Odświeżenie zachowuje zaakceptowany stan,
ale nie niezapisane znaki w formularzu. Limit to 500 produktów w wizardzie,
1000 aktywnych wizardów w procesie, 2 godziny bezczynności i maksymalnie 8 godzin.
Restart lub usunięcie z cache traci niezapisany stan; UI pokazuje komunikat
Workspace unavailable. Zapisane Drafty można ponownie otworzyć z SQL.
Przy skalowaniu na wiele procesów potrzebny będzie wspólny magazyn stanu;
obecna wersja jest przeznaczona dla jednego procesu aplikacji.

ProjectService nadaje numer dopiero przy pierwszym Save Draft poprzez
istniejący IProjectNumberGenerator i dbo.ProjectNumberSequence.
Projekt i wszystkie produkty zapisuje w jednej transakcji SQL.
Powtórzenie Save tego samego aktywnego wizardu kieruje do zapisanego projektu.
Edycja zachowuje numer i identyfikatory istniejących produktów; UpdatedAtUtc
jest porównywane w warunkowym UPDATE w tej samej transakcji. Nieaktualny
wizard nie nadpisuje nowszego Draftu. To nie jest jeszcze interfejs rozwiązywania
konfliktów — należy ponownie otworzyć Draft.

ProjectService wymaga aktywnego ProjectManagera i filtruje każdy odczyt,
edycję i zapis po ProjectManagerId. Manager/Admin nie otrzymują w tym etapie
domyślnego dostępu do projektów innych osób. ReviewStatus pozostaje Pending;
projekt z danymi review nie może być zmieniany tą ścieżką.

Formularze korzystają z ViewModels, walidacji serwerowej i klienckiej.
DecimalModelBinder centralnie przyjmuje przecinek lub kropkę, bez separatorów
tysięcy. UI formatuje liczby według pl-PL; pieniądze w PLN, maks. 2 miejsca
dziesiętne, marża 0–100%. Wyświetlane daty są jawnie oznaczone UTC.

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
Endpoint DEV został potwierdzony i skonfigurowany w User Secrets.
Na nowym komputerze uzyskaj od IT ten sam endpoint i uprawnienia Windows;
nie zakładaj SQLEXPRESS. Dla certyfikatu należy użyć zgodnej nazwy DNS.

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
W ETAPIE 3 serwis tworzenia projektu wywołuje generator i przypisuje wynik;
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
Migrację i weryfikację wykonano na DEV. Nie używamy EnsureCreated,
SQLite ani automatycznego update podczas startu.

## Pierwszy Admin development

Po utworzeniu bazy administrator SQL może uruchomić w SSMS lokalną kopię
scripts/bootstrap-dev-admin.sql, zastępując dwa placeholdery zatwierdzonym
kontem DOMAIN\username i nazwą wyświetlaną. Skrypt wymaga bazy
PrivateBrandsPortal_DEV, używa transakcji i odmawia zmian, jeśli istnieje już
wskazane konto lub dowolny Admin. Nie podnosi uprawnień istniejącego użytkownika.
Nie zapisuj rzeczywistego loginu w repozytorium. Skrypt nie został wykonany
i nie jest mechanizmem automatycznego tworzenia użytkowników produkcyjnych.
Utworzenie profilu Admin nie włącza ekranów administracyjnych ani nie nadaje
dostępu ProjectManagera — ETAP 3 obejmuje wyłącznie własne Drafty ProjectManagera.

## Uwierzytelnianie i autoryzacja

Negotiate działa z Kestrelem; pod IIS wykorzystuje uwierzytelnianie serwera.
Fallback policy wymaga zalogowania na wszystkich stronach. Wyjątkiem jest
bezpieczna strona błędu; pliki statyczne nie zawierają danych użytkowników.

PortalAuthorization definiuje ReviewProjects (Manager, Admin) i
AdministerPortal (Admin), odczytując role z aktywnego AppUser. ManagerReview dopuszcza wyłącznie Managera.
Nie utożsamiamy grup AD z rolami aplikacji. CurrentUserService centralizuje
DomainLogin i IsAuthenticated. AppUserService mapuje zalogowane konto na AppUser.
Wyłącznie w Development brakujący profil tworzy się jako aktywny ProjectManager;
unikalny indeks loginu i obsługa kolizji chronią przed duplikatami.
Production odmawia dostępu nieznanemu profilowi. Nieaktywne konta nie są
reaktywowane ani awansowane automatycznie. Brak loginów i haseł w kodzie.
ProjectAccessFilter oraz ProjectService sprawdzają aktualny profil z bazy,
a nie tylko widoczność przycisków. Sidebar pokazuje rolę odczytanego profilu.

Approvals wymaga polityki ManagerReview. Informacyjny szkielet Administration pozostaje dostępny uwierzytelnionym, bez operacji administracyjnych. Ukrycie linku nie stanowi autoryzacji. Nie istnieje development bypass ani testowy login w projekcie Web.

MVC globalnie sprawdza antiforgery dla metod modyfikujących dane.
Formularze Razor korzystają z Form Tag Helper / AntiForgeryToken,
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

64 testy: zachowane 38 testów fundamentu/modelu oraz 26 testów ETAPU 3.
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

WizardTests sprawdzają walidację, liczby, izolację stanu i CSRF.
ProjectSqlTests korzystają z rzeczywistego SQL Server przez User Secrets
projektu Web. Wymagają bazy PrivateBrandsPortal_DEV i odmawiają pracy z inną
nazwą bazy. Tworzą unikalne profile PORTALTEST i projekty, po czym usuwają
wyłącznie własne rekordy. Nie modyfikują słowników ani schematu.
Sprawdzają mapowanie profilu, odmowę provisioningu Production, nieaktywność,
zapis wielu produktów, ownership/IDOR, edycję, konflikt wersji i rollback.
Testy zużywają wartości sekwencji — luki w numeracji są oczekiwane.

```powershell
.\dotnet.ps1 test PrivateBrandsPortal.sln --logger "trx;LogFileName=stage3.trx" --results-directory artifacts/TestResults
```

Weryfikacja ETAPU 1: build bez błędów i ostrzeżeń; 14/14 testów;
Kestrel wystartował, anonimowy HTTP 401 + Negotiate, Windows SSO HTTP 200.
Dashboard sprawdzono w przeglądarce desktop i przy szerokości 390 px.
Nie testowano połączenia SQL ani wdrożenia na docelowym IIS.

Weryfikacja ETAPU 3 (17.09.2026): build 0 błędów / 0 ostrzeżeń,
64/64 testy, w tym integracja SQL Server. Manualnie sprawdzono Windows SSO,
Brief → Products → Summary, dodawanie / edycję / usuwanie kart, Back,
odświeżenie, Save Draft, Details oraz listę projektów na desktop i mobile.
Pozostawiono DEV Draft PB-2026-0005 (Demo Customer, Germany): dwa produkty,
245 000 PLN, oba Pending. Edit Draft zmienił ilość Shampoo z 20 000 na
22 000, zachowując numer projektu i aktualizując UpdatedAtUtc.
Schemat i InitialBusinessSchema pozostały bez zmian; nowa migracja nie była potrzebna.

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

Nie wykonano zmian DNS ani IIS. Zmiany SQL dotyczą wyłącznie bazy DEV.

Dokumentacja Microsoft:
- [Windows Authentication](https://learn.microsoft.com/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0)
- [IIS hosting](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
- [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets?view=aspnetcore-10.0)

## Następny etap — wyłącznie po poleceniu

Kolejne działy, ponowne wysyłanie odrzuconych produktów i powiadomienia wymagają osobnego polecenia.



## Manager Approval — ETAP 4

Submit na Project Details wymaga potwierdzenia oraz POST z antiforgery.
ApprovalService sprawdza właściciela, aktywnego ProjectManagera, status Draft,
wersję projektu, minimum jeden poprawny produkt oraz aktywne słowniki.
Transakcja ustawia AwaitingManagerReview, SubmittedAtUtc i UpdatedAtUtc,
zachowuje produkty Pending oraz zapisuje AuditLog ProjectSubmitted.
Po Submit Edit Draft i operacje starego, otwartego wizardu są blokowane na backendzie.

Windows/AD odpowiada za authentication. Wszystkie polityki biznesowe odczytują
aktywnego AppUser; nie ufają przychodzącym claims ról ani grupom AD.
W Production ManagerReview wymaga dokładnie roli Manager. Wyjątek Development opisano poniżej. Istniejące polityki ReviewProjects
(Manager/Admin) i AdministerPortal (Admin) zachowują zakres ról.
AppUser jest cache'owany wyłącznie w bieżącym żądaniu; zmiana roli w bazie
obowiązuje od kolejnego requestu. Sidebar pokazuje DisplayName, a przy jego
braku DomainLogin. Approvals badge wykonuje jeden Count na żądanie Managera lub uprawnionego użytkownika demo.

Kolejka zawiera AwaitingManagerReview i PartiallyReviewed z Pending,
posortowane według SubmittedAtUtc, potem Id. Manager widzi wszystkie takie
projekty; przydział do konkretnego Managera nie jest częścią tego etapu.
Approve ma ekran potwierdzenia; Reject wymaga niepustego komentarza (do 2000 znaków).
Edit & Approve udostępnia Product Type, SKU, Quantity, Estimated Value,
Estimated Margin i Formula, z tą samą walidacją liczb co wizard.

Każda decyzja tworzy nowy ProductReview z autorem, UTC i komentarzem.
Każde faktycznie zmienione pole tworzy osobny AuditLog ManagerEdit
z pełnymi OldValue/NewValue. W SQL liczby audytu są zapisane invariant;
UI formatuje je lokalnie i pokazuje przekreśloną starą wartość, nową wartość,
autora oraz czas. ProductTypeId przechowuje stabilne ID; UI rozwiązuje nazwę
ze słownika (także dla nieaktywnych typów). Nie nadpisujemy wcześniejszej historii.

Decyzja, zmiany danych, audyt i status projektu są jedną transakcją.
Porównanie UpdatedAtUtc i warunkowy UPDATE projektu blokują równoległe decyzje,
a wersja i status produktu blokują powtórne/stare decyzje. Konflikt wymaga
ponownego otwarcia review; dane nie są nadpisywane po cichu.

Status centralnie wylicza ApprovalService.CalculateStatus:
- wszystkie Pending: AwaitingManagerReview;
- część Pending: PartiallyReviewed;
- wszystkie Approved/EditedAndApproved: Approved;
- wszystkie Rejected: Rejected;
- zakończone mieszane decyzje: PartiallyApproved.

Migracja 20260919144934_ManagerApprovalWorkflow rozszerza wyłącznie dwa
ograniczenia CHECK (Projects.Status i AuditLogs.ChangeType). InitialBusinessSchema
pozostaje bez zmian. Migracja została zastosowana w DEV. Cofnięcie jej wymaga
uprzedniego rozwiązania danych używających nowych statusów — Down nie kasuje historii.

DEV helper: scripts/set-dev-user-role.sql. W lokalnej kopii podaj DomainLogin
istniejącego aktywnego AppUser i rolę Manager lub ProjectManager. Skrypt odmawia
pracy poza PrivateBrandsPortal_DEV, dla nieistniejącego konta i dla Admina.
Nie wpisuj rzeczywistych loginów do repozytorium. Nie twórz równoległych ról w AD.

Manualny test 19–20.09.2026: PB-2026-0052 (Approval Demo, Sweden).
Shampoo Approved; Body Lotion EditedAndApproved: Quantity 5000 → 6000,
EstimatedMargin 25 → 27.5, dokładnie dwa wpisy ManagerEdit.
Dodatkowy Conditioner odrzucony z komentarzem; pusta przyczyna była blokowana.
Wynik PartiallyApproved, Reviewed 3/3, projekt znika z kolejki.
Rolę konta użytego do testu przywrócono do ProjectManager.
PB-2026-0005 pozostaje przykładowym Draftem i nie został zmieniony.

Końcowa weryfikacja ETAPU 4: build 0 błędów / 0 ostrzeżeń, 91/91 testów.
Sprawdzono desktop i mobile 390 px, SSO, odczyt historii i blokady HTTP:
Projects/Edit po Submit 404, Approvals dla ProjectManagera 403. Logi bez nowych błędów.
Strona błędu pomija zapytania badge do SQL, także dla zalogowanego użytkownika.

## Demo — jedno konto Windows

Dodatkowe uprawnienie review jest przeznaczone wyłącznie do prezentacji lokalnej.
W katalogu projektu skonfiguruj User Secrets (przykładowy login zastąp swoim):

```powershell
.\dotnet.ps1 user-secrets set "DemoAccess:Enabled" "true" --project src/PrivateBrandsPortal.Web
.\dotnet.ps1 user-secrets set "DemoAccess:UserDomainLogin" "DOMAIN\username" --project src/PrivateBrandsPortal.Web
```

Uruchom ponownie aplikację w Development. `DemoAccess` wymaga jednocześnie:
Development, Enabled=true, uwierzytelnionego Windows usera zgodnego z konfiguracją
oraz aktywnego profilu AppUser o tym samym DomainLogin i roli ProjectManager.
Porównanie loginu nie rozróżnia wielkości liter. Brak konfiguracji oznacza brak wyjątku.
Production, Staging i inne środowiska całkowicie ignorują to uprawnienie nawet przy Enabled=true.
Login i konfiguracja lokalna pozostają w User Secrets, poza repozytorium.

Ta sama kontrola działa w politykach i ApprovalService, także dla badge kolejki.
Nie nadaje Admina, nie zmienia AppRole ani żadnej roli w SQL. Demo user zachowuje
ograniczenie Projects do własnych projektów, a w Approvals uzyskuje standardowy
zakres Managera (wszystkie projekty oczekujące na review). Nie uruchamiaj skryptu
zmiany roli do tej prezentacji. Zwykły Manager działa bez konfiguracji demo;
zwykły ProjectManager nadal nie ma dostępu do Approvals.

Wyłączenie demo (następnie restart aplikacji):

```powershell
.\dotnet.ps1 user-secrets set "DemoAccess:Enabled" "false" --project src/PrivateBrandsPortal.Web
```

W demonstracji z 28.09.2026 proces kończył się po review na Approved, Rejected lub PartiallyApproved.
Nie ma dalszego routingu, InProgress ani Department Tasks. Zakończone projekty
znikają z aktywnej kolejki bez usuwania historii. Nie była potrzebna migracja.

Project Details i zakończony Manager Review pokazują Decision Summary: numer,
decyzję, liczbę produktów oraz niezależne liczniki Approved, Edited & Approved,
Rejected. Approved nie obejmuje produktów EditedAndApproved. Statusy końcowe mają
zielone, czerwone i bursztynowe badges. Karty zachowują dane, komentarze, autora,
czas UTC oraz rzeczywiste różnice z audytu.

Weryfikacja demo 28.09.2026, jedno konto Windows bez zmiany roli:
- PB-2026-0144: Approved, 2 produkty; Approved=1, EditedAndApproved=1, Rejected=0.
  Body Lotion: Quantity 10000 → 12000, SKU MEETING-BL-001 → MEETING-BL-002;
  dokładnie dwa wpisy audytu. Projekt zachowany jako dane DEV do prezentacji.
- PB-2026-0145: PartiallyApproved, 2 produkty; Approved=1, EditedAndApproved=0,
  Rejected=1, komentarz odrzucenia zachowany. Oba projekty zniknęły z kolejki.
- SSO, Projects, Approvals, Submit, decyzje, odczyt historii i Decision Summary
  zweryfikowane w przeglądarce; podsumowanie sprawdzone także przy szerokości 390 px.
- SQL potwierdza niezmienioną rolę ProjectManager. PB-2026-0005 pozostaje Draftem.
- Build: 0 błędów / 0 ostrzeżeń; 105/105 testów (91 istniejących i 14 nowych).
  Testy SQL współdzielą bazę DEV i wykonują się w jednej kolekcji bez równoległego
  sprzątania danych innych testów. Testy wersji i konfliktów edycji pozostają aktywne.

## Reporting i Commercial Workflow

Po manager review decyzja projektu pozostaje Approved, Rejected lub PartiallyApproved.
Dalszy proces jest zapisywany osobno jako `ProjectProduct.CommercialStatus`:
PriceOfferSubmitted → OfferUnderNegotiation → CustomerApprovedOrder →
ImplementationIntoProduction → SalesAndDelivery. Dostępny jest także
CustomerNotApproved. PM może wybrać status odpowiedni do bieżącej sytuacji;
system nie wymusza przejścia przez wszystkie pośrednie statusy.

Tylko aktywny ProjectManager będący właścicielem może zmieniać status SKU
Approved lub EditedAndApproved. Null wyświetla się jako **Awaiting PM update**;
nie jest dodatkową opcją dropdown. Pending i Rejected nie mają procesu komercyjnego.
SalesAndDelivery kończy SKU i blokuje dalsze zmiany. Każda rzeczywista zmiana
zapisuje stary/nowy status, autora i UTC w AuditLogs. Ponowne wybranie tego samego
statusu nie tworzy wpisu. Aktualizacja, audyt i archiwizacja są jedną transakcją.
Porównanie wersji projektu blokuje zapis starego formularza oraz równoległe zmiany.

Projekt trafia do **Archive**, jeżeli ma przynajmniej jeden zaakceptowany SKU,
wszystkie zaakceptowane SKU mają SalesAndDelivery, a pozostałe są Rejected.
Pending blokuje archiwizację. Same Rejected oraz CustomerNotApproved nie kończą
projektu. `ArchivedAtUtc` jest ustawiane tylko raz. Projekt znika z aktywnej listy
Projects, ale zachowuje szczegóły, decyzje i historię; archiwum obejmuje własne
projekty PM. Raporty mogą obejmować zarówno aktywne, jak i archiwalne projekty.

### Słowniki i istniejące dane

Nowe produkty wymagają aktywnej **ProductCategory** i tekstowej **Subcategory**
(trim, maksymalnie 100 znaków). Kategorie początkowe: Hair Care, Body Care,
Face Cream. Tabela ProductTypes i powiązania historyczne pozostają zachowane.
Migracja kopiuje nazwę istniejącego ProductType do Subcategory, bez zgadywania
kategorii. Historyczny brak kategorii jest oznaczony jako legacy. Stary Draft
można wysłać do review; przy edycji produktu należy wskazać kategorię.

Nowe odrzucenie wymaga aktywnego **RejectionReason**. Seed zawiera:
Price barrier; Lack of technology; Inability to meet quality requirements;
Not meeting the NPD; Not meeting the MOQ; Project with low potential;
Price too high; Formulation quality below expectations; Lack of information; Other.
Other wymaga komentarza także po zmianie nazwy słownikowej (`RequiresComment`).
Dla pozostałych powodów komentarz jest opcjonalny, chyba że Admin włączy obowiązek.
Raz włączony obowiązek pozostaje aktywny. Nazwa powodu jest utrwalana przy decyzji;
zmiana nazwy słownika nie zmienia historii. Historyczne odrzucenia bez słownika
zachowują swój komentarz.

Administration udostępnia Adminowi dodawanie, edycję, sortowanie i dezaktywację
Product Categories oraz Rejection Reasons. Nie ma fizycznego usuwania wpisów.
Dezaktywacja nie usuwa powiązań historycznych. Users, Countries i legacy Product
Types zachowują dotychczasowe strony bazowe. DemoAccess nie nadaje uprawnień Admina.

### Raporty i CSV

Reports jest dostępne aktywnym ProjectManagerom, Managerom i Adminom i obejmuje
portfel wszystkich PM. To szerszy zakres od własnej listy Projects i Archive.
KPI: Projects, SKUs, Approved SKUs, Edited & Approved SKUs, Rejected SKUs oraz
Sales & Delivery SKUs. Approved nie zawiera EditedAndApproved. Projects liczy
unikalne ProjectId reprezentowane przez SKU spełniające filtry; SKUs liczy wiersze
produktów. Te same definicje obowiązują w agregacji per PM.

Filtry: PM, Created Date From/To (UTC, obie granice dni włącznie), Country,
Project Status, Manager Decision, Commercial Status, Product Category,
Subcategory, Customer oraz Archived (All/Active/Archived). Historyczne nieaktywne
słowniki są dostępne do filtrowania. Filtrowanie i agregacje wykonuje SQL Server;
szczegóły są stronicowane po 50 SKU. Tekstowe fallbacki legacy mają jawną kolację
w zapytaniu, aby nie zmieniać kolacji istniejących tabel i danych.

Export CSV używa dokładnie tych samych filtrów, eksportując wszystkie pasujące
SKU, nie tylko bieżącą stronę. Eksport jest strumieniowany, UTF-8 z BOM, separator
średnik, polski przecinek dziesiętny, cytowanie pól oraz zabezpieczenie przed
interpretowaniem tekstów użytkownika jako formuł arkusza. XLSX nie jest wdrożony.

### Migracja i weryfikacja

Nowa migracja: `20260930102447_ReportingAndCommercialWorkflow`.
Poprzednie migracje nie zostały zmienione. Po skonfigurowaniu connection stringa
w User Secrets, przed uruchomieniem nowej wersji:

```powershell
.\dotnet.ps1 ef database update --project src/PrivateBrandsPortal.Web
.\dotnet.ps1 build
.\dotnet.ps1 test
```

Nie stosuj EnsureCreated ani ręcznego tworzenia tabel. Seed migracji nie dubluje
danych przy ponownym database update. Cofnięcie migracji jest blokowane, jeśli
spowodowałoby utratę nowych danych biznesowych. Przed migracją produkcyjną wykonaj
backup zgodnie z procedurą firmy i zatrzymaj wcześniejszą wersję aplikacji.

Testy obejmują transakcje SQL, ownership, konflikty wersji, historię, archiwizację,
słowniki, legacy, autoryzację HTTP, CSRF, filtry, agregacje, paginację i CSV.
Wymagają skonfigurowanej bazy DEV; tworzą i sprzątają własne izolowane rekordy.
Numery sekwencji zużyte przez testy nie są cofane.

Test ręczny 30.09.2026: **PB-2026-0361**, Analytics Demo, Germany, 2 SKU.
Shampoo Approved przeszedł pięć zmian aż do SalesAndDelivery; Body Lotion Rejected
z powodem Price too high i komentarzem. Projekt jest w Archive, ma pięć wpisów
historii komercyjnej i pozostaje jako dane DEV. Połączone filtry raportu zwracają
1 projekt / 1 SKU / 100000,00 PLN, zgodnie z pobranym CSV. Sprawdzono desktop i
mobile 390 px: szczegóły, historię PM, archiwum, filtry, KPI i tabele raportowe.
Formularze Admina są objęte testami HTTP i SQL; ręczny test wymaga konta Admin.
Końcowy build: 0 błędów / 0 ostrzeżeń. Testy: 145/145, bez pominiętych. Model EF zgodny z migracją. Porównanie SQL potwierdziło zachowanie 9 SKU wcześniejszych projektów demo (0005, 0052, 0144, 0145).

## SuperAdmin, permissions i administracja użytkowników

Migracja `20261001083808_UserPermissions` dodaje Permissions (unikalny Code),
AppUserPermissions (klucz złożony AppUserId + PermissionId) oraz rozszerza edycję
Countries o UpdatedAtUtc do wykrywania konfliktów. Poprzednie migracje pozostają
bez zmian. Historyczna rola Admin jest przekształcana w SuperAdmin; użytkownicy,
projekty i ich właściciele nie są usuwani. Nie dodano CanManageDictionaries.
Cofnięcie tej migracji wymaga jawnej procedury przeniesienia uprawnień, ponieważ
starsza wersja aplikacji inaczej autoryzuje dostęp.

Role biznesowe nadal oznaczają ProjectManager (własne projekty i commercial)
oraz Manager (review). SuperAdmin daje automatycznie pełne uprawnienia
administracyjne oraz dostęp do obu workflow (PM i Manager), bez pomijania Windows SSO ani
IsActive. Copy From i operacje edycji nadal respektują własność projektu. SuperAdmin ma globalne listy Projects i Archive oraz podgląd cudzych projektów tylko do odczytu. Nieaktywny profil nie ma dostępu do aplikacji. DemoAccess zachowuje
wyłącznie dotychczasowy wyjątek review w Development, bez obejścia permissions.

Cztery początkowe uprawnienia:
- MANAGE_USERS — Manage users;
- MANAGE_DICTIONARIES — Manage dictionaries (Countries, Product Categories, Rejection Reasons);
- VIEW_REPORTS — View reports;
- EXPORT_REPORTS — Export reports.

Zwykły użytkownik nie otrzymuje tych uprawnień automatycznie z roli.
Raporty wymagają VIEW_REPORTS: ProjectManager widzi wyłącznie własne projekty, a Manager i SuperAdmin portfel globalny.
EXPORT_REPORTS jest niezależne: chroni endpoint CSV i widoczność przycisku.
IPermissionService, policies i handler korzystają z profilu wraz z aktywnymi
przypisaniami odczytanego raz na request. Nieaktywne Permission nie daje dostępu.
SuperAdmin nie wymaga osobnych wpisów w tabeli łączącej.

Administration → Users oferuje wyszukiwanie po nazwie lub DomainLogin, listę
z rolą, aktywnością, uprawnieniami i timestampami UTC oraz Edit User.
MANAGE_USERS pozwala edytować zwykłe konta i nadawać im dodatkowe uprawnienia.
Wyłącznie SuperAdmin może nadać/odebrać rolę SuperAdmin lub edytować taki profil,
w tym jego aktywność i permissions. Delegowany administrator nie może nadać sobie
SuperAdmin. Zmiany roli, aktywności i przypisań zapisują AuditLogs.

Zapis użytkownika jest transakcyjny i wymaga zgodnej wersji UpdatedAtUtc.
Transakcyjna blokada SQL `PrivateBrandsPortal.UserAdministration` serializuje
zmiany dostępu; wewnątrz blokady ponownie sprawdzane są aktualne prawa autora,
wersja celu i istnienie innego aktywnego SuperAdmina. Backend odrzuca odebranie
roli lub dezaktywację ostatniego aktywnego SuperAdmina, także przy równoczesnych
żądaniach. Formularz nie jest jedynym zabezpieczeniem.

### Pierwszy SuperAdmin w DEV

W lokalnej kopii `scripts/set-dev-user-role.sql` wskaż zatwierdzony DomainLogin
istniejącego aktywnego AppUser i ustaw @Role = N'SuperAdmin'. Helper działa tylko
w PrivateBrandsPortal_DEV i odmawia bootstrapu, gdy aktywny SuperAdmin już istnieje.
Nie wpisuj loginu do repozytorium. Helper używa tej samej blokady co serwis Users.
Po bootstrapie zarządzaj dostępem przez Administration → Users. Nie używaj
helpera do obchodzenia ochrony ostatniego SuperAdmina.

Dnia 01.10.2026, po osobnym potwierdzeniu użytkownika, jego bieżący profil DEV
został ustawiony jako SuperAdmin. Pozostał aktywny; nie nadano zbędnych wierszy
permissions. W przeglądarce potwierdzono działanie Users i słowników oraz odrzucenie
prób dezaktywacji i odebrania roli ostatniemu aktywnemu SuperAdminowi.

### Copy From / Duplicate Product

W kroku Products przycisk Duplicate otwiera edytowalną kopię karty z nowym kluczem.
Copy From pozwala wyszukać produkt we własnych projektach (numer, klient, SKU;
maksymalnie 100 wyników). Backend zawsze sprawdza właściciela źródła oraz token
i wersję docelowego wizardu. POST jest chroniony antiforgery. Można kopiować także
z własnych projektów zakończonych; produkt docelowy zawsze powstaje w Drafcie.

Kopiowane są wyłącznie dane wejściowe: kategoria, subkategoria, ilość, wartość,
marża i formula. Nie przechodzą ID bazy, decyzje, commercial status ani historia.
Nieaktywna lub nieprzypisana kategoria wymaga ponownego wyboru. Użytkownik najpierw
przegląda/edytuje kopię, potem wybiera Save product; dopiero Save Draft utrwala
ją w bazie jako nowy produkt Pending. SKU jest zawsze puste i wymaga uzupełnienia. Źródło nie jest modyfikowane.
Test przeglądarkowy potwierdził Copy From i niezależną edycję Duplicate w wizardzie,
bez zapisywania dodatkowego projektu DEV.

Końcowa weryfikacja 01.10.2026: build 0 błędów / 0 ostrzeżeń, 164/164 testy bez pominiętych. Zachowano wszystkie 145 wcześniejszych testów z aktualizacją oczekiwań zmienionego modelu i autoryzacji. SQL potwierdził zachowane projekty demo i cztery seedowane permissions.

### SuperAdmin podczas prezentacji

Od 01.10.2026 SuperAdmin może korzystać z Projects, Approvals i Archive oraz
przeprowadzić cały scenariusz: Draft, Copy From / Duplicate, Submit, Manager Review,
commercial status i archiwizacja. Reguła WorkflowAccess jest wspólna dla filtrów,
polityk i serwisów. Nie zależy od DemoAccess ani od środowiska Development.
Projects, edycja Draft, kopiowanie, commercial i Archive nadal dotyczą własnych
projektów; kolejka review ma standardowy zakres Managera. Walidacja statusów,
autoryzacja właściciela, audyt i kontrola współbieżności pozostają aktywne.
Nieaktywny SuperAdmin nie ma dostępu. Zwykłe role nie otrzymują dodatkowych praw.
Zmiana nie wymaga migracji ani dodatkowych permissions na koncie SuperAdmina.

Weryfikacja rozszerzenia SuperAdmin: build 0 błędów / 0 ostrzeżeń, 168/168 testów. Windows SSO i strony Projects, Approvals, Archive sprawdzone w przeglądarce na rzeczywistym koncie SuperAdmin.

## Dashboard i transfer odpowiedzialności za projekt

Dashboard był statycznym szkieletem pierwszego etapu. Obecnie IDashboardService /
DashboardService pobiera agregaty i projekcje bezpośrednio z SQL przez EF Core,
AsNoTracking i Select, bez pobierania grafów produktów dla kart i bez N+1.
ProjectManager widzi tylko własne projekty; SuperAdmin widzi portfel globalny.
Pozostałe role nie otrzymują globalnego wglądu przez Dashboard.

| Karta | Reguła |
| --- | --- |
| My Active Projects / Active Projects | ArchivedAtUtc jest null i Status nie jest Rejected; wspólna definicja ProjectQueries.Active. Draft, Approved i PartiallyApproved mogą być aktywne. |
| Awaiting Approval | Liczba projektów AwaitingManagerReview lub PartiallyReviewed, niezależnie od liczby SKU. |
| Recently Changed | UpdatedAtUtc od teraz minus 7 dni do teraz, UTC; granica włączona. TimeProvider pozwala testować ten przedział. |
| Completed | ArchivedAtUtc nie jest null; samo Approved nie wystarcza. |
| Awaiting PM Update | Liczba SKU Approved lub EditedAndApproved bez CommercialStatus. Współdzielone ProjectService.AwaitingPmAsync i ProjectQueries.AwaitingPmUpdate. |

Recent Projects pokazuje do 6 kart, UpdatedAtUtc malejąco (Id rozstrzyga remis),
z numerem, klientem, krajem, PM, liczbą SKU, statusem i datą UTC. Pusty stan
pojawia się tylko przy rzeczywistym braku projektów. Link View projects prowadzi
do listy własnych projektów PM lub globalnej listy SuperAdmina. Awaiting PM Update otwiera listę
odpowiednich projektów (50 na stronę), z takim samym zakresem PM / SuperAdmin.
SuperAdmin może otworzyć cudzy projekt przez Dashboard/Project jako podgląd
tylko do odczytu. Projects/Details przekierowuje SuperAdmina do tego podglądu; nie rozszerza to prawa do Edit Draft ani
commercial — ich backend nadal sprawdza właściciela.

### Transfer project

Transfer jest dostępny wyłącznie dla aktywnego Managera lub SuperAdmina. Historyczne REASSIGN_PROJECTS nie daje prawa transferu PM i nie jest już dostępne do nadawania w Users.
SuperAdmin otrzymuje je automatycznie przez istniejący mechanizm permissions.
Przycisk Transfer project jest na szczegółach i globalnym podglądzie projektu.
Formularz wymaga innego aktywnego ProjectManagera lub SuperAdmina i powodu
(maksymalnie 1000 znaków). Zwykły Manager nie może być właścicielem docelowym.
Brak kandydatów wyłącza przycisk i wyświetla wyjaśnienie.

POST wymaga Windows Authentication, aktywnego profilu, uprawnienia i antiforgery.
ProjectTransferService ponownie odczytuje aktywność / prawa autora oraz rolę
i aktywność celu w transakcji RepeatableRead. Atomowy UPDATE sprawdza Id,
poprzedniego właściciela i wersję UpdatedAtUtc przesłaną przez formularz.
Konflikt zwraca „The project has changed. Reload it and try again.”; użytkownik
może przeładować formularz. Nie ma automatycznego nadpisywania cudzych zmian.

Zmieniają się wyłącznie ProjectManagerId i UpdatedAtUtc. Numer projektu,
status, dane produktów, decyzje, commercial i archiwizacja pozostają zachowane.
W tej samej transakcji powstaje AuditLog typu ProjectReassigned, pola ProjectManager:
stary / nowy PM (nazwa, login i ID), autor, czas UTC i Reason. Nieudany zapis
audytu cofa również właściciela i timestamp. Historia jest dostępna na szczegółach.
Reason jest opcjonalnym polem generycznego audytu, wymaganym przez serwis transferu;
nie kodujemy powodu w wartości nowego właściciela.

Po transferze stary PM traci Projects, bezpośredni URL i możliwość zapisu starego
wizardu. Nie usuwamy stanów z pamięci innych sesji; każda operacja i finalny zapis
ponownie sprawdzają ownership. Nowy PM może kontynuować aktualny stan workflow
lub otworzyć projekt w Archive. Reports agreguje po aktualnym ProjectManagerId,
więc projekt i SKU od razu przechodzą do nowego PM. Dashboard właścicieli zmienia
się po odczycie; globalna liczba projektów pozostaje taka sama. Recently Changed
może wzrosnąć, jeżeli transfer aktualizuje projekt niezmieniany przez ponad 7 dni.

### Migracja i weryfikacja

Migracja `20261005063153_DashboardAndProjectReassignment` dodaje permission,
nullable AuditLogs.Reason i rozszerza CHECK ChangeType o ProjectReassigned.
Wcześniejsze migracje nie zostały zmienione. Database update w DEV zakończył się
poprawnie. Cofnięcie migracji jest blokowane, jeśli istnieją powody / historia
transferów albo przypisania nowego permission, aby nie utracić historii i grantów.

Weryfikacja 05.10.2026: build 0 błędów / 0 ostrzeżeń, 197/197 testów.
Nowe testy obejmują zakres i granice czasu Dashboardu, globalny podgląd, prawa
transferu, IDOR i stary wizard, raporty, historię, zachowanie review / commercial /
archive, konflikt równoczesnych transferów i rollback przy błędzie audytu.
Testy SQL używają izolowanych profili PORTALTEST i usuwają tylko własne rekordy.

Na rzeczywistym koncie SuperAdmin potwierdzono SSO i zgodność Dashboardu z SQL:
Active 4, Awaiting Approval 0, Recently Changed 5, Completed 3, Awaiting PM Update
4 SKU. Sprawdzono Dashboard i formularz transferu na desktopie i mobile.
Ręczny transfer nie został wykonany, ponieważ DEV nie ma drugiego rzeczywistego
aktywnego PM. Nie tworzono konta domenowego ani dodatkowego projektu demonstracyjnego.

## Customers, Product Subcategories i zakres danych — 06.10.2026

Aktualne reguły zastępują wcześniejszy tekstowy wybór klienta i podkategorii.
Administration → Dictionaries zawiera Customers, Countries, Product Categories,
Product Subcategories i Rejection Reasons. Zarządzanie wymaga MANAGE_DICTIONARIES
lub aktywnej roli SuperAdmin. Wpisy można dodawać, edytować, wyszukiwać,
porządkować przez DisplayOrder i dezaktywować; nie ma operacji hard delete.

Customer ma Name (200 znaków, unikalne bez rozróżniania wielkości liter), opcjonalne
Code (50), IsActive, DisplayOrder i timestampy UTC. Zapis normalizuje białe znaki.
Nowy Brief wymaga aktywnego Customer. Project.CustomerId jest nullable dla danych
historycznych, a Project.Customer pozostaje snapshotem. Zmiana nazwy lub dezaktywacja
słownika nie przepisuje istniejących projektów. Zmiana wyboru klienta podczas edycji
Draftu zapisuje snapshot nowo wybranego wpisu. Nie wykonujemy automatycznego backfillu.

ProductSubcategory należy do jednej ProductCategory; Name (100 znaków) jest unikalne
w obrębie kategorii. Select zależy od wybranej kategorii, a jej zmiana czyści wybór.
Backend sprawdza istnienie, aktywność obu wpisów i ich relację. Nowe produkty wymagają
aktywnej podkategorii, także po Copy From / Duplicate. Istniejącej podkategorii nie
przenosimy między kategoriami: należy dodać nowy wpis. Słowniki nie mają seedów
z wymyślonymi klientami ani produktami.

ProjectProduct.ProductSubcategoryId jest nullable; Subcategory pozostaje snapshotem,
a ProductTypeId i fallback do ProductType.Name zachowują kompatybilność historyczną.
Edycja ilości lub ceny istniejącego produktu nie wymusza zmiany starego słownika i
nie aktualizuje snapshotu. Copy From / Duplicate zachowują identyfikatory wyboru oraz
liczby i formułę, ale czyszczą SKU, ID produktu, decyzje i historię. Nieaktywne
słowniki wymagają ponownego wyboru dla nowej kopii.

PM ma zakres własnych danych w Projects, szczegółach, edycji, commercial, Archive,
Dashboardzie, Awaiting PM Update, raportach i CSV. Zakres raportów jest nakładany
przed filtrami, agregacją, paginacją i eksportem; podanie cudzego ProjectManagerId
nie rozszerza dostępu. Manager i SuperAdmin mają globalny zakres raportów (z
zachowaniem VIEW_REPORTS / EXPORT_REPORTS). Wyjątek DemoAccess pozwala PM wykonywać
review wyłącznie własnych projektów. Transfer sprawdza rolę Manager / SuperAdmin
również wewnątrz transakcji; historyczny grant PM nie jest wystarczający.

Projects i Archive wyszukują po numerze, snapshocie Customer i SKU, po zastosowaniu
zakresu właściciela. SuperAdmin przeszukuje wszystkie projekty i archiwum. Dostęp do
cudzego szczegółu jest tylko do odczytu; mutacje i kopiowanie źródeł nadal sprawdzają
własność.

Migracja `20261005131116_CustomerAndSubcategoryDictionaries` dodaje dwie tabele,
nullable FK i indeksy unikalności, bez zmiany wcześniejszych migracji oraz bez
przepisywania snapshotów. Database update w DEV wykonany poprawnie; historia ma
6 migracji, a `ef migrations has-pending-model-changes` potwierdza zgodność modelu.

Weryfikacja: build 0 błędów / 0 ostrzeżeń, 214/214 testów. Zachowano dotychczasowe
197 przypadków i dodano 17 obejmujących słowniki, snapshoty, historyczne rekordy,
kopiowanie, scope raportów/CSV (także endpointy HTTP), wyszukiwanie oraz role transferu.
Testy używają SQL Server DEV i usuwają tylko rekordy utworzone przez dany test.

Test przeglądarkowy na Windows SSO / SuperAdmin: dodanie klienta, kategorii i dwóch
podkategorii, filtrowanie zależne, reset wyboru, Duplicate i Copy From z pustym SKU,
Save Draft, Details, Projects search po kliencie/SKU oraz Archive search po numerze.
Po dezaktywacji klienta i podkategorii Details nadal pokazywał snapshoty. Raporty
SuperAdmina sprawdzono w przeglądarce; PM/Manager i próby zmiany filtra sprawdzono
w testach SQL i HTTP bez zmiany roli rzeczywistego konta Windows.
Utworzony wyłącznie do tego testu Draft PB-2026-1701 usunięto wraz z jego trzema
produktami po weryfikacji dokładnego ID/numeru/klienta/właściciela. Testowe wpisy
słowników pozostają nieaktywne. Istniejące projekty DEV pozostawiono bez zmian.

## Customer Default Country i docelowe słowniki — 06.10.2026

Migracja `20261006070239_CustomerDefaultCountriesAndProductionDictionaries` dodaje
nullable Customer.DefaultCountryId z FK NO ACTION do Countries oraz jednorazowy
import danych biznesowych. Poprzednich migracji nie zmieniono. Import nie jest
uruchamiany przy starcie i nie jest HasData odtwarzającym późniejsze zmiany admina.
Administracja nadal pozwala zmieniać nazwy, kraje domyślne, DisplayOrder i aktywność.

Brief otrzymuje minimalne dane domyślnego kraju w opcjach Customer. Mały skrypt
customer-country.js ustawia Country wyłącznie po zdarzeniu zmiany Customer.
Dla pustego lub nieaktywnego domyślnego kraju czyści wybór. Country pozostaje
edytowalne. Back, refresh i Edit Draft nie nadpisują już zapisanego wyboru.
Bez JavaScript użytkownik nadal może wybrać kraj ręcznie; backend waliduje aktywność
Country i Customer dla nowych wyborów, bez wymogu zgodności z defaultem.
Historyczne snapshoty i obsługa istniejących nieaktywnych klientów pozostają bez zmian.

W DEV dodano 22 aktywnych klientów, 4 kraje (Belgium, Finland, Netherlands,
The Czech Republic) i 27 podkategorii. Istniejące kraje i nieaktywne dane testowe
zachowano. Face Cream przemianowano na FACE CARE z zachowaniem Id=3 i relacji;
Body Care / Hair Care zachowały swoje ID. Aktywne kategorie i DisplayOrder to
1 BODY CARE, 2 HAIR CARE, 3 FACE CARE. Customer i podkategorie mają kolejność
zgodną z listami wejściowymi.

| Default Country | Customers |
| --- | --- |
| Poland | CARREFOUR, DOZ, NATURA, POLWELL / MILA, ALBA THYMENT, BIEDRONKA, POLOMARKET, ALDI, HEBE, HERBAPOL, LIDL, BETLEY, GEMINI, ULTRAMENT |
| Belgium | COLRUYT, PHARMA BOULEVARD, CERES PHARMA |
| Finland | TOKMANNI |
| Netherlands | ORCHARD, BEYOND LABELS, MDV / STYLEDRY |
| The Czech Republic | DR.MAX |

Podkategorie w kolejności DisplayOrder:

- BODY CARE: LIQUID SOAP; SHOWER GEL; SHOWER GEL & SHAMPOO; BATH FOAM; BATH SALT;
  BODY LOTION; BODY BUTTER; HAND CREAM; FOOT CREAM; INTIMATE HYGIENE;
  CREAMY BODY SCRUB; SHOWER & PEELING; BODY MIST.
- HAIR CARE: SHAMPOO; HAIR CONDITIONER; SHAMPOO & CONDINTIONER; HAIR MASK;
  HAIR SPRAY; LEAVE IN CONDITIONER; HAIR SERUM; SCALP PEELING.
- FACE CARE: FACE CREAM; MICELLAR WATER; CLEANSING GEL; CLEANSING FOAM;
  CLEANSING MILK; FACE TONIC.

Zachowano pisownię z listy użytkownika: BODY BUTTER oraz SHAMPOO & CONDINTIONER.
Warianty BODY BUTTTER i SHAMPOO&CONDINTIONER pojawiły się tylko w pytaniach raportu,
nie w danych wejściowych, więc nie utworzono dodatkowych aliasów. SHOWER & PEELING
nie zawiera końcowego whitespace/NBSP. Normalizacja porównań importu obejmuje
wielkość liter, skrajne spacje, NBSP i powtórzone białe znaki.

Import używa kodów ISO i znormalizowanych nazw, zachowuje istniejące identyfikatory,
uzupełnia tylko pusty/zgodny DefaultCountry i odrzuca niejednoznaczne dopasowania.
Konflikt podkategorii pod inną kategorią zatrzymuje migrację zamiast ją przenosić.
EF wykonuje migrację transakcyjnie. W DEV nie stwierdzono duplikatów ani konfliktów.
Nie wykonano backfillu ProductSubcategoryId ani zmian snapshotów projektów.
Automatyczny downgrade tej migracji jest zablokowany: powrót wymaga zatwierdzonej
kopii zapasowej albo migracji naprawczej, aby nie usuwać edytowanych danych biznesowych.

Weryfikacja: build 0 błędów / 0 ostrzeżeń, 222/222 testów. Testy obejmują nullable
DefaultCountry, dane dla dropdownu, ręczny inny kraj, nieaktywne wpisy, niezmienność
projektów po edycji słownika oraz faktyczny SQL importu w izolowanych tabelach
tymczasowych SQL Server (początkowe dane, ponowne wykonanie, zachowanie ID, konflikty).
Nie tworzą alternatywnej bazy ani nie modyfikują istniejących danych DEV.
Pełny test wszystkich migracji na pustej bazie nie został wykonany: konto SQL zwróciło
`CREATE DATABASE permission denied in database 'master'.` Do tej kontroli przed
wdrożeniem administrator musi przygotować pustą bazę testową lub nadać uprawnienie.
Nie zmieniano uwierzytelniania ani uprawnień SQL. Nie powstała żadna tymczasowa baza.

Database update w DEV zakończony sukcesem: 7 migracji w historii, brak pending model
changes, FK DefaultCountry z NO_ACTION, 9 Countries i 10 Rejection Reasons.
Test Windows SSO w przeglądarce: CARREFOUR → Poland, COLRUYT → Belgium;
ręczne CARREFOUR → Germany zaakceptowane przez Next i zachowane po Back/refresh.
BODY CARE / HAIR CARE / FACE CARE pokazują odpowiednio 13/8/6 właściwych podkategorii;
zmiana kategorii usuwa poprzedni wybór. Nie zapisano nowego projektu testowego.
Copy From / Duplicate i dotychczasowy workflow pokrywa pełny zestaw testów regresji.
