The Traveller Map — Setup Guide
================================

This guide covers running the Traveller Map server on Linux using Docker
(recommended) or directly with the .NET 8 SDK.

---

## Docker (recommended)

### Prerequisites

- Docker 20.10+ with Compose v2 (`docker compose`)
- A MariaDB 10.6+ or MariaDB 11 instance, **or** use the bundled dev stack below

> **Database is optional for map rendering.** The app starts and serves map
> tiles without a database configured. Only the search endpoint (`/api/search`)
> and reindex (`/admin/reindex`) return HTTP 503 when no `ConnectionString` is
> set. You can add a database at any time and run `/admin/reindex` to enable
> search.

### Quick start with the bundled dev stack

The repository includes a `docker-compose.yml` that starts both the app and a
MariaDB 11 database in a single command:

```sh
docker compose up -d
```

The app is available at **http://localhost:18080**.

### Quick start — no database

```sh
docker run -d -p 8080:8080 travellermap
```

Map tiles render immediately. Search is disabled until a database is connected.

---

## After first deploy (all scenarios)

Once the container is running, populate the search index:

```sh
curl http://<host>:<port>/admin/reindex
```

This is required after every fresh database — it may take a minute. Verify
with:

```sh
curl "http://<host>:<port>/api/search?q=Regina"
```

If `AdminKey` is set, append `?key=<adminkey>` to admin requests.

---

## Building the image

```sh
docker build -t travellermap .
```

Or let Compose build it:

```sh
docker compose build
```

---

## Deploying behind Nginx Proxy Manager

NPM and the travellermap container need to be able to reach each other.
Choose one of the two approaches below.

### Option A — shared Docker network (cleaner, no host-port exposure)

Create a network that both stacks share:

```sh
docker network create npm_proxy
```

Add it to your NPM `docker-compose.yml`:

```yaml
networks:
  npm_proxy:
    external: true
```

Add it to a `docker-compose.override.yml` in this repo (do not edit the
tracked `docker-compose.yml`):

```yaml
services:
  app:
    ports: []          # remove host-port binding
    networks:
      - default
      - npm_proxy

networks:
  npm_proxy:
    external: true
```

In NPM, create a Proxy Host pointing to **`travellermap-app-1`** (the
container name) on port **8080**. Enable "Websockets Support" if you
use the interactive map in a browser. Leave "SSL" off for the upstream —
NPM handles TLS termination.

### Option B — host port (simpler)

Leave the default `docker-compose.yml` as-is (port 18080 exposed on the
host). In NPM, create a Proxy Host pointing to **`<docker-host-IP>`** on
port **18080**.

### Note on proxy headers

The app does not redirect HTTP→HTTPS and does not generate absolute URLs, so
`X-Forwarded-Proto` / `ForwardedHeaders` middleware is not required. NPM can
pass forwarded headers or not — it makes no difference to the app.

---

## Using an external MariaDB (production)

If you have an existing MariaDB instance (including one managed by another
Compose stack), skip the bundled `db` service entirely. Create a
`docker-compose.override.yml`:

```yaml
services:
  app:
    environment:
      ConnectionString: "Server=<host>;Database=travellermap;User=<user>;Password=<pass>;"
      DatabaseProvider: mariadb
      AdminKey: ""     # or set a secret key
```

Or use a `.env` file (see below) and reference it:

```yaml
services:
  app:
    env_file: .env
```

You will need to create the `travellermap` database and user on the external
instance before starting the container. The app creates its own tables on the
first `/admin/reindex` call — no manual schema migration is needed.

---

## Secrets via `.env`

Create a `.env` file next to `docker-compose.yml` (never commit this):

```
ConnectionString=Server=db;Database=travellermap;User=travellermap;Password=changeme;
DatabaseProvider=mariadb
AdminKey=your-secret-key-here
```

Reference it in `docker-compose.override.yml`:

```yaml
services:
  app:
    env_file: .env
```

> **Never bake `AdminKey` or `ConnectionString` into the image.** Pass them at
> runtime via env vars or `.env`.

---

## Configuration reference

| Variable           | Default         | Description                                              |
|--------------------|-----------------|----------------------------------------------------------|
| `ConnectionString` | *(empty)*       | ADO.NET connection string for the search database        |
| `DatabaseProvider` | `mariadb`       | `mariadb` or `sqlserver`                                 |
| `AdminKey`         | *(empty)*       | Secret required for `/admin/*` endpoints; empty = open   |
| `ASPNETCORE_URLS`  | `http://+:8080` | Kestrel listen address (set in the image)                |

---

## Admin endpoints

| Endpoint               | Description                             |
|------------------------|-----------------------------------------|
| `GET /admin/reindex`   | Rebuild the search database             |
| `GET /admin/flush`     | Flush the in-memory sector cache        |
| `GET /admin/overview`  | Render a 1000×1000 overview PNG         |
| `GET /admin/errors`    | Validate sector data (allegiance codes) |
| `GET /admin/codes`     | List unknown world codes                |
| `GET /admin/dump`      | Dump a sample of world data             |
| `GET /admin/profile`   | Process memory / thread stats           |
| `GET /admin/uptime`    | Application uptime                      |

---

## Local development (dotnet run)

### Prerequisites

- .NET 8 SDK
- (Optional) MariaDB or SQL Server for search features

### Build and run

```sh
dotnet build
ASPNETCORE_URLS=http://+:18083 dotnet run
```

The map is available at **http://localhost:18083**.

### Running tests

```sh
dotnet test unittests/UnitTests/UnitTests.csproj
```

### Enabling search locally

Set `ConnectionString` and `DatabaseProvider` via environment variables:

```sh
ConnectionString="Server=localhost;Database=travellermap;User=travellermap;Password=travellermap;" \
DatabaseProvider=mariadb \
ASPNETCORE_URLS=http://+:18083 \
dotnet run
```

Then trigger reindex:

```sh
curl http://localhost:18083/admin/reindex
```

---

## JavaScript linting

```sh
npm install
npx eslint index.js map.js world_util.js
```
