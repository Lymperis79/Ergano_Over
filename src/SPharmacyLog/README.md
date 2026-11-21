# S Pharmacy Log
This application is being refactored into the same three-layer pattern as ErganiManager:
- SPharmacyLog.Data: EF Core entities and database.
- SPharmacyLog.Core: authentication and business services.
- SPharmacyLog.UI: Avalonia executable and views.
Separate SQLite database in LocalAppData. Initial login setup and expiry calendar started.