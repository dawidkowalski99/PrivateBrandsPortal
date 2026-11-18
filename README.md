# Private Brands Portal

Private Brands Portal to wewnętrzna aplikacja webowa służąca do obsługi projektów Private Brands w Forte Sweden.

Aplikacja porządkuje proces od utworzenia projektu przez Project Managera, przez akceptację produktów, aż do dalszych etapów handlowych i wdrożenia do produkcji. Dane projektu, decyzje, komentarze, załączniki i historia zmian są przechowywane w jednym miejscu.

## Główne funkcje

- logowanie przez Windows Authentication / Active Directory,
- tworzenie i edycja projektów Private Brands,
- automatyczna numeracja projektów `PB-YYYY-NNNN`,
- obsługa wielu produktów / SKU w jednym projekcie,
- zapis projektu jako Draft,
- workflow akceptacji przez kierownika,
- niezależne decyzje dla poszczególnych produktów,
- zatwierdzanie, odrzucanie oraz edycja i zatwierdzanie produktów,
- rejestrowanie komentarzy i powodów decyzji,
- statusy handlowe i wdrożeniowe,
- proces akceptacji wdrożenia produktu do produkcji,
- obsługa załączników,
- historia zmian i audit log,
- zmiana Project Managera projektu,
- zarządzanie użytkownikami, rolami i uprawnieniami,
- słowniki administracyjne,
- raporty i eksport danych,
- kopiowanie produktów pomiędzy projektami,
- powiadomienia e-mail związane ze zmianami w workflow.

## Powiadomienia

Najważniejsze zdarzenia w procesie mogą generować powiadomienia e-mail do odpowiednich użytkowników.

Przykładowo Project Manager może otrzymać informację o zatwierdzeniu lub odrzuceniu produktu, a osoba akceptująca informację o nowym projekcie oczekującym na decyzję.

Wiadomości są wysyłane przez Microsoft Graph z firmowej skrzynki i zawierają bezpośredni link do odpowiedniego miejsca w portalu.

Wysyłka wykorzystuje mechanizm Notification Outbox, dzięki czemu status wiadomości, liczba prób oraz ewentualne błędy są zapisywane w bazie danych.

## Role

System wykorzystuje kilka poziomów dostępu:

**Project Manager**  
Tworzy i prowadzi własne projekty.

**Manager**  
Obsługuje projekty wymagające akceptacji i podejmuje decyzje dotyczące produktów.

**SuperAdmin**  
Zarządza użytkownikami, uprawnieniami i konfiguracją administracyjną aplikacji.

Dodatkowe uprawnienia do administracji, słowników, raportów i eksportu są przydzielane niezależnie od roli użytkownika.

## Technologie

- .NET 10
- ASP.NET Core MVC / Razor
- Entity Framework Core
- Microsoft SQL Server
- IIS
- Windows Authentication
- Active Directory
- Microsoft Entra ID
- Microsoft Graph
- Exchange Online

## Środowisko

Aplikacja działa jako wewnętrzny system firmowy hostowany w IIS.

Dostęp odbywa się przez firmowe konto Windows bez dodatkowego loginu i hasła.

Środowiska deweloperskie i produkcyjne korzystają z oddzielnych baz danych oraz konfiguracji.

## Status

Projekt jest aktywnie rozwijany.

Obecna wersja obejmuje podstawowy proces zarządzania projektami Private Brands, workflow akceptacji, administrację użytkownikami, obsługę załączników, statusy handlowe i wdrożeniowe oraz automatyczne powiadomienia e-mail.
