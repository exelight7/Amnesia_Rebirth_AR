using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerWindow());
    }
}

internal sealed class InstallerWindow : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(23, 22, 22);
    private static readonly Color CardColor = Color.FromArgb(34, 32, 30);
    private static readonly Color TextColor = Color.FromArgb(243, 237, 223);
    private static readonly Color MutedColor = Color.FromArgb(190, 182, 166);
    private static readonly Color GoldColor = Color.FromArgb(218, 181, 106);
    private readonly TextBox folderBox;
    private readonly Button browseButton;
    private readonly Button installButton;
    private readonly Button uninstallButton;
    private readonly Label statusLabel;
    private readonly Label percentLabel;
    private readonly GoldenProgress progress;
    private bool busy;

    internal InstallerWindow()
    {
        Text = "Amnesia Rebirth Arabic 1.0.0";
        Icon = Icon.ExtractAssociatedIcon(typeof(Program).Assembly.Location);
        ClientSize = new Size(820, 605);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 11F, FontStyle.Regular, GraphicsUnit.Point);
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = false;

        Label eyebrow = MakeLabel("AMNESIA: REBIRTH", new Rectangle(40, 27, 740, 26), GoldColor, 11F);
        eyebrow.RightToLeft = RightToLeft.No;
        eyebrow.TextAlign = ContentAlignment.MiddleRight;
        Label title = MakeLabel("رحلتك… باللغة العربية", new Rectangle(40, 60, 740, 57), TextColor, 28F);
        title.Font = new Font("Segoe UI", 28F, FontStyle.Bold);
        Label intro = MakeLabel("تعريب Amnesia Rebirth  ·  الإصدار 1.0.0", new Rectangle(40, 123, 740, 31), MutedColor, 12F);
        Controls.Add(eyebrow);
        Controls.Add(title);
        Controls.Add(intro);

        WarmCard folderCard = new WarmCard(CardColor);
        folderCard.SetBounds(36, 180, 748, 160);
        Controls.Add(folderCard);
        folderCard.Controls.Add(MakeLabel("مجلد اللعبة", new Rectangle(24, 16, 700, 31), TextColor, 13F));
        folderCard.Controls.Add(MakeLabel("اختر المجلد الذي يحتوي على AmnesiaRebirth.exe", new Rectangle(24, 50, 700, 25), MutedColor, 10F));

        browseButton = MakeButton("استعراض…", new Rectangle(24, 91, 126, 43), false);
        browseButton.TabIndex = 1;
        browseButton.AccessibleName = "اختيار مجلد اللعبة";
        browseButton.Click += BrowseClicked;
        folderCard.Controls.Add(browseButton);

        folderBox = new TextBox();
        folderBox.SetBounds(167, 96, 555, 34);
        folderBox.BorderStyle = BorderStyle.FixedSingle;
        folderBox.BackColor = Color.FromArgb(23, 23, 23);
        folderBox.ForeColor = TextColor;
        folderBox.Font = new Font("Segoe UI", 11F);
        folderBox.RightToLeft = RightToLeft.No;
        folderBox.TextAlign = HorizontalAlignment.Left;
        folderBox.TabIndex = 0;
        folderBox.AccessibleName = "مسار مجلد اللعبة";
        folderCard.Controls.Add(folderBox);

        installButton = MakeButton("تثبيت التعريب", new Rectangle(420, 363, 364, 56), true);
        installButton.TabIndex = 2;
        installButton.Click += delegate { StartOperation(true); };
        uninstallButton = MakeButton("إزالة التعريب", new Rectangle(36, 363, 364, 56), false);
        uninstallButton.TabIndex = 3;
        uninstallButton.Click += delegate { StartOperation(false); };
        Controls.Add(installButton);
        Controls.Add(uninstallButton);
        AcceptButton = installButton;

        statusLabel = MakeLabel("جار البحث عن مجلد اللعبة…", new Rectangle(97, 448, 683, 53), MutedColor, 11F);
        statusLabel.AutoEllipsis = true;
        statusLabel.AccessibleName = "حالة العملية";
        Controls.Add(statusLabel);
        percentLabel = MakeLabel("0%", new Rectangle(38, 458, 57, 31), GoldColor, 11F);
        percentLabel.RightToLeft = RightToLeft.No;
        percentLabel.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(percentLabel);
        progress = new GoldenProgress(GoldColor);
        progress.SetBounds(38, 512, 744, 9);
        progress.AccessibleName = "تقدم التثبيت أو الإزالة";
        Controls.Add(progress);
        Label footer = MakeLabel("أغلق اللعبة قبل التثبيت أو الإزالة. لن يشغّل المثبّت اللعبة.", new Rectangle(40, 548, 740, 28), MutedColor, 10F);
        Controls.Add(footer);

        Shown += delegate { FindGame(); };
        FormClosing += delegate(object sender, FormClosingEventArgs args)
        {
            if (!busy) return;
            args.Cancel = true;
            statusLabel.Text = "انتظر اكتمال العملية قبل إغلاق المثبّت.";
        };
    }

    private Label MakeLabel(string text, Rectangle bounds, Color color, float size)
    {
        Label label = new Label();
        label.Text = text;
        label.Bounds = bounds;
        label.ForeColor = color;
        label.BackColor = Color.Transparent;
        label.Font = new Font("Segoe UI", size);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.RightToLeft = RightToLeft.Yes;
        return label;
    }

    private Button MakeButton(string text, Rectangle bounds, bool primary)
    {
        Button button = new Button();
        button.Text = text;
        button.Bounds = bounds;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? GoldColor : CardColor;
        button.ForeColor = primary ? BackgroundColor : TextColor;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? GoldColor : Color.FromArgb(92, 79, 58);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(235, 203, 145) : Color.FromArgb(54, 48, 39);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(193, 152, 80) : Color.FromArgb(69, 58, 42);
        return button;
    }

    private void BrowseClicked(object sender, EventArgs args)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "اختر مجلد Amnesia Rebirth الذي يحتوي على ملف تشغيل اللعبة.";
            dialog.ShowNewFolderButton = false;
            if (!String.IsNullOrWhiteSpace(folderBox.Text)) dialog.SelectedPath = folderBox.Text.Trim();
            if (dialog.ShowDialog(this) == DialogResult.OK) folderBox.Text = dialog.SelectedPath;
        }
    }

    private void FindGame()
    {
        Task.Run(delegate
        {
            string found = null;
            try { found = InstallerEngine.FindGameDirectory(); }
            catch { /* Manual folder selection remains available when detection fails. */ }
            Post(delegate
            {
                if (!String.IsNullOrEmpty(found) && String.IsNullOrWhiteSpace(folderBox.Text)) folderBox.Text = found;
                if (!busy) statusLabel.Text = String.IsNullOrWhiteSpace(folderBox.Text)
                    ? "اختر مجلد اللعبة للبدء."
                    : "جاهز. يمكنك تثبيت التعريب أو إزالته من المجلد المحدد.";
            });
        });
    }

    private void StartOperation(bool install)
    {
        if (busy) return;
        string directory = folderBox.Text.Trim();
        if (String.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show(this, "اختر مجلد اللعبة أولا.", "مجلد اللعبة", MessageBoxButtons.OK, MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
            folderBox.Focus();
            return;
        }
        busy = true;
        folderBox.Enabled = false;
        browseButton.Enabled = false;
        installButton.Enabled = false;
        uninstallButton.Enabled = false;
        UpdateProgress(0, install ? "جار تجهيز التثبيت…" : "جار تجهيز الإزالة…");
        Task.Run(delegate
        {
            try
            {
                Action<int, string> callback = delegate(int value, string message)
                {
                    Post(delegate { UpdateProgress(value, message); });
                };
                if (install) InstallerEngine.Install(directory, callback);
                else InstallerEngine.Uninstall(directory, callback);
                Post(delegate
                {
                    FinishOperation();
                    UpdateProgress(100, install ? "اكتمل تثبيت التعريب بنجاح." : "اكتملت إزالة التعريب بنجاح.");
                });
            }
            catch (Exception error)
            {
                string details = error.Message;
                Post(delegate
                {
                    FinishOperation();
                    statusLabel.Text = "تعذر إكمال العملية. راجع التفاصيل ثم حاول مجددا.";
                    MessageBox.Show(this, "لم تكتمل العملية:\n\n" + details, "تعذر إكمال العملية",
                        MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1,
                        MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                });
            }
        });
    }

    private void FinishOperation()
    {
        busy = false;
        folderBox.Enabled = true;
        browseButton.Enabled = true;
        installButton.Enabled = true;
        uninstallButton.Enabled = true;
    }

    private void UpdateProgress(int value, string message)
    {
        progress.Value = Math.Max(0, Math.Min(100, value));
        percentLabel.Text = progress.Value.ToString() + "%";
        if (!String.IsNullOrWhiteSpace(message)) statusLabel.Text = message;
    }

    private void Post(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(action); }
        catch (InvalidOperationException) { /* The window can close while automatic detection completes. */ }
    }
}

internal sealed class WarmCard : Panel
{
    internal WarmCard(Color color)
    {
        BackColor = color;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        using (Pen pen = new Pen(Color.FromArgb(73, 64, 49)))
            args.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class GoldenProgress : Control
{
    private int value;
    private readonly Color gold;

    internal GoldenProgress(Color color)
    {
        gold = color;
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.ProgressBar;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal int Value
    {
        get { return value; }
        set
        {
            this.value = value;
            AccessibleDescription = value.ToString() + "%";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        args.Graphics.Clear(Color.FromArgb(60, 54, 45));
        int width = Width * value / 100;
        if (width <= 0) return;
        using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(167, 125, 58), gold, LinearGradientMode.Horizontal))
            args.Graphics.FillRectangle(brush, Width - width, 0, width, Height);
    }
}
