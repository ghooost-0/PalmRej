using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PalmRejFilterManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

/// <summary>
/// One driver we care about: what the service says, and what is actually on disk.
/// </summary>
internal sealed class DriverInfo
{
    public string Service = "";
    public string Label = "";
    public bool ServiceExists;
    public string ImagePath = "";
    public string FilePath = "";
    public bool FileExists;
    public string Sha256 = "";
    public string Build = "";
    public string SignStatus = "";
    /// <summary>Services key "Start": 3 demand-start, 4 disabled. -1 if unreadable.</summary>
    public int Start = -1;
    /// <summary>Start=4. Deliberately off, not broken - shown neutral, never red.</summary>
    public bool Down;
    /// <summary>Service key has DeleteFlag set: OFF ran and the reboot has not happened yet. ON.ps1 refuses then.</summary>
    public bool DeletePending;
    /// <summary>Its package is staged in the DriverStore.</summary>
    public bool PackagePresent;
    /// <summary>INF DriverVer of the package the .sys was loaded from, e.g. "1.0.0.0".</summary>
    public string Version = "";
    /// <summary>Package staged but no service key yet: 드라이버 ON done, reboot pending.</summary>
    public bool PendingReboot;
    /// <summary>
    /// Not part of this install at all. On a model other than the Cintiq Pro 24
    /// only the filter goes in - the pipeline and the USB tap are tied to its
    /// product number - so their absence is how it is supposed to look.
    /// </summary>
    public bool NotUsed;
    /// <summary>
    /// What the service manager says right now: true running, false stopped,
    /// null not asked or not answerable. "The file is there and signed" is not
    /// the same thing - with Secure Boot back on, or test mode off, every file
    /// is still there and signed and none of them loads.
    /// </summary>
    public bool? Running;
    /// <summary>
    /// The file the service points at was put there after this boot, so what is
    /// running (if anything) is not it yet: an install or upgrade waiting for
    /// its reboot.
    /// </summary>
    public bool InstalledAfterBoot;
    /// <summary>Shown grey rather than green or red: down, not used here, or OFF.</summary>
    public bool Neutral;
    public bool Ok;
    public string Summary = "";
}

internal sealed class MainForm : Form
{
    /// <summary>
    /// This app's own version. The screen shows the INSTALLED driver version too,
    /// which is a different number whenever a release changes only the app.
    /// 1.0.1-exp: a labelled build ("-exp") shows its label, which only the
    /// informational version can hold; numeric builds show exactly as before.
    /// </summary>
    internal static readonly string AppVersion = AppVersionText(
        typeof(MainForm).Assembly.GetName().Version?.ToString() ?? "0",
        (Attribute.GetCustomAttribute(typeof(MainForm).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
            as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion);

    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly string _gameMode;

    // 0.1.5: one status banner, three rows, two buttons, two links. The
    // switch panel (dead since A510 stopped reading Parameters), the three
    // per-driver cards, the folder and DebugView buttons and the always-on
    // technical text are gone; the technical text lives on in the saved report.
    private readonly StatusBanner _banner = new();
    private readonly StatusRow _driverRow = new("드라이버");
    private readonly StatusRow _testModeRow = new("테스트 모드");
    private readonly StatusRow _tabletRow = new("타블렛");
    private readonly Button _offButton = new();
    private readonly Button _onButton = new();
    private readonly LinkLabel _refreshLink = new();
    private readonly LinkLabel _saveLink = new();
    private readonly LinkLabel _diagLink = new();
    private readonly TitleBar _titleBar;
    /// <summary>The 진단 로그 window, while it is open.</summary>
    private DiagLogForm? _diagForm;
    private bool _closeAfterDiagStop;

    private Report? _last;
    private bool _busy;
    private bool _scriptRunning;

    /// <summary>A folder called <paramref name="name"/> in <paramref name="exeDir"/>, one or two levels up; null if none.</summary>
    private static string? FindFolderNear(string exeDir, string name)
    {
        string here = exeDir.TrimEnd('\\');
        string? up1 = Path.GetDirectoryName(here);
        string? up2 = up1 == null ? null : Path.GetDirectoryName(up1);
        foreach (var root in new[] { here, up1, up2 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            try
            {
                string c = Path.Combine(root, name);
                if (Directory.Exists(c)) return c;
            }
            catch { /* a malformed path is just a candidate that does not match */ }
        }
        return null;
    }

    /// <summary>
    /// Where reports used to go and which ON/OFF scripts this copy drives.
    ///
    /// Two layouts. On the development machine the app sits in
    /// 파일 나오는 곳\관리앱 and everything hangs off 파일 나오는 곳. Installed
    /// from the distribution zip it sits in Program Files\PalmRej with its own
    /// 드라이버 ON-OFF beside it and no 파일 나오는 곳 anywhere.
    ///
    /// The ON-OFF folder nearest the app wins, so an installed copy always
    /// drives its own scripts and keeps its own backups, even on a machine that
    /// also has the development folders.
    /// </summary>
    internal static (string ResultRoot, string GameMode) ResolveFolders(string exeDir, string userProfile)
    {
        string installDir = exeDir.TrimEnd('\\');
        bool installedLayout = Directory.Exists(Path.Combine(installDir, "드라이버 ON-OFF"));
        string resultRoot = FindFolderNear(exeDir, "파일 나오는 곳")
                            ?? (installedLayout ? installDir : Path.Combine(userProfile, @"Desktop\파일 나오는 곳"));
        string gameMode =
            FindFolderNear(exeDir, "드라이버 ON-OFF")
            ?? (Directory.Exists(Path.Combine(resultRoot, "드라이버 ON-OFF")) ? Path.Combine(resultRoot, "드라이버 ON-OFF")
            : Directory.Exists(Path.Combine(resultRoot, "드라이버 내리기")) ? Path.Combine(resultRoot, "드라이버 내리기")
            : Path.Combine(resultRoot, "드라이버 ON-OFF"));
        return (resultRoot, gameMode);
    }

    public MainForm()
    {
        (_, _gameMode) = ResolveFolders(AppContext.BaseDirectory, _userProfile);

        SuspendLayout();
        // Laid out at 96 DPI and scaled by the system DPI. Without this the
        // text grew on a 150% screen while every box stayed its 100% size.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "PalmRej";
        Font = Ui.Body;
        BackColor = Ui.Window;
        // The frame is drawn by the app: see TitleBar. A 1px border is painted
        // in OnPaint inside this padding.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        Padding = new Padding(1);
        _titleBar = new TitleBar(this);
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        try
        {
            string ico = Path.Combine(AppContext.BaseDirectory, "PalmRejFilterManager.ico");
            if (File.Exists(ico)) Icon = new Icon(ico);
            else Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { /* an icon is not worth failing startup over */ }

        BuildUi();
        ResumeLayout(true);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        Shown += async (_, _) => { if (!NoAutoRefresh) await RefreshStatusAsync(); };
        // A 진단 로그 session outlives a copy of this app that was killed; stop
        // it (only when no other copy is recording - CleanupOrphan checks).
        Shown += (_, _) => { if (!NoAutoRefresh) _ = Task.Run(() => DiagLogCapture.CleanupOrphan()); };
    }

    /// <summary>Set by the self-test's screenshot mode so a made-up state is not replaced by this machine's.</summary>
#pragma warning disable CS0649 // set only by the self-test's screenshot mode
    internal static bool NoAutoRefresh;
#pragma warning restore CS0649

    /// <summary>
    /// A borderless window still needs the minimise box and system menu styles:
    /// without them a taskbar click cannot minimise or restore it and Alt+Space
    /// does nothing.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_MAXIMIZEBOX = 0x00010000, WS_MINIMIZEBOX = 0x00020000, WS_SYSMENU = 0x00080000;
            var cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
            // MaximizeBox = false does nothing without a border, and the form's
            // WS_TABSTOP is the same bit as WS_MAXIMIZEBOX on a top-level window:
            // Win+Up blew this fixed-size window up to the whole screen.
            cp.Style &= ~WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            // The same shadow a normal window gets, on all four sides. A borderless
            // window has none until DWM is told to render its frame and the frame
            // is extended 1px into the window. (CS_DROPSHADOW, tried first, shades
            // only the right and bottom, so on a white desktop the top edge vanished.)
            int policy = 2; // DWMNCRP_ENABLED
            DwmSetWindowAttribute(Handle, 2 /* DWMWA_NCRENDERING_POLICY */, ref policy, sizeof(int));
            var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);

            // Windows 11 rounds the corners of a window that asks; Windows 10
            // refuses the attribute and keeps them square, which is fine.
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        }
        catch { /* dwmapi missing: a plain window is still a working window */ }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Ui.Frame);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // A report that changes the height while the window is minimised (a
        // refresh, or a script finishing) is not applied on restore: the window
        // came back at its old height with the buttons cut off. Fit it again.
        if (WindowState == FormWindowState.Normal) PerformLayout();
    }

    // ---------------------------------------------------------------- UI ----

    private const int ContentWidth = 408;

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 4, 16, 12),
            BackColor = Color.Transparent,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
        for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 1. the one thing to read
        _banner.Width = ContentWidth;
        _banner.Margin = new Padding(0, 0, 0, 10);
        _banner.SetState(null, "확인 중", "잠시만 기다려 주세요");
        root.Controls.Add(_banner, 0, 0);

        // 2. the three things it rests on
        var rows = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.Transparent,
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
        foreach (var (row, i) in new[] { (_driverRow, 0), (_testModeRow, 1), (_tabletRow, 2) })
        {
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.Width = ContentWidth;
            row.DrawDivider = i < 2;
            row.SetValue(null, "확인 중");
            rows.Controls.Add(row, 0, i);
        }
        root.Controls.Add(rows, 0, 1);

        // 3. the two actions
        var buttons = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = ContentWidth,
            Height = 44,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent,
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        StyleActionButton(_offButton, "PalmRej 끄기");
        StyleActionButton(_onButton, "PalmRej 켜기");
        _offButton.Margin = new Padding(0, 0, 4, 0);
        _onButton.Margin = new Padding(4, 0, 0, 0);
        _offButton.Click += (_, _) => RunOnOff(isOff: true);
        _onButton.Click += (_, _) => RunOnOff(isOff: false);
        buttons.Controls.Add(_offButton, 0, 0);
        buttons.Controls.Add(_onButton, 1, 0);
        root.Controls.Add(buttons, 0, 2);

        // 4. three quiet links
        var links = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0),
            BackColor = Color.Transparent,
        };
        StyleLink(_refreshLink, "새로고침");
        StyleLink(_saveLink, "상태 보고서 저장");
        _refreshLink.LinkClicked += async (_, _) => await RefreshStatusAsync();
        _saveLink.LinkClicked += async (_, _) => await SaveReportAsync();
        links.Controls.Add(_refreshLink);
        links.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(6, 3, 6, 0) });
        links.Controls.Add(_saveLink);
        // 0.1.17: kernel log capture in place of DebugView + palm filter + Save.
        StyleLink(_diagLink, "진단 로그 기록");
        _diagLink.LinkClicked += (_, _) => OpenDiagLog();
        links.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(6, 3, 6, 0) });
        links.Controls.Add(_diagLink);
        root.Controls.Add(links, 0, 3);

        // Title strip above the content, as wide as the content.
        var outer = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _titleBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _titleBar.SetVersion(AppVersion);
        outer.Controls.Add(_titleBar, 0, 0);
        root.Margin = new Padding(0);
        outer.Controls.Add(root, 0, 1);
        // Inside the 1px border that OnPaint draws: at (0,0) the content covered
        // its top and left edges, and on a white desktop the window had no top.
        outer.Location = new Point(1, 1);
        Controls.Add(outer);
    }

    internal static void StyleActionButton(Button b, string text)
    {
        b.Text = text;
        b.Dock = DockStyle.Fill;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Ui.ButtonBorder;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = Ui.ButtonHover;
        b.FlatAppearance.MouseDownBackColor = Ui.ButtonDown;
        b.BackColor = Ui.Window;
        b.ForeColor = Ui.Text;
        b.Font = Ui.Button;
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
    }

    private static void StyleLink(LinkLabel l, string text)
    {
        l.Text = text;
        l.AutoSize = true;
        l.Font = Ui.Body;
        l.LinkColor = Ui.Link;
        l.ActiveLinkColor = Ui.Link;
        l.VisitedLinkColor = Ui.Link;
        l.LinkBehavior = LinkBehavior.HoverUnderline;
        l.Margin = new Padding(0, 3, 0, 0);
    }

    /// <summary>
    /// Paints a judged report. Every word and colour comes from Judge(), so the
    /// self-test can check what the screen will say without showing it.
    /// </summary>
    internal void ApplyReport(Report r)
    {
        _last = r;
        _banner.SetState(r.OverallColor, r.Headline, r.HeadlineDetail);
        _driverRow.SetValue(r.DriverRowColor, r.DriverRowText);
        _testModeRow.SetValue(r.TestModeColor, r.TestModeText);
        _tabletRow.SetValue(r.DeviceColor, r.DeviceSummary);

        // One number: the installed PalmRej. The app ships in the same zip and
        // carries the same number; it only stands in when nothing is installed.
        _titleBar.SetVersion(!string.IsNullOrEmpty(r.Filter.Version) ? ReleaseVersion(r.Filter.Version, r.Filter.Build) : AppVersion);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        // Nothing while a check is running: before the first one nobody knows
        // which button makes sense, and a check still collecting when a script
        // starts or ends paints its stale result over "진행 중" and swallows the
        // script's own refresh (_busy). No refresh during a script, same reason.
        _offButton.Enabled = !_scriptRunning && !_busy && (_last?.CanTurnOff ?? true);
        _onButton.Enabled = !_scriptRunning && !_busy && (_last?.CanTurnOn ?? true);
        _refreshLink.Enabled = !_scriptRunning && !_busy;
        _saveLink.Enabled = !_busy;
        // A flat button's disabled text is only a shade lighter; fill and
        // border make "not now" readable at a glance.
        foreach (var b in new[] { _offButton, _onButton })
        {
            b.BackColor = b.Enabled ? Ui.Window : Ui.DisabledFill;
            b.FlatAppearance.BorderColor = b.Enabled ? Ui.ButtonBorder : Ui.Divider;
        }
    }

    // ------------------------------------------------------------ status ----

    private async Task RefreshStatusAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        _banner.SetState(null, "확인 중", "잠시만 기다려 주세요");
        foreach (var row in new[] { _driverRow, _testModeRow, _tabletRow }) row.SetValue(null, "확인 중");

        try
        {
            ApplyReport(await Task.Run(BuildReport));
        }
        catch (Exception ex)
        {
            _banner.SetState(false, "상태를 확인하지 못했습니다", ex.Message);
            foreach (var row in new[] { _driverRow, _testModeRow, _tabletRow }) row.SetValue(null, "알 수 없음");
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }


    internal sealed class Report
    {
        public DriverInfo Filter = new();
        public DriverInfo Pipeline = new();
        public DriverInfo Tap = new();
        public bool TestSigning;
        public bool TestSigningKnown;
        public Dictionary<string, string[]> Binds = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// The installed filter package binds by collection usage instead of by
        /// the Cintiq Pro 24's product number: the other-model install, where
        /// the filter is the only part.
        /// </summary>
        public bool OtherModel;
        /// <summary>A filter package was found at all (so OtherModel means something).</summary>
        public bool FilterPackageFound;
        public string DeviceSummary = "";
        public bool DevicesOk;
        public string Overall = "";
        public bool OverallOk;
        /// <summary>Nothing running and nothing bound: the user pressed 드라이버 OFF.</summary>
        public bool DriverOff;
        /// <summary>
        /// 'PalmRej 끄기' stopped at its check (OFF.ps1 exit 5): a service is marked
        /// for deletion at the next boot while one of ours is still registered on a
        /// device or still in the driver store. A reboot now can leave
        /// a device asking for a service that is gone - the touch stops.
        /// </summary>
        public bool OffIncomplete;

        /// <summary>
        /// Test signing as THIS boot has it (SystemStartOptions), as opposed to
        /// TestSigning, which is what bcdedit will use at the NEXT boot. They
        /// differ between a change and its reboot, and - the case that matters -
        /// when Secure Boot is on: bcdedit still says Yes, the boot manager
        /// ignores it, and no test-signed driver loads. null if unreadable.
        /// </summary>
        public bool? TestSigningEffective;
        /// <summary>UEFI Secure Boot is on. null if unreadable (old BIOS).</summary>
        public bool? SecureBoot;
        /// <summary>The presence check below ran at all.</summary>
        public bool PresenceKnown;
        /// <summary>Bound instances that are plugged in and switched on now.</summary>
        public int PresentBound;
        /// <summary>Bound instances that are present but report a problem code.</summary>
        public List<(string Id, uint Problem)> DeviceProblems = new();
        public Dictionary<string, string> DeviceStates = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// Which bound instances are present now, one by one. null when the
        /// presence check did not answer for every instance - then nothing is
        /// judged "absent" per part, only the whole tablet (PresentBound).
        /// </summary>
        public HashSet<string>? PresentIds;

        /// <summary>Filters are bound but none of those devices is present: the tablet is off or unplugged.</summary>
        public bool TabletAbsent;
        /// <summary>Test signing is set for the next boot but not in effect yet, and nothing blocks it.</summary>
        public bool TestModePending;
        public string TestModeText = "";
        /// <summary>true green, false red, null grey.</summary>
        public bool? TestModeColor;
        public bool? DeviceColor;
        public bool? OverallColor;
        /// <summary>The banner: one short bold line, and one line of what to do.</summary>
        public string Headline = "";
        public string HeadlineDetail = "";
        /// <summary>The three drivers as one row.</summary>
        public string DriverRowText = "";
        public bool? DriverRowColor;
        /// <summary>Which of the two buttons make sense right now.</summary>
        public bool CanTurnOff = true;
        public bool CanTurnOn = true;
        public string Text = "";
    }

    /// <summary>Everything the status screen needs, read from this machine.</summary>
    internal static Report Collect()
    {
        // The filter first: the pipeline and the tap cards borrow its release
        // label (1.0.1-exp), which only the filter binary carries.
        var filter = ReadDriver("PalmRejFilter", "필터");
        var rep = new Report
        {
            Filter = filter,
            Pipeline = ReadDriver("PalmRejPipeline", "파이프라인", filter),
            Tap = ReadDriver("PalmRawUsbTap", "탭", filter),
        };
        foreach (var d in new[] { rep.Filter, rep.Pipeline, rep.Tap })
            if (d.ServiceExists) d.Running = QueryServiceRunning(d.Service);
        rep.TestSigningKnown = TryReadTestSigning(out rep.TestSigning);
        rep.TestSigningEffective = ReadEffectiveTestSigning();
        rep.SecureBoot = ReadSecureBoot();
        rep.Binds = ReadFilterBindings();
        ReadPresence(rep);

        string? inf = FindFilterPackageInf(rep.Filter);
        rep.FilterPackageFound = inf != null;
        rep.OtherModel = inf != null && InfBindsByUsage(ReadInfText(inf));
        // Installed but not yet rebooted: no service key, so no file was read
        // and no version with it. The staged package still says which it is.
        if (inf != null && string.IsNullOrEmpty(rep.Filter.Version) && rep.Filter.PendingReboot)
        {
            rep.Filter.Version = ReadDriverVer(inf);
            // Its binary says whether it is a labelled build (1.0.1-exp), for the title.
            string staged = Path.Combine(Path.GetDirectoryName(inf) ?? "", "PalmRejFilter.sys");
            if (string.IsNullOrEmpty(rep.Filter.Build) && File.Exists(staged))
                rep.Filter.Build = ExtractBuild(staged, "PalmRejFilter");
        }

        Judge(rep);
        return rep;
    }

    /// <summary>
    /// Turns what was read into what the screen says. Kept apart from the reading
    /// so every state - including ones this machine is not in (another model, OFF
    /// after the reboot) - can be checked without being in it.
    /// </summary>
    internal static void Judge(Report rep)
    {
        int withFilter = rep.Binds.Count(b => b.Value.Contains("PalmRejFilter", StringComparer.OrdinalIgnoreCase));
        rep.DevicesOk = withFilter > 0;
        // The tablet row. Short: the banner above says what to do.
        rep.DeviceSummary = withFilter > 0
            ? "연결됨"
            : "드라이버가 붙은 장치가 없습니다";

        // The bindings above come from the registry, which keeps a device's
        // entry after it is unplugged or switched off. Whether any of them is
        // actually here now decides how "not running" below is read: a stopped
        // filter with the tablet off is just idle.
        rep.TabletAbsent = rep.PresenceKnown && withFilter > 0 && rep.PresentBound == 0;
        if (rep.TabletAbsent)
            rep.DeviceSummary = "꺼져 있거나 연결되지 않음";

        // Another model gets the filter alone. The pipeline and the tap are
        // Cintiq Pro 24 parts, so "no service, no package" is correct there -
        // it used to show red and turn the whole screen into "확인이 필요한".
        // Only when they are truly absent: a half-present one is still reported.
        if (rep.OtherModel)
        {
            foreach (var d in new[] { rep.Pipeline, rep.Tap })
            {
                if (d.ServiceExists || d.PackagePresent) continue;
                d.NotUsed = true;
                d.Summary = "이 기종에서는 쓰지 않습니다 — Cintiq Pro 24 전용";
            }
        }

        var parts = new[] { rep.Filter, rep.Pipeline, rep.Tap };

        // A part marked for deletion at the next boot while one of ours is still
        // registered on a device or still in the driver store: OFF stopped at its
        // check. A finished OFF passes that check only with nothing left, so this
        // never fires after one - not even when its Start=4 write alone failed.
        // Start is not looked at: OFF.ps1 1.0.0 leaves it at 3 when it stops, the
        // older one had already written 4. Without this the row said
        // "드라이버 파일이 없습니다 — 다시 설치해 주세요" (or "끄는 중 — 재부팅하면
        // 꺼집니다"), and the installer answers that state with "재부팅한 다음".
        // ON.ps1 and install.ps1 decide OFF_INCOMPLETE by this same rule.
        rep.OffIncomplete = parts.Any(d => d.DeletePending)
            && (rep.Binds.Values.Any(v => v.Any(n => n.StartsWith("Palm", StringComparison.OrdinalIgnoreCase)))
                || parts.Any(d => d.PackagePresent));

        // Marked for deletion with nothing of ours left: a finished OFF waiting
        // for its reboot, even when its Start=4 write failed (then Start is still
        // 3, the package and its file are gone, and the row said "드라이버 파일이
        // 없습니다 — 다시 설치해 주세요"). Nothing asks for the service any more;
        // the reboot removes it. It reads as being turned off, like Start=4.
        if (!rep.OffIncomplete)
        {
            foreach (var d in parts.Where(d => d.DeletePending && !d.Down))
            {
                d.Down = true;
                d.Ok = false;
                d.Summary = "끄는 중 — 재부팅하면 꺼집니다";
            }
        }

        // 드라이버 OFF is a state the user asked for, not a fault. Before the
        // reboot each part is still there with Start=4 (or marked for deletion,
        // above); after it, OFF has removed the packages and Windows has removed
        // the service keys, so each part is simply gone. Both count - the old
        // check only knew the first, and the screen went red the moment the user
        // rebooted into the state they chose.
        static bool IsOff(DriverInfo d) => d.Start == 4 || d.Down || (!d.ServiceExists && !d.PackagePresent);
        rep.DriverOff = IsOff(rep.Filter) && IsOff(rep.Pipeline) && IsOff(rep.Tap) && !rep.DevicesOk;

        foreach (var d in new[] { rep.Filter, rep.Pipeline, rep.Tap })
        {
            if (rep.DriverOff && !d.ServiceExists)
                d.Summary = "꺼짐";
            d.Neutral = d.Down || d.NotUsed || (rep.DriverOff && !d.ServiceExists);
        }
        if (rep.DriverOff && withFilter == 0)
            rep.DeviceSummary = "PalmRej 꺼짐 — 연결하지 않음";

        JudgeTestMode(rep);

        // Problems a reboot is expected to clear, so they are not faults:
        //  14  Windows' own "restart needed" - an install or upgrade on a live device;
        //  52  while test signing is switched on but this boot started without it.
        //      드라이버 ON and the installer turn it on and install in the same boot,
        //      so every device our packages bind to reports 52 until the reboot.
        //      With test signing in effect, or Secure Boot on, 52 is real and stays red.
        var faults = rep.DeviceProblems
            .Where(p => !(p.Problem == 14 || (p.Problem == 52 && rep.TestModePending)))
            .ToList();
        bool devicesAwaitReboot = faults.Count < rep.DeviceProblems.Count;
        if (!rep.TabletAbsent && faults.Count > 0)
            rep.DeviceSummary = $"오류 — {ProblemText(faults[0].Problem)}";
        else if (!rep.TabletAbsent && devicesAwaitReboot)
            rep.DeviceSummary = "재부팅 전이라 멈춰 있음";

        // Secure Boot turned back on (a BIOS reset does it) is the failure this
        // check exists for: bcdedit still says test signing is on, every file is
        // still there and signed, and none of them loads. Said by name, because
        // "not running" alone gives nobody a way out.
        bool blockedBySecureBoot = rep.SecureBoot == true && rep.TestSigningEffective == false && !rep.DriverOff;

        // Something new is installed but the next boot will not run it either.
        bool rebootWontHelp = blockedBySecureBoot || (rep.TestSigningKnown && !rep.TestSigning && !rep.DriverOff);

        // A part is idle, not broken, when every device it is bound to is gone
        // (the touch half appears a few seconds after the pen when the tablet
        // powers on). Only when presence was answered instance by instance.
        bool OwnDevicesAbsent(DriverInfo d)
        {
            if (rep.PresentIds == null) return false;
            var own = rep.Binds.Where(b => b.Value.Contains(d.Service, StringComparer.OrdinalIgnoreCase))
                               .Select(b => b.Key).ToList();
            return own.Count > 0 && !own.Any(rep.PresentIds.Contains);
        }
        // The part's own device's problem first, then anyone's.
        uint? ProblemFor(DriverInfo d)
        {
            foreach (var (id, problem) in faults)
                if (rep.Binds.TryGetValue(id, out var f) && f.Contains(d.Service, StringComparer.OrdinalIgnoreCase))
                    return problem;
            return faults.Count > 0 ? faults[0].Problem : null;
        }

        bool partIdle = false;

        // "File there and signed" was all Ok ever meant. Now also: it is the one
        // this boot loaded, and it is actually running.
        foreach (var d in parts)
        {
            // Package staged, no service key yet (ReadDriver set Ok): nothing of
            // ours runs from it until the reboot. Grey, never green.
            if (d.PendingReboot && !d.ServiceExists && !d.NotUsed)
            {
                if (rebootWontHelp) { d.Ok = false; d.Summary = RebootWontHelpText(blockedBySecureBoot); }
                else d.Neutral = true;
                continue;
            }

            if (d.NotUsed || d.Down || !d.ServiceExists || !d.Ok) continue;

            if (d.InstalledAfterBoot)
            {
                // An install or upgrade waiting for its reboot. What runs now,
                // if anything, is the previous build; the card says so rather
                // than presenting the new file as the one in effect. Green only
                // while that previous build is actually running.
                d.PendingReboot = true;
                if (rebootWontHelp)
                {
                    d.Ok = false;
                    d.Summary = RebootWontHelpText(blockedBySecureBoot);
                }
                else if (d.Running == true)
                {
                    d.Summary += "  — 새로 설치됨, 재부팅하면 적용";
                }
                else
                {
                    d.Neutral = true;
                    d.Summary = "설치됨 — 재부팅하면 올라옵니다";
                }
                continue;
            }

            if (d.Running != false) continue;

            if (rep.TabletAbsent)
            {
                // Windows unloads a filter whose devices are all gone. Nothing
                // is wrong; there is nothing for it to filter.
                d.Neutral = true;
                d.Summary = "쉬는 중 — 타블렛이 꺼져 있습니다";
                continue;
            }

            if (rep.TestModePending)
            {
                // Test signing was just switched on; nothing test-signed loads
                // until the reboot, which is what the test mode card says too.
                d.Neutral = true;
                d.Summary = "재부팅하면 올라옵니다 — 테스트 모드를 방금 켰습니다";
                continue;
            }

            // Not when a device reports a real fault: then the part is missing
            // because of it (the touch USB device failing takes the touch
            // collections with it), and the red text below names it.
            if (!blockedBySecureBoot && rep.TestSigningEffective != false && faults.Count == 0 && OwnDevicesAbsent(d))
            {
                d.Neutral = true;
                partIdle = true;
                d.Summary = "쉬는 중 — 터치 장치가 아직 안 보입니다";
                continue;
            }

            d.Ok = false;
            uint? problem = ProblemFor(d);
            d.Summary = blockedBySecureBoot
                ? "실행되지 않음 — 보안 부팅이 켜져 있어 막혔습니다"
                : rep.TestSigningEffective == false
                    ? "실행되지 않음 — 테스트 모드가 꺼진 채 부팅했습니다"
                    : problem != null
                        ? $"실행되지 않음 — {ProblemText(problem.Value)}"
                        : "실행되지 않음 — 타블렛을 방금 켰다면 잠시 뒤 새로고침, 아니면 재부팅하세요";
        }

        bool pending = parts.Any(d => d.PendingReboot) || rep.TestModePending;
        // Only a reboot is missing, and until it nothing new of ours is running.
        bool waitingForReboot = parts.Any(d => d.PendingReboot && d.Running != true)
                                || devicesAwaitReboot || rep.TestModePending;

        if (partIdle && faults.Count == 0 && !rep.TabletAbsent)
            rep.DeviceSummary = "펜만 보임 — 터치 쪽이 아직 준비 중";

        rep.DeviceColor = faults.Count > 0 ? false
                        : (rep.TabletAbsent || devicesAwaitReboot || partIdle) ? (bool?)null
                        : rep.DevicesOk ? true
                        : rep.DriverOff ? (bool?)null
                        : false;

        bool partsOk = parts.All(d => d.Ok || d.NotUsed);
        bool calm = partsOk && faults.Count == 0 && rep.TestModeColor != false;

        rep.OverallOk = calm
                        && rep.DevicesOk
                        && !rep.TabletAbsent
                        && !partIdle
                        && !waitingForReboot;

        // Grey states: nobody's fault, and nothing else on the screen is red.
        bool rebootOnly = !rep.OverallOk && !rep.DriverOff && calm && rep.DevicesOk && waitingForReboot;
        // The tablet being off.
        bool absentOnly = !rep.OverallOk && calm && rep.TabletAbsent;
        // Pen there, touch half not (yet).
        bool touchAbsentOnly = !rep.OverallOk && calm && partIdle;

        // The three drivers as one row: the worst of them decides, and a part
        // is named only when the parts disagree.
        var shown = parts.Where(d => !d.NotUsed).ToList();
        static bool? ColorOf(DriverInfo d) => d.Neutral ? null : d.Ok;
        static string NameOf(DriverInfo d) => string.IsNullOrEmpty(d.Label) ? d.Service : d.Label;
        string RowText(IEnumerable<DriverInfo> group)
        {
            var g = group.ToList();
            bool same = g.Select(d => d.Summary).Distinct().Count() == 1;
            var first = g[0];
            return same && g.Count == shown.Count ? first.Summary
                 : same ? string.Join("·", g.Select(NameOf)) + ": " + first.Summary
                 : NameOf(first) + ": " + first.Summary;
        }
        var reds = shown.Where(d => ColorOf(d) == false).ToList();
        var greys = shown.Where(d => ColorOf(d) == null).ToList();

        // rep.OffIncomplete was decided at the top (it changes what counts as off).
        if (rep.OffIncomplete)
        {
            // The per-part cards (and the saved report) said "다시 설치해 주세요" or
            // "재부팅하면 꺼집니다" here - both the wrong thing to do now.
            foreach (var d in parts.Where(d => d.DeletePending))
                d.Summary = "끄기가 덜 끝났습니다 — 재부팅하지 마세요";
            rep.DriverRowColor = false;
            rep.DriverRowText = "끄기가 덜 끝났습니다 — 재부팅하지 마세요";
        }
        else if (shown.Count == 0)
        {
            rep.DriverRowColor = false;
            rep.DriverRowText = "설치된 드라이버가 없습니다";
        }
        else if (reds.Count > 0)
        {
            rep.DriverRowColor = false;
            rep.DriverRowText = RowText(reds);
        }
        else if (greys.Count > 0)
        {
            rep.DriverRowColor = null;
            rep.DriverRowText = RowText(greys);
        }
        else
        {
            rep.DriverRowColor = true;
            rep.DriverRowText = shown.Any(d => d.PendingReboot)
                ? "실행 중 — 새 판은 재부팅하면 적용"
                : "실행 중";
        }

        string ver = string.IsNullOrEmpty(rep.Filter.Version)
            ? (string.IsNullOrEmpty(rep.Filter.Build) ? "" : rep.Filter.Build)
            : "PalmRej " + ReleaseVersion(rep.Filter.Version, rep.Filter.Build);
        string model = rep.OtherModel ? " · 다른 기종 (필터만 사용)" : "";

        // The red row already says what is wrong; the banner says where to look
        // and what to do if that does not help, instead of repeating it.
        string firstRed = rep.DriverRowColor == false || rep.TestModeColor == false || rep.DeviceColor == false
            ? "빨간 줄을 확인하세요. 해결되지 않으면 '상태 보고서 저장'으로 보고서를 보내 주세요"
            : "'상태 보고서 저장'으로 보고서를 저장해서 보내 주세요";

        (rep.Headline, rep.HeadlineDetail) =
            rep.OffIncomplete
                ? ("PalmRej 끄기가 덜 끝났습니다",
                   "지금 재부팅하면 터치가 멈출 수 있습니다. 재부팅하지 말고 'PalmRej 끄기'를 한 번 더 누르세요")
            : rep.OverallOk
                ? ("PalmRej 켜짐",
                   pending
                       ? (string.IsNullOrEmpty(ver) ? "새 판이 설치됐습니다 — 재부팅하면 적용됩니다" : $"{ver} 설치됨 — 재부팅하면 적용됩니다") + model
                       : "정상 동작 중입니다" + model)
            // Marked for deletion is "being turned off" too, running or not: 'PalmRej
            // 켜기' is dimmed then, so "다시 쓰려면 'PalmRej 켜기'" would point at nothing.
            : rep.DriverOff && parts.Any(d => d.Down && (d.Running == true || d.DeletePending))
                ? ("PalmRej 끄는 중", "재부팅하면 꺼집니다")
            : rep.DriverOff
                ? ("PalmRej 꺼짐", "다시 쓰려면 'PalmRej 켜기'를 누르세요")
            : blockedBySecureBoot
                ? ("PalmRej 멈춤", "보안 부팅이 켜져 있습니다. BIOS 에서 보안 부팅을 끄거나 'PalmRej 끄기'를 누르세요")
            : rebootOnly
                ? ("재부팅이 필요합니다",
                   parts.Any(d => d.PendingReboot)
                       ? (string.IsNullOrEmpty(ver) ? "설치를 마쳤습니다." : $"{ver} 설치를 마쳤습니다.") + " 재부팅 전에는 터치가 멈춰 있을 수 있습니다"
                       : "재부팅 전에는 터치가 멈춰 있을 수 있습니다")
            : absentOnly
                ? ("타블렛이 꺼져 있습니다", "타블렛을 켜고 새로고침을 누르세요")
            : touchAbsentOnly
                ? ("터치 장치를 기다리는 중", "타블렛을 방금 켰다면 잠시 뒤 새로고침, 계속되면 재부팅하세요")
                : ("확인이 필요합니다", firstRed);

        // The saved report and the self-test read this one line.
        rep.Overall = rep.Headline + " — " + rep.HeadlineDetail;

        rep.OverallColor = rep.OffIncomplete ? false
                         : rep.OverallOk ? true
                         : (rep.DriverOff || rebootOnly || absentOnly || touchAbsentOnly) ? (bool?)null
                         : false;

        // Off is pointless when it is already off - unless test mode was left on
        // (an OFF that could not turn it off, or an ON that stopped after turning
        // it on): OFF.ps1 turns just that off now. On is pointless when it is
        // already working, when all that is missing is the reboot, or while
        // Secure Boot keeps test mode off (ON.ps1 refuses there).
        // "Left on" is about the next boot: right after an OFF this boot still
        // has it, but bcdedit already says No and the reboot finishes the job.
        bool testModeLeftOn = rep.TestSigningKnown ? rep.TestSigning : rep.TestSigningEffective == true;
        bool secureBootBlocks = rep.SecureBoot == true && rep.TestSigningEffective != true;
        rep.CanTurnOff = rep.OffIncomplete || !rep.DriverOff || (testModeLeftOn && !secureBootBlocks);
        rep.CanTurnOn = !(rep.OverallOk || rebootOnly || secureBootBlocks || parts.Any(d => d.DeletePending));
    }

    private static string RebootWontHelpText(bool blockedBySecureBoot) => blockedBySecureBoot
        ? "설치됐지만 보안 부팅 때문에 재부팅해도 안 올라옵니다"
        : "설치됐지만 테스트 모드가 꺼져 재부팅해도 안 올라옵니다";

    /// <summary>
    /// The test mode card, from what this boot actually has (SystemStartOptions)
    /// and what bcdedit has set for the next one. Reading only bcdedit, as the
    /// app did, showed "ON — 정상" on a machine where Secure Boot had quietly
    /// switched test signing off and no driver of ours was loading.
    /// </summary>
    internal static void JudgeTestMode(Report rep)
    {
        bool? eff = rep.TestSigningEffective;
        bool bcdKnown = rep.TestSigningKnown, bcd = rep.TestSigning;
        rep.TestModePending = false;

        if (eff == true)
        {
            if (!bcdKnown || bcd)
            {
                // With the drivers OFF this is not the finished OFF state: either
                // 드라이버 ON stopped at the test-signing step and the machine has
                // since rebooted, or OFF could not turn test signing off.
                rep.TestModeText = rep.DriverOff
                    ? "켜짐 (PalmRej 는 꺼져 있음)"
                    : "켜짐";
                rep.TestModeColor = rep.DriverOff ? (bool?)null : true;
            }
            else
            {
                // On now, set to go off at the next boot: what 드라이버 OFF does.
                rep.TestModeText = rep.DriverOff
                    ? "켜짐 — 재부팅하면 꺼집니다"
                    : "켜짐 — 재부팅하면 꺼져서 드라이버가 안 뜹니다";
                rep.TestModeColor = rep.DriverOff ? (bool?)null : false;
            }
            return;
        }

        if (eff == false)
        {
            if (rep.DriverOff)
            {
                // With Secure Boot on, OFF's "bcdedit testsigning off" is refused
                // (the value is protected by Secure Boot policy) and OFF still ends
                // with exit 0, so bcdedit keeps saying Yes. Test mode is off in
                // fact, which is what OFF is for - and ON cannot work until
                // Secure Boot is off, so "press ON again" would be wrong here.
                // Otherwise: 드라이버 ON exits early (code 2) after turning test
                // signing on, and nothing is installed until the reboot and a
                // second press.
                rep.TestModeText = rep.SecureBoot == true
                    ? "꺼짐 (보안 부팅이 켜져 있음) — 다시 켜려면 먼저 BIOS 에서 보안 부팅을 끄세요"
                    : bcdKnown && bcd
                        ? "켜 둠 — 재부팅한 뒤 'PalmRej 켜기'를 한 번 더 누르세요"
                        : "꺼짐";
                rep.TestModeColor = null;
            }
            else if (rep.SecureBoot == true)
            {
                rep.TestModeText = "보안 부팅이 막고 있음 — BIOS 에서 보안 부팅을 끄세요";
                rep.TestModeColor = false;
            }
            else if (bcdKnown && bcd)
            {
                // Just turned on (an install or 드라이버 ON), not yet rebooted.
                rep.TestModeText = "켜 둠 — 재부팅하면 적용됩니다";
                rep.TestModeColor = null;
                rep.TestModePending = true;
            }
            else
            {
                rep.TestModeText = "꺼짐 — 이대로는 드라이버가 안 뜹니다";
                rep.TestModeColor = false;
            }
            return;
        }

        // This boot's state unreadable: fall back to bcdedit alone, as before.
        rep.TestModeText = bcdKnown
            ? (bcd ? "켜짐"
               : rep.DriverOff ? "꺼짐"
               : "꺼짐 — 재부팅하면 드라이버가 안 뜹니다")
            : "확인하지 못했습니다";
        rep.TestModeColor = rep.DriverOff ? (bool?)null : bcdKnown ? bcd : (bool?)null;
    }

    /// <summary>A device manager problem code, in words the user can act on.</summary>
    internal static string ProblemText(uint code) => code switch
    {
        52 => "서명 확인 실패 (코드 52) — 보안 부팅이 켜졌거나 테스트 모드가 꺼졌습니다",
        39 => "드라이버를 불러오지 못했습니다 (코드 39)",
        38 => "옛 드라이버가 아직 남아 있습니다 (코드 38) — 재부팅하세요",
        31 => "드라이버를 붙이지 못했습니다 (코드 31)",
        // Seen here: the pipeline's service key did not exist yet when COL02
        // first started. Windows creates it during that boot; the next is fine.
        19 => "등록이 덜 끝났습니다 (코드 19) — 재부팅을 한 번 더 하세요",
        14 => "재부팅해야 적용됩니다 (코드 14)",
        10 => "장치를 시작하지 못했습니다 (코드 10)",
        22 => "장치가 사용 안 함으로 되어 있습니다 (코드 22)",
        _ => $"장치 관리자 오류 코드 {code}",
    };

    /// <summary>
    /// What the screen said, word for word and colour by colour. The saved report
    /// is what gets sent when something looks wrong; without this it held every
    /// reading but not the verdict drawn from them.
    /// </summary>
    internal static string ScreenSection(Report rep)
    {
        static string C(bool? c) => c == true ? "초록" : c == false ? "빨강" : "회색";
        var sb = new StringBuilder();
        sb.AppendLine("[화면]");
        sb.AppendLine($"  상단              {C(rep.OverallColor)}  {rep.Headline}");
        sb.AppendLine($"                          {rep.HeadlineDetail}");
        sb.AppendLine($"  드라이버          {C(rep.DriverRowColor)}  {rep.DriverRowText}");
        sb.AppendLine($"  테스트 모드       {C(rep.TestModeColor)}  {rep.TestModeText}");
        sb.AppendLine($"  타블렛            {C(rep.DeviceColor)}  {rep.DeviceSummary}");
        sb.AppendLine($"  버튼              끄기 {(rep.CanTurnOff ? "사용" : "흐림")} · 켜기 {(rep.CanTurnOn ? "사용" : "흐림")}");
        sb.AppendLine("  (드라이버별)");
        foreach (var d in new[] { rep.Filter, rep.Pipeline, rep.Tap })
            sb.AppendLine($"  {d.Service,-17} {C(d.Neutral ? null : d.Ok)}  {d.Summary}");
        return sb.ToString();
    }

    internal static Report BuildReport()
    {
        var rep = Collect();
        var binds = rep.Binds;

        var sb = new StringBuilder();
        sb.AppendLine("PALMREJ 상태");
        sb.AppendLine($"시각   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"관리자 {IsAdmin()}");
        sb.AppendLine($"앱     {AppVersion}");
        sb.AppendLine($"설치   {(!rep.FilterPackageFound ? "필터 패키지 없음" : rep.OtherModel ? "다른 기종용 (손날 인식 필터만)" : "Cintiq Pro 24 용")}");
        sb.AppendLine();
        sb.Append(ScreenSection(rep));
        sb.AppendLine();
        foreach (var d in new[] { rep.Filter, rep.Pipeline, rep.Tap })
        {
            sb.AppendLine($"[{d.Service}]");
            sb.AppendLine($"  서비스     {(d.ServiceExists ? "있음" : "없음")}");
            sb.AppendLine($"  실행       {(d.Running == true ? "실행 중" : d.Running == false ? "멈춤" : "알 수 없음")}" +
                          $"{(d.InstalledAfterBoot ? "   (이번 부팅 뒤에 설치된 파일 — 재부팅 전)" : "")}");
            sb.AppendLine($"  패키지     {(d.PackagePresent ? "저장소에 있음" : "저장소에 없음")}");
            sb.AppendLine($"  Start      {d.Start}  {d.Start switch { 0 => "부팅", 1 => "시스템", 2 => "자동", 3 => "요청 시", 4 => "사용 안 함 (내려감)", _ => "읽지 못함" }}");
            sb.AppendLine($"  ImagePath  {d.ImagePath}");
            sb.AppendLine($"  파일       {(d.FileExists ? "있음" : "없음")}  {d.FilePath}");
            sb.AppendLine($"  버전       {(string.IsNullOrEmpty(d.Version) ? "(패키지 버전 없음)" : d.Version)}");
            sb.AppendLine($"  빌드       {d.Build}");
            sb.AppendLine($"  SHA256     {d.Sha256}");
            sb.AppendLine($"  서명       {d.SignStatus}");
            sb.AppendLine();
        }
        sb.AppendLine("[테스트 모드]");
        sb.AppendLine(rep.TestSigningKnown ? $"  다음 부팅 (bcdedit)   testsigning = {(rep.TestSigning ? "Yes" : "No")}" : "  다음 부팅 (bcdedit)   읽지 못했습니다");
        sb.AppendLine($"  이번 부팅 (실제 적용) {(rep.TestSigningEffective == true ? "켜짐" : rep.TestSigningEffective == false ? "꺼짐" : "읽지 못했습니다")}");
        sb.AppendLine($"  보안 부팅             {(rep.SecureBoot == true ? "켜짐" : rep.SecureBoot == false ? "꺼짐" : "알 수 없음")}");
        sb.AppendLine($"  화면 표시             {rep.TestModeText}");
        sb.AppendLine();
        sb.AppendLine("[장치 연결]");
        foreach (var b in binds)
        {
            string st = rep.DeviceStates.TryGetValue(b.Key, out var s) ? s : "";
            // Two AppendLine calls, not an embedded "\n": the details TextBox
            // breaks lines only on CRLF, so a bare LF ran the two together.
            sb.AppendLine($"  {b.Key}   {st}");
            sb.AppendLine($"      {string.Join(", ", b.Value)}");
        }
        rep.Text = sb.ToString();
        return rep;
    }

    /// <param name="releaseOf">The filter, already read, for the pipeline and the tap (see the card text below); null for the filter itself.</param>
    private static DriverInfo ReadDriver(string service, string label, DriverInfo? releaseOf = null)
    {
        var d = new DriverInfo { Service = service, Label = label };
        d.PackagePresent = DriverStoreHasPackage(service);
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\{service}");
            if (k == null)
            {
                // PalmRejPipeline has no service key until Windows processes its
                // INF at the next boot - the restore script says so and skips it.
                // The other two only have one early because the install scripts
                // write ImagePath themselves. Package staged + no key is 드라이버
                // ON having worked, not anything being missing.
                if (d.PackagePresent)
                {
                    d.PendingReboot = true;
                    d.Ok = true;
                    d.Summary = "설치됨 — 재부팅하면 올라옵니다";
                }
                else
                {
                    d.Summary = "설치되어 있지 않습니다";
                }
                return d;
            }
            d.ServiceExists = true;
            d.ImagePath = k.GetValue("ImagePath")?.ToString() ?? "";
            d.Start = k.GetValue("Start") is int s ? s : -1;
            // Known before anything else is read, because every path below has
            // to answer to it - including the one that gives up early.
            d.Down = d.Start == 4;
            d.DeletePending = k.GetValue("DeleteFlag") is int df && df != 0;
        }
        catch (Exception ex)
        {
            d.Summary = "레지스트리를 읽지 못했습니다: " + ex.Message;
            return d;
        }

        d.FilePath = ResolveSystemRoot(d.ImagePath);
        d.FileExists = !string.IsNullOrWhiteSpace(d.FilePath) && File.Exists(d.FilePath);
        if (!d.FileExists)
        {
            // PalmRejPipeline's ImagePath points INTO the DriverStore, so 드라이버
            // OFF removes the package and the file with it. That is the OFF state
            // working, not a fault - the other two only survive because the
            // install scripts left loose copies in drivers\.
            d.Summary = d.Down
                ? "끄는 중 — 재부팅하면 꺼집니다"
                : "드라이버 파일이 없습니다 — 다시 설치해 주세요";
            return d;
        }

        try
        {
            using var fs = File.OpenRead(d.FilePath);
            d.Sha256 = Convert.ToHexString(SHA256.HashData(fs));
        }
        catch { d.Sha256 = ""; }

        d.Build = ExtractBuild(d.FilePath, service);
        d.SignStatus = ReadSignature(d.FilePath);
        d.Version = ReadPackageVersion(d.FilePath);
        d.InstalledAfterBoot = CreatedSinceBoot(d.FilePath);

        // "Ok" here means: the service exists, the file it points at exists, and it is
        // signed. It deliberately does NOT compare against a hardcoded hash - the old
        // app did that, went stale five builds ago, and then reported a month-old
        // driver as "정상 — 해시 일치".
        d.Ok = d.SignStatus.StartsWith("Valid", StringComparison.OrdinalIgnoreCase);

        // The card shows the package version when there is one (1.0 onwards):
        // it is the same for all three parts of a release, where the internal
        // build names are not - the pipeline and the tap were carried over
        // byte-for-byte and still say PV1K and A460 inside. Those names stay in
        // the detailed report, where they are useful.
        // 1.0.1-exp: a labelled release says its label only in the filter's
        // banner; the pipeline and the tap of the same package set (same
        // DriverVer) borrow it, so all three cards read "PalmRej 1.0.1-exp".
        string releaseBuild = releaseOf == null ? d.Build
            : (releaseOf.Version.Length > 0 && releaseOf.Version == d.Version ? releaseOf.Build : "");
        string shown = string.IsNullOrEmpty(d.Version) ? d.Build : "PalmRej " + ReleaseVersion(d.Version, releaseBuild);
        d.Summary = d.Ok
            ? $"{shown}   ({Path.GetFileName(d.FilePath)})"
            : $"서명을 확인할 수 없습니다 ({d.SignStatus})";

        // Start=4 is disabled - 드라이버 OFF sets it, and it is the one thing that
        // decides whether this driver comes back at the next boot. After OFF the
        // loose .sys files stay in drivers\ (the script removes the DriverStore
        // packages and the registrations, not the copies the install scripts
        // made), so "file exists and is signed" said green while the stack was
        // down. Decided LAST, on top of everything else: the build name and the
        // hash are still worth reading and reporting while it is down.
        if (d.Down)
        {
            d.Ok = false;
            d.Summary = string.IsNullOrWhiteSpace(d.Build)
                ? "끄는 중 — 재부팅하면 꺼집니다"
                : "끄는 중 — 재부팅하면 꺼집니다";
        }
        return d;
    }

    /// <summary>
    /// The package version (INF DriverVer) of the driver at this path, or "" if
    /// the .sys is not inside a DriverStore package. From 1.0 on every part is
    /// loaded straight from its package, so the INF sits in the same folder.
    /// INFs here are UTF-16 (they carry Korean names) or UTF-8; try both.
    /// </summary>
    private static string ReadPackageVersion(string sysPath)
    {
        try
        {
            if (string.IsNullOrEmpty(sysPath) ||
                sysPath.IndexOf(@"\DriverStore\", StringComparison.OrdinalIgnoreCase) < 0)
                return "";
            string dir = Path.GetDirectoryName(sysPath) ?? "";
            var inf = Directory.EnumerateFiles(dir, "*.inf").FirstOrDefault();
            return inf == null ? "" : ReadDriverVer(inf);
        }
        catch { return ""; }
    }

    internal static string ReadDriverVer(string infPath)
    {
        var m = Regex.Match(ReadInfText(infPath), @"(?im)^\s*DriverVer\s*=\s*[^,\r\n]+,\s*([0-9.]+)");
        return m.Success ? m.Groups[1].Value : "";
    }

    internal static string ReadInfText(string infPath)
    {
        try
        {
            byte[] raw = File.ReadAllBytes(infPath);
            return (raw.Length > 1 && raw[0] == 0xFF && raw[1] == 0xFE)
                ? Encoding.Unicode.GetString(raw, 2, raw.Length - 2)
                : Encoding.UTF8.GetString(raw);
        }
        catch { return ""; }
    }

    /// <summary>
    /// The INF of the filter package that is installed: the one the running
    /// service loads from, or - between install and reboot, when there is no
    /// service key yet - the newest staged one. null when there is none.
    /// </summary>
    private static string? FindFilterPackageInf(DriverInfo filter)
    {
        try
        {
            string? dir = null;
            if (!string.IsNullOrEmpty(filter.FilePath) &&
                filter.FilePath.IndexOf(@"\DriverStore\", StringComparison.OrdinalIgnoreCase) >= 0 &&
                File.Exists(filter.FilePath))
            {
                dir = Path.GetDirectoryName(filter.FilePath);
            }
            if (dir == null)
            {
                string repo = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "DriverStore", "FileRepository");
                if (!Directory.Exists(repo)) return null;
                dir = new DirectoryInfo(repo).EnumerateDirectories("palmrejfilter.inf_*")
                    .OrderByDescending(x => x.LastWriteTimeUtc)
                    .FirstOrDefault()?.FullName;
            }
            if (dir == null) return null;
            return Directory.EnumerateFiles(dir, "*.inf").FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>
    /// True for the other-model package: every device line binds by
    /// "HID\VID_056A&amp;UP:...", none by the Cintiq Pro 24's "&amp;PID_".
    /// Read from the INF, the only place that says which package this is -
    /// both carry the same file names and the same filter binary.
    /// </summary>
    internal static bool InfBindsByUsage(string infText)
    {
        var ids = Regex.Matches(infText, @"(?im)^[^;\r\n]*,\s*(HID\\VID_056A&[^\s;]+)\s*$")
                       .Select(m => m.Groups[1].Value)
                       .ToList();
        return ids.Count > 0 && ids.All(id => id.IndexOf("&UP:", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>"1.0.0.0" -> "1.0". Trailing zero parts past the second are noise on a card.</summary>
    private static string ShortVersion(string v)
    {
        var parts = v.Split('.');
        int keep = parts.Length;
        while (keep > 2 && parts[keep - 1] == "0") keep--;
        return string.Join(".", parts.Take(keep));
    }

    /// <summary>
    /// The filter banner of a labelled build: "PalmRej-1.0.1-exp" = numbers, then
    /// a dash and a word that starts with a letter. A const, not a Regex field:
    /// AppVersion above is initialised before any static field below it.
    /// </summary>
    private const string LabelledBuildPattern = @"^PalmRej-(\d+\.\d+\.\d+)(-[A-Za-z][A-Za-z0-9]{0,15})$";

    /// <summary>
    /// What the screen calls an installed release. Normally ShortVersion of the
    /// package's DriverVer. A labelled build (1.0.1-exp) cannot say so in its INF -
    /// DriverVer is numbers only (1.0.1.1) - so its filter binary says it, in the
    /// banner this app already reads ("BUILD: PalmRej-1.0.1-exp"). Only a banner
    /// WITH a label whose numbers match the DriverVer changes anything: every
    /// plain release shows exactly as before (1.0.1.0 -> "1.0.1", 1.0.0.0 -> "1.0").
    /// </summary>
    internal static string ReleaseVersion(string? driverVer, string? filterBuild)
    {
        if (string.IsNullOrEmpty(driverVer)) return "";
        var m = Regex.Match(filterBuild ?? "", LabelledBuildPattern);
        if (m.Success)
        {
            string nums = m.Groups[1].Value;
            if (driverVer == nums || driverVer.StartsWith(nums + ".", StringComparison.Ordinal))
                return nums + m.Groups[2].Value;
        }
        return ShortVersion(driverVer);
    }

    /// <summary>
    /// This app's version text: the informational version when it carries a
    /// label ("1.0.1-exp", from -p:Version), else ShortVersion of the assembly
    /// version as before. A "+commit" tail the SDK may add is dropped.
    /// </summary>
    internal static string AppVersionText(string assemblyVersion, string? informational)
    {
        string info = (informational ?? "").Split('+')[0].Trim();
        if (Regex.IsMatch(info, @"^\d+\.\d+\.\d+-[A-Za-z][A-Za-z0-9]{0,15}$")) return info;
        return ShortVersion(assemblyVersion);
    }

    /// <summary>
    /// ReleaseVersion of the filter at <paramref name="sysPath"/>: its package's
    /// DriverVer and its own banner. "" when the .sys is not in a package.
    /// </summary>
    internal static string PackageReleaseVersion(string sysPath)
    {
        string v = ReadPackageVersion(sysPath);
        return v.Length == 0 ? "" : ReleaseVersion(v, ExtractBuild(sysPath, "PalmRejFilter"));
    }

    /// <summary>
    /// Is this driver's package staged in the DriverStore? The folder is named
    /// "&lt;inf base&gt;.inf_amd64_&lt;hash&gt;", and every one of our INFs is the service
    /// name in lower case, so the service name is enough to find it.
    /// </summary>
    private static bool DriverStoreHasPackage(string service)
    {
        try
        {
            string repo = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "DriverStore", "FileRepository");
            if (!Directory.Exists(repo)) return false;
            string prefix = service.ToLowerInvariant() + ".inf_";
            return Directory.EnumerateDirectories(repo)
                .Any(p => Path.GetFileName(p)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static string ResolveSystemRoot(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return "";
        string p = imagePath.Trim().Trim('"');
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p[12..]);
        if (p.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase)) return p[4..];
        if (p.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p);
        return p;
    }

    /// <summary>
    /// Read the build name out of the binary itself rather than trusting a constant.
    /// The driver prints "PalmRejFilter BUILD: A505R1-SIZE ..." at every device add,
    /// so that string is in the image and it is always the truth about what is loaded.
    /// </summary>
    private static string ExtractBuild(string path, string service)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            string ascii = Encoding.ASCII.GetString(bytes);

            var m = Regex.Match(ascii, @"BUILD: ([A-Za-z0-9._\-]{3,40})");
            if (m.Success) return m.Groups[1].Value;

            m = Regex.Match(ascii, @"build=([A-Za-z0-9._\-]{3,40})");
            if (m.Success) return m.Groups[1].Value;

            // No banner: the filename carries the version for the filter and the tap
            // (PalmRawUsbTap_A461_BRIDGE_SHADOW.sys), which is more reliable than
            // fishing for tokens in the image - a loose pattern once matched a bare
            // "A" and displayed that as the build name.
            var fromName = Regex.Match(Path.GetFileNameWithoutExtension(path), @"_(Ad{3}[A-Z]?d?)_");
            if (fromName.Success) return fromName.Groups[1].Value;

            // Pipeline carries a version tag instead, and no version in its filename.
            var tags = Regex.Matches(ascii, service == "PalmRejPipeline" ? @"\bPV1[A-Z]\b" : @"\bA\d{3}[A-Z]?\d?\b")
                            .Select(x => x.Value).ToList();
            if (tags.Count > 0)
                return tags.GroupBy(x => x).OrderByDescending(g => g.Key, StringComparer.Ordinal).First().Key;
        }
        catch { /* fall through to the filename */ }
        return Path.GetFileNameWithoutExtension(path);
    }

    private static string ReadSignature(string path)
    {
        var r = Run("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"(Get-AuthenticodeSignature -LiteralPath '{path.Replace("'", "''")}').Status\"", 20000);
        string s = r.Output.Trim();
        return string.IsNullOrWhiteSpace(s) ? "알 수 없음" : s.Split('\n')[0].Trim();
    }

    private static bool TryReadTestSigning(out bool on)
    {
        on = false;
        var r = Run("bcdedit.exe", "/enum {current}", 12000);
        if (r.ExitCode != 0) return false;
        var m = Regex.Match(r.Output, @"(?im)^\s*testsigning\s+(\S+)\s*$");
        if (!m.Success) { on = false; return true; }   // absent means off
        string v = m.Groups[1].Value.Trim();
        on = v.Equals("Yes", StringComparison.OrdinalIgnoreCase)
             || v.Equals("On", StringComparison.OrdinalIgnoreCase)
             || v == "예";
        return true;
    }

    /// <summary>
    /// Test signing as this boot has it. Asked of Code Integrity itself first
    /// (NtQuerySystemInformation, SystemCodeIntegrityInformation): its TESTSIGN
    /// bit is documented as "test signed content is allowed", which is exactly
    /// the question, and it needs no admin. SystemStartOptions - the options
    /// the kernel was started with - is undocumented and only the fallback:
    /// nothing says what it holds with Secure Boot on and bcdedit still Yes.
    /// null if neither answers.
    /// </summary>
    internal static bool? ReadEffectiveTestSigning()
    {
        try
        {
            var ci = new Native.SYSTEM_CODEINTEGRITY_INFORMATION { Length = 8 };
            if (Native.NtQuerySystemInformation(Native.SystemCodeIntegrityInformation, ref ci, 8, out _) == 0)
                return (ci.CodeIntegrityOptions & Native.CODEINTEGRITY_OPTION_TESTSIGN) != 0;
        }
        catch { /* fall back to the registry */ }

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            if (k?.GetValue("SystemStartOptions") is not string s) return null;
            return s.Split(new[] { ' ', '\t', '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(t => t.Equals("TESTSIGNING", StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    /// <summary>UEFI Secure Boot on (true) or off (false); null on a machine that does not report it.</summary>
    internal static bool? ReadSecureBoot()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return k?.GetValue("UEFISecureBootEnabled") is int v ? v != 0 : (bool?)null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Is this driver loaded right now, per the service control manager.
    ///
    /// Asked directly rather than through WMI or "sc query": Win32_SystemDriver
    /// reports PalmRejPipeline, PalmRawUsbTap and even the Wacom router as
    /// Stopped while they are loaded and working (seen 2026-09-22), and parsing
    /// sc's text has already cost this project one wrong answer. The numeric
    /// state from QueryServiceStatus has neither problem.
    /// </summary>
    internal static bool? QueryServiceRunning(string service)
    {
        IntPtr scm = IntPtr.Zero, svc = IntPtr.Zero;
        try
        {
            scm = Native.OpenSCManagerW(null, null, Native.SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return null;
            svc = Native.OpenServiceW(scm, service, Native.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return null;
            if (!Native.QueryServiceStatus(svc, out var st)) return null;
            return st.dwCurrentState == Native.SERVICE_RUNNING;
        }
        catch { return null; }
        finally
        {
            if (svc != IntPtr.Zero) Native.CloseServiceHandle(svc);
            if (scm != IntPtr.Zero) Native.CloseServiceHandle(scm);
        }
    }

    /// <summary>
    /// Which of the bound instances are here now, and whether any reports a
    /// problem. The registry keeps an instance after the tablet is switched
    /// off, so the bindings alone cannot tell "off" from "on and broken".
    /// </summary>
    internal static void ReadPresence(Report rep)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool unanswered = false;
        try
        {
            foreach (var (id, filters) in rep.Binds)
            {
                // Only devices one of our drivers sits on count. The map also
                // holds Wacom's own filtered collections (wacomrouterfilter on the
                // pen's COL01), and one of those staying present must not make a
                // switched-off touch tablet look "on but not running". "Palm", not
                // "PalmRej": the USB tap is PalmRawUsbTap.
                bool ours = filters.Any(f => f.StartsWith("Palm", StringComparison.OrdinalIgnoreCase));
                int cr = Native.CM_Locate_DevNodeW(out uint dn, id, Native.CM_LOCATE_DEVNODE_NORMAL);
                if (cr == Native.CR_NO_SUCH_DEVNODE)
                {
                    rep.DeviceStates[id] = "(연결 안 됨)";
                    continue;
                }
                if (cr != Native.CR_SUCCESS)
                {
                    // Not "absent": the question went unanswered. Counting it as
                    // absent could turn a real fault into a grey "tablet off".
                    unanswered = true;
                    rep.DeviceStates[id] = $"(확인 실패 CR 0x{cr:X2})";
                    continue;
                }
                present.Add(id);
                if (ours) rep.PresentBound++;
                int cs = Native.CM_Get_DevNode_Status(out uint status, out uint problem, dn, 0);
                if (cs != Native.CR_SUCCESS)
                {
                    unanswered = true;
                    rep.DeviceStates[id] = $"(상태 확인 실패 CR 0x{cs:X2})";
                }
                else if ((status & Native.DN_HAS_PROBLEM) != 0)
                {
                    if (ours) rep.DeviceProblems.Add((id, problem));
                    rep.DeviceStates[id] = $"(오류 코드 {problem})";
                }
                else
                {
                    rep.DeviceStates[id] = "(연결됨)";
                }
            }
            rep.PresenceKnown = !unanswered;
            rep.PresentIds = unanswered ? null : present;
        }
        catch
        {
            // Unknown presence is treated as present: better a red card that
            // says "not running" than a grey one that says "tablet off" wrongly.
            rep.PresenceKnown = false;
            rep.PresentIds = null;
        }
    }

    /// <summary>
    /// Was this file created after the current boot? An install or upgrade
    /// stages a new DriverStore folder; until the reboot, the driver that runs
    /// (if any) is the one from before. Boot time comes from the tick count,
    /// which counts across sleep and restarts only with a real restart - so a
    /// Fast Startup "shutdown and power on", which does not reload drivers,
    /// correctly still reads as "not rebooted yet".
    /// </summary>
    internal static bool CreatedSinceBoot(string path)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            DateTime boot = now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            DateTime created = File.GetCreationTimeUtc(path);
            // A file dated in the future says the clock moved - a BIOS reset sets
            // it back, the same reset that turns Secure Boot on - not that
            // anything was installed. No evidence either way, so not "pending".
            return created > boot.AddSeconds(60) && created <= now.AddMinutes(5);
        }
        catch { return false; }
    }

    private static class Native
    {
        public const uint SC_MANAGER_CONNECT = 0x0001;
        public const uint SERVICE_QUERY_STATUS = 0x0004;
        public const uint SERVICE_RUNNING = 0x00000004;
        public const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
        public const int CR_SUCCESS = 0;
        public const int CR_NO_SUCH_DEVNODE = 0x0000000D;
        public const uint DN_HAS_PROBLEM = 0x00000400;
        public const int SystemCodeIntegrityInformation = 103;
        public const uint CODEINTEGRITY_OPTION_TESTSIGN = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_CODEINTEGRITY_INFORMATION
        {
            public uint Length;
            public uint CodeIntegrityOptions;
        }

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int systemInformationClass,
            ref SYSTEM_CODEINTEGRITY_INFORMATION systemInformation, int systemInformationLength, out int returnLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, uint desiredAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseServiceHandle(IntPtr handle);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

        [DllImport("cfgmgr32.dll")]
        public static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);
    }

    internal static Dictionary<string, string[]> ReadFilterBindings()
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var hid = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID");
            if (hid == null) return result;
            foreach (var devId in hid.GetSubKeyNames().Where(n => n.Contains("VID_056A", StringComparison.OrdinalIgnoreCase)))
            {
                using var dev = hid.OpenSubKey(devId);
                if (dev == null) continue;
                foreach (var inst in dev.GetSubKeyNames())
                {
                    using var k = dev.OpenSubKey(inst);
                    var up = k?.GetValue("UpperFilters") as string[];
                    var lo = k?.GetValue("LowerFilters") as string[];
                    var all = (up ?? Array.Empty<string>()).Concat(lo ?? Array.Empty<string>()).ToArray();
                    if (all.Length > 0) result[$@"HID\{devId}\{inst}"] = all;
                }
            }
        }
        catch { /* a partial map is still useful */ }

        // PalmRawUsbTap sits below the Wacom driver on the touch tablet's USB
        // device, not on a HID collection, and its extension INF registers it
        // as a value name under Filters\*Lower rather than in LowerFilters. When
        // that device fails, the touch collections above it vanish and only its
        // own problem code says why - so it is read too. Only instances that
        // carry one of ours are kept; the rest of the USB tree is noise here.
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb != null)
            {
                foreach (var devId in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_056A", StringComparison.OrdinalIgnoreCase)))
                {
                    using var dev = usb.OpenSubKey(devId);
                    if (dev == null) continue;
                    foreach (var inst in dev.GetSubKeyNames())
                    {
                        using var k = dev.OpenSubKey(inst);
                        if (k == null) continue;
                        var names = new List<string>();
                        names.AddRange(k.GetValue("UpperFilters") as string[] ?? Array.Empty<string>());
                        names.AddRange(k.GetValue("LowerFilters") as string[] ?? Array.Empty<string>());
                        using (var filters = k.OpenSubKey("Filters"))
                        {
                            if (filters != null)
                            {
                                foreach (var position in filters.GetSubKeyNames())
                                {
                                    using var pk = filters.OpenSubKey(position);
                                    if (pk != null) names.AddRange(pk.GetValueNames().Where(n => n.Length > 0));
                                }
                            }
                        }
                        if (names.Any(n => n.StartsWith("Palm", StringComparison.OrdinalIgnoreCase)))
                            result[$@"USB\{devId}\{inst}"] = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    }
                }
            }
        }
        catch { /* the HID part above still stands */ }
        return result;
    }


    // ------------------------------------------------------------ actions ----

    /// <summary>
    /// The script for OFF or ON. Named OFF.ps1 / ON.ps1 from 1.0; the old names
    /// are still tried so a folder that was never reorganised keeps working.
    /// </summary>
    private string? FindOnOffScript(bool isOff)
    {
        string[] names = isOff
            ? new[] { "OFF.ps1", "1_내리기.ps1" }
            : new[] { "ON.ps1", "2_되돌리기.ps1" };
        foreach (var n in names)
        {
            string p = Path.Combine(_gameMode, n);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static string ActionName(bool isOff) => isOff ? "PalmRej 끄기" : "PalmRej 켜기";

    /// <summary>When the running ON/OFF script was started; its own log is written after this.</summary>
    private DateTime _scriptStartUtc = DateTime.MinValue;

    private void RunOnOff(bool isOff)
    {
        string what = ActionName(isOff);
        string? path = FindOnOffScript(isOff);
        if (path == null)
        {
            MessageBox.Show(this,
                $"{what}에 필요한 파일이 없습니다.\n\n{_gameMode}\n\n" +
                "PalmRej 를 다시 설치해 주세요.",
                what, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // No part count and no single reason: another model has one part, the
        // Cintiq Pro 24 three, and people turn it off for more than games.
        string warn = isOff
            ? "PalmRej 를 끕니다.\n\n" +
              "• 터치는 Wacom 기본 드라이버로 계속 됩니다. 손날·손바닥 걸러내기만 꺼집니다.\n" +
              "• 지금 드라이버는 보관해 두었다가 'PalmRej 켜기'로 다시 올립니다.\n" +
              "• Windows 테스트 모드도 함께 꺼집니다. 테스트 모드를 허용하지 않는\n" +
              "  프로그램(일부 게임의 보안 프로그램 등)을 쓸 때 필요합니다.\n\n" +
              "끝나면 재부팅해야 적용됩니다. 계속할까요?"
            : "꺼 두었던 PalmRej 를 다시 켭니다.\n\n" +
              "• Windows 테스트 모드를 켜고, 보관해 둔 드라이버를 다시 설치합니다.\n" +
              "• 테스트 모드를 허용하지 않는 프로그램을 쓸 때는\n" +
              "  다시 'PalmRej 끄기'를 누르세요.\n\n" +
              "끝나면 재부팅해야 적용됩니다. 계속할까요?";

        if (MessageBox.Show(this, warn, what, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        try
        {
            // The script does the work and writes its own log; this app only
            // launches it. Deliberately NOT reimplemented here - the scripts
            // already handle the extension-INF registration and the test
            // signing reboot, and a second implementation of that would be a
            // second thing to get wrong.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                // -Quiet: no MessageBox and no "press Enter" from the script.
                // This app owns the dialog.
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\" -Quiet",
                // No runas: this app already runs elevated by its manifest, so
                // the child inherits the token. Asking again would pop a second
                // UAC prompt and force UseShellExecute, which cannot hide a
                // console window reliably.
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = _gameMode,
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Exited += (_, _) =>
            {
                int code = proc.ExitCode;
                proc.Dispose();
                // Exited arrives on a pool thread; the dialog belongs on the UI one.
                BeginInvoke(new Action(() => OnScriptFinished(isOff, code)));
            };

            _scriptRunning = true;
            UpdateButtons();
            _banner.SetState(null, $"{what} 진행 중", "끝날 때까지 창을 닫지 마세요");
            _scriptStartUtc = DateTime.UtcNow;
            proc.Start();
        }
        catch (Exception ex)
        {
            _scriptRunning = false;
            UpdateButtons();
            MessageBox.Show(this, "실행하지 못했습니다.\n\n" + ex.Message, what,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _ = RefreshStatusAsync();
        }
    }

    /// <summary>
    /// The scripts used to raise their own MessageBox. With no console there is
    /// nowhere for one to appear - it opened BEHIND the console window and the
    /// script waited forever for an OK nobody could click. The result dialog
    /// belongs to this app, which has a window to own it.
    /// </summary>
    private void OnScriptFinished(bool isOff, int exitCode)
    {
        _scriptRunning = false;
        UpdateButtons();

        string what = ActionName(isOff);
        string log = NewestRunLog();
        string tail = string.IsNullOrEmpty(log) ? "" : "\n\n기록: " + log;
        // The exit code alone does not say everything: the scripts also print
        // marker lines (PAUSED_UNTIL_REBOOT=1, PARTIAL=1, ...) into their log.
        string? logText = ReadRunLogSince(log, _scriptStartUtc);

        var (text, icon) = ScriptResultMessage(isOff, exitCode, logText);
        MessageBox.Show(this, text + tail, what, MessageBoxButtons.OK, icon);

        _ = RefreshStatusAsync();
    }

    /// <summary>
    /// What the result dialog says. Kept apart from the dialog so every exit code
    /// and marker can be checked without running a script or showing a window.
    /// <paramref name="logText"/> is this run's log, or null when it could not be read.
    /// </summary>
    internal static (string Text, MessageBoxIcon Icon) ScriptResultMessage(bool isOff, int exitCode, string? logText)
    {
        string what = ActionName(isOff);

        if (exitCode == 0)
        {
            // ON in a boot that started without test signing (always the case after
            // an OFF and its reboot): the install binds our test-signed drivers right
            // away, Windows refuses them (code 52), and the touch and part of the pen
            // stop until the reboot. ON.ps1 prints PAUSED_UNTIL_REBOOT=1 then. With no
            // readable log the app cannot tell, so it says the careful thing.
            if (!isOff && (logText == null || HasMarker(logText, "PAUSED_UNTIL_REBOOT=1")))
                return ($"{what} 완료.\n\n" +
                        "재부팅 전까지 터치와 펜 일부가 멈춰 있을 수 있습니다.\n" +
                        "지금 재부팅하세요.", MessageBoxIcon.Warning);
            return ($"{what} 완료.\n\n재부팅하면 적용됩니다.", MessageBoxIcon.Information);
        }

        if (exitCode == 3 || exitCode == 4)
        {
            // OFF only: the drivers are down but Windows test mode is still on.
            // 3: bcdedit would not turn it off. 4: BitLocker could not be paused
            // for the change - a reboot does not help that one, pausing it does.
            return ("PalmRej 는 껐지만 Windows 테스트 모드는 켜진 채입니다.\n\n" +
                    "터치는 Wacom 드라이버로 계속 됩니다.\n" +
                    (exitCode == 4
                        ? "BitLocker 보호를 잠시 멈출 수 없어서 테스트 모드를 끄지 않았습니다.\n" +
                          "테스트 모드를 허용하지 않는 프로그램을 쓰려면, Windows 설정에서\n" +
                          "BitLocker 보호를 일시 중단한 뒤 'PalmRej 끄기'를 한 번 더 누르세요."
                        : "테스트 모드를 허용하지 않는 프로그램을 쓰려면, 재부팅한 뒤\n" +
                          "'PalmRej 끄기'를 한 번 더 누르세요. 이유는 기록에 있습니다."),
                    MessageBoxIcon.Warning);
        }

        if (isOff)
        {
            // 5: OFF stopped at its check with something of ours left behind. If the
            // packages were already removed, their services go at the next boot while
            // a device still names one of them - the touch stops. Any other failure
            // may also have stopped half way, so the advice is the same: no reboot,
            // press it again.
            string head = exitCode == 5
                ? $"{what}가 다 끝나지 않았습니다.\n\n지금 재부팅하면 터치가 멈출 수 있습니다.\n"
                : $"{what} 실패  (코드 {exitCode})\n\n";
            return (head +
                    "재부팅하지 말고 'PalmRej 끄기'를 한 번 더 누르세요.\n\n" +
                    "두 번째에도 안 되면 재부팅하지 말고, '상태 보고서 저장'으로\n" +
                    "보고서를 저장해서 기록 파일과 함께 보내 주세요.",
                    exitCode == 5 ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
        }

        if (exitCode == 2)
        {
            // ON only. PARTIAL_STOP=filter / PARTIAL=1: some packages went in and one
            // did not. Otherwise: test signing was turned on but does not take effect
            // until a reboot, so nothing could be installed yet.
            if (HasMarker(logText, "PARTIAL_STOP=filter") || HasMarker(logText, "PARTIAL=1"))
                return ("일부만 설치하고 멈췄습니다.\n\n" +
                        "재부팅한 다음 'PalmRej 켜기'를 한 번 더 누르세요.\n" +
                        "두 번째에도 멈추면 '상태 보고서 저장'으로 보고서를 저장해서\n" +
                        "기록 파일과 함께 보내 주세요.", MessageBoxIcon.Warning);
            return ("테스트 모드만 켜고 멈췄습니다.\n\n" +
                    "재부팅한 다음 'PalmRej 켜기'를 한 번 더 누르세요.\n" +
                    "그때 설치가 끝납니다.", MessageBoxIcon.Warning);
        }

        return ($"{what} 실패  (코드 {exitCode})\n\n" +
                "'상태 보고서 저장'으로 보고서를 저장해서 기록 파일과 함께 보내 주세요.",
                MessageBoxIcon.Error);
    }

    /// <summary>A whole line in the script's log, as the script printed it.</summary>
    internal static bool HasMarker(string? logText, string marker) =>
        logText != null && Regex.IsMatch(logText, "(?m)^" + Regex.Escape(marker) + @"\s*$");

    /// <summary>
    /// The run log's text, or null when it is missing, unreadable, or older than
    /// this run (then it belongs to some other run and says nothing about this one).
    /// </summary>
    internal static string? ReadRunLogSince(string path, DateTime startUtc)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            // A little slack for the file system's time resolution. (MinValue - 2 s
            // would throw, and then every log would read as missing.)
            if (startUtc > DateTime.MinValue.AddSeconds(2) && fi.LastWriteTimeUtc < startUtc.AddSeconds(-2)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return sr.ReadToEnd();
        }
        catch { return null; }
    }

    private string NewestRunLog()
    {
        try
        {
            // 1.0 writes run logs into a 실행기록 subfolder; older scripts wrote
            // them beside themselves. Look in both and take the newest.
            var dirs = new[] { Path.Combine(_gameMode, "실행기록"), _gameMode }
                .Where(Directory.Exists);
            var f = dirs
                .SelectMany(d => new DirectoryInfo(d).GetFiles("실행기록_*.txt"))
                .OrderByDescending(x => x.LastWriteTimeUtc)
                .FirstOrDefault();
            return f?.FullName ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Saved where the person chooses, the Desktop by default. It used to go
    /// into the app's own folder, which for an installed copy is Program Files -
    /// somewhere nobody would think to look for a file they were asked to send.
    /// </summary>
    private async Task SaveReportAsync()
    {
        if (_last == null && !_scriptRunning) await RefreshStatusAsync();
        if (_last == null) return;

        using var dlg = new SaveFileDialog
        {
            Title = "상태 보고서 저장",
            Filter = "텍스트 파일 (*.txt)|*.txt",
            FileName = $"PalmRej_상태_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            await File.WriteAllTextAsync(dlg.FileName, _last.Text, new UTF8Encoding(true));
            MessageBox.Show(this, "저장했습니다.\n\n" + dlg.FileName, "상태 보고서 저장",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "저장하지 못했습니다.\n\n" + ex.Message, "상태 보고서 저장",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------------------------------------------------- diag log ----

    /// <summary>
    /// "진단 로그 기록": one window at a time. It is not owned by this one, so it
    /// can sit beside it and keep its own taskbar button; closing this window
    /// while it records asks first (OnFormClosing).
    /// </summary>
    private void OpenDiagLog()
    {
        if (_diagForm != null && !_diagForm.IsDisposed)
        {
            if (_diagForm.WindowState == FormWindowState.Minimized) _diagForm.WindowState = FormWindowState.Normal;
            _diagForm.Activate();
            return;
        }
        string installed = _last != null && !string.IsNullOrEmpty(_last.Filter.Version) ? ReleaseVersion(_last.Filter.Version, _last.Filter.Build) : "-";
        // Explorer opens on the saved file, as in the test program: the user's next step is sending it.
        var f = new DiagLogForm { AppLabel = $"PalmRej 관리 {AppVersion} (설치된 PalmRej {installed})", OpenFolderWhenDone = true };
        f.StartPosition = FormStartPosition.Manual;
        var wa = Screen.FromControl(this).WorkingArea;
        f.Location = new Point(Math.Max(wa.Left, Math.Min(Left + 40, wa.Right - 460)), Math.Max(wa.Top, Math.Min(Top + 40, wa.Bottom - 520)));
        f.RecordingChanged += () => _diagLink.Text = f.IsRecording ? "진단 로그 기록 중" : "진단 로그 기록";
        f.FormClosed += (_, _) => { _diagForm = null; _diagLink.Text = "진단 로그 기록"; };
        _diagForm = f;
        f.Show();
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        var diag = _diagForm;
        if (diag != null && !diag.IsDisposed && diag.IsRecording
            && e.CloseReason is not (CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing))
        {
            e.Cancel = true;
            if (_closeAfterDiagStop) return;   // already stopping
            if (MessageBox.Show(this, "진단 로그를 기록하고 있습니다.\n\n기록을 멈추고 저장한 뒤 닫을까요?",
                    "진단 로그", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _closeAfterDiagStop = true;
            try { await diag.StopAndSaveAsync("관리 창 닫기"); } catch { }
            // Both windows are about to go: say where the file went (and whether it is whole) first.
            try
            {
                var cap = diag.LastCapture;
                if (cap != null)
                {
                    var (state, title, detail) = DiagLogForm.ResultText(cap);
                    MessageBox.Show(this, $"{title}\n\n{detail}\n\n{cap.LogPath}", "진단 로그", MessageBoxButtons.OK,
                        state == false ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
            catch { }
            try { if (!diag.IsDisposed) diag.Close(); } catch { }
            Close();
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>
    /// The installed PalmRej version ("0.1.17", "1.0.1-exp"), or "" when it cannot be
    /// told. Read the same way as the status card (DriverVer of the package the
    /// running filter's ImagePath points into, and the label in its banner);
    /// registry and file reads only, no admin needed.
    /// </summary>
    internal static string InstalledFilterVersion()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"{ServicesRoot}\PalmRejFilter");
            string ip = k?.GetValue("ImagePath")?.ToString() ?? "";
            if (ip.Length == 0) return "";
            return PackageReleaseVersion(ResolveSystemRoot(ip));
        }
        catch { return ""; }
    }

    // -------------------------------------------------------------- util ----

    private static bool IsAdmin()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private sealed class RunResult
    {
        public int ExitCode = -1;
        public string Output = "";
    }

    private static RunResult Run(string exe, string args, int timeoutMs)
    {
        var res = new RunResult();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return res;
            string o = p.StandardOutput.ReadToEnd();
            string e = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } }
            res.ExitCode = p.HasExited ? p.ExitCode : -1;
            res.Output = (o + "\n" + e).Trim();
        }
        catch (Exception ex) { res.Output = ex.Message; }
        return res;
    }
}

/// <summary>Colours and fonts, in one place.</summary>
internal static class Ui
{
    public static readonly Color Window = Color.FromArgb(255, 255, 255);
    public static readonly Color Text = Color.FromArgb(44, 44, 42);
    public static readonly Color Secondary = Color.FromArgb(95, 94, 90);
    public static readonly Color Muted = Color.FromArgb(136, 135, 128);
    public static readonly Color Divider = Color.FromArgb(232, 230, 222);
    public static readonly Color Link = Color.FromArgb(24, 95, 165);
    public static readonly Color ButtonBorder = Color.FromArgb(200, 198, 190);
    public static readonly Color ButtonHover = Color.FromArgb(244, 243, 238);
    public static readonly Color ButtonDown = Color.FromArgb(232, 230, 222);
    public static readonly Color DisabledFill = Color.FromArgb(247, 246, 242);
    public static readonly Color Frame = Color.FromArgb(200, 198, 190);
    public static readonly Color CloseHover = Color.FromArgb(232, 17, 35);
    public static readonly Color CloseDown = Color.FromArgb(241, 112, 122);

    // (fill, strong, text) per state: green, red, grey
    public static readonly (Color Fill, Color Strong, Color Text) Good = (Color.FromArgb(234, 243, 222), Color.FromArgb(59, 109, 17), Color.FromArgb(39, 80, 10));
    public static readonly (Color Fill, Color Strong, Color Text) Bad = (Color.FromArgb(252, 235, 235), Color.FromArgb(163, 45, 45), Color.FromArgb(121, 31, 31));
    public static readonly (Color Fill, Color Strong, Color Text) Calm = (Color.FromArgb(241, 239, 232), Color.FromArgb(95, 94, 90), Color.FromArgb(68, 68, 65));

    /// <summary>
    /// Lines for <paramref name="text"/> at <paramref name="width"/>, broken at
    /// spaces. GDI's own word break splits Hangul between syllables ("막혔습 /
    /// 니다"), which reads badly; only a word too long for a whole line is
    /// split, and then by character.
    /// </summary>
    public static List<string> Wrap(string text, Font font, int width)
    {
        var lines = new List<string>();
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        int W(string s) => TextRenderer.MeasureText(s, font, new Size(int.MaxValue, int.MaxValue), flags).Width;
        foreach (var para in (text ?? "").Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (W(candidate) <= width) { line = candidate; continue; }
                if (line.Length > 0) { lines.Add(line); line = ""; }
                string w = word;
                while (W(w) > width && w.Length > 1)
                {
                    int n = w.Length - 1;
                    while (n > 1 && W(w.Substring(0, n)) > width) n--;
                    lines.Add(w.Substring(0, n));
                    w = w.Substring(n);
                }
                line = w;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Height of <see cref="Wrap"/>'s lines.</summary>
    public static int WrappedHeight(string text, Font font, int width) =>
        Wrap(text, font, width).Count * LineHeight(font);

    public static int LineHeight(Font font) =>
        TextRenderer.MeasureText("가Ag", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;

    /// <summary>Draws <see cref="Wrap"/>'s lines from the top of <paramref name="r"/>.</summary>
    public static void DrawWrapped(Graphics g, string text, Font font, Rectangle r, Color color)
    {
        int y = r.Y, lh = LineHeight(font);
        foreach (var line in Wrap(text, font, r.Width))
        {
            TextRenderer.DrawText(g, line, font, new Rectangle(r.X, y, r.Width, lh), color,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            y += lh;
        }
    }
    public static (Color Fill, Color Strong, Color Text) For(bool? state) => state switch
    {
        true => Good,
        false => Bad,
        _ => Calm,
    };

    private const string Face = "Malgun Gothic";
    public static readonly Font Body = new(Face, 9.75F, FontStyle.Regular);
    public static readonly Font Small = new(Face, 8.25F, FontStyle.Regular);
    public static readonly Font Button = new(Face, 10F, FontStyle.Regular);
    public static readonly Font Title = new(Face, 13F, FontStyle.Bold);
    public static readonly Font Header = new(Face, 10.5F, FontStyle.Bold);
    public static readonly Font Detail = new(Face, 9.75F, FontStyle.Regular);
    public static readonly Font Glyph = new("Segoe UI Symbol", 14F, FontStyle.Bold);
}

/// <summary>
/// The coloured block at the top: a round mark, one bold line, one line of
/// what to do. Painted rather than built from Labels so the corners can be
/// round and the height can follow the text.
/// </summary>
internal sealed class StatusBanner : Control
{
    private bool? _state;
    private string _title = "";
    private string _detail = "";

    private const int Pad = 14;
    private const int Mark = 34;
    private const int Gap = 12;

    public StatusBanner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 76;
    }

    public void SetState(bool? state, string title, string detail)
    {
        _state = state;
        _title = title ?? "";
        _detail = detail ?? "";
        AccessibleName = _title + ". " + _detail;
        Height = Measure();
        Invalidate();
    }

    private int TextWidth => Math.Max(50, Width - Scale(Pad) * 2 - Scale(Mark) - Scale(Gap));

    private int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    private int Measure()
    {
        int t = Ui.WrappedHeight(_title, Ui.Title, TextWidth);
        int d = string.IsNullOrEmpty(_detail) ? 0 : Ui.WrappedHeight(_detail, Ui.Detail, TextWidth);
        int text = t + (d > 0 ? 4 + d : 0);
        return Math.Max(Scale(Mark), text) + Scale(Pad) * 2;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int h = Measure();
        if (h != Height) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var (fill, strong, text) = Ui.For(_state);

        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Scale(10)))
        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);

        int mark = Scale(Mark);
        var markRect = new Rectangle(Scale(Pad), (Height - mark) / 2, mark, mark);
        using (var b = new SolidBrush(strong)) g.FillEllipse(b, markRect);
        string glyph = _state == true ? "✓" : _state == false ? "!" : "–";
        TextRenderer.DrawText(g, glyph, Ui.Glyph, markRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int x = Scale(Pad) + mark + Scale(Gap);
        int th = Ui.WrappedHeight(_title, Ui.Title, TextWidth);
        int dh = string.IsNullOrEmpty(_detail) ? 0 : Ui.WrappedHeight(_detail, Ui.Detail, TextWidth);
        int total = th + (dh > 0 ? 4 + dh : 0);
        int y = (Height - total) / 2;
        Ui.DrawWrapped(g, _title, Ui.Title, new Rectangle(x, y, TextWidth, th), text);
        if (dh > 0)
            Ui.DrawWrapped(g, _detail, Ui.Detail, new Rectangle(x, y + th + 4, TextWidth, dh), text);
    }

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>One "name — value" line under the banner, with a hairline below it.</summary>
internal sealed class StatusRow : Control
{
    private readonly string _name;
    private bool? _state;
    private string _value = "";

    private const int NameWidth = 96;
    private const int VPad = 9;

    public bool DrawDivider { get; set; } = true;

    public StatusRow(string name)
    {
        _name = name;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = new Padding(0);
        Height = 36;
    }

    public void SetValue(bool? state, string value)
    {
        _state = state;
        _value = value ?? "";
        AccessibleName = _name + ": " + _value;
        Height = Measure();
        Invalidate();
    }

    private int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96.0);
    private int ValueWidth => Math.Max(50, Width - Scale(NameWidth) - Scale(18));

    private int Measure()
    {
        int v = Ui.WrappedHeight(_value, Ui.Body, ValueWidth);
        int n = Ui.LineHeight(Ui.Body);
        return Math.Max(v, n) + Scale(VPad) * 2;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int h = Measure();
        if (h != Height) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        var (_, strong, _) = Ui.For(_state);

        TextRenderer.DrawText(g, _name, Ui.Body, new Rectangle(0, Scale(VPad), Scale(NameWidth), Height - Scale(VPad) * 2), Ui.Secondary, flags);

        string mark = _state == true ? "✓" : _state == false ? "✕" : "–";
        int markX = Scale(NameWidth);
        TextRenderer.DrawText(g, mark, Ui.Body, new Rectangle(markX, Scale(VPad), Scale(18), Height - Scale(VPad) * 2), strong, flags);
        Ui.DrawWrapped(g, _value, Ui.Body, new Rectangle(markX + Scale(18), Scale(VPad), ValueWidth, Height - Scale(VPad) * 2),
            _state == null ? Ui.Text : strong);

        if (DrawDivider)
        {
            using var pen = new Pen(Ui.Divider);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>
/// The window's own title strip: the name and version on the left, minimise
/// and close on the right. The system frame on Windows 10 is a white bar with
/// a greyed-out maximise button this fixed-size window can never use; drawing
/// the strip ourselves gives one look on Windows 10 and 11.
/// </summary>
internal sealed class TitleBar : Control
{
    private readonly Form _form;
    private string _title = "PalmRej";
    private string _version = "";
    private Rectangle _minRect, _closeRect;
    private int _hover;     // 0 none, 1 minimise, 2 close
    private int _pressed;   // same codes, while the mouse button is down on it

    private const int BarHeight = 40;
    private const int ButtonWidth = 46;

    public TitleBar(Form form)
    {
        _form = form;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Ui.Window;
        Height = BarHeight;
        Margin = new Padding(0);
        AccessibleRole = AccessibleRole.TitleBar;
        AccessibleName = _title;
    }

    /// <summary>The name on the left; "PalmRej" unless a window says otherwise.</summary>
    public void SetTitle(string title)
    {
        _title = title ?? "";
        AccessibleName = string.IsNullOrEmpty(_version) ? _title : _title + " " + _version;
        Invalidate();
    }

    public void SetVersion(string version)
    {
        _version = version ?? "";
        AccessibleName = string.IsNullOrEmpty(_version) ? _title : _title + " " + _version;
        Invalidate();
    }

    private int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int bw = Scale(ButtonWidth);
        _closeRect = new Rectangle(Width - bw, 0, bw, Height);
        _minRect = new Rectangle(Width - bw * 2, 0, bw, Height);
        Invalidate();
    }

    private int HitButton(Point p) => _closeRect.Contains(p) ? 2 : _minRect.Contains(p) ? 1 : 0;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitButton(e.Location);
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != 0) { _hover = 0; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        int h = HitButton(e.Location);
        if (h != 0) { _pressed = h; Invalidate(); return; }
        // Anywhere else on the strip drags the window, the way a real title bar does.
        Native.ReleaseCapture();
        Native.SendMessage(_form.Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int was = _pressed;
        _pressed = 0;
        Invalidate();
        if (was == 0 || HitButton(e.Location) != was) return;
        if (was == 1) _form.WindowState = FormWindowState.Minimized;
        else _form.Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Window);

        // name, then the version in grey
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        int x = Scale(16);
        var nameSize = TextRenderer.MeasureText(_title, Ui.Header, new Size(int.MaxValue, Height), flags);
        TextRenderer.DrawText(g, _title, Ui.Header, new Rectangle(x, 0, nameSize.Width, Height), Ui.Text, flags);
        if (!string.IsNullOrEmpty(_version))
        {
            var vRect = new Rectangle(x + nameSize.Width + Scale(8), 1, Scale(120), Height);
            TextRenderer.DrawText(g, _version, Ui.Body, vRect, Ui.Muted, flags);
        }

        // buttons: grey on hover for minimise, the Windows red for close
        if (_hover == 1 || _pressed == 1)
            using (var b = new SolidBrush(_pressed == 1 ? Ui.ButtonDown : Ui.ButtonHover)) g.FillRectangle(b, _minRect);
        if (_hover == 2 || _pressed == 2)
            using (var b = new SolidBrush(_pressed == 2 ? Ui.CloseDown : Ui.CloseHover)) g.FillRectangle(b, _closeRect);

        int s = Scale(5);   // half the glyph size
        using (var pen = new Pen(Ui.Text, Math.Max(1, Scale(1))))
        {
            var c = new Point(_minRect.X + _minRect.Width / 2, _minRect.Y + _minRect.Height / 2);
            g.DrawLine(pen, c.X - s, c.Y, c.X + s, c.Y);
        }
        using (var pen = new Pen(_hover == 2 || _pressed == 2 ? Color.White : Ui.Text, Math.Max(1, Scale(1))))
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var c = new Point(_closeRect.X + _closeRect.Width / 2, _closeRect.Y + _closeRect.Height / 2);
            g.DrawLine(pen, c.X - s, c.Y - s, c.X + s, c.Y + s);
            g.DrawLine(pen, c.X - s, c.Y + s, c.X + s, c.Y - s);
            g.SmoothingMode = old;
        }
    }

    private static class Native
    {
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 2;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
