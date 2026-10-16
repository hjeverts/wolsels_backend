# Wolsels backend

REST API (ASP.NET Core / .NET 10, EF Core + SQLite) voor Wolsels: wolvoorraad, mengsels met
mengverhoudingen, spinresultaten (opbrengst, garendikte, twijnen, verven) en de productie van ruwe wol
door eigen dieren.

Hoort bij [wolsels_frontend](https://github.com/hjeverts/wolsels_frontend) en [wolsels_deploy](https://github.com/hjeverts/wolsels_deploy).

## Ontwikkelen

```sh
dotnet run --project src/Wolsels.Api      # http://localhost:5262
dotnet test
```

De database (`data/wolsels.db`) wordt bij het opstarten aangemaakt en gemigreerd.
OpenAPI-document in development: `GET /openapi/v1.json`.

Nieuwe migratie na een modelwijziging:

```sh
dotnet tool restore
dotnet ef migrations add <Naam> -p src/Wolsels.Api -o Data/Migrations
```

## Configuratie

| Instelling                     | Standaard                     | Omschrijving                               |
|--------------------------------|-------------------------------|--------------------------------------------|
| `ConnectionStrings__Wolsels`   | `Data Source=data/wolsels.db` | SQLite-database (in Docker `/data/...`)    |
| `Blends__MaxComponents`        | `6`                           | Maximaal aantal soorten wol in een mengsel |
| `Auth__AllowRegistration`      | `true`                        | Zelf registreren toestaan (het eerste account mag altijd) |
| `Auth__RateLimitPerMinute`     | `10`                          | Max. auth-verzoeken per IP per minuut      |
| `DataProtection__KeysPath`     | `data/keys`                   | Sleutels voor sessiecookies (bewaren!)     |
| `App__PublicUrl`               | –                             | Openbare URL, voor links in e-mails        |
| `Smtp__Host` / `Port` / `Security` / `Username` / `Password` / `From` / `FromName` | – | Mailserver voor wachtwoordherstel |
| `Auth__RateLimitPerHour`       | `60`                          | Max. auth-verzoeken per IP per uur         |
| `Auth__PasswordResetPerIpPerHour` / `PerEmailPerHour` | `5` / `3` | Limieten herstelmails          |
| `Auth__MaxFailedAccessAttempts` / `LockoutMinutes` | `5` / `15` | Accountblokkering                   |
| `Auth__AdminEmail`             | –                             | Dit account wordt altijd beheerder         |

## Huishoudens

Gegevens (wol, mengsels, dieren, locaties, oogsten) horen bij een **huishouden**. Meerdere accounts kunnen
één huishouden delen, en een account kan lid zijn van meerdere huishoudens. De frontend stuurt per verzoek
`X-Household: <id>` mee; de backend controleert bij elk verzoek of de gebruiker daar lid van is (403
`household-access` als dat niet zo is). Zonder header geldt het eerste huishouden van de gebruiker.

| Endpoint                                   | Omschrijving                                                 |
|--------------------------------------------|--------------------------------------------------------------|
| `GET/POST /api/households`                 | Mijn huishoudens / nieuw huishouden aanmaken                 |
| `GET/PUT /api/household`                   | Actief huishouden: leden, open uitnodigingen / naam wijzigen |
| `POST /api/household/invites`              | Uitnodigingslink maken (eenmalig, 7 dagen; alleen de hash wordt opgeslagen) |
| `DELETE /api/household/invites/{id}`       | Uitnodiging intrekken                                        |
| `GET /api/household/invites/{token}`       | Info over een uitnodiging (zonder login)                     |
| `POST /api/household/join`                 | Lid worden met een uitnodiging                               |
| `POST /api/household/leave`                | Actief huishouden verlaten (niet als laatste lid)            |
| `DELETE /api/household/members/{userId}`   | Lid verwijderen                                              |

Registreren met `?invite=<token>` werkt ook als registreren verder uit staat.

## Beheer

Het eerste account is **beheerder** (bij bestaande installaties: de maker van het eerste huishouden).
`Auth__AdminEmail` maakt een account bij het opstarten altijd beheerder, om toegang te herstellen.
Rechten worden per verzoek gecontroleerd. Beheerders beheren accounts, maar zien geen gegevens van
huishoudens.

| Endpoint                                     | Omschrijving                                         |
|----------------------------------------------|------------------------------------------------------|
| `GET /api/admin/users`                       | Alle accounts met 2FA-status, blokkade, huishoudens  |
| `POST /api/admin/users/{id}/block`/`unblock` | Blokkeren (sessie vervalt direct) / deblokkeren      |
| `POST /api/admin/users/{id}/reset-2fa`       | 2FA uitschakelen (telefoon en herstelcodes kwijt)    |
| `POST /api/admin/users/{id}/password-reset`  | Herstellink om zelf te delen (ook zonder mailserver) |
| `PUT /api/admin/users/{id}/admin`            | Beheerder maken/afnemen (laatste beheerder blijft)   |
| `GET /api/admin/users/{id}/delete-preview`   | Welke huishoudens (alleen dit lid) meeverdwijnen     |
| `DELETE /api/admin/users/{id}`               | Account verwijderen                                  |

## Accounts en 2FA

Gebaseerd op ASP.NET Core Identity met een sessiecookie (`wolsels.auth`, HttpOnly, SameSite=Strict).
Elk huishouden ziet alleen zijn eigen gegevens (EF Core query filters op `HouseholdId`). Wachtwoorden
minimaal 10 tekens; na 5 foute pogingen (wachtwoord of 2FA-code) is een account 15 minuten geblokkeerd.
Daarnaast geldt een limiet per IP-adres per minuut en per uur op alle gevoelige auth-endpoints.
Gegevens van vóór de invoering van accounts gaan naar het eerste huishouden.

| Endpoint                                 | Omschrijving                                                     |
|------------------------------------------|------------------------------------------------------------------|
| `POST /api/auth/register`                | `{ email, password }`                                            |
| `POST /api/auth/login?useCookies=true`   | `{ email, password, twoFactorCode?, twoFactorRecoveryCode? }`; 401 met `RequiresTwoFactor` als een code nodig is |
| `POST /api/auth/logout`                  | Uitloggen                                                        |
| `GET /api/auth/me`                       | E-mail, 2FA-status, resterende herstelcodes                      |
| `GET /api/auth/options`                  | Of registreren mogelijk is                                       |
| `POST /api/auth/manage/2fa`              | 2FA (TOTP): sleutel ophalen, inschakelen, uitschakelen, herstelcodes resetten |
| `POST /api/auth/password/forgot`         | Herstelmail (alleen met mailserver én `App__PublicUrl`) |
| `POST /api/auth/password/reset`          | Nieuw wachtwoord met code uit de mail                 |
| `POST /api/auth/password`                | Wachtwoord wijzigen (`oldPassword`, `newPassword`), sessie blijft geldig |
| `POST /api/auth/renew`                   | Sessiecookie vernieuwen na het wijzigen van 2FA-instellingen     |

"Wachtwoord vergeten" werkt zodra `Smtp__Host`, `Smtp__From` en `App__PublicUrl` zijn ingesteld. De link in de
mail gebruikt altijd `App__PublicUrl`, nooit de Host-header van het verzoek.

## API

| Endpoint                         | Omschrijving                                              |
|----------------------------------|-----------------------------------------------------------|
| `/api/wools`                     | Wolvoorraad (CRUD)                                        |
| `/api/blends`                    | Mengsels (CRUD); gewichten worden server-side berekend. Status `Spun` boekt de wol af van de voorraad; terugzetten, aanpassen of verwijderen verrekent dat weer |
| `POST /api/blends/calculate`     | Verhouding omrekenen naar gewichten zonder op te slaan    |
| `/api/locations`                 | Locaties (CRUD)                                           |
| `/api/animals`                   | Dieren (CRUD), incl. totale opbrengst                     |
| `POST /api/animals/import`       | Dieren importeren (max. 1000, alles of niets). Koppelt op naam: bestaande dieren overslaan, of met `updateExisting` alleen de velden uit `fields` bijwerken. Onbekende locaties worden aangemaakt. Fouten per regel (`line`) |
| `/api/harvests`                  | Oogsten ruwe wol (CRUD), filter `animalId`, `locationId`, `year`, `woolId`. Met `addToStock` (+ optioneel `stockWoolId`) bij een nieuwe of bestaande partij; ook later voor een al opgeslagen oogst. Gewichtswijzigingen worden met de partij verrekend; `DELETE ?removeFromStock=true` haalt het gewicht ook van de partij af |
| `POST /api/wools/{id}/merge`     | Partij samenvoegen met `targetWoolId`: gewicht, oogsten, mengsels en foto's gaan mee |
| `/api/wools/{id}/photos`         | Foto's: `POST` (multipart `photo` + `thumbnail`, JPEG, max. 2048 resp. 512 px en 2 MB resp. 256 kB, max. 10 per partij), `GET /{photoId}` (`?size=thumb` voor de miniatuur; `no-cache` met ETag, dus elke keer langs de autorisatie), `DELETE /{photoId}`. Worden met de partij verwijderd; samenvoegen boven de 10 geeft 409. Voor `<img>` mag het huishouden bij GET als `?household=` in plaats van de header |
| `GET /api/harvests/summary`      | Productie per maand en per dier/locatie (`?year=`)        |
| `GET /api/settings`              | `maxBlendComponents`                                      |
| `GET /health`                    | Healthcheck                                               |

### Mengverhouding

Een verhouding mag in delen (`3:2`) of procenten (`60:40`); hij wordt genormaliseerd. Gewichten
worden berekend vanuit:

- **`TotalWeight`** – het totale inputgewicht (`totalWeightGrams`). Afronding op 0,1 g met de
  grootste-restmethode, zodat de som altijd exact het totaal is.
- **`ComponentWeight`** – het gewicht van één soort (`basisComponentIndex` +
  `basisComponentWeightGrams`). Die soort houdt exact dat gewicht; de rest schaalt mee.

De frontend heeft dezelfde berekening voor directe feedback; bij opslaan rekent de backend opnieuw.

## Docker

GitHub Actions (`.github/workflows/image.yml`) draait bij elke pull request en push naar `main` de
tests. Een image wordt alleen gebouwd voor een versietag:

```sh
git switch main && git pull
git tag v1.2.0 && git push origin v1.2.0     # of via GitHub: Releases → Draft a new release
```

De image komt op `ghcr.io/hjeverts/wolsels-backend` met de tags `1.2.0`, `1.2` en, als het de nieuwste
versie is, `latest`. Gebruik [semver](https://semver.org/lang/nl/): `v1.2.1` voor een bugfix, `v1.3.0`
voor een nieuwe functie. Elke maandag wordt de nieuwste versie opnieuw gebouwd voor updates van de
base-image (handmatig: *Actions → Image → Run workflow*).

Zelf bouwen:

```sh
docker build -t wolsels-backend .
docker run -p 8080:8080 -v wolsels-data:/data wolsels-backend
```

## Licentie

Copyright (C) 2026 Hans Everts

Dit programma is vrije software: je mag het verspreiden en/of aanpassen onder de voorwaarden van de
[GNU Affero General Public License](LICENSE), versie 3. Bied je een aangepaste versie aan als
webdienst, dan moet je de broncode van die versie ook beschikbaar stellen aan de gebruikers.
