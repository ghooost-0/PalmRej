// PalmRej 설치 창 (PalmRej 설치.exe)
//
// install.ps1 and uninstall.ps1 still do all the work. This window only runs
// them with no console and shows where they are. With -Gui the scripts print
// their usual lines, which go under "자세히 보기", and a few lines that start
// with @@PALMREJ| for the things this window acts on:
//
//   ENC     the code page of the plain lines that follow
//   PLAN    the step titles, in order
//   STEP    the step now running (1-based)
//   ASK     a yes/no question; the answer goes back on stdin
//   BOX     the final message (icon, caption, text)
//   REBOOT  1 when a reboot is needed to finish
//   LOG     the path of the script's own log
//
// Every field is base64 UTF-8, so whatever the hidden console's code page, the
// parts this window acts on arrive intact.
//
// Built for .NET Framework 4.8, which every supported Windows 10 and 11
// already has, so the zip does not carry a second copy of a runtime.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PalmRejSetup;

internal static class Program
{
    /// <summary>The folder name prefix of the temporary copy that runs an uninstall.</summary>
    internal const string TempPrefix = "PalmRej_uninstall_";

#if !SELFTEST
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var opt = Options.Parse(args);

        // Settings > Apps starts us from C:\Program Files\PalmRej, the folder the
        // uninstall removes. A running exe cannot be deleted, and uninstall.ps1
        // refuses while anything runs from that folder, so run from a copy.
        if (opt.Uninstall && opt.From == null)
            return RelaunchFromTemp();

        int code;
        using (var gate = SingleInstance.TryEnter())
        {
            if (gate == null)
            {
                MessageBox.Show("PalmRej 설치 창이 이미 열려 있습니다.", "PalmRej",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
            using var form = new SetupForm(Job.From(opt));
            Application.Run(form);
            code = form.ResultCode;
        }
        if (opt.From != null) ScheduleTempCleanup();
        return code;
    }
#endif

    internal static string AppDir => Path.GetDirectoryName(Application.ExecutablePath)!.TrimEnd('\\');

    /// <summary>A command-line argument in quotes. A trailing backslash would escape the closing quote, so it goes.</summary>
    internal static string Quote(string s) => "\"" + s.TrimEnd('\\') + "\"";

    private static int RelaunchFromTemp()
    {
        try
        {
            string exe = Application.ExecutablePath;
            string tmp = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            string copy = Path.Combine(tmp, Path.GetFileName(exe));
            File.Copy(exe, copy, true);
            // No runas: this process is already elevated (the manifest asks for
            // it), and a child started without the shell inherits the token.
            var psi = new ProcessStartInfo(copy,
                "/uninstall /from " + Quote(AppDir) + " /waitpid " + Process.GetCurrentProcess().Id)
            {
                UseShellExecute = false,
                WorkingDirectory = tmp,
            };
            Process.Start(psi)?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("제거를 시작하지 못했습니다.\n\n" + ex.Message, "PalmRej 제거",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    /// <summary>
    /// The temporary copy removes its own folder a moment after it exits. Only a
    /// folder in %TEMP% with our prefix, so a copy started some other way never
    /// deletes anything else.
    /// </summary>
    internal static void ScheduleTempCleanup()
    {
        try
        {
            string dir = AppDir;
            string temp = Path.GetTempPath().TrimEnd('\\');
            if (!dir.StartsWith(temp + "\\", StringComparison.OrdinalIgnoreCase)) return;
            if (!Path.GetFileName(dir).StartsWith(TempPrefix, StringComparison.Ordinal)) return;
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q " + Quote(dir))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temp,
            };
            Process.Start(psi)?.Dispose();
        }
        catch { /* a leftover folder in %TEMP% is harmless */ }
    }
}

internal sealed class Options
{
    public bool Uninstall;
    /// <summary>The installed folder, when a temporary copy runs the uninstall.</summary>
    public string? From;
    /// <summary>The process that started this copy; the uninstall waits for it to exit.</summary>
    public int WaitPid;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i].Trim();
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (a.ToLowerInvariant())
            {
                case "/uninstall":
                case "-uninstall":
                    o.Uninstall = true;
                    break;
                case "/from":
                    if (next != null) { o.From = next.TrimEnd('\\'); i++; }
                    break;
                case "/waitpid":
                    if (next != null) { int.TryParse(next, out o.WaitPid); i++; }
                    break;
                default:
                    // Anything else is ignored: a stray argument must not stop an uninstall.
                    break;
            }
        }
        return o;
    }
}

internal enum Mode { Install, Uninstall }

/// <summary>What this window will run, or why it cannot.</summary>
internal sealed class Job
{
    public Mode Mode;
    public string ScriptPath = "";
    public string WorkDir = "";
    public string ExtraArgs = "";
    public int WaitPid;
    public string Version = "";
    public string? InstalledVersion;
    /// <summary>Set when there is nothing to run; shown in place of the first page.</summary>
    public string? Problem;

    internal const string ArpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PalmRej";

    public static Job From(Options opt)
    {
        var j = new Job
        {
            Mode = opt.Uninstall ? Mode.Uninstall : Mode.Install,
            WaitPid = opt.WaitPid,
            Version = AppVersion(),
            InstalledVersion = ReadInstalledVersion(),
        };
        if (j.Mode == Mode.Install)
        {
            j.WorkDir = Program.AppDir;
            j.ScriptPath = Path.Combine(j.WorkDir, "install.ps1");
            if (!File.Exists(j.ScriptPath))
            {
                // The copies in 프로그램\ and C:\Program Files\PalmRej sit next to
                // uninstall.ps1 and exist for Settings > Apps > 제거.
                j.Problem = File.Exists(Path.Combine(j.WorkDir, "uninstall.ps1"))
                    ? "이 'PalmRej 설치.exe' 는 제거할 때 쓰는 사본입니다 (설정 > 앱 에서 PalmRej 를 제거하면 이것이 열립니다).\n\n" +
                      "설치하려면 zip 을 푼 폴더 맨 위에 있는 'PalmRej 설치.exe' 를 실행해 주세요."
                    : "이 폴더에 설치 파일(install.ps1)이 없습니다.\n\n" +
                      "zip 을 모두 푼 다음, 푼 폴더 맨 위에 있는 'PalmRej 설치.exe' 를 실행해 주세요. " +
                      "압축 파일 안에서 바로 실행하면 이렇게 됩니다.";
            }
        }
        else
        {
            j.WorkDir = opt.From ?? Program.AppDir;
            j.ScriptPath = Path.Combine(j.WorkDir, "uninstall.ps1");
            if (!File.Exists(j.ScriptPath))
                j.Problem = "제거 스크립트(uninstall.ps1)를 찾지 못했습니다.\n\n" + j.WorkDir;
        }
        return j;
    }

    internal static string AppVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = (AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(asm, typeof(AssemblyInformationalVersionAttribute));
        return ShortVersion(info?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "");
    }

    internal static string ShortVersion(string v)
    {
        // 0.1.13.0 -> 0.1.13; a four-part version with a real fourth part stays as it is.
        var parts = (v ?? "").Split('.');
        return parts.Length == 4 && parts[3] == "0" ? string.Join(".", parts, 0, 3) : (v ?? "");
    }

    private static string? ReadInstalledVersion()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var k = hklm.OpenSubKey(ArpKey);
            string? v = k?.GetValue("DisplayVersion") as string;
            return string.IsNullOrWhiteSpace(v) ? null : v!.Trim();
        }
        catch { return null; }
    }
}

internal static class SingleInstance
{
    /// <summary>Null when another copy of this window already runs.</summary>
    public static IDisposable? TryEnter()
    {
        Mutex? m = null;
        bool created = false;
        foreach (var name in new[] { @"Global\PalmRejSetupWindow", @"Local\PalmRejSetupWindow" })
        {
            try { m = new Mutex(true, name, out created); break; }
            catch (Exception) { m = null; }
        }
        if (m == null) return new Releaser(null);   // no mutex at all: better to run than to refuse
        if (!created) { m.Dispose(); return null; }
        return new Releaser(m);
    }

    private sealed class Releaser : IDisposable
    {
        private Mutex? _m;
        public Releaser(Mutex? m) { _m = m; }
        public void Dispose()
        {
            if (_m == null) return;
            try { _m.ReleaseMutex(); } catch { }
            _m.Dispose();
            _m = null;
        }
    }
}

// ------------------------------------------------------------ the script ----

internal enum MsgKind { Line, Error, Plan, Step, Ask, Box, Reboot, Log }

internal sealed class Msg
{
    public MsgKind Kind;
    /// <summary>For Line and Error.</summary>
    public string Text = "";
    /// <summary>For the markers, decoded.</summary>
    public string[] Fields = new string[0];

    public string Field(int i) => i < Fields.Length ? Fields[i] : "";
}

internal static class Protocol
{
    public const string Prefix = "@@PALMREJ|";

    /// <summary>
    /// The kind and decoded fields of a marker line; null when the line is not a
    /// well-formed marker, and is then shown as ordinary text.
    /// </summary>
    public static (string Kind, string[] Fields)? Parse(byte[] b, int len)
    {
        if (len < Prefix.Length) return null;
        for (int i = 0; i < Prefix.Length; i++)
            if (b[i] != (byte)Prefix[i]) return null;
        for (int i = Prefix.Length; i < len; i++)
            if (b[i] > 127) return null;

        string s = Encoding.ASCII.GetString(b, Prefix.Length, len - Prefix.Length);
        string[] parts = s.Split('|');
        if (parts[0].Length == 0) return null;
        var fields = new string[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++)
        {
            try { fields[i - 1] = Encoding.UTF8.GetString(Convert.FromBase64String(parts[i])); }
            catch (FormatException) { return null; }
        }
        return (parts[0], fields);
    }
}

/// <summary>
/// Runs one script with no window and reports what it says. Output is read as
/// bytes and split into lines here, so the code page can change mid-stream
/// when the script says which one its console uses (ENC).
/// </summary>
internal sealed class ScriptRunner
{
    private readonly Action<Msg> _onMsg;   // called on the reader threads
    private readonly Action<int> _onDone;  // called once, on a pool thread
    private readonly ManualResetEvent _outEof = new ManualResetEvent(false);
    private readonly ManualResetEvent _errEof = new ManualResetEvent(false);
    private Process? _p;
    private volatile Encoding _enc;
    private int _done;

    /// <summary>How long to wait for the output to end after the script has exited.
    /// A child the script left running can hold the pipe open; the result does not wait for it.</summary>
    internal static int DrainTimeoutMs = 3000;

    public ScriptRunner(Action<Msg> onMsg, Action<int> onDone)
    {
        _onMsg = onMsg;
        _onDone = onDone;
        _enc = OemEncoding();
    }

    internal static string PowerShellPath() =>
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    public void Start(string script, string workDir, string extraArgs)
    {
        // -NonInteractive: a host prompt (Read-Host, a -Confirm, the execution-policy
        // question a Group Policy can force) would wait on stdin for an answer this
        // window never sends. With it the prompt fails at once and the error shows.
        // [Console]::In.ReadLine(), which ASK uses, still works.
        var psi = new ProcessStartInfo(PowerShellPath(),
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Gui" + extraArgs)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = workDir,
        };
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, _) => Finish();
        _p = p;
        p.Start();
        StartPump(p.StandardOutput.BaseStream, false);
        StartPump(p.StandardError.BaseStream, true);
    }

    /// <summary>
    /// The answer to an ASK. It is sent after an empty line: if the writer puts a
    /// byte-order mark first, the mark lands on that empty line and cannot merge
    /// with the answer (in CP949 the mark swallowed the Y of YES).
    /// </summary>
    public void Answer(bool yes)
    {
        try
        {
            var w = _p?.StandardInput;
            if (w == null) return;
            w.Write("\r\n" + (yes ? "YES" : "NO") + "\r\n");
            w.Flush();
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }   // ObjectDisposedException is one of these
    }

    private void Finish()
    {
        if (Interlocked.Exchange(ref _done, 1) != 0) return;
        WaitHandle.WaitAll(new WaitHandle[] { _outEof, _errEof }, DrainTimeoutMs);
        int code = -1;
        try { code = _p!.ExitCode; } catch { }
        _onDone(code);
    }

    private void StartPump(Stream s, bool isErr)
    {
        var t = new Thread(() => Pump(s, isErr)) { IsBackground = true, Name = isErr ? "stderr" : "stdout" };
        t.Start();
    }

    private void Pump(Stream s, bool isErr)
    {
        var line = new MemoryStream();
        var chunk = new byte[4096];
        try
        {
            int n;
            while ((n = s.Read(chunk, 0, chunk.Length)) > 0)
            {
                int start = 0;
                for (int i = 0; i < n; i++)
                {
                    if (chunk[i] != (byte)'\n') continue;
                    line.Write(chunk, start, i - start);
                    Emit(line, isErr);
                    line.SetLength(0);
                    start = i + 1;
                }
                line.Write(chunk, start, n - start);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        if (line.Length > 0) Emit(line, isErr);
        (isErr ? _errEof : _outEof).Set();
    }

    private void Emit(MemoryStream ms, bool isErr)
    {
        byte[] b = ms.GetBuffer();
        int len = (int)ms.Length;
        if (len > 0 && b[len - 1] == (byte)'\r') len--;

        var marker = Protocol.Parse(b, len);
        if (marker is { } m)
        {
            MsgKind? kind = m.Kind switch
            {
                "PLAN" => MsgKind.Plan,
                "STEP" => MsgKind.Step,
                "ASK" => MsgKind.Ask,
                "BOX" => MsgKind.Box,
                "REBOOT" => MsgKind.Reboot,
                "LOG" => MsgKind.Log,
                _ => null,
            };
            if (m.Kind == "ENC") SetEncoding(m.Fields);
            else if (kind != null) _onMsg(new Msg { Kind = kind.Value, Fields = m.Fields });
            // Any other kind is from a newer script: skipped, not shown as base64.
            return;
        }

        string text;
        try { text = _enc.GetString(b, 0, len); }
        catch { text = Encoding.ASCII.GetString(b, 0, len); }
        _onMsg(new Msg { Kind = isErr ? MsgKind.Error : MsgKind.Line, Text = text });
    }

    private void SetEncoding(string[] fields)
    {
        if (fields.Length == 0 || !int.TryParse(fields[0], out int cp)) return;
        try { _enc = Encoding.GetEncoding(cp); } catch { /* keep the current one */ }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    /// <summary>What a new hidden console uses until the script says otherwise.</summary>
    internal static Encoding OemEncoding()
    {
        try { return Encoding.GetEncoding((int)GetOEMCP()); }
        catch { return Encoding.Default; }
    }
}

// ---------------------------------------------------------------- window ----

internal enum StepState { Pending, Running, Done, Failed, Stopped }

internal sealed class SetupForm : Form
{
    private const int ContentWidth = 408;
    private const int MaxSteps = 8;
    private static readonly string[] Spinner = { "◐", "◓", "◑", "◒" };

    private readonly Job _job;
    private readonly string _what;   // 설치 or 제거
    private readonly TitleBar _titleBar;
    private readonly StatusBanner _banner = new StatusBanner();
    private readonly StatusRow _installedRow = new StatusRow("지금 설치된 판");
    private readonly StatusRow _newRow = new StatusRow("설치할 판");
    private readonly TextBlock _notes = new TextBlock();
    private readonly StepRow[] _stepRows = new StepRow[MaxSteps];
    private readonly LinkLabel _detailsLink = new LinkLabel();
    private readonly LinkLabel _logLink = new LinkLabel();
    private readonly TextBox _details = new TextBox();
    private readonly Button _primary = new Button();
    private readonly Button _secondary = new Button();
    private readonly System.Windows.Forms.Timer _spin = new System.Windows.Forms.Timer { Interval = 150 };

    private enum Page { Problem, Ready, Running, Result }
    private Page _page;
    private int _stepCount;
    private int _currentStep;   // 1-based; 0 before the first STEP
    private bool _running;
    private bool _detailsOpen;
    // What the page wants shown. Control.Visible reads false until the form itself
    // is on screen, so the first layout cannot ask the controls.
    private bool _showPrimary, _showSecondary;
    private int _frame;
    private ScriptRunner? _runner;
    private string[]? _box;     // icon, caption, text
    private bool _reboot;
    private string? _logPath;
    // The first error message on stderr. PowerShell writes it first and then "위치 ...",
    // "+ ~~~" and "+ CategoryInfo" lines, and wraps it at the hidden console's width
    // (120 cells, a Hangul character takes two) with nothing in between, so the pieces
    // are joined back until the first of those lines. 0 waiting, 1 joining, 2 done.
    private readonly StringBuilder _errMsg = new StringBuilder();
    private int _errState;
    private Action? _primaryAction, _secondaryAction;

    /// <summary>0 done, 1 failed, 2 cancelled or closed before it started.</summary>
    internal int ResultCode { get; private set; } = 2;

    // For the self-test: answer questions, stand in for the reboot, and know when the result is up.
#pragma warning disable CS0649 // set only by the self-test
    internal Func<string, string, string, bool>? AskOverride;
    internal Func<bool>? RebootOverride;
#pragma warning restore CS0649
    internal event Action? ResultShown;

    public SetupForm(Job job)
    {
        _job = job;
        _what = job.Mode == Mode.Install ? "설치" : "제거";

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;   // every size below goes through Ui.S
        Text = "PalmRej " + _what;
        Font = Ui.Body;
        BackColor = Ui.Window;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _titleBar = new TitleBar(this);
        _titleBar.SetVersion(job.Version);
        Controls.Add(_titleBar);
        Controls.Add(_banner);
        Controls.Add(_installedRow);
        Controls.Add(_newRow);
        _notes.ForeColor = Ui.Secondary;
        Controls.Add(_notes);
        for (int i = 0; i < MaxSteps; i++)
        {
            _stepRows[i] = new StepRow { Visible = false };
            Controls.Add(_stepRows[i]);
        }

        StyleLink(_detailsLink, "자세히 보기");
        _detailsLink.LinkClicked += (_, _) => { _detailsOpen = !_detailsOpen; ApplyDetails(); LayoutPage(); };
        Controls.Add(_detailsLink);
        StyleLink(_logLink, "기록 파일 열기");
        _logLink.LinkClicked += (_, _) => OpenLog();
        Controls.Add(_logLink);

        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.WordWrap = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.MaxLength = 0;
        _details.Font = Ui.Log;
        _details.BackColor = Ui.LogFill;
        _details.ForeColor = Ui.Secondary;
        _details.BorderStyle = BorderStyle.FixedSingle;
        _details.Visible = false;
        Controls.Add(_details);

        StyleButton(_primary, primary: true);
        StyleButton(_secondary, primary: false);
        _primary.Click += (_, _) => _primaryAction?.Invoke();
        _secondary.Click += (_, _) => _secondaryAction?.Invoke();
        Controls.Add(_primary);
        Controls.Add(_secondary);

        _spin.Tick += (_, _) => Spin();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        if (job.Problem != null) ShowProblem();
        else if (job.Mode == Mode.Install) ShowReady();
        else ShowRunning();
        ResumeLayout(false);
        LayoutPage();

        Shown += (_, _) =>
        {
            Activate();
            // The uninstall has no ready page: uninstall.ps1 asks its own question.
            if (_page == Page.Running && _runner == null && !_running) StartScript();
        };
    }

    // ------------------------------------------------------------ frame ----

    protected override CreateParams CreateParams
    {
        get
        {
            // Same as the manager app: taskbar minimise and Alt+Space need these
            // styles on a borderless window, and WS_MAXIMIZEBOX must go or
            // Win+Up blows the fixed-size window up to the whole screen.
            const int WS_MAXIMIZEBOX = 0x00010000, WS_MINIMIZEBOX = 0x00020000, WS_SYSMENU = 0x00080000;
            var cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX | WS_SYSMENU;
            cp.Style &= ~WS_MAXIMIZEBOX;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            // The shadow a normal window has, on all four sides, and round
            // corners on Windows 11 (Windows 10 refuses the attribute).
            int policy = 2; // DWMNCRP_ENABLED
            DwmSetWindowAttribute(Handle, 2 /* DWMWA_NCRENDERING_POLICY */, ref policy, sizeof(int));
            var margins = new Margins { Left = 1, Right = 1, Top = 1, Bottom = 1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, sizeof(int));
        }
        catch { /* dwmapi missing: a plain window still works */ }
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Task Manager's "작업 끝내기" arrives as TaskManagerClosing: held too. Only
        // Windows shutting down gets through.
        if (_running && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.TaskManagerClosing))
        {
            // Stopping halfway could leave test mode on with no driver, or a
            // driver with no service key. The script is short; wait for it.
            e.Cancel = true;
            MessageBox.Show(this,
                _what + "가 끝날 때까지 기다려 주세요.\n\n중간에 멈추면 반만 된 상태로 남을 수 있습니다.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _spin.Dispose();
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------ pages ----

    private void ShowProblem()
    {
        _page = Page.Problem;
        _banner.Glyph = null;
        _banner.SetState(false, _what + "할 수 없습니다", _job.Problem ?? "");
        SetButtons("닫기", Close, null, null);
    }

    private void ShowReady()
    {
        _page = Page.Ready;
        string? installed = _job.InstalledVersion;
        string detail = installed == null
            ? "Wacom 손날 인식 드라이버를 설치합니다."
            : installed == _job.Version
                ? "같은 판이 이미 설치돼 있습니다. 한 번 더 설치합니다."
                : "지금 설치된 PalmRej 를 이 판으로 바꿉니다.";
        _banner.Glyph = "↓";
        _banner.SetState(null, "PalmRej " + _job.Version + " 설치", detail);
        _installedRow.SetValue(null, installed ?? "없음");
        _newRow.SetValue(null, _job.Version);
        _newRow.DrawDivider = false;
        _notes.Text =
            "• Windows 테스트 모드가 켜집니다. 화면 오른쪽 아래에 '테스트 모드' 글씨가 보입니다.\n" +
            "• 끝나면 재부팅해야 적용됩니다.\n" +
            "• 설치하는 동안 타블렛을 켜 두세요.";
        SetButtons("설치", OnInstallClicked, "취소", Close);
    }

    private void ShowRunning()
    {
        _page = Page.Running;
        _banner.Glyph = Spinner[0];
        _banner.SetState(null, _what + " 중", "끝날 때까지 창을 닫지 마세요.");
        SetButtons(null, null, null, null);
    }

    private void ShowResult(bool? state, string title, string detail)
    {
        _page = Page.Result;
        _banner.Glyph = null;
        _banner.SetState(state, title, detail);
    }

    /// <summary>
    /// Lays the page out and keeps it on the screen. The window is centred for the
    /// short first page and then grows downward; on a laptop at 125-150% the result
    /// page ran off the bottom and took the buttons with it. So: move it up, and if
    /// it is still taller than the screen, shorten the details box and then drop the
    /// step list from the result page (every step is ✓ or – by then).
    /// </summary>
    /// <summary>The self-test sets this to stand in for a small screen.</summary>
#pragma warning disable CS0649 // set only by the self-test
    internal static Rectangle? WorkAreaForTest;
#pragma warning restore CS0649

    private void LayoutPage()
    {
        Rectangle wa;
        try { wa = WorkAreaForTest ?? (IsHandleCreated ? Screen.FromHandle(Handle).WorkingArea : Screen.FromPoint(Cursor.Position).WorkingArea); }
        catch { wa = Rectangle.Empty; }

        int detailsH = Ui.S(170);
        bool steps = true;
        int h = LayoutOnce(detailsH, steps);
        if (wa.Height > 0 && h > wa.Height)
        {
            if (_detailsOpen && _page != Page.Ready)
            {
                detailsH = Math.Max(Ui.S(60), detailsH - (h - wa.Height));
                h = LayoutOnce(detailsH, steps);
            }
            if (h > wa.Height && _page == Page.Result)
            {
                steps = false;
                h = LayoutOnce(detailsH, steps);
                // Give the room the steps freed back to the details box.
                if (_detailsOpen && h < wa.Height && detailsH < Ui.S(170))
                {
                    detailsH = Math.Min(Ui.S(170), detailsH + (wa.Height - h));
                    h = LayoutOnce(detailsH, steps);
                }
            }
        }
        if (wa.Height > 0 && IsHandleCreated && Visible && WindowState == FormWindowState.Normal)
        {
            if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
            if (Top < wa.Top) Top = wa.Top;
        }
    }

    private FormWindowState _lastState;

    /// <summary>
    /// A result that arrives while the window is minimised is laid out but not moved
    /// (a minimised window has no real position). Fit it once the restore is done;
    /// the first size message of a restore still carries the minimised bounds, hence
    /// BeginInvoke.
    /// </summary>
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        var s = WindowState;
        if (_lastState == FormWindowState.Minimized && s == FormWindowState.Normal && IsHandleCreated)
            BeginInvoke(new Action(LayoutPage));
        _lastState = s;
    }

    /// <summary>Which controls each page shows, and where. Returns the window height.</summary>
    private int LayoutOnce(int detailsHeight, bool showSteps)
    {
        bool ready = _page == Page.Ready;
        bool work = _page == Page.Running || _page == Page.Result;
        int stepCount = showSteps ? _stepCount : 0;

        _installedRow.Visible = ready;
        _newRow.Visible = ready;
        _notes.Visible = ready;
        for (int i = 0; i < MaxSteps; i++) _stepRows[i].Visible = work && i < stepCount;
        bool showDetails = work && _detailsOpen;
        bool showLog = _page == Page.Result && !string.IsNullOrEmpty(_logPath) && File.Exists(_logPath);
        _detailsLink.Visible = work;
        _details.Visible = showDetails;
        _logLink.Visible = showLog;
        _primary.Visible = _showPrimary;
        _secondary.Visible = _showSecondary;

        int w = Ui.S(ContentWidth);
        int x = 1 + Ui.S(16);
        int outer = w + Ui.S(32);

        _titleBar.SetBounds(1, 1, outer, Ui.S(40));
        int y = 1 + _titleBar.Height + Ui.S(4);

        y = Place(_banner, x, y, w, Ui.S(12));
        if (ready)
        {
            y = Place(_installedRow, x, y, w, 0);
            y = Place(_newRow, x, y, w, Ui.S(10));
            y = Place(_notes, x, y, w, Ui.S(14));
        }
        if (work)
        {
            bool any = false;
            for (int i = 0; i < stepCount; i++)
            {
                _stepRows[i].DrawDivider = i < stepCount - 1;
                y = Place(_stepRows[i], x, y, w, 0);
                any = true;
            }
            if (any) y += Ui.S(10);

            _detailsLink.Location = new Point(x, y);
            if (showLog)
                _logLink.Location = new Point(x + _detailsLink.PreferredSize.Width + Ui.S(16), y);
            y += Math.Max(_detailsLink.PreferredSize.Height, Ui.LineHeight(Ui.Body)) + Ui.S(6);
            if (showDetails)
            {
                _details.SetBounds(x, y, w, detailsHeight);
                y += _details.Height + Ui.S(8);
            }
            y += Ui.S(6);
        }

        if (_showPrimary || _showSecondary)
        {
            int bw = Ui.S(112), bh = Ui.S(34), gap = Ui.S(8);
            int right = x + w;
            if (_showSecondary)
            {
                _secondary.SetBounds(right - bw, y, bw, bh);
                right -= bw + gap;
            }
            if (_showPrimary) _primary.SetBounds(right - bw, y, bw, bh);
            y += bh;
        }
        y += Ui.S(14);

        ClientSize = new Size(outer + 2, y + 1);
        Invalidate();
        return ClientSize.Height;
    }

    private static int Place(Control c, int x, int y, int w, int gapAfter)
    {
        // The painted controls size their own height from the width.
        c.Location = new Point(x, y);
        c.Width = w;
        return y + c.Height + gapAfter;
    }

    private void SetButtons(string? primary, Action? primaryAction, string? secondary, Action? secondaryAction)
    {
        _primary.Text = primary ?? "";
        _showPrimary = primary != null;
        _primaryAction = primaryAction;
        _secondary.Text = secondary ?? "";
        _showSecondary = secondary != null;
        _secondaryAction = secondaryAction;
        AcceptButton = _showPrimary ? _primary : null;
    }

    private void ApplyDetails()
    {
        _detailsLink.Text = _detailsOpen ? "자세히 닫기" : "자세히 보기";
        if (_detailsOpen)
        {
            _details.SelectionStart = _details.TextLength;
            _details.ScrollToCaret();
        }
    }

    // ------------------------------------------------------------ running ----

    private void OnInstallClicked()
    {
        if (ManagerAppRunning())
        {
            MessageBox.Show(this,
                "PalmRej 관리 앱이 열려 있습니다.\n\n앱을 닫은 다음 [설치] 를 다시 눌러 주세요.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        StartScript();
    }

    /// <summary>install.ps1 refuses while the manager app runs from Program Files\PalmRej; say so before starting.</summary>
    private static bool ManagerAppRunning()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PalmRej") + "\\";
        foreach (var name in new[] { "PalmRej 관리", "PalmRejFilterManager" })
        {
            Process[] ps;
            try { ps = Process.GetProcessesByName(name); } catch { continue; }
            foreach (var p in ps)
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (path != null && path.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { /* not ours to read */ }
                finally { p.Dispose(); }
            }
        }
        return false;
    }

    private void StartScript()
    {
        _running = true;
        ResultCode = 1;
        ShowRunning();
        LayoutPage();
        _spin.Start();

        int pid = _job.WaitPid;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            // The copy in Program Files that started us must be gone first:
            // uninstall.ps1 refuses while anything runs from that folder.
            if (pid > 0)
            {
                try { using var p = Process.GetProcessById(pid); p.WaitForExit(10000); }
                catch { /* already gone */ }
            }
            Post(StartRunnerNow);
        });
    }

    private void StartRunnerNow()
    {
        try
        {
            _runner = new ScriptRunner(m => Post(() => OnMsg(m)), code => Post(() => OnDone(code)));
            // Never run inside the folder an uninstall deletes: Windows cannot remove a
            // folder that is some process's current directory, and the script's children
            // inherit it. The scripts find their own files through $PSScriptRoot.
            string cwd = _job.Mode == Mode.Uninstall ? Environment.SystemDirectory : _job.WorkDir;
            _runner.Start(_job.ScriptPath, cwd, _job.ExtraArgs);
        }
        catch (Exception ex)
        {
            string line = "PowerShell 을 실행하지 못했습니다: " + ex.Message;
            AppendDetail(line, true);
            _errMsg.Clear().Append(line);
            _errState = 2;
            OnDone(-1);
        }
    }

    private void Post(Action a)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(a);
        }
        catch (InvalidOperationException) { }   // ObjectDisposedException is one of these
    }

    private void OnMsg(Msg m)
    {
        switch (m.Kind)
        {
            case MsgKind.Line:
                AppendDetail(m.Text, false);
                break;
            case MsgKind.Error:
                AppendDetail(m.Text, true);
                CollectError(m.Text);
                break;
            case MsgKind.Plan:
                SetPlan(m.Fields);
                break;
            case MsgKind.Step:
                if (int.TryParse(m.Field(0), out int n)) SetStep(n);
                break;
            case MsgKind.Ask:
                HandleAsk(m.Field(0), m.Field(1), m.Field(2));
                break;
            case MsgKind.Box:
                _box = new[] { m.Field(0), m.Field(1), m.Field(2) };
                break;
            case MsgKind.Reboot:
                _reboot = m.Field(0) == "1";
                break;
            case MsgKind.Log:
                _logPath = m.Field(0);
                break;
        }
    }

    private void CollectError(string line)
    {
        if (_errState == 2) return;
        string t = line.Trim();
        bool decoration = t.StartsWith("+") || t.StartsWith("위치 ") || t.StartsWith("At ");
        if (_errState == 0)
        {
            if (t.Length == 0 || decoration) return;
            _errMsg.Append(line);
            _errState = 1;
        }
        else if (t.Length == 0 || decoration) _errState = 2;
        else _errMsg.Append(line);   // the console wrap drops nothing, so no separator
        if (_errMsg.Length > 300) { _errMsg.Length = 300; _errMsg.Append('…'); _errState = 2; }
    }

    private void AppendDetail(string text, bool isErr)
    {
        _details.AppendText((isErr ? "! " : "") + text + Environment.NewLine);
    }

    private void SetPlan(string[] titles)
    {
        _stepCount = Math.Min(titles.Length, MaxSteps);
        for (int i = 0; i < _stepCount; i++)
        {
            _stepRows[i].StepText = titles[i];
            _stepRows[i].State = StepState.Pending;
        }
        LayoutPage();
    }

    private void SetStep(int n)
    {
        _currentStep = n;
        for (int i = 0; i < _stepCount; i++)
            _stepRows[i].State = i < n - 1 ? StepState.Done : i == n - 1 ? StepState.Running : StepState.Pending;
    }

    private void HandleAsk(string icon, string caption, string text)
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        bool yes = AskOverride != null
            ? AskOverride(icon, caption, text)
            : MessageBox.Show(this, text, caption, MessageBoxButtons.YesNo, IconOf(icon),
                              MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        _runner?.Answer(yes);
    }

    private static MessageBoxIcon IconOf(string icon) => icon switch
    {
        "Warning" => MessageBoxIcon.Warning,
        "Error" => MessageBoxIcon.Error,
        "Question" => MessageBoxIcon.Question,
        _ => MessageBoxIcon.Information,
    };

    private void Spin()
    {
        _frame++;
        string g = Spinner[_frame % Spinner.Length];
        if (_page == Page.Running) _banner.Glyph = g;
        for (int i = 0; i < _stepCount; i++)
            if (_stepRows[i].State == StepState.Running) _stepRows[i].Glyph = g;
    }

    private void OnDone(int code)
    {
        if (!_running) return;
        _running = false;
        _spin.Stop();

        string title, detail;
        bool? state;
        if (code == 0)
        {
            state = true;
            ResultCode = 0;
            // With the final message the script reached its end: every step ran.
            // Without it (a dry run stops after the first) only the ones it reached.
            for (int i = 0; i < _stepCount; i++)
                _stepRows[i].State = _box != null || i < _currentStep ? StepState.Done : StepState.Stopped;
            title = _what + "를 마쳤습니다";
            detail = "";
            if (_reboot) SetButtons("지금 재부팅", Reboot, "나중에", Close);
            else SetButtons("닫기", Close, null, null);
        }
        else if (code == 2)
        {
            state = null;
            ResultCode = 2;
            MarkStopped(StepState.Stopped);
            title = _what + "를 취소했습니다";
            detail = "아무것도 바꾸지 않았습니다.";
            SetButtons("닫기", Close, null, null);
        }
        else
        {
            state = false;
            ResultCode = 1;
            MarkStopped(StepState.Failed);
            title = _what + " 중 문제가 생겼습니다";
            string msg = _errMsg.ToString().Trim();
            bool log = !string.IsNullOrEmpty(_logPath) && File.Exists(_logPath);
            detail = (msg.Length > 0 ? msg + "\n\n" : "예상하지 못한 오류로 멈췄습니다.\n\n") +
                     "아래에 전체 내용이 있습니다." + (log ? " '기록 파일 열기'로 기록을 열어 둘 수 있습니다." : "");
            if (_box == null) _detailsOpen = true;
            // A failure can still need a reboot: an uninstall that removed the drivers
            // but could not turn test mode off says so with REBOOT 1 before its BOX.
            if (_reboot) SetButtons("지금 재부팅", Reboot, "닫기", Close);
            else SetButtons("닫기", Close, null, null);
        }

        // The script's own words win over the ones above.
        if (_box != null) SplitMessage(_box[2], ref title, ref detail);

        ShowResult(state, title, detail);
        ApplyDetails();
        LayoutPage();
        ResultShown?.Invoke();
    }

    /// <summary>The step that was running ends as <paramref name="how"/>; the ones after it never ran.</summary>
    private void MarkStopped(StepState how)
    {
        for (int i = 0; i < _stepCount; i++)
        {
            if (_stepRows[i].State == StepState.Running) _stepRows[i].State = how;
            else if (_stepRows[i].State == StepState.Pending) _stepRows[i].State = StepState.Stopped;
        }
    }

    /// <summary>First line is the headline, the rest the detail.</summary>
    internal static void SplitMessage(string text, ref string title, ref string detail)
    {
        string t = (text ?? "").Replace("\r\n", "\n").Trim();
        if (t.Length == 0) return;
        int nl = t.IndexOf('\n');
        if (nl < 0) { title = t; detail = ""; return; }
        title = t.Substring(0, nl).Trim();
        detail = t.Substring(nl + 1).Trim('\n', ' ');
    }

    private void Reboot()
    {
        if (RebootOverride != null)
        {
            if (RebootOverride()) Close();
            return;
        }
        if (MessageBox.Show(this,
                "지금 재부팅합니다.\n\n다른 프로그램에 저장하지 않은 작업이 있으면 먼저 저장해 주세요.",
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "/r /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p != null && p.WaitForExit(10000) && p.ExitCode != 0)
                throw new InvalidOperationException("shutdown.exe 코드 " + p.ExitCode);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "재부팅하지 못했습니다. 시작 메뉴에서 직접 다시 시작해 주세요.\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenLog()
    {
        if (string.IsNullOrEmpty(_logPath) || !File.Exists(_logPath)) return;
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe"), Program.Quote(_logPath!))
            {
                UseShellExecute = false,
            })?.Dispose();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "기록 파일을 열지 못했습니다.\n\n" + _logPath + "\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ------------------------------------------------------------ styling ----

    private static void StyleButton(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.Font = Ui.Button;
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
        if (primary)
        {
            b.BackColor = Ui.PrimaryFill;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Ui.PrimaryFill;
            b.FlatAppearance.MouseOverBackColor = Ui.PrimaryHover;
            b.FlatAppearance.MouseDownBackColor = Ui.PrimaryDown;
        }
        else
        {
            b.BackColor = Ui.Window;
            b.ForeColor = Ui.Text;
            b.FlatAppearance.BorderColor = Ui.ButtonBorder;
            b.FlatAppearance.MouseOverBackColor = Ui.ButtonHover;
            b.FlatAppearance.MouseDownBackColor = Ui.ButtonDown;
        }
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
        l.BackColor = Ui.Window;
    }
}

// -------------------------------------------------------------- controls ----

/// <summary>Colours and fonts, the same as the manager app's.</summary>
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
    public static readonly Color Frame = Color.FromArgb(200, 198, 190);
    public static readonly Color CloseHover = Color.FromArgb(232, 17, 35);
    public static readonly Color CloseDown = Color.FromArgb(241, 112, 122);
    public static readonly Color PrimaryFill = Color.FromArgb(24, 95, 165);
    public static readonly Color PrimaryHover = Color.FromArgb(20, 82, 143);
    public static readonly Color PrimaryDown = Color.FromArgb(16, 68, 120);
    public static readonly Color LogFill = Color.FromArgb(250, 250, 248);

    // (fill, strong, text) per state: green, red, grey
    public static readonly (Color Fill, Color Strong, Color Text) Good = (Color.FromArgb(234, 243, 222), Color.FromArgb(59, 109, 17), Color.FromArgb(39, 80, 10));
    public static readonly (Color Fill, Color Strong, Color Text) Bad = (Color.FromArgb(252, 235, 235), Color.FromArgb(163, 45, 45), Color.FromArgb(121, 31, 31));
    public static readonly (Color Fill, Color Strong, Color Text) Calm = (Color.FromArgb(241, 239, 232), Color.FromArgb(95, 94, 90), Color.FromArgb(68, 68, 65));

    public static (Color Fill, Color Strong, Color Text) For(bool? state) => state switch
    {
        true => Good,
        false => Bad,
        _ => Calm,
    };

    private const string Face = "Malgun Gothic";
    public static readonly Font Body = new Font(Face, 9.75F, FontStyle.Regular);
    public static readonly Font BodyBold = new Font(Face, 9.75F, FontStyle.Bold);
    public static readonly Font Button = new Font(Face, 10F, FontStyle.Regular);
    public static readonly Font Title = new Font(Face, 13F, FontStyle.Bold);
    public static readonly Font Header = new Font(Face, 10.5F, FontStyle.Bold);
    public static readonly Font Detail = new Font(Face, 9.75F, FontStyle.Regular);
    public static readonly Font Glyph = new Font("Segoe UI Symbol", 14F, FontStyle.Bold);
    public static readonly Font Mark = new Font("Segoe UI Symbol", 10F, FontStyle.Regular);
    public static readonly Font Log = new Font("Consolas", 8.25F, FontStyle.Regular);

    /// <summary>The system DPI. The manifest makes this process system-DPI aware, so it does not change while it runs.</summary>
    public static readonly float Dpi = ReadDpi();

    private static float ReadDpi()
    {
        try { using var g = Graphics.FromHwnd(IntPtr.Zero); return g.DpiX > 0 ? g.DpiX : 96f; }
        catch { return 96f; }
    }

    public static int S(int px) => (int)Math.Round(px * Dpi / 96.0);

    /// <summary>
    /// Lines for <paramref name="text"/> at <paramref name="width"/>, broken at
    /// spaces. GDI's own word break splits Hangul between syllables, which reads
    /// badly; only a word too long for a whole line is split, and then by character.
    /// </summary>
    public static List<string> Wrap(string text, Font font, int width)
    {
        var lines = new List<string>();
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        int W(string s) => TextRenderer.MeasureText(s, font, new Size(int.MaxValue, int.MaxValue), flags).Width;
        foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (var word in para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
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

    public static int WrappedHeight(string text, Font font, int width) => Wrap(text, font, width).Count * LineHeight(font);

    public static int LineHeight(Font font) =>
        TextRenderer.MeasureText("가Ag", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;

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
}

/// <summary>The coloured block at the top: a round mark, one bold line, and the detail under it.</summary>
internal sealed class StatusBanner : Control
{
    private bool? _state;
    private string _title = "";
    private string _detail = "";
    private string? _glyph;

    private const int Pad = 14;
    private const int MarkSize = 34;
    private const int Gap = 12;

    public StatusBanner()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Ui.Window;
        Height = Ui.S(76);
    }

    /// <summary>Drawn in the round mark instead of ✓ / ! / –. Null for the default.</summary>
    public string? Glyph
    {
        get => _glyph;
        set { if (_glyph != value) { _glyph = value; Invalidate(); } }
    }

    public string TitleText => _title;
    public string DetailText => _detail;

    public void SetState(bool? state, string title, string detail)
    {
        _state = state;
        _title = title ?? "";
        _detail = detail ?? "";
        AccessibleName = _title + ". " + _detail;
        Height = Measure();
        Invalidate();
    }

    private int TextWidth => Math.Max(50, Width - Ui.S(Pad) * 2 - Ui.S(MarkSize) - Ui.S(Gap));

    private int Measure()
    {
        int t = Ui.WrappedHeight(_title, Ui.Title, TextWidth);
        int d = string.IsNullOrEmpty(_detail) ? 0 : Ui.WrappedHeight(_detail, Ui.Detail, TextWidth);
        int text = t + (d > 0 ? Ui.S(4) + d : 0);
        return Math.Max(Ui.S(MarkSize), text) + Ui.S(Pad) * 2;
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
        g.Clear(Ui.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var (fill, strong, text) = Ui.For(_state);

        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
        using (var b = new SolidBrush(fill))
            g.FillPath(b, path);

        int mark = Ui.S(MarkSize);
        var markRect = new Rectangle(Ui.S(Pad), (Height - mark) / 2, mark, mark);
        using (var b = new SolidBrush(strong)) g.FillEllipse(b, markRect);
        string glyph = _glyph ?? (_state == true ? "✓" : _state == false ? "!" : "–");
        TextRenderer.DrawText(g, glyph, Ui.Glyph, markRect, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        int x = Ui.S(Pad) + mark + Ui.S(Gap);
        int th = Ui.WrappedHeight(_title, Ui.Title, TextWidth);
        int dh = string.IsNullOrEmpty(_detail) ? 0 : Ui.WrappedHeight(_detail, Ui.Detail, TextWidth);
        int total = th + (dh > 0 ? Ui.S(4) + dh : 0);
        int y = (Height - total) / 2;
        Ui.DrawWrapped(g, _title, Ui.Title, new Rectangle(x, y, TextWidth, th), text);
        if (dh > 0)
            Ui.DrawWrapped(g, _detail, Ui.Detail, new Rectangle(x, y + th + Ui.S(4), TextWidth, dh), text);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

/// <summary>One "name — value" line, with a hairline below it.</summary>
internal sealed class StatusRow : Control
{
    private readonly string _name;
    private bool? _state;
    private string _value = "";

    private const int NameWidth = 110;
    private const int VPad = 9;

    public bool DrawDivider { get; set; } = true;

    public StatusRow(string name)
    {
        _name = name;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Ui.Window;
        Height = Ui.S(36);
    }

    public void SetValue(bool? state, string value)
    {
        _state = state;
        _value = value ?? "";
        AccessibleName = _name + ": " + _value;
        Height = Measure();
        Invalidate();
    }

    private int ValueWidth => Math.Max(50, Width - Ui.S(NameWidth));

    private int Measure()
    {
        int v = Ui.WrappedHeight(_value, Ui.Body, ValueWidth);
        return Math.Max(v, Ui.LineHeight(Ui.Body)) + Ui.S(VPad) * 2;
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
        g.Clear(Ui.Window);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var (_, strong, _) = Ui.For(_state);
        TextRenderer.DrawText(g, _name, Ui.Body, new Rectangle(0, Ui.S(VPad), Ui.S(NameWidth), Ui.LineHeight(Ui.Body)), Ui.Secondary, flags);
        Ui.DrawWrapped(g, _value, Ui.Body, new Rectangle(Ui.S(NameWidth), Ui.S(VPad), ValueWidth, Height - Ui.S(VPad) * 2),
            _state == null ? Ui.Text : strong);
        if (DrawDivider)
        {
            using var pen = new Pen(Ui.Divider);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>One step of the script: a mark (waiting, running, done, failed) and its title.</summary>
internal sealed class StepRow : Control
{
    private string _text = "";
    private StepState _state = StepState.Pending;
    private string _glyph = "◐";

    private const int MarkWidth = 26;
    private const int VPad = 7;

    public bool DrawDivider { get; set; } = true;

    public StepRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Ui.Window;
        Height = Ui.S(32);
    }

    public string StepText
    {
        get => _text;
        set { _text = value ?? ""; AccessibleName = _text; Height = Measure(); Invalidate(); }
    }

    public StepState State
    {
        get => _state;
        set { if (_state != value) { _state = value; Height = Measure(); Invalidate(); } }
    }

    /// <summary>The spinner frame shown while running.</summary>
    public string Glyph
    {
        get => _glyph;
        set { if (_glyph != value) { _glyph = value; if (_state == StepState.Running) Invalidate(); } }
    }

    private Font TextFont => _state == StepState.Running ? Ui.BodyBold : Ui.Body;
    private int TextWidth => Math.Max(50, Width - Ui.S(MarkWidth));

    private int Measure() => Math.Max(Ui.WrappedHeight(_text, TextFont, TextWidth), Ui.LineHeight(Ui.Body)) + Ui.S(VPad) * 2;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int h = Measure();
        if (h != Height) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Window);
        string mark;
        Color markColor, textColor;
        switch (_state)
        {
            case StepState.Running: mark = _glyph; markColor = Ui.Link; textColor = Ui.Text; break;
            case StepState.Done: mark = "✓"; markColor = Ui.Good.Strong; textColor = Ui.Text; break;
            case StepState.Failed: mark = "✕"; markColor = Ui.Bad.Strong; textColor = Ui.Bad.Text; break;
            case StepState.Stopped: mark = "–"; markColor = Ui.Muted; textColor = Ui.Muted; break;
            default: mark = "·"; markColor = Ui.Muted; textColor = Ui.Muted; break;
        }
        int lh = Ui.LineHeight(Ui.Body);
        TextRenderer.DrawText(g, mark, Ui.Mark, new Rectangle(0, Ui.S(VPad), Ui.S(MarkWidth) - Ui.S(6), lh), markColor,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        Ui.DrawWrapped(g, _text, TextFont, new Rectangle(Ui.S(MarkWidth), Ui.S(VPad), TextWidth, Height - Ui.S(VPad) * 2), textColor);
        if (DrawDivider)
        {
            using var pen = new Pen(Ui.Divider);
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }
}

/// <summary>Wrapped text that sizes its own height; Label breaks Hangul mid-word.</summary>
internal sealed class TextBlock : Control
{
    public TextBlock()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Ui.Window;
        ForeColor = Ui.Text;
        Font = Ui.Body;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Height = Measure();
        Invalidate();
    }

    private int Measure() => Math.Max(Ui.LineHeight(Font), Ui.WrappedHeight(Text, Font, Math.Max(50, Width)));

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int h = Measure();
        if (h != Height) Height = h;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.Window);
        Ui.DrawWrapped(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor);
    }
}

/// <summary>The window's own title strip: name and version, minimise and close. Same as the manager app's.</summary>
internal sealed class TitleBar : Control
{
    private readonly Form _form;
    private readonly string _title = "PalmRej";
    private string _version = "";
    private Rectangle _minRect, _closeRect;
    private int _hover;     // 0 none, 1 minimise, 2 close
    private int _pressed;

    private const int ButtonWidth = 46;

    public TitleBar(Form form)
    {
        _form = form;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Ui.Window;
        Height = Ui.S(40);
        AccessibleRole = AccessibleRole.TitleBar;
        AccessibleName = _title;
    }

    public void SetVersion(string version)
    {
        _version = version ?? "";
        AccessibleName = string.IsNullOrEmpty(_version) ? _title : _title + " " + _version;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int bw = Ui.S(ButtonWidth);
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
        ReleaseCapture();
        SendMessage(_form.Handle, 0x00A1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
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

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        int x = Ui.S(16);
        var nameSize = TextRenderer.MeasureText(_title, Ui.Header, new Size(int.MaxValue, Height), flags);
        TextRenderer.DrawText(g, _title, Ui.Header, new Rectangle(x, 0, nameSize.Width, Height), Ui.Text, flags);
        if (!string.IsNullOrEmpty(_version))
        {
            var vRect = new Rectangle(x + nameSize.Width + Ui.S(8), 1, Ui.S(120), Height);
            TextRenderer.DrawText(g, _version, Ui.Body, vRect, Ui.Muted, flags);
        }

        if (_hover == 1 || _pressed == 1)
            using (var b = new SolidBrush(_pressed == 1 ? Ui.ButtonDown : Ui.ButtonHover)) g.FillRectangle(b, _minRect);
        if (_hover == 2 || _pressed == 2)
            using (var b = new SolidBrush(_pressed == 2 ? Ui.CloseDown : Ui.CloseHover)) g.FillRectangle(b, _closeRect);

        int s = Ui.S(5);
        using (var pen = new Pen(Ui.Text, Math.Max(1, Ui.S(1))))
        {
            var c = new Point(_minRect.X + _minRect.Width / 2, _minRect.Y + _minRect.Height / 2);
            g.DrawLine(pen, c.X - s, c.Y, c.X + s, c.Y);
        }
        using (var pen = new Pen(_hover == 2 || _pressed == 2 ? Color.White : Ui.Text, Math.Max(1, Ui.S(1))))
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var c = new Point(_closeRect.X + _closeRect.Width / 2, _closeRect.Y + _closeRect.Height / 2);
            g.DrawLine(pen, c.X - s, c.Y - s, c.X + s, c.Y + s);
            g.DrawLine(pen, c.X - s, c.Y + s, c.X + s, c.Y - s);
            g.SmoothingMode = old;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
