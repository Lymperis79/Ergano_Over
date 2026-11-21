# S Pharmacy Log

A standalone Avalonia desktop application in the same solution, using the same layered structure as ErganiManager.

## Project layers
- **SPharmacyLog.Data** — EF Core entities, SQLite DbContext and migration history.
- **SPharmacyLog.Core** — authentication and business services.
- **SPharmacyLog.UI** — Avalonia desktop application and views.

## Database and migrations
Pharmacy data is stored in its own SQLite database at `%LOCALAPPDATA%\\SPharmacyLog\\pharmacy.db`, independent of ErganiManager's database.
EF Core migrations run during startup. The initial migration creates users, customers, prescriptions, medicines, borrowing records, subscriptions, payments and ledger entries.
A design-time context factory is included for future migration commands.

## Licensing
Uses the shared ErganiManager signed-license system. The license generator now includes the `SPharmacyLog` product module, and the app rejects licenses that do not contain that module (or the wildcard `*`). Activation is saved in the pharmacy app's own LocalAppData folder.

## Current functional starter
- First-run administrator account creation (password minimum 8 characters, BCrypt).
- Login validation.
- Month-view prescription expiry calendar with per-day active prescription counts and a selected-day list.
- Navigation placeholders for Customers, Borrowing, Subscriptions, Payments/Ledger and Users.

## Build and run
`dotnet run --project src/SPharmacyLog.UI/SPharmacyLog.UI.csproj`

This branch has not been built in a local .NET environment yet.