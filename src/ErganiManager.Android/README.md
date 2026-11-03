# Ergani Manager for Android

This is the Android application project, built with Avalonia UI and targeting .NET 10 Android (API 23+).

## Build

Install the .NET 10 Android workload, then run:

```bash
dotnet workload install android
dotnet build src/ErganiManager.Android/ErganiManager.Android.csproj -f net10.0-android
```

To install on a connected device:

```bash
dotnet build src/ErganiManager.Android/ErganiManager.Android.csproj -f net10.0-android -t:Install
```

## Mobile integration plan

The Android client is intentionally a separate application from the desktop shell. The next integration stage is a secured mobile API so the phone does not need direct access to SQL Server/MariaDB credentials or the desktop computer's local database files. The mobile API will reuse the existing Core services and expose authenticated, company-scoped operations for work cards, daily status, employees, work calendar, overtime and submission history.

The current commit provides the Android application target and responsive mobile navigation shell. It does not yet authenticate users or load/write live company records.
