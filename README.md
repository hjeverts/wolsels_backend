# Wolsels backend

REST API (ASP.NET Core / .NET 10, EF Core + SQLite) for Wolsels: wool stock, blends with blend ratios,
spinning results (yield, yarn weight, plying, dyeing) and the production of raw wool by your own animals.

Belongs with [wolsels_frontend](https://github.com/hjeverts/wolsels_frontend) and [wolsels_deploy](https://github.com/hjeverts/wolsels_deploy).

## Development

```sh
dotnet run --project src/Wolsels.Api      # http://localhost:5262
dotnet test
```

The database (`data/wolsels.db`) is created and migrated at startup.
OpenAPI document in development: `GET /openapi/v1.json`.

New migration after a model change:

```sh
dotnet tool restore
dotnet ef migrations add <Name> -p src/Wolsels.Api -o Data/Migrations
```

## Configuration

| Setting                        | Default                       | Description                                |
|--------------------------------|-------------------------------|--------------------------------------------|
| `ConnectionStrings__Wolsels`   | `Data Source=data/wolsels.db` | SQLite database (in Docker `/data/...`)    |
| `Blends__MaxComponents`        | `6`                           | Maximum number of wools in a blend         |
| `Auth__AllowRegistration`      | `true`                        | Allow self sign-up (the first account is always allowed) |
| `Auth__RateLimitPerMinute`     | `10`                          | Max. auth requests per IP per minute       |
| `DataProtection__KeysPath`     | `data/keys`                   | Keys for session cookies (keep them!)      |
| `App__PublicUrl`               | –                             | Public URL, for links in emails            |
| `Smtp__Host` / `Port` / `Security` / `Username` / `Password` / `From` / `FromName` | – | Mail server for password resets |
| `Auth__RateLimitPerHour`       | `60`                          | Max. auth requests per IP per hour         |
| `Auth__PasswordResetPerIpPerHour` / `PerEmailPerHour` | `5` / `3` | Limits for reset mails         |
| `Auth__MaxFailedAccessAttempts` / `LockoutMinutes` | `5` / `15` | Account lockout                     |
| `Auth__AdminEmail`             | –                             | This account always becomes administrator  |

## Households

Data (wool, blends, animals, locations, harvests) belongs to a **household**. Several accounts can share
one household, and an account can be a member of several households. The frontend sends
`X-Household: <id>` with every request; the backend checks on every request that the user is a member of it
(403 `household-access` if not). Without the header the user's first household applies.

| Endpoint                                   | Description                                                  |
|--------------------------------------------|--------------------------------------------------------------|
| `GET/POST /api/households`                 | My households / create a new household                       |
| `GET/PUT /api/household`                   | Active household: members, open invitations / rename         |
| `POST /api/household/invites`              | Create an invitation link (single use, 7 days; only the hash is stored) |
| `DELETE /api/household/invites/{id}`       | Revoke an invitation                                         |
| `GET /api/household/invites/{token}`       | Info about an invitation (without login)                     |
| `POST /api/household/join`                 | Join with an invitation                                      |
| `POST /api/household/leave`                | Leave the active household (not as the last member)          |
| `DELETE /api/household/members/{userId}`   | Remove a member                                              |

Signing up with `?invite=<token>` also works when sign-up is otherwise disabled.

## Administration

The first account is **administrator** (in existing installations: the creator of the first household).
`Auth__AdminEmail` always makes an account administrator at startup, to restore access.
Permissions are checked per request. Administrators manage accounts, but don't see household data.

| Endpoint                                     | Description                                          |
|----------------------------------------------|------------------------------------------------------|
| `GET /api/admin/users`                       | All accounts with 2FA status, block status, households |
| `POST /api/admin/users/{id}/block`/`unblock` | Block (session ends immediately) / unblock           |
| `POST /api/admin/users/{id}/reset-2fa`       | Turn off 2FA (lost phone and recovery codes)         |
| `POST /api/admin/users/{id}/password-reset`  | Reset link to share yourself (also without a mail server) |
| `PUT /api/admin/users/{id}/admin`            | Grant/revoke administrator (the last administrator stays) |
| `GET /api/admin/users/{id}/delete-preview`   | Which households (only this member) are deleted too  |
| `DELETE /api/admin/users/{id}`               | Delete the account                                   |

## Accounts and 2FA

Based on ASP.NET Core Identity with a session cookie (`wolsels.auth`, HttpOnly, SameSite=Strict).
Each household only sees its own data (EF Core query filters on `HouseholdId`). Passwords are at least
10 characters; after 5 failed attempts (password or 2FA code) an account is locked for 15 minutes.
In addition there is a per-IP limit per minute and per hour on all sensitive auth endpoints.
Data from before accounts were introduced goes to the first household.

| Endpoint                                 | Description                                                      |
|------------------------------------------|------------------------------------------------------------------|
| `POST /api/auth/register`                | `{ email, password }`                                            |
| `POST /api/auth/login?useCookies=true`   | `{ email, password, twoFactorCode?, twoFactorRecoveryCode? }`; 401 with `RequiresTwoFactor` when a code is needed |
| `POST /api/auth/logout`                  | Log out                                                          |
| `GET /api/auth/me`                       | Email, 2FA status, remaining recovery codes, language            |
| `GET /api/auth/options`                  | Whether sign-up is possible                                      |
| `POST /api/auth/manage/2fa`              | 2FA (TOTP): get the key, enable, disable, reset recovery codes   |
| `POST /api/auth/password/forgot`         | Reset mail (only with a mail server and `App__PublicUrl`)        |
| `POST /api/auth/password/reset`          | New password with the code from the mail                         |
| `POST /api/auth/password`                | Change password (`oldPassword`, `newPassword`), the session stays valid |
| `POST /api/auth/renew`                   | Renew the session cookie after changing 2FA settings             |

"Forgot password" works once `Smtp__Host`, `Smtp__From` and `App__PublicUrl` are set. The link in the mail
always uses `App__PublicUrl`, never the request's Host header.

## Languages

Messages (errors, validation) are returned in the language from the `Accept-Language` header; the frontend
sends the app language. Without a (supported) language: Dutch. The password reset mail uses the language from
the profile. Translations live in `src/Wolsels.Api/Localization/Texts/<language>.json` (`nl.json` is the source;
a test checks that every language has the same keys and parameters).

| Endpoint                   | Description                                                          |
|----------------------------|----------------------------------------------------------------------|
| `GET /api/auth/me`         | Contains `language` (null for an account without a chosen language) |
| `PUT /api/auth/language`   | `{ "language": "de" }`; nl, de, en, fr, it, sv, nb, da, es, pt, pl   |

## API

| Endpoint                         | Description                                               |
|----------------------------------|-----------------------------------------------------------|
| `/api/wools`                     | Wool stock (CRUD)                                         |
| `/api/blends`                    | Blends (CRUD); weights are calculated server-side. Status `Spun` deducts the wool from stock; setting it back, changing or deleting settles that again |
| `POST /api/blends/calculate`     | Convert a ratio to weights without saving                 |
| `/api/locations`                 | Locations (CRUD)                                          |
| `/api/animals`                   | Animals (CRUD), incl. total yield                         |
| `POST /api/animals/import`       | Import animals (max. 1000, all or nothing). Matches by name: skip existing animals, or with `updateExisting` only update the fields in `fields`. Unknown locations are created. Errors per line (`line`) |
| `/api/harvests`                  | Raw wool harvests (CRUD), filter `animalId`, `locationId`, `year`, `woolId`. With `addToStock` (+ optional `stockWoolId`) to a new or existing lot; also later for an already saved harvest. Weight changes are settled with the lot; `DELETE ?removeFromStock=true` also subtracts the weight from the lot |
| `POST /api/wools/{id}/merge`     | Merge a lot into `targetWoolId`: weight, harvests, blends and photos move along |
| `/api/wools/{id}/photos`         | Photos: `POST` (multipart `photo` + `thumbnail`, JPEG, max. 2048 resp. 512 px and 2 MB resp. 256 kB, max. 10 per lot), `GET /{photoId}` (`?size=thumb` for the thumbnail; `no-cache` with ETag, so authorisation is checked every time), `DELETE /{photoId}`. Deleted along with the lot; merging above 10 gives 409. For `<img>` the household may be passed as `?household=` on GET instead of the header |
| `GET /api/harvests/summary`      | Production per month and per animal/location (`?year=`)   |
| `GET /api/settings`              | `maxBlendComponents`                                      |
| `GET /health`                    | Health check                                              |

### Blend ratio

A ratio may be given in parts (`3:2`) or percentages (`60:40`); it is normalised. Weights are calculated
from:

- **`TotalWeight`** – the total input weight (`totalWeightGrams`). Rounded to 0.1 g with the largest
  remainder method, so that the sum is always exactly the total.
- **`ComponentWeight`** – the weight of one wool (`basisComponentIndex` + `basisComponentWeightGrams`).
  That wool keeps exactly that weight; the rest scales along.

The frontend has the same calculation for immediate feedback; on save the backend calculates again.

## Docker

GitHub Actions (`.github/workflows/image.yml`) runs the tests on every pull request and push to `main`.
An image is only built for a version tag:

```sh
git switch main && git pull
git tag v1.2.0 && git push origin v1.2.0     # or via GitHub: Releases → Draft a new release
```

The image is published to `ghcr.io/hjeverts/wolsels-backend` with the tags `1.2.0`, `1.2` and, if it is the
newest version, `latest`. Use [semver](https://semver.org/): `v1.2.1` for a bug fix, `v1.3.0` for a new
feature. Every Monday the newest version is rebuilt to pick up base image updates (manually:
*Actions → Image → Run workflow*).

Building it yourself:

```sh
docker build -t wolsels-backend .
docker run -p 8080:8080 -v wolsels-data:/data wolsels-backend
```

## License

Copyright (C) 2026 Hans Everts

This program is free software: you can redistribute it and/or modify it under the terms of the
[GNU Affero General Public License](LICENSE), version 3. If you offer a modified version as a web service,
you must also make the source code of that version available to its users.
