# Wolsels backend

REST API (ASP.NET Core / .NET 10, EF Core + SQLite) voor Wolsels: wolvoorraad, mengsels met
mengverhoudingen, spinresultaten (opbrengst, garendikte, twijnen, verven) en de productie van ruwe wol
door eigen dieren.

Hoort bij [wolsels-frontend](../wolsels-frontend) en [wolsels-deploy](../wolsels-deploy).

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

## API

| Endpoint                         | Omschrijving                                              |
|----------------------------------|-----------------------------------------------------------|
| `/api/wools`                     | Wolvoorraad (CRUD)                                        |
| `/api/blends`                    | Mengsels (CRUD); gewichten worden server-side berekend    |
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
