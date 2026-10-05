# kiirlink-server

Backend for **KiirLink**, a URL shortener with per-link click analytics. Built on ASP.NET Core 10 minimal APIs, ASP.NET Core Identity and EF Core with SQLite.

The mobile client lives in [kiirlink-client](https://github.com/nikita-lit/kiirlink-client).

## Features

- Sign-up and sign-in through ASP.NET Core Identity with bearer tokens (`/api/auth/*`)
- Short links with an optional expiry date and public or private visibility
- Redirects at `/{shortUrl}`. Private links only redirect for their owner, and expired links return `410 Gone`
- Click analytics grouped by date, device, traffic source and country (country lookup via ip-api.com)
- Favourites, categories and a per-link activity log
- Soft delete and paginated link lists
- OpenAPI document and a Scalar UI in Development

## API overview

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/auth/register`, `/api/auth/login`, … | Identity endpoints |
| `POST` | `/api/links/shorten` | Create a short link |
| `GET` | `/api/links/get` | List your links (`page`, `limit`, `categoryId`) |
| `POST` | `/api/links/remove` | Delete a link |
| `GET` | `/api/links/{id}/stats` | Click statistics |
| `GET` | `/api/links/{id}/activity` | Click and action history |
| `GET` / `POST` | `/api/links/favourites`, `/favourite`, `/unfavourite` | Favourites |
| `GET` / `POST` / `DELETE` / `PUT` | `/api/links/categories`, `/category`, `/category/{id}`, `/{id}/category` | Categories |
| `GET` | `/{shortUrl}` | Redirect to the original URL |

Every `/api/links/*` route requires a bearer token.

## Getting started

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run
```

The API starts on `http://localhost:5129`. Pending migrations are applied on startup, and the SQLite database is stored at `data/kiirlink.db` (set by `ConnectionStrings:DefaultConnection` in `appsettings.json`).

In Development, the API reference is served at `/scalar` and the OpenAPI document at `/openapi/v1.json`.

### Docker

```bash
docker compose up -d --build
```

The container listens on port 80 and keeps the database in the `kiirlink_data` volume.

## Tests

`Test/` contains an end-to-end smoke test that exercises the API over HTTP. Start the server first, then run:

```bash
dotnet run --project Test
```
