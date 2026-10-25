#nullable disable
namespace ErganiManager.LicenseGenerator
{
    partial class MainForm
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - the layout of the window. The texts set here are the English
        /// defaults; the program replaces them with the chosen language (see MainForm.ApplyLanguage).
        /// </summary>
        private void InitializeComponent()
        {
            tabControl = new TabControl();
            tabNew = new TabPage();
            layoutNew = new TableLayoutPanel();
            grpKey = new GroupBox();
            layoutKey = new TableLayoutPanel();
            txtKeyPath = new TextBox();
            btnBrowse = new Button();
            flowKeyButtons = new FlowLayoutPanel();
            btnCreateKey = new Button();
            btnShowPublic = new Button();
            grpLicense = new GroupBox();
            layoutLicense = new TableLayoutPanel();
            lblLicensee = new Label();
            txtLicensee = new TextBox();
            lblType = new Label();
            cmbType = new ComboBox();
            lblShared = new Label();
            lblPc = new Label();
            flowPc = new FlowLayoutPanel();
            txtPc = new TextBox();
            lblPcHint = new Label();
            lblSeats = new Label();
            numSeats = new NumericUpDown();
            lblValid = new Label();
            flowValid = new FlowLayoutPanel();
            rbPermanent = new RadioButton();
            rbUntil = new RadioButton();
            dtpUntil = new DateTimePicker();
            lblModules = new Label();
            layoutModules = new TableLayoutPanel();
            chkAllModules = new CheckBox();
            clbModules = new CheckedListBox();
            btnGenerate = new Button();
            grpOutput = new GroupBox();
            layoutOutput = new TableLayoutPanel();
            txtKeyOutput = new TextBox();
            flowOutputButtons = new FlowLayoutPanel();
            btnCopy = new Button();
            btnSave = new Button();
            lblStatus = new Label();
            tabHistory = new TabPage();
            layoutHistory = new TableLayoutPanel();
            grid = new DataGridView();
            txtHistoryKey = new TextBox();
            flowHistoryButtons = new FlowLayoutPanel();
            btnHistoryCopy = new Button();
            panelFooter = new FlowLayoutPanel();
            lblLanguage = new Label();
            cmbLanguage = new ComboBox();
            tabControl.SuspendLayout();
            tabNew.SuspendLayout();
            layoutNew.SuspendLayout();
            grpKey.SuspendLayout();
            layoutKey.SuspendLayout();
            flowKeyButtons.SuspendLayout();
            grpLicense.SuspendLayout();
            layoutLicense.SuspendLayout();
            flowPc.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)numSeats).BeginInit();
            flowValid.SuspendLayout();
            layoutModules.SuspendLayout();
            grpOutput.SuspendLayout();
            layoutOutput.SuspendLayout();
            flowOutputButtons.SuspendLayout();
            tabHistory.SuspendLayout();
            layoutHistory.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)grid).BeginInit();
            flowHistoryButtons.SuspendLayout();
            panelFooter.SuspendLayout();
            SuspendLayout();
            // 
            // tabControl
            // 
            tabControl.Controls.Add(tabNew);
            tabControl.Controls.Add(tabHistory);
            tabControl.Dock = DockStyle.Fill;
            tabControl.Location = new Point(0, 0);
            tabControl.Name = "tabControl";
            tabControl.SelectedIndex = 0;
            tabControl.Size = new Size(880, 737);
            tabControl.TabIndex = 0;
            tabControl.SelectedIndexChanged += tabControl_SelectedIndexChanged;
            // 
            // tabNew
            // 
            tabNew.AutoScroll = true;
            tabNew.Controls.Add(layoutNew);
            tabNew.Location = new Point(4, 26);
            tabNew.Name = "tabNew";
            tabNew.Size = new Size(872, 707);
            tabNew.TabIndex = 0;
            tabNew.Text = "New license";
            tabNew.UseVisualStyleBackColor = true;
            // 
            // layoutNew
            // 
            layoutNew.AutoSize = true;
            layoutNew.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layoutNew.ColumnCount = 1;
            layoutNew.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutNew.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutNew.Controls.Add(grpKey, 0, 0);
            layoutNew.Controls.Add(grpLicense, 0, 1);
            layoutNew.Controls.Add(grpOutput, 0, 2);
            layoutNew.Dock = DockStyle.Top;
            layoutNew.Location = new Point(0, 0);
            layoutNew.MinimumSize = new Size(780, 0);
            layoutNew.Name = "layoutNew";
            layoutNew.Padding = new Padding(10);
            layoutNew.RowCount = 3;
            layoutNew.RowStyles.Add(new RowStyle());
            layoutNew.RowStyles.Add(new RowStyle());
            layoutNew.RowStyles.Add(new RowStyle());
            layoutNew.Size = new Size(855, 881);
            layoutNew.TabIndex = 0;
            // 
            // grpKey
            // 
            grpKey.AutoSize = true;
            grpKey.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grpKey.Controls.Add(layoutKey);
            grpKey.Dock = DockStyle.Fill;
            grpKey.Location = new Point(13, 13);
            grpKey.Name = "grpKey";
            grpKey.Padding = new Padding(8, 4, 8, 8);
            grpKey.Size = new Size(829, 116);
            grpKey.TabIndex = 0;
            grpKey.TabStop = false;
            grpKey.Text = "Signing key (private key)";
            // 
            // layoutKey
            // 
            layoutKey.AutoSize = true;
            layoutKey.ColumnCount = 2;
            layoutKey.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutKey.ColumnStyles.Add(new ColumnStyle());
            layoutKey.Controls.Add(txtKeyPath, 0, 0);
            layoutKey.Controls.Add(btnBrowse, 1, 0);
            layoutKey.Controls.Add(flowKeyButtons, 0, 1);
            layoutKey.Dock = DockStyle.Fill;
            layoutKey.Location = new Point(8, 21);
            layoutKey.Name = "layoutKey";
            layoutKey.RowCount = 2;
            layoutKey.RowStyles.Add(new RowStyle());
            layoutKey.RowStyles.Add(new RowStyle());
            layoutKey.Size = new Size(813, 87);
            layoutKey.TabIndex = 0;
            // 
            // txtKeyPath
            // 
            txtKeyPath.Dock = DockStyle.Fill;
            txtKeyPath.Location = new Point(3, 8);
            txtKeyPath.Margin = new Padding(3, 8, 3, 3);
            txtKeyPath.Name = "txtKeyPath";
            txtKeyPath.Size = new Size(701, 24);
            txtKeyPath.TabIndex = 0;
            txtKeyPath.TextChanged += txtKeyPath_TextChanged;
            // 
            // btnBrowse
            // 
            btnBrowse.AutoSize = true;
            btnBrowse.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnBrowse.Location = new Point(710, 5);
            btnBrowse.Margin = new Padding(3, 5, 3, 3);
            btnBrowse.MinimumSize = new Size(100, 32);
            btnBrowse.Name = "btnBrowse";
            btnBrowse.Padding = new Padding(12, 4, 12, 4);
            btnBrowse.Size = new Size(100, 35);
            btnBrowse.TabIndex = 1;
            btnBrowse.Text = "Browse…";
            btnBrowse.UseVisualStyleBackColor = true;
            btnBrowse.Click += btnBrowse_Click;
            // 
            // flowKeyButtons
            // 
            flowKeyButtons.AutoSize = true;
            layoutKey.SetColumnSpan(flowKeyButtons, 2);
            flowKeyButtons.Controls.Add(btnCreateKey);
            flowKeyButtons.Controls.Add(btnShowPublic);
            flowKeyButtons.Dock = DockStyle.Fill;
            flowKeyButtons.Location = new Point(0, 43);
            flowKeyButtons.Margin = new Padding(0);
            flowKeyButtons.Name = "flowKeyButtons";
            flowKeyButtons.Size = new Size(813, 44);
            flowKeyButtons.TabIndex = 2;
            // 
            // btnCreateKey
            // 
            btnCreateKey.AutoSize = true;
            btnCreateKey.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnCreateKey.Location = new Point(3, 6);
            btnCreateKey.Margin = new Padding(3, 6, 9, 3);
            btnCreateKey.MinimumSize = new Size(0, 32);
            btnCreateKey.Name = "btnCreateKey";
            btnCreateKey.Padding = new Padding(12, 4, 12, 4);
            btnCreateKey.Size = new Size(166, 35);
            btnCreateKey.TabIndex = 0;
            btnCreateKey.Text = "Create new key pair…";
            btnCreateKey.UseVisualStyleBackColor = true;
            btnCreateKey.Click += btnCreateKey_Click;
            // 
            // btnShowPublic
            // 
            btnShowPublic.AutoSize = true;
            btnShowPublic.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnShowPublic.Location = new Point(181, 6);
            btnShowPublic.Margin = new Padding(3, 6, 9, 3);
            btnShowPublic.MinimumSize = new Size(0, 32);
            btnShowPublic.Name = "btnShowPublic";
            btnShowPublic.Padding = new Padding(12, 4, 12, 4);
            btnShowPublic.Size = new Size(135, 35);
            btnShowPublic.TabIndex = 1;
            btnShowPublic.Text = "Show public key";
            btnShowPublic.UseVisualStyleBackColor = true;
            btnShowPublic.Click += btnShowPublic_Click;
            // 
            // grpLicense
            // 
            grpLicense.AutoSize = true;
            grpLicense.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grpLicense.Controls.Add(layoutLicense);
            grpLicense.Dock = DockStyle.Fill;
            grpLicense.Location = new Point(13, 135);
            grpLicense.Name = "grpLicense";
            grpLicense.Padding = new Padding(8, 4, 8, 8);
            grpLicense.Size = new Size(829, 557);
            grpLicense.TabIndex = 1;
            grpLicense.TabStop = false;
            grpLicense.Text = "License";
            // 
            // layoutLicense
            // 
            layoutLicense.AutoSize = true;
            layoutLicense.ColumnCount = 1;
            layoutLicense.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutLicense.Controls.Add(lblLicensee, 0, 0);
            layoutLicense.Controls.Add(txtLicensee, 0, 1);
            layoutLicense.Controls.Add(lblType, 0, 2);
            layoutLicense.Controls.Add(cmbType, 0, 3);
            layoutLicense.Controls.Add(lblShared, 0, 4);
            layoutLicense.Controls.Add(lblPc, 0, 5);
            layoutLicense.Controls.Add(flowPc, 0, 6);
            layoutLicense.Controls.Add(lblSeats, 0, 7);
            layoutLicense.Controls.Add(numSeats, 0, 8);
            layoutLicense.Controls.Add(lblValid, 0, 9);
            layoutLicense.Controls.Add(flowValid, 0, 10);
            layoutLicense.Controls.Add(lblModules, 0, 11);
            layoutLicense.Controls.Add(layoutModules, 0, 12);
            layoutLicense.Controls.Add(btnGenerate, 0, 13);
            layoutLicense.Dock = DockStyle.Fill;
            layoutLicense.Location = new Point(8, 21);
            layoutLicense.Name = "layoutLicense";
            layoutLicense.RowCount = 14;
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.RowStyles.Add(new RowStyle());
            layoutLicense.Size = new Size(813, 528);
            layoutLicense.TabIndex = 0;
            // 
            // lblLicensee
            // 
            lblLicensee.AutoSize = true;
            lblLicensee.Location = new Point(3, 10);
            lblLicensee.Margin = new Padding(3, 10, 3, 2);
            lblLicensee.Name = "lblLicensee";
            lblLicensee.Size = new Size(123, 17);
            lblLicensee.TabIndex = 0;
            lblLicensee.Text = "Customer / licensee";
            // 
            // txtLicensee
            // 
            txtLicensee.Dock = DockStyle.Fill;
            txtLicensee.Location = new Point(3, 32);
            txtLicensee.Name = "txtLicensee";
            txtLicensee.Size = new Size(807, 24);
            txtLicensee.TabIndex = 1;
            // 
            // lblType
            // 
            lblType.AutoSize = true;
            lblType.Location = new Point(3, 69);
            lblType.Margin = new Padding(3, 10, 3, 2);
            lblType.Name = "lblType";
            lblType.Size = new Size(79, 17);
            lblType.TabIndex = 2;
            lblType.Text = "License type";
            // 
            // cmbType
            // 
            cmbType.Dock = DockStyle.Fill;
            cmbType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbType.FormattingEnabled = true;
            cmbType.Items.AddRange(new object[] { "Single PC - locked to one PC", "Single user - one PC at a time (any PC)", "Multi-user - several PCs at the same time", "Unlimited PCs (site license)" });
            cmbType.Location = new Point(3, 91);
            cmbType.Name = "cmbType";
            cmbType.Size = new Size(807, 25);
            cmbType.TabIndex = 3;
            cmbType.SelectedIndexChanged += cmbType_SelectedIndexChanged;
            // 
            // lblShared
            // 
            lblShared.AutoSize = true;
            lblShared.ForeColor = SystemColors.GrayText;
            lblShared.Location = new Point(3, 123);
            lblShared.Margin = new Padding(3, 4, 3, 0);
            lblShared.Name = "lblShared";
            lblShared.Size = new Size(550, 17);
            lblShared.TabIndex = 4;
            lblShared.Text = "The last three types need every PC to use the same shared database (SQL Server / MariaDB).";
            // 
            // lblPc
            // 
            lblPc.AutoSize = true;
            lblPc.Location = new Point(3, 150);
            lblPc.Margin = new Padding(3, 10, 3, 2);
            lblPc.Name = "lblPc";
            lblPc.Size = new Size(190, 17);
            lblPc.TabIndex = 5;
            lblPc.Text = "PC code (sent by the customer)";
            // 
            // flowPc
            // 
            flowPc.AutoSize = true;
            flowPc.Controls.Add(txtPc);
            flowPc.Controls.Add(lblPcHint);
            flowPc.Dock = DockStyle.Fill;
            flowPc.Location = new Point(0, 169);
            flowPc.Margin = new Padding(0);
            flowPc.Name = "flowPc";
            flowPc.Size = new Size(813, 31);
            flowPc.TabIndex = 6;
            // 
            // txtPc
            // 
            txtPc.CharacterCasing = CharacterCasing.Upper;
            txtPc.Font = new Font("Consolas", 11F);
            txtPc.Location = new Point(3, 3);
            txtPc.Name = "txtPc";
            txtPc.Size = new Size(320, 25);
            txtPc.TabIndex = 0;
            txtPc.Leave += txtPc_Leave;
            // 
            // lblPcHint
            // 
            lblPcHint.AutoSize = true;
            lblPcHint.ForeColor = SystemColors.GrayText;
            lblPcHint.Location = new Point(334, 8);
            lblPcHint.Margin = new Padding(8, 8, 0, 0);
            lblPcHint.Name = "lblPcHint";
            lblPcHint.Size = new Size(417, 17);
            lblPcHint.TabIndex = 1;
            lblPcHint.Text = "like 1A2B-3C4D-5E6F-7A8B-9C0D (only for the license type \"Single PC\")";
            // 
            // lblSeats
            // 
            lblSeats.AutoSize = true;
            lblSeats.Location = new Point(3, 210);
            lblSeats.Margin = new Padding(3, 10, 3, 2);
            lblSeats.Name = "lblSeats";
            lblSeats.Size = new Size(198, 17);
            lblSeats.TabIndex = 7;
            lblSeats.Text = "Number of PCs at the same time";
            // 
            // numSeats
            // 
            numSeats.Location = new Point(3, 232);
            numSeats.Maximum = new decimal(new int[] { 999, 0, 0, 0 });
            numSeats.Minimum = new decimal(new int[] { 2, 0, 0, 0 });
            numSeats.Name = "numSeats";
            numSeats.Size = new Size(90, 24);
            numSeats.TabIndex = 8;
            numSeats.Value = new decimal(new int[] { 2, 0, 0, 0 });
            // 
            // lblValid
            // 
            lblValid.AutoSize = true;
            lblValid.Location = new Point(3, 269);
            lblValid.Margin = new Padding(3, 10, 3, 2);
            lblValid.Name = "lblValid";
            lblValid.Size = new Size(49, 17);
            lblValid.TabIndex = 9;
            lblValid.Text = "Validity";
            // 
            // flowValid
            // 
            flowValid.AutoSize = true;
            flowValid.Controls.Add(rbPermanent);
            flowValid.Controls.Add(rbUntil);
            flowValid.Controls.Add(dtpUntil);
            flowValid.Dock = DockStyle.Fill;
            flowValid.Location = new Point(0, 288);
            flowValid.Margin = new Padding(0);
            flowValid.Name = "flowValid";
            flowValid.Size = new Size(813, 30);
            flowValid.TabIndex = 10;
            // 
            // rbPermanent
            // 
            rbPermanent.AutoSize = true;
            rbPermanent.Checked = true;
            rbPermanent.Location = new Point(3, 3);
            rbPermanent.Name = "rbPermanent";
            rbPermanent.Size = new Size(88, 21);
            rbPermanent.TabIndex = 0;
            rbPermanent.TabStop = true;
            rbPermanent.Text = "Permanent";
            rbPermanent.UseVisualStyleBackColor = true;
            rbPermanent.CheckedChanged += validity_CheckedChanged;
            // 
            // rbUntil
            // 
            rbUntil.AutoSize = true;
            rbUntil.Location = new Point(118, 3);
            rbUntil.Margin = new Padding(24, 3, 3, 3);
            rbUntil.Name = "rbUntil";
            rbUntil.Size = new Size(82, 21);
            rbUntil.TabIndex = 1;
            rbUntil.Text = "Valid until";
            rbUntil.UseVisualStyleBackColor = true;
            rbUntil.CheckedChanged += validity_CheckedChanged;
            // 
            // dtpUntil
            // 
            dtpUntil.Format = DateTimePickerFormat.Short;
            dtpUntil.Location = new Point(206, 3);
            dtpUntil.Name = "dtpUntil";
            dtpUntil.Size = new Size(150, 24);
            dtpUntil.TabIndex = 2;
            // 
            // lblModules
            // 
            lblModules.AutoSize = true;
            lblModules.Location = new Point(3, 328);
            lblModules.Margin = new Padding(3, 10, 3, 2);
            lblModules.Name = "lblModules";
            lblModules.Size = new Size(59, 17);
            lblModules.TabIndex = 11;
            lblModules.Text = "Modules";
            // 
            // layoutModules
            // 
            layoutModules.AutoSize = true;
            layoutModules.ColumnCount = 2;
            layoutModules.ColumnStyles.Add(new ColumnStyle());
            layoutModules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutModules.Controls.Add(chkAllModules, 0, 0);
            layoutModules.Controls.Add(clbModules, 1, 0);
            layoutModules.Dock = DockStyle.Fill;
            layoutModules.Location = new Point(0, 347);
            layoutModules.Margin = new Padding(0);
            layoutModules.Name = "layoutModules";
            layoutModules.RowCount = 1;
            layoutModules.RowStyles.Add(new RowStyle());
            layoutModules.Size = new Size(813, 126);
            layoutModules.TabIndex = 12;
            // 
            // chkAllModules
            // 
            chkAllModules.AutoSize = true;
            chkAllModules.Checked = true;
            chkAllModules.CheckState = CheckState.Checked;
            chkAllModules.Location = new Point(3, 6);
            chkAllModules.Margin = new Padding(3, 6, 24, 3);
            chkAllModules.Name = "chkAllModules";
            chkAllModules.Size = new Size(95, 21);
            chkAllModules.TabIndex = 0;
            chkAllModules.Text = "All modules";
            chkAllModules.UseVisualStyleBackColor = true;
            chkAllModules.CheckedChanged += chkAllModules_CheckedChanged;
            // 
            // clbModules
            // 
            clbModules.CheckOnClick = true;
            clbModules.Dock = DockStyle.Fill;
            clbModules.FormattingEnabled = true;
            clbModules.IntegralHeight = false;
            clbModules.Location = new Point(125, 3);
            clbModules.Name = "clbModules";
            clbModules.Size = new Size(685, 120);
            clbModules.TabIndex = 1;
            // 
            // btnGenerate
            // 
            btnGenerate.AutoSize = true;
            btnGenerate.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnGenerate.Location = new Point(3, 487);
            btnGenerate.Margin = new Padding(3, 14, 3, 3);
            btnGenerate.MinimumSize = new Size(260, 38);
            btnGenerate.Name = "btnGenerate";
            btnGenerate.Padding = new Padding(12, 4, 12, 4);
            btnGenerate.Size = new Size(260, 38);
            btnGenerate.TabIndex = 13;
            btnGenerate.Text = "Create license key";
            btnGenerate.UseVisualStyleBackColor = true;
            btnGenerate.Click += btnGenerate_Click;
            // 
            // grpOutput
            // 
            grpOutput.Controls.Add(layoutOutput);
            grpOutput.Dock = DockStyle.Fill;
            grpOutput.Location = new Point(13, 698);
            grpOutput.MinimumSize = new Size(0, 170);
            grpOutput.Name = "grpOutput";
            grpOutput.Padding = new Padding(8, 4, 8, 8);
            grpOutput.Size = new Size(829, 170);
            grpOutput.TabIndex = 2;
            grpOutput.TabStop = false;
            grpOutput.Text = "License key";
            // 
            // layoutOutput
            // 
            layoutOutput.ColumnCount = 1;
            layoutOutput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutOutput.Controls.Add(txtKeyOutput, 0, 0);
            layoutOutput.Controls.Add(flowOutputButtons, 0, 1);
            layoutOutput.Dock = DockStyle.Fill;
            layoutOutput.Location = new Point(8, 21);
            layoutOutput.Name = "layoutOutput";
            layoutOutput.RowCount = 2;
            layoutOutput.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layoutOutput.RowStyles.Add(new RowStyle());
            layoutOutput.Size = new Size(813, 141);
            layoutOutput.TabIndex = 0;
            // 
            // txtKeyOutput
            // 
            txtKeyOutput.Dock = DockStyle.Fill;
            txtKeyOutput.Font = new Font("Consolas", 9.5F);
            txtKeyOutput.Location = new Point(3, 8);
            txtKeyOutput.Margin = new Padding(3, 8, 3, 3);
            txtKeyOutput.Multiline = true;
            txtKeyOutput.Name = "txtKeyOutput";
            txtKeyOutput.ReadOnly = true;
            txtKeyOutput.ScrollBars = ScrollBars.Vertical;
            txtKeyOutput.Size = new Size(807, 86);
            txtKeyOutput.TabIndex = 0;
            // 
            // flowOutputButtons
            // 
            flowOutputButtons.AutoSize = true;
            flowOutputButtons.Controls.Add(btnCopy);
            flowOutputButtons.Controls.Add(btnSave);
            flowOutputButtons.Controls.Add(lblStatus);
            flowOutputButtons.Dock = DockStyle.Fill;
            flowOutputButtons.Location = new Point(0, 97);
            flowOutputButtons.Margin = new Padding(0);
            flowOutputButtons.Name = "flowOutputButtons";
            flowOutputButtons.Size = new Size(813, 44);
            flowOutputButtons.TabIndex = 1;
            // 
            // btnCopy
            // 
            btnCopy.AutoSize = true;
            btnCopy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnCopy.Location = new Point(3, 6);
            btnCopy.Margin = new Padding(3, 6, 9, 3);
            btnCopy.MinimumSize = new Size(110, 32);
            btnCopy.Name = "btnCopy";
            btnCopy.Padding = new Padding(12, 4, 12, 4);
            btnCopy.Size = new Size(110, 35);
            btnCopy.TabIndex = 0;
            btnCopy.Text = "Copy";
            btnCopy.UseVisualStyleBackColor = true;
            btnCopy.Click += btnCopy_Click;
            // 
            // btnSave
            // 
            btnSave.AutoSize = true;
            btnSave.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnSave.Location = new Point(125, 6);
            btnSave.Margin = new Padding(3, 6, 9, 3);
            btnSave.MinimumSize = new Size(150, 32);
            btnSave.Name = "btnSave";
            btnSave.Padding = new Padding(12, 4, 12, 4);
            btnSave.Size = new Size(150, 35);
            btnSave.TabIndex = 1;
            btnSave.Text = "Save as file…";
            btnSave.UseVisualStyleBackColor = true;
            btnSave.Click += btnSave_Click;
            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(296, 12);
            lblStatus.Margin = new Padding(12, 12, 0, 0);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(0, 17);
            lblStatus.TabIndex = 2;
            // 
            // tabHistory
            // 
            tabHistory.Controls.Add(layoutHistory);
            tabHistory.Location = new Point(4, 24);
            tabHistory.Name = "tabHistory";
            tabHistory.Size = new Size(872, 709);
            tabHistory.TabIndex = 1;
            tabHistory.Text = "History";
            tabHistory.UseVisualStyleBackColor = true;
            // 
            // layoutHistory
            // 
            layoutHistory.ColumnCount = 1;
            layoutHistory.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layoutHistory.Controls.Add(grid, 0, 0);
            layoutHistory.Controls.Add(txtHistoryKey, 0, 1);
            layoutHistory.Controls.Add(flowHistoryButtons, 0, 2);
            layoutHistory.Dock = DockStyle.Fill;
            layoutHistory.Location = new Point(0, 0);
            layoutHistory.Name = "layoutHistory";
            layoutHistory.Padding = new Padding(10);
            layoutHistory.RowCount = 3;
            layoutHistory.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layoutHistory.RowStyles.Add(new RowStyle(SizeType.Absolute, 130F));
            layoutHistory.RowStyles.Add(new RowStyle());
            layoutHistory.Size = new Size(872, 709);
            layoutHistory.TabIndex = 0;
            // 
            // grid
            // 
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Dock = DockStyle.Fill;
            grid.Location = new Point(13, 13);
            grid.MultiSelect = false;
            grid.Name = "grid";
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.Size = new Size(846, 509);
            grid.TabIndex = 0;
            grid.SelectionChanged += grid_SelectionChanged;
            // 
            // txtHistoryKey
            // 
            txtHistoryKey.Dock = DockStyle.Fill;
            txtHistoryKey.Font = new Font("Consolas", 9.5F);
            txtHistoryKey.Location = new Point(13, 533);
            txtHistoryKey.Margin = new Padding(3, 8, 3, 3);
            txtHistoryKey.Multiline = true;
            txtHistoryKey.Name = "txtHistoryKey";
            txtHistoryKey.ReadOnly = true;
            txtHistoryKey.ScrollBars = ScrollBars.Vertical;
            txtHistoryKey.Size = new Size(846, 119);
            txtHistoryKey.TabIndex = 1;
            // 
            // flowHistoryButtons
            // 
            flowHistoryButtons.AutoSize = true;
            flowHistoryButtons.Controls.Add(btnHistoryCopy);
            flowHistoryButtons.Dock = DockStyle.Fill;
            flowHistoryButtons.Location = new Point(10, 655);
            flowHistoryButtons.Margin = new Padding(0);
            flowHistoryButtons.Name = "flowHistoryButtons";
            flowHistoryButtons.Size = new Size(852, 44);
            flowHistoryButtons.TabIndex = 2;
            // 
            // btnHistoryCopy
            // 
            btnHistoryCopy.AutoSize = true;
            btnHistoryCopy.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnHistoryCopy.Location = new Point(3, 6);
            btnHistoryCopy.Margin = new Padding(3, 6, 9, 3);
            btnHistoryCopy.MinimumSize = new Size(110, 32);
            btnHistoryCopy.Name = "btnHistoryCopy";
            btnHistoryCopy.Padding = new Padding(12, 4, 12, 4);
            btnHistoryCopy.Size = new Size(110, 35);
            btnHistoryCopy.TabIndex = 0;
            btnHistoryCopy.Text = "Copy";
            btnHistoryCopy.UseVisualStyleBackColor = true;
            btnHistoryCopy.Click += btnHistoryCopy_Click;
            // 
            // panelFooter
            // 
            panelFooter.AutoSize = true;
            panelFooter.Controls.Add(lblLanguage);
            panelFooter.Controls.Add(cmbLanguage);
            panelFooter.Dock = DockStyle.Bottom;
            panelFooter.Location = new Point(0, 737);
            panelFooter.Name = "panelFooter";
            panelFooter.Padding = new Padding(10, 6, 10, 6);
            panelFooter.Size = new Size(880, 43);
            panelFooter.TabIndex = 1;
            // 
            // lblLanguage
            // 
            lblLanguage.AutoSize = true;
            lblLanguage.Location = new Point(13, 13);
            lblLanguage.Margin = new Padding(3, 7, 6, 3);
            lblLanguage.Name = "lblLanguage";
            lblLanguage.Size = new Size(65, 17);
            lblLanguage.TabIndex = 0;
            lblLanguage.Text = "Language";
            // 
            // cmbLanguage
            // 
            cmbLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLanguage.FormattingEnabled = true;
            cmbLanguage.Items.AddRange(new object[] { "English", "Ελληνικά" });
            cmbLanguage.Location = new Point(87, 9);
            cmbLanguage.Name = "cmbLanguage";
            cmbLanguage.Size = new Size(150, 25);
            cmbLanguage.TabIndex = 1;
            cmbLanguage.SelectedIndexChanged += cmbLanguage_SelectedIndexChanged;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(880, 780);
            Controls.Add(tabControl);
            Controls.Add(panelFooter);
            Font = new Font("Segoe UI", 9.5F);
            MinimumSize = new Size(880, 720);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Ergani Manager - License generator";
            tabControl.ResumeLayout(false);
            tabNew.ResumeLayout(false);
            tabNew.PerformLayout();
            layoutNew.ResumeLayout(false);
            layoutNew.PerformLayout();
            grpKey.ResumeLayout(false);
            grpKey.PerformLayout();
            layoutKey.ResumeLayout(false);
            layoutKey.PerformLayout();
            flowKeyButtons.ResumeLayout(false);
            flowKeyButtons.PerformLayout();
            grpLicense.ResumeLayout(false);
            grpLicense.PerformLayout();
            layoutLicense.ResumeLayout(false);
            layoutLicense.PerformLayout();
            flowPc.ResumeLayout(false);
            flowPc.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)numSeats).EndInit();
            flowValid.ResumeLayout(false);
            flowValid.PerformLayout();
            layoutModules.ResumeLayout(false);
            layoutModules.PerformLayout();
            grpOutput.ResumeLayout(false);
            layoutOutput.ResumeLayout(false);
            layoutOutput.PerformLayout();
            flowOutputButtons.ResumeLayout(false);
            flowOutputButtons.PerformLayout();
            tabHistory.ResumeLayout(false);
            layoutHistory.ResumeLayout(false);
            layoutHistory.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)grid).EndInit();
            flowHistoryButtons.ResumeLayout(false);
            flowHistoryButtons.PerformLayout();
            panelFooter.ResumeLayout(false);
            panelFooter.PerformLayout();
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl;
        private System.Windows.Forms.TabPage tabNew;
        private System.Windows.Forms.TableLayoutPanel layoutNew;
        private System.Windows.Forms.GroupBox grpKey;
        private System.Windows.Forms.TableLayoutPanel layoutKey;
        private System.Windows.Forms.TextBox txtKeyPath;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.FlowLayoutPanel flowKeyButtons;
        private System.Windows.Forms.Button btnCreateKey;
        private System.Windows.Forms.Button btnShowPublic;
        private System.Windows.Forms.GroupBox grpLicense;
        private System.Windows.Forms.TableLayoutPanel layoutLicense;
        private System.Windows.Forms.Label lblLicensee;
        private System.Windows.Forms.TextBox txtLicensee;
        private System.Windows.Forms.Label lblType;
        private System.Windows.Forms.ComboBox cmbType;
        private System.Windows.Forms.Label lblShared;
        private System.Windows.Forms.Label lblPc;
        private System.Windows.Forms.FlowLayoutPanel flowPc;
        private System.Windows.Forms.TextBox txtPc;
        private System.Windows.Forms.Label lblPcHint;
        private System.Windows.Forms.Label lblSeats;
        private System.Windows.Forms.NumericUpDown numSeats;
        private System.Windows.Forms.Label lblValid;
        private System.Windows.Forms.FlowLayoutPanel flowValid;
        private System.Windows.Forms.RadioButton rbPermanent;
        private System.Windows.Forms.RadioButton rbUntil;
        private System.Windows.Forms.DateTimePicker dtpUntil;
        private System.Windows.Forms.Label lblModules;
        private System.Windows.Forms.TableLayoutPanel layoutModules;
        private System.Windows.Forms.CheckBox chkAllModules;
        private System.Windows.Forms.CheckedListBox clbModules;
        private System.Windows.Forms.Button btnGenerate;
        private System.Windows.Forms.GroupBox grpOutput;
        private System.Windows.Forms.TableLayoutPanel layoutOutput;
        private System.Windows.Forms.TextBox txtKeyOutput;
        private System.Windows.Forms.FlowLayoutPanel flowOutputButtons;
        private System.Windows.Forms.Button btnCopy;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.TabPage tabHistory;
        private System.Windows.Forms.TableLayoutPanel layoutHistory;
        private System.Windows.Forms.DataGridView grid;
        private System.Windows.Forms.TextBox txtHistoryKey;
        private System.Windows.Forms.FlowLayoutPanel flowHistoryButtons;
        private System.Windows.Forms.Button btnHistoryCopy;
        private System.Windows.Forms.FlowLayoutPanel panelFooter;
        private System.Windows.Forms.Label lblLanguage;
        private System.Windows.Forms.ComboBox cmbLanguage;
    }
}
