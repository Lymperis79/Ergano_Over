# Ergani Manager - license protection (offline activation)

The program can ask for a license key on every PC it is installed on. The key is tied to the PC, so copying the
program (or its data folder) to another computer does not work. Keys are created offline with **ErganiManager.LicenseTool**.

## The license generator window (recommended)

`src/ErganiManager.LicenseGenerator` is a Windows application (no console). Start it, then:

* **Signing key:** click *Create new key pair...* the first time (it saves your private key file and shows the public key
  line to paste into `LicenseKeys.cs`), or *Browse...* to pick your existing `license-private.key`. *Show public key*
  shows that line again at any time.
* **License:** enter the customer name and the PC code the customer sent, choose *Permanent* or a *Valid until* date, tick
  the modules (or *All modules*) and click *Create license key*.
* **Key:** copy it or save it as a `.lic` file and send it to the customer. Every key is kept in the **History** tab, so it
  can be copied again later. The window is available in English and Greek.

The console tool described below does the same from the command line.

## One-time setup (supplier)

1. Build and run the tool:  `dotnet run --project src/ErganiManager.LicenseTool -- genkey`
   * writes `license-private.key` - **keep it secret and back it up** (never ship it, never commit it)
   * prints the public key
2. Paste the public key into `src/ErganiManager.Licensing/LicenseKeys.cs` (`PublicKeyBase64`) and rebuild the application.
   * While `PublicKeyBase64` is empty the program does **not** check licenses at all (development mode).
   * Once it is filled in, every PC must be activated and only keys signed with *your* private key are accepted.

## For every customer PC

1. The customer starts the program. The activation window shows the **PC code** (for example `1A2B-3C4D-5E6F-7A8B-9C0D`).
2. The customer sends you the PC code.
3. You create the key:

   `dotnet run --project src/ErganiManager.LicenseTool -- issue`

   It asks for the customer name, the PC code, the end date (empty = permanent) and the modules (empty or `*` = all).
   Non-interactive: `issue --licensee "ACME" --pc 1A2B-3C4D-5E6F-7A8B-9C0D --expires 2027-12-31 --modules Schedules,Reports`
4. Send the printed key (or the `.lic` file) to the customer. They paste it into the activation window (or use *Load from file*).
   The program starts and does not ask again on that PC.

## Modules

`Schedules`, `WorkCards` (work card history, manual scan, today board), `Overtime`, `Reports` (incl. Excel export),
`Logs` (API log + email log), `Terminal` (the clock-in terminal for operators). Companies, branches, employees, users,
help and the database tools are always available. `*` = all modules.

## Renewal / changes

Open the program, click the key button in the header, enter the new key, restart. A license with an end date shows a
warning in the last 30 days. After the end date the program asks for a renewed key at the next start.

## Good to know

* `issue --pc "*"` creates a key that is **not** tied to one PC - only for testing or a deliberate site license.
* A different Windows installation or a different processor model changes the PC code - then a new key is needed.
* If the system date is set back on a PC with an expiring license, the license is refused until the date is corrected
  or a new key is entered.
* This is protection against copying and casual sharing. Like any program that runs on the customer's PC, a determined
  person with the right tools could remove the check; code obfuscation of the shipped program makes that harder.
* A license is checked at startup. A program that stays open for months keeps running past the end date until it is restarted.
