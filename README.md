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

## Accounts en 2FA

Gebaseerd op ASP.NET Core Identity met een sessiecookie (`wolsels.auth`, HttpOnly, SameSite=Strict).
Elke gebruiker ziet alleen zijn eigen gegevens (EF Core query filters op `OwnerId`). Wachtwoorden
minimaal 10 tekens; na 5 foute pogingen (wachtwoord of 2FA-code) is een account 15 minuten geblokkeerd.
Daarnaast geldt een limiet per IP-adres per minuut en per uur op alle gevoelige auth-endpoints.
Gegevens van vóór de invoering van accounts gaan naar het eerste geregistreerde account.

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
| `/api/harvests`                  | Oogsten ruwe wol (CRUD), filter `animalId`, `locationId`, `year` |
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

```sh
docker build -t wolsels-backend .
docker run -p 8080:8080 -v wolsels-data:/data wolsels-backend
```
