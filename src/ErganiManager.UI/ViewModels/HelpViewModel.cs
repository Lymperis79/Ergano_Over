using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ErganiManager.LocalCache;
using ErganiManager.Core.Models;

namespace ErganiManager.UI.ViewModels;

/// <summary>
/// In-app help. The text is editable by the administrator and stored per language in the
/// application data folder (help.en.txt / help.el.txt); until edited, a built-in default is shown.
/// </summary>
public partial class HelpViewModel : ViewModelBase, IAdminSectionViewModel
{
    [ObservableProperty] private string _helpText = string.Empty;
    [ObservableProperty] private string _editText = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public HelpViewModel()
    {
        Load();
        // Switching language shows that language's help
        Loc.LanguageChanged += (_, _) => { if (!IsEditing) Load(); };
    }

    public void Initialize(UserSession session) => Load();

    private string FilePath =>
        Path.Combine(AppPaths.GetAppDataFolder(),
            Loc.CurrentLanguage == AppLanguage.Greek ? "help.el.txt" : "help.en.txt");

    private string DefaultText => Loc.CurrentLanguage == AppLanguage.Greek ? DefaultGreek : DefaultEnglish;

    private void Load()
    {
        try { HelpText = File.Exists(FilePath) ? File.ReadAllText(FilePath) : DefaultText; }
        catch (Exception ex) { HelpText = DefaultText; StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void StartEdit() { EditText = HelpText; IsEditing = true; }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void SaveEdit()
    {
        try
        {
            File.WriteAllText(FilePath, EditText);
            HelpText = EditText;
            IsEditing = false;
            StatusMessage = "✅ Help saved.";
        }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; }
    }

    [RelayCommand]
    private void RestoreDefault()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception ex) { StatusMessage = $"❌ {ex.Message}"; return; }
        HelpText = DefaultText;
        EditText = DefaultText;
    }

    private const string DefaultEnglish =
@"ERGANI MANAGER — QUICK HELP

Companies
  Add each company with its ERGANI credentials. The User Type decides how the login works:
  01 External, 02 Login with ""ERGANI"" credentials, 03 Login with credentials for Construction Works from EFKA.
  Use Test Connection before importing branches and employees.

Schedules
  Click a day to edit it, or select several days and use Bulk Apply.
  Days longer than 8 hours are split: the first part is the schedule, the rest is overtime.
  With days selected, the Submit buttons (schedule, overtime, holidays) send only those days;
  with nothing selected they send the whole month.

Holidays
  Add leave with the Holidays button, then send it with Submit Holidays.

Work cards and API log
  Failed submissions are kept and retried automatically when Ergani is back; see the API log.

Administration (super administrator)
  Backup database, delete the temporary local cache, or delete the database.

(Administrators can edit this page with the Edit button.)";

    private const string DefaultGreek =
@"ERGANI MANAGER — ΣΥΝΤΟΜΗ ΒΟΗΘΕΙΑ

Εταιρείες
  Προσθέστε κάθε εταιρεία με τους κωδικούς ΕΡΓΑΝΗ. Ο Τύπος χρήστη καθορίζει τη σύνδεση:
  01 Εξωτερικός, 02 Σύνδεση με κωδικούς ""ΕΡΓΑΝΗ"", 03 Σύνδεση με κωδικούς για Οικοδομοτεχνικά Έργα από ΕΦΚΑ.
  Χρησιμοποιήστε το Test Connection πριν την εισαγωγή υποκαταστημάτων και εργαζομένων.

Προγράμματα
  Πατήστε σε μια ημέρα για επεξεργασία ή επιλέξτε πολλές και χρησιμοποιήστε το Bulk Apply.
  Ημέρες άνω των 8 ωρών χωρίζονται: το πρώτο μέρος είναι πρόγραμμα, το υπόλοιπο υπερωρία.
  Με επιλεγμένες ημέρες τα κουμπιά υποβολής (πρόγραμμα, υπερωρίες, άδειες) στέλνουν μόνο αυτές·
  χωρίς επιλογή στέλνουν ολόκληρο τον μήνα.

Άδειες
  Προσθέστε άδεια με το κουμπί Holidays και στείλτε την με το Submit Holidays.

Κάρτες εργασίας και αρχείο API
  Οι αποτυχημένες υποβολές αποθηκεύονται και επαναλαμβάνονται αυτόματα όταν η Εργάνη επανέλθει.

Διαχείριση (υπερδιαχειριστής)
  Αντίγραφο ασφαλείας βάσης, διαγραφή της προσωρινής τοπικής cache ή διαγραφή της βάσης.

(Οι διαχειριστές μπορούν να επεξεργαστούν αυτή τη σελίδα με το κουμπί Επεξεργασία.)";
}
