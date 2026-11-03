# ErganiManager Mobile API

This ASP.NET Core host exposes a small HTTPS API for the Android client while
reusing the existing `ErganiManager.Core` authentication and business services.
It does not define duplicate database entities or reimplement attendance,
employee, schedule, or overtime rules.

## Current endpoints

- `GET /api/mobile/health` — liveness check.
- `POST /api/mobile/login` — authenticates through the existing `IAuthService`.
- `POST /api/mobile/logout` — ends the authenticated session.
- `GET /api/mobile/me` — current authenticated session.
- `GET /api/mobile/companies` — companies visible to the signed-in user.
- `GET /api/mobile/employees` — active employees in the selected company.
- `GET /api/mobile/today?date=YYYY-MM-DD&graceMinutes=10` — daily status from the existing `ITodayStatusService`.

The API intentionally rejects offline-cache logins: remote mobile access requires
the authoritative database to be available.

## Security and deployment

- Deploy behind a trusted HTTPS reverse proxy or configure ASP.NET Core HTTPS
  certificates directly. The authentication cookie is Secure, HttpOnly and
  SameSite=Strict.
- Do not expose this service over plain HTTP or forward database credentials to
  Android.
- Run the service under the same OS account/profile and on the same host as the
  existing Ergani II installation so the existing `AppPaths` connection
  configuration is available. The service uses the same configured database.
- Restrict inbound network access to trusted networks and use firewall rules.
- The API uses a request-scoped `ICompanyContext`; it does not share the
  desktop application's process-wide company session between mobile users.

## Build

```powershell
dotnet restore src/ErganiManager.MobileApi/ErganiManager.MobileApi.csproj
dotnet build src/ErganiManager.MobileApi/ErganiManager.MobileApi.csproj -c Release
dotnet run --project src/ErganiManager.MobileApi/ErganiManager.MobileApi.csproj
```

The API is an initial integration layer. More operations should be exposed by
calling existing Core interfaces, not by copying their implementation.
