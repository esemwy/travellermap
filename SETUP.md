The Traveller Map — Setup Guide
================================

This guide covers running the Traveller Map server on Linux using Docker
(recommended) or directly with the .NET 8 SDK.

---

## Docker (recommended)

### Prerequisites

- Docker 20.10+ with Compose v2 (`docker compose`)
- A MariaDB 10.6+ or MariaDB 11 instance (or use the bundled dev stack below)

### Quick start with the bundled dev stack

The repository includes a `docker-compose.yml` that starts both the app and a
MariaDB 11 database in a single command:

```sh
docker compose up -d
```

The app is available at **http://localhost:18080** (port 18080 is used for
local dev; production deployments map 8080:8080).

On first run, populate the search database:

```sh
curl http://localhost:18080/admin/reindex
```

Verify search is working:

```sh
curl "http://localhost:18080/api/search?q=Regina"
```

### Building the image

```sh
docker build -t travellermap .
```

### Running against an existing MariaDB instance

```sh
docker run -d \
  -p 8080:8080 \
  -e ConnectionString="Server=<host>;Database=travellermap;User=<user>;Password=<pass>;" \
  -e DatabaseProvider=mariadb \
  travellermap
```

For SQL Server:

```sh
docker run -d \
  -p 8080:8080 \
  -e ConnectionString="Server=<host>;Database=travellermap;Trusted_Connection=True;" \
  -e DatabaseProvider=sqlserver \
  travellermap
```

### Configuration (environment variables)

| Variable           | Default    | Description                                              |
|--------------------|------------|----------------------------------------------------------|
| `ConnectionString` | *(empty)*  | ADO.NET connection string for the search database        |
| `DatabaseProvider` | `mariadb`  | `mariadb` or `sqlserver`                                 |
| `AdminKey`         | *(empty)*  | Secret required for `/admin/*` endpoints; empty = open   |
| `ASPNETCORE_URLS`  | `http://+:8080` | Kestrel listen address (set in the image)           |

> **Never bake `AdminKey` or `ConnectionString` into the image.** Pass them at
> `docker run` time or via your orchestrator's secrets mechanism.

### Admin endpoints

Once the container is running:

| Endpoint               | Description                            |
|------------------------|----------------------------------------|
| `GET /admin/reindex`   | Rebuild the search database            |
| `GET /admin/flush`     | Flush the in-memory sector cache       |
| `GET /admin/overview`  | Render a 1000×1000 overview PNG        |
| `GET /admin/errors`    | Validate sector data (allegiance codes)|
| `GET /admin/codes`     | List unknown world codes               |
| `GET /admin/dump`      | Dump a sample of world data            |
| `GET /admin/profile`   | Process memory / thread stats          |
| `GET /admin/uptime`    | Application uptime                     |

If `AdminKey` is set, append `?key=<adminkey>` to each request.

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

Set `ConnectionString` and `DatabaseProvider` in `appsettings.json`
(do not commit secrets), or via environment variables:

```sh
ConnectionString="Server=localhost;..." \
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
