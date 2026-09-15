# Ergani Manager

Multi-tenant C# desktop application for managing Greek Ergani labour API submissions —
work cards (clock-in/out), schedules, and overtime. Runs on **Windows** and **Linux**.

## Prerequisites

| Dependency | Version | Notes |
|---|---|---|
| .NET SDK | **10.0** | https://dotnet.microsoft.com/download/dotnet/10.0 |
| SQL Server Express | Any | Only if using SQL Server provider |
| MariaDB | 10.6+ | Only if using MariaDB provider |

SQLite requires no external installation — it is the default and recommended option.

## First Run

```bash
dotnet run --project src/ErganiManager.UI
```

On first launch the Database Setup wizard appears. Choose SQLite and click Save & Continue.
You will then be prompted to create the first administrator account.

## EF Core Migrations Tools

Install the dotnet-ef CLI tool matching EF Core 10:

```bash
dotnet tool install --global dotnet-ef --version 10.0.9
```

Generate migrations per provider:

```bash
cd src/ErganiManager.Data
dotnet ef migrations add InitialCreate -- --provider Sqlite      # default, no DB needed
dotnet ef migrations add InitialCreate -- --provider SqlServer
dotnet ef migrations add InitialCreate -- --provider MariaDb
```

## MariaDB Provider Note

Official Pomelo.EntityFrameworkCore.MySql does not yet support EF Core 10
(tracked at https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/2007).

This project currently uses **Microting.EntityFrameworkCore.MySql 10.0.8**, a community
fork that is a drop-in replacement (same namespace, same API). Once the official Pomelo
10.x release lands, update the package reference in
`src/ErganiManager.Data/ErganiManager.Data.csproj` and remove the Microting package.

## Publishing

### Windows (self-contained EXE)

```powershell
.\build\publish-windows.ps1 -Version 1.0.0
# Output: build\publish\win-x64\ErganiManager.exe
```

### Linux (self-contained binary)

```bash
bash build/publish-linux.sh 1.0.0
# Output: build/publish/linux-x64/ErganiManager
sudo bash build/install-linux.sh   # optional system install
```

## Ergani API Endpoints

All API paths live in one file — edit here if Ergani changes their API:

```
src/ErganiManager.ErganiApi/ErganiEndpoints.cs
```

## Project Structure

```
src/
  ErganiManager.Data/        # EF Core 10 + multi-provider DbContext (.NET 10)
  ErganiManager.LocalCache/  # Always-on SQLite cache for offline support (.NET 10)
  ErganiManager.Core/        # Business logic, services, interfaces (.NET 10)
  ErganiManager.ErganiApi/   # HTTP client, MailKit email, credential encryption (.NET 10)
  ErganiManager.UI/          # Avalonia UI 11.3 MVVM (.NET 10)
build/
  publish-windows.ps1
  publish-linux.sh
  install-linux.sh
```

## Configuration (auto-created on first run)

- **Windows**: `%AppData%\ErganiManager\`
- **Linux**: `~/.config/ErganiManager/`

Files: `connection.json`, `local_cache.db`, `.keyfile` (Linux only), `logs/`

## Licence

MIT
