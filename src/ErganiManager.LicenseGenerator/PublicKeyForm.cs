using static ErganiManager.LicenseGenerator.Strings;

namespace ErganiManager.LicenseGenerator;

/// <summary>Shows the line to paste into LicenseKeys.cs of the application. The layout is in PublicKeyForm.Designer.cs.</summary>
public partial class PublicKeyForm : Form
{
    public PublicKeyForm() : this(string.Empty)
    {
    }

    public PublicKeyForm(string publicKey)
    {
        InitializeComponent();

        if (System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime)
            return;

        Text = T("pub.title");
        lblInfo.Text = T("pub.text");
        btnCopy.Text = T("btn.copy");
        btnClose.Text = T("btn.close");
        txtKey.Text = $"public const string PublicKeyBase64 = \"{publicKey}\";";
    }

    private void btnCopy_Click(object? sender, EventArgs e)
    {
        try { Clipboard.SetText(txtKey.Text); } catch { /* the text can be selected and copied by hand */ }
    }
}
