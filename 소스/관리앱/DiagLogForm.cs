using System.Diagnostics;

namespace PalmRejFilterManager;

/// <summary>
/// "진단 로그 기록": the window around DiagLogCapture. Used by the manager app
/// (link under the two big buttons) and by the stand-alone "PalmRej 로그 시험.exe".
///
/// Ready → 기록 시작 → Recording (live counts) → 멈추고 저장 → Done (summary).
/// </summary>
internal sealed class DiagLogForm : Form
{
    private enum Phase { Ready, Recording, Stopping, Done }

    private const int ContentWidth = 408;

    /// <summary>Open Explorer on the saved file when done (both the test program and the manager set it).</summary>
    public bool OpenFolderWhenDone { get; set; }
    /// <summary>Written into the summary file: which program made the log.</summary>
    public string AppLabel { get; set; } = "";
    /// <summary>The installed PalmRej version, read when 기록 시작 is pressed: it goes at the front of the file name.</summary>
    public Func<string> InstalledVersion { get; set; } = MainForm.InstalledFilterVersion;
    /// <summary>Raised on the UI thread whenever recording starts or ends.</summary>
    public event Action? RecordingChanged;

    private readonly TitleBar _titleBar;
    private readonly StatusBanner _banner = new();
    private readonly StatusRow _whereRow = new("저장 위치");
    private readonly StatusRow _timeRow = new("걸린 시간");
    private readonly StatusRow _linesRow = new("저장한 줄");
    private readonly StatusRow _lossRow = new("빠진 부분");
    private readonly StatusRow _driverRow = new("드라이버별");
    private readonly Button _leftButton = new();
    private readonly Button _rightButton = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private Phase _phase = Phase.Ready;
    private string _folder;
    private DiagLogCapture? _cap;
    private DateTime _startedUtc;
    private Task? _stopTask;
    private bool _closeWhenStopped;
    private DiagLogException? _lastError;

    public bool IsRecording => _phase is Phase.Recording or Phase.Stopping;

    public DiagLogForm()
    {
        _folder = DefaultFolder();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "PalmRej 진단 로그";
        Font = Ui.Body;
        BackColor = Ui.Window;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        Padding = new Padding(1);
        _titleBar = new TitleBar(this);
        _titleBar.SetTitle("PalmRej 진단 로그");
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildUi();
        ResumeLayout(true);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && !IsRecording) Close(); };
        _timer.Tick += (_, _) => Tick();
        ShowReady();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_MAXIMIZEBOX = 0x00010000, WS_MINIMIZEBOX = 0x00020000, WS_SYSMENU = 0x00080000;
            var cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
            cp.Style &= ~WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Ui.Frame);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 4, 16, 14), BackColor = Color.Transparent, Margin = new Padding(0),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _banner.Width = ContentWidth;
        _banner.Margin = new Padding(0, 0, 0, 10);
        root.Controls.Add(_banner, 0, 0);

        var rows = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 5, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 12), BackColor = Color.Transparent,
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));
        var all = new[] { _whereRow, _timeRow, _linesRow, _lossRow, _driverRow };
        for (int i = 0; i < all.Length; i++)
        {
            rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            all[i].Width = ContentWidth;
            all[i].DrawDivider = i < all.Length - 1;
            rows.Controls.Add(all[i], 0, i);
        }
        root.Controls.Add(rows, 0, 1);

        var buttons = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, Width = ContentWidth, Height = 44,
            Margin = new Padding(0), BackColor = Color.Transparent,
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        MainForm.StyleActionButton(_leftButton, "");
        MainForm.StyleActionButton(_rightButton, "");
        _leftButton.Margin = new Padding(0, 0, 4, 0);
        _rightButton.Margin = new Padding(4, 0, 0, 0);
        _leftButton.Click += (_, _) => LeftClicked();
        _rightButton.Click += async (_, _) => await RightClickedAsync();
        buttons.Controls.Add(_leftButton, 0, 0);
        buttons.Controls.Add(_rightButton, 1, 0);
        root.Controls.Add(buttons, 0, 2);

        var outer = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _titleBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        outer.Controls.Add(_titleBar, 0, 0);
        outer.Controls.Add(root, 0, 1);
        outer.Location = new Point(1, 1);
        Controls.Add(outer);
    }

    private void SetButtons(string left, bool leftOn, string right, bool rightOn)
    {
        _leftButton.Text = left; _leftButton.Enabled = leftOn;
        _rightButton.Text = right; _rightButton.Enabled = rightOn;
        foreach (var b in new[] { _leftButton, _rightButton })
        {
            b.BackColor = b.Enabled ? Ui.Window : Ui.DisabledFill;
            b.FlatAppearance.BorderColor = b.Enabled ? Ui.ButtonBorder : Ui.Divider;
        }
    }

    // ------------------------------------------------------------ phases ----

    private void ShowReady(DiagLogException? error = null)
    {
        _phase = Phase.Ready;
        _lastError = error;
        if (error == null)
            _banner.SetState(null, "진단 로그 기록",
                "'기록 시작'을 누른 뒤부터만 저장됩니다.\n문제가 생기기 전에 미리 켜 두세요");
        else
        {
            var (t, d) = error.UserText();
            _banner.SetState(false, t, d);
        }
        _whereRow.SetValue(null, _folder);
        _timeRow.SetValue(null, "기록 전");
        _linesRow.SetValue(null, "");
        _lossRow.SetValue(null, "");
        _driverRow.SetValue(null, "");
        SetButtons("위치 바꾸기", true, "기록 시작", true);
    }

    private void Start()
    {
        string version = "";
        try { version = DiagLogFormat.SafeVersion(InstalledVersion()); } catch { }
        var now = DateTime.Now;
        string path = Path.Combine(_folder, DiagLogFormat.FileNameFor(now, version));
        for (int i = 2; File.Exists(path) && i < 100; i++)
            path = Path.Combine(_folder, Path.GetFileNameWithoutExtension(DiagLogFormat.FileNameFor(now, version)) + $" ({i}).log");

        var cap = new DiagLogCapture(path) { AppLabel = AppLabel, InstalledVersion = version };
        SetButtons("위치 바꾸기", false, "시작하는 중…", false);
        Cursor = Cursors.WaitCursor;
        try
        {
            cap.Start();
        }
        catch (DiagLogException ex)
        {
            ShowReady(ex);
            return;
        }
        catch (Exception ex)
        {
            ShowReady(new DiagLogException(DiagLogError.Other, 0, ex.Message));
            return;
        }
        finally { Cursor = Cursors.Default; }

        _cap = cap;
        _startedUtc = DateTime.UtcNow;
        RememberFolder(_folder);
        _phase = Phase.Recording;
        _banner.SetState(null, "기록 중", "문제가 생기는 동작을 해 본 뒤 '멈추고 저장'을 누르세요");
        _whereRow.SetValue(null, path);
        SetButtons("폴더 열기", true, "멈추고 저장", true);
        Tick();
        _timer.Start();
        RecordingChanged?.Invoke();
    }

    /// <summary>Stops and saves; safe to call more than once (the same task comes back).</summary>
    public Task StopAndSaveAsync(string reason = "사용자")
    {
        if (_cap == null || _phase == Phase.Done) return Task.CompletedTask;
        if (_stopTask != null) return _stopTask;
        _phase = Phase.Stopping;
        _banner.SetState(null, "저장하는 중", "몇 초 걸릴 수 있습니다");
        SetButtons("폴더 열기", false, "저장하는 중…", false);
        var cap = _cap;
        _stopTask = StopCore(cap, reason);
        return _stopTask;
    }

    /// <summary>
    /// Never leaves the window in Stopping: whatever Stop does, the phase ends in
    /// Done and the task completes, so neither this window nor the manager is
    /// left refusing to close.
    /// </summary>
    private async Task StopCore(DiagLogCapture cap, string reason)
    {
        try { await Task.Run(() => cap.Stop(reason)); }
        catch (Exception ex) { cap.StopError ??= ex.GetType().Name + ": " + ex.Message; }
        try
        {
            _timer.Stop();
            ShowDone(cap);
        }
        catch { _phase = Phase.Done; }
        finally { if (_phase == Phase.Stopping) _phase = Phase.Done; }
        try { RecordingChanged?.Invoke(); } catch { }
        if (OpenFolderWhenDone) OpenFolder(cap.LogPath);
        if (_closeWhenStopped) Close();
    }

    /// <summary>
    /// The result in the window's words: (green / yellow / red, headline, detail).
    /// The manager shows the same text when it closes this window itself.
    /// </summary>
    internal static (bool? State, string Title, string Detail) ResultText(DiagLogCapture cap)
    {
        if (cap.LinesWritten == 0)
            return (false, "저장한 줄이 없습니다", "palm 이 든 줄이 하나도 오지 않았습니다. 요약 파일을 보내 주세요");
        if (cap.WriteError != null)
            return (false, "일부만 저장했습니다", cap.WriteError);
        if (cap.Incomplete)
            return (false, "저장했지만 빠진 부분이 있습니다", "같은 동작을 한 번 더 기록해 주세요. 자세한 내용은 요약 파일에 있습니다");
        if (cap.LowDisk)
            return (null, "저장했습니다", "디스크 공간이 모자라 일찍 멈췄습니다. 그때까지의 기록은 모두 저장했습니다");
        if (cap.SessionEnded)
            return (null, "저장했습니다", "Windows 가 기록을 일찍 멈췄습니다. 그때까지의 기록은 모두 저장했습니다");
        return (true, "저장했습니다", "파일 두 개를 저장했습니다: 로그와 요약");
    }

    private void ShowDone(DiagLogCapture cap)
    {
        _phase = Phase.Done;
        FillNumbers(cap, final: true);
        var (state, title, detail) = ResultText(cap);
        _banner.SetState(state, title, detail);
        _whereRow.SetValue(null, cap.LogPath);
        SetButtons("다시 기록", true, "닫기", true);
    }

    /// <summary>The last capture, once it is done (for the manager's closing message).</summary>
    internal DiagLogCapture? LastCapture => _phase == Phase.Done ? _cap : null;

    private void Tick()
    {
        var cap = _cap;
        if (cap == null || _phase != Phase.Recording) return;
        FillNumbers(cap, final: false);
        if (cap.NeedsStop)
            _ = StopAndSaveAsync(cap.LowDisk ? "디스크 부족" : cap.WriteError != null ? "쓰기 오류" : "세션 끝남");
    }

    private void FillNumbers(DiagLogCapture cap, bool final)
    {
        var dur = final ? cap.EndedLocal - cap.StartedLocal : DateTime.UtcNow - _startedUtc;
        _timeRow.SetValue(null, DiagLogFormat.Duration(dur));
        _linesRow.SetValue(null, $"{cap.LinesWritten:N0}줄 · {DiagLogFormat.Size(cap.BytesQueued)}");
        // Yes / no only: the counters are in different units (events, whole
        // buffers, notices) and cannot be added into a number of lines.
        bool missing = cap.Incomplete;
        _lossRow.SetValue(missing ? false : null, missing ? "있음 (요약 파일 참고)" : "없음");
        _driverRow.SetValue(null,
            $"필터 {cap.DriverLines[0]:N0} · 파이프라인 {cap.DriverLines[1]:N0} · 탭 {cap.DriverLines[2]:N0} · 기타 {cap.DriverLines[3]:N0}");
    }

    // ----------------------------------------------------------- buttons ----

    private void LeftClicked()
    {
        if (_phase == Phase.Done)
        {
            // 다시 기록: back to Ready in the same window (no new UAC prompt for the test exe).
            _cap = null;
            _stopTask = null;
            _closeWhenStopped = false;
            ShowReady();
            return;
        }
        if (_phase == Phase.Ready)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "진단 로그를 저장할 폴더",
                UseDescriptionForTitle = true,
                SelectedPath = _folder,
                ShowNewFolderButton = true,
            };
            if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
            {
                _folder = dlg.SelectedPath;
                _whereRow.SetValue(null, _folder);
            }
            return;
        }
        if (_cap != null) OpenFolder(_cap.LogPath);
    }

    private async Task RightClickedAsync()
    {
        switch (_phase)
        {
            case Phase.Ready: Start(); break;
            case Phase.Recording: await StopAndSaveAsync(); break;
            case Phase.Done: Close(); break;
        }
    }

    private static void OpenFolder(string file)
    {
        try
        {
            if (File.Exists(file))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            else if (Directory.Exists(Path.GetDirectoryName(file)))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(file)}\"") { UseShellExecute = true });
        }
        catch { /* nothing to open is not worth a dialog */ }
    }

    // ----------------------------------------------------------- closing ----

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
        {
            _cap?.EmergencyStop("Windows 종료");
            base.OnFormClosing(e);
            return;
        }
        if (_phase == Phase.Stopping) { _closeWhenStopped = true; e.Cancel = true; return; }
        if (_phase == Phase.Recording)
        {
            e.Cancel = true;
            if (MessageBox.Show(this, "진단 로그를 기록하고 있습니다.\n\n멈추고 저장한 뒤 닫을까요?",
                    "진단 로그", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _closeWhenStopped = true;
                _ = StopAndSaveAsync("창 닫기");
            }
            return;
        }
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------ folder ----

    private static string MemoryFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PalmRej", "진단로그_폴더.txt");

    /// <summary>The folder used last time; else Desktop\테스트 if there is one; else the Desktop.</summary>
    internal static string DefaultFolder()
    {
        try
        {
            if (File.Exists(MemoryFile))
            {
                string f = File.ReadAllText(MemoryFile).Trim();
                if (f.Length > 0 && Directory.Exists(f)) return f;
            }
        }
        catch { }
        string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try
        {
            string test = Path.Combine(desk, "테스트");
            if (Directory.Exists(test)) return test;
        }
        catch { }
        return desk;
    }

    private static void RememberFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MemoryFile)!);
            File.WriteAllText(MemoryFile, folder);
        }
        catch { }
    }

    // ------------------------------------------------------ self-test only ----
    /// <summary>Shows the window as it would look in a phase, with made-up numbers. No ETW, no files.</summary>
    internal void ShowForTest(string phase, DiagLogCapture? fake = null, DiagLogException? error = null)
    {
        switch (phase)
        {
            case "ready": ShowReady(error); break;
            case "recording":
                _cap = fake; _startedUtc = DateTime.UtcNow - TimeSpan.FromSeconds(723);
                _phase = Phase.Recording;
                _banner.SetState(null, "기록 중", "문제가 생기는 동작을 해 본 뒤 '멈추고 저장'을 누르세요");
                _whereRow.SetValue(null, fake!.LogPath);
                SetButtons("폴더 열기", true, "멈추고 저장", true);
                FillNumbers(fake, final: false);
                break;
            case "done": _cap = fake; ShowDone(fake!); break;
        }
    }

    /// <summary>
    /// What "기록 시작" does, for the non-admin check. Refuses to run with admin
    /// rights, so a test can never start a real session by accident.
    /// </summary>
    internal DiagLogException? TryStartForTest(string folder)
    {
        if (DiagLogCapture.IsAdmin()) throw new InvalidOperationException("admin: not a test");
        _folder = folder;
        Start();
        return _lastError;
    }

    internal string BannerTextForTest => _banner.AccessibleName ?? "";
    internal string ButtonsForTest => _leftButton.Text + "|" + _rightButton.Text;
    internal string LossRowForTest => (_lossRow.AccessibleName ?? "").Replace("빠진 부분: ", "");
    internal void PressLeftForTest() => LeftClicked();
}
