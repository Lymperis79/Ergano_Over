# S Pharmacy Log

A separate Avalonia desktop app inside the ErganiManager solution. Its SQLite database is isolated from ErganiManager and stored under the current user's LocalAppData folder.

## Initial implementation
- First-run administrator creation and BCrypt password verification.
- Independent pharmacy data schema: users, customers, prescriptions, medicine catalogue, borrowing records, subscriptions, payments and customer ledger.
- Initial prescription expiry calendar: select a date and view active prescriptions expiring that day.
- Starter navigation placeholders for the remaining modules.

## Run
From the repository root:
`dotnet run --project src/SPharmacyLog/SPharmacyLog.csproj`

This is an early foundation. User permissions and CRUD workflows are still being built.