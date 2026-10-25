using System.Globalization;
using ErganiManager.Licensing;
using static ErganiManager.LicenseGenerator.Strings;

namespace ErganiManager.LicenseGenerator;

/// <summary>
/// The supplier's window for creating license keys. The controls are laid out in MainForm.Designer.cs, which you can
/// open in the Visual Studio designer; this file holds what the window does.
/// Pick the private key file, enter the customer, choose the license type (single PC, single user, multi-user,
/// unlimited), the validity and the modules, and press the button. Every key is kept in the History tab.
/// </summary>
public partial class MainForm : Form
{
    private Settings _settings = new();
    private bool _ready;

    public MainForm()
    {
        InitializeComponent();

        // The Visual Studio designer only needs the layout above; nothing below runs there.
        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            return;

        _settings = Settings.Load();
        Greek = _settings.Greek;
        cmbLanguage.SelectedIndex = Greek ? 1 : 0;
        txtKeyPath.Text = _settings.PrivateKeyPath;
        dtpUntil.Value = DateTime.Today.AddYears(1);
        cmbType.SelectedIndex = 0;

        ApplyLanguage();
        UpdateEnabled();
        _ready = true;
    }

    // -- Texts (the designer holds the English defaults) --------------------------------------------------

    private void ApplyLanguage()
    {
        Text = T("title");
        tabNew.Text = T("tab.new");
        tabHistory.Text = T("tab.history");
        grpKey.Text = T("grp.key");
        btnBrowse.Text = T("btn.browse");
        btnCreateKey.Text = T("btn.createKey");
        btnShowPublic.Text = T("btn.showPublic");
        grpLicense.Text = T("grp.license");
        lblLicensee.Text = T("lbl.licensee");
        lblType.Text = T("lbl.type");
        lblShared.Text = T("hint.shared");
        lblPc.Text = T("lbl.pc");
        lblPcHint.Text = T("hint.pc");
        lblSeats.Text = T("lbl.seats");
        lblValid.Text = T("lbl.valid");
        rbPermanent.Text = T("rb.permanent");
        rbUntil.Text = T("rb.until");
        lblModules.Text = T("lbl.modules");
        chkAllModules.Text = T("chk.all");
        btnGenerate.Text = T("btn.generate");
        grpOutput.Text = T("grp.output");
        btnCopy.Text = T("btn.copy");
        btnSave.Text = T("btn.save");
        btnHistoryCopy.Text = T("btn.copy");
        lblLanguage.Text = T("lbl.language");

        // The license types keep their selection when the language changes.
        var type = Math.Max(0, cmbType.SelectedIndex);
        cmbType.Items.Clear();
        for (var i = 0; i < 4; i++) cmbType.Items.Add(T("type." + i));
        cmbType.SelectedIndex = type;

        // The module list keeps its ticks too.
        var ticked = CheckedModuleIds();
        clbModules.Items.Clear();
        foreach (var module in LicenseModules.Names)
            clbModules.Items.Add(new ModuleItem(module, T("mod." + module)), ticked.Contains(module));

        LoadHistory();
    }

    private sealed record ModuleItem(string Id, string Text)
    {
        public override string ToString() => Text;
    }

    private List<string> CheckedModuleIds() =>
        clbModules.CheckedItems.Cast<ModuleItem>().Select(m => m.Id).ToList();

    /// <summary>Which fields matter for the chosen license type and validity.</summary>
    private void UpdateEnabled()
    {
        var type = cmbType.SelectedIndex;
        var locked = type == 0;       // the PC code is only for a license locked to one PC
        var multi = type == 2;        // the number of PCs only for the multi-user license

        
        lblSeats.Enabled = numSeats.Enabled = multi;
        dtpUntil.Enabled = rbUntil.Checked;
        clbModules.Enabled = !chkAllModules.Checked;
    }

    // -- Events -------------------------------------------------------------------------------------------------

    private void cmbType_SelectedIndexChanged(object? sender, EventArgs e) => UpdateEnabled();

    private void validity_CheckedChanged(object? sender, EventArgs e) => UpdateEnabled();

    private void chkAllModules_CheckedChanged(object? sender, EventArgs e) => UpdateEnabled();

    private void txtPc_Leave(object? sender, EventArgs e)
    {
        var normalized = MachineCode.Normalize(txtPc.Text);
        if (normalized.Length == 20) txtPc.Text = FormatCode(normalized);
    }

    private void txtKeyPath_TextChanged(object? sender, EventArgs e)
    {
        if (!_ready) return;
        _settings.PrivateKeyPath = txtKeyPath.Text.Trim();
        _settings.Save();
    }

    private void cmbLanguage_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (!_ready && Greek == (cmbLanguage.SelectedIndex == 1)) return;

        Greek = cmbLanguage.SelectedIndex == 1;
        _settings.Greek = Greek;
        _settings.Save();
        ApplyLanguage();
    }

    private void tabControl_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (tabControl.SelectedTab == tabHistory) LoadHistory();
    }

    private void grid_SelectionChanged(object? sender, EventArgs e)
    {
        txtHistoryKey.Text = grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Tag is IssuedLicense issued
            ? issued.Key
            : string.Empty;
    }

    private void btnCopy_Click(object? sender, EventArgs e) => CopyText(txtKeyOutput.Text);

    private void btnHistoryCopy_Click(object? sender, EventArgs e) => CopyText(txtHistoryKey.Text);

    private void btnSave_Click(object? sender, EventArgs e) => SaveKeyFile(txtKeyOutput.Text);

    // -- Key file -------------------------------------------------------------------------------------------------

    private void btnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = T("dlg.keyfile"),
            Filter = $"{T("dlg.keyfile")} (*.key)|*.key|{T("dlg.all")} (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) txtKeyPath.Text = dialog.FileName;
    }

    private void btnCreateKey_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Title = T("btn.createKey"),
            FileName = "license-private.key",
            Filter = $"{T("dlg.keyfile")} (*.key)|*.key|{T("dlg.all")} (*.*)|*.*",
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (File.Exists(dialog.FileName) &&
            MessageBox.Show(this, T("ask.overwrite"), T("title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        try
        {
            var (privateKey, publicKey) = LicenseCodec.CreateKeyPair();
            File.WriteAllText(dialog.FileName, privateKey);
            txtKeyPath.Text = dialog.FileName;

            MessageBox.Show(this, F("msg.keyCreated", dialog.FileName), T("title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            using var form = new PublicKeyForm(publicKey);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void btnShowPublic_Click(object? sender, EventArgs e)
    {
        try
        {
            var path = txtKeyPath.Text.Trim();
            if (!File.Exists(path)) { Warn(T("err.nokey")); return; }

            using var form = new PublicKeyForm(LicenseCodec.GetPublicKey(File.ReadAllText(path)));
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    // -- Creating a license -----------------------------------------------------------------------------------------

    private static string FormatCode(string normalized) =>
        string.Join("-", Enumerable.Range(0, 5).Select(i => normalized.Substring(i * 4, 4)));

    private void btnGenerate_Click(object? sender, EventArgs e)
    {
        try
        {
            var keyFile = txtKeyPath.Text.Trim();
            if (!File.Exists(keyFile)) { Warn(T("err.nokey")); return; }

            var licensee = txtLicensee.Text.Trim();
            if (licensee.Length == 0) { Warn(T("err.licensee")); txtLicensee.Focus(); return; }

            // 0 = locked to one PC, 1 = single user (one PC at a time), 2 = multi-user (N PCs), 3 = unlimited
            var type = cmbType.SelectedIndex;
            string pc = "*";
            var seats = 0;
            switch (type)
            {
                case 0:
                    var normalized = MachineCode.Normalize(txtPc.Text);
                    if (normalized.Length != 20) { Warn(T("err.pc")); txtPc.Focus(); return; }
                    pc = FormatCode(normalized);
                    txtPc.Text = pc;
                    break;
                case 1:
                    seats = 1;
                    break;
                case 2:
                    seats = (int)numSeats.Value;
                    break;
            }

            List<string> modules;
            if (chkAllModules.Checked) modules = new List<string> { LicenseModules.All };
            else
            {
                modules = CheckedModuleIds();
                if (modules.Count == 0) { Warn(T("err.modules")); return; }
            }

            var expires = rbUntil.Checked ? dtpUntil.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

            var payload = new LicensePayload
            {
                Licensee = licensee,
                MachineCode = pc,
                Seats = seats,
                Issued = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Expires = expires,
                Modules = modules
            };

            var key = LicenseCodec.Issue(payload, File.ReadAllText(keyFile));
            txtKeyOutput.Text = key;

            History.Add(new IssuedLicense
            {
                IssuedAt = DateTime.Now,
                Licensee = licensee,
                PcCode = pc,
                Seats = seats,
                Expires = expires,
                Modules = string.Join(", ", modules),
                Key = key
            });

            lblStatus.ForeColor = Color.ForestGreen;
            lblStatus.Text = T("msg.created");
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    // -- History ---------------------------------------------------------------------------------------------------

    private void LoadHistory()
    {
        grid.Columns.Clear();
        grid.Rows.Clear();
        foreach (var header in new[] { "col.date", "col.licensee", "col.type", "col.pc", "col.valid", "col.modules" })
            grid.Columns.Add(header, T(header));

        foreach (var issued in History.Load().OrderByDescending(h => h.IssuedAt))
        {
            var type = issued.PcCode != "*" ? T("hist.single")
                : issued.Seats == 1 ? T("hist.floating")
                : issued.Seats > 1 ? F("hist.multi", issued.Seats)
                : T("hist.unlimited");

            var index = grid.Rows.Add(
                issued.IssuedAt.ToString("dd/MM/yyyy HH:mm"),
                issued.Licensee,
                type,
                issued.PcCode == "*" ? T("any") : issued.PcCode,
                issued.Expires ?? T("permanent"),
                issued.Modules == LicenseModules.All ? T("all") : issued.Modules);
            grid.Rows[index].Tag = issued;
        }
        txtHistoryKey.Text = string.Empty;
    }

    // -- Small helpers ---------------------------------------------------------------------------------------------------

    private void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { Warn(T("err.nokeyyet")); return; }

        try
        {
            Clipboard.SetText(text);
            lblStatus.ForeColor = SystemColors.GrayText;
            lblStatus.Text = T("msg.copied");
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private void SaveKeyFile(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { Warn(T("err.nokeyyet")); return; }

        using var dialog = new SaveFileDialog
        {
            Title = T("btn.save"),
            FileName = SafeFileName(txtLicensee.Text) + ".lic",
            Filter = $"{T("dlg.licfile")} (*.lic)|*.lic|{T("dlg.all")} (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, key);
            lblStatus.ForeColor = SystemColors.GrayText;
            lblStatus.Text = F("msg.saved", dialog.FileName);
        }
        catch (Exception ex)
        {
            Error(ex);
        }
    }

    private static string SafeFileName(string text)
    {
        var name = new string(text.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return name.Length == 0 ? "license" : name;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, T("title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void Error(Exception ex) =>
        MessageBox.Show(this, F("err.generic", ex.Message), T("title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
}
