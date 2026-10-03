using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace PalmRejFilterManager;

// =====================================================================
//  Kernel DbgPrint capture through ETW, saved in DebugView's log format.
//
//  Replaces "DebugView + palm filter + File > Save". What it rests on
//  (ntoskrnl 10.0.19041.6456 read offline with its own symbols):
//   - EVENT_TRACE_FLAG_DBGPRINT on a system logger session makes the
//     kernel register EtwpTraceDebugPrint as a debug-print callback.
//     vDbgPrintExWithPrefixInternal only reaches its callbacks after the
//     Debug Print Filter check, so ETW gets the same set DebugView gets.
//   - The event (hook 0x0A20, DbgPrintGuid, opcode 0x20) carries
//     {ULONG ComponentId, ULONG Level}, the ANSI text, one NUL.
//   - The callback runs at IRQL 12, where ETW cannot grow the pool, so the
//     buffers are allocated up front (MinimumBuffers == MaximumBuffers).
//
//  Only this file talks to ETW. DiagLogFormat is pure and tested offline;
//  DiagLogCapture needs an elevated process and refuses before touching
//  anything when it is not.
// =====================================================================

/// <summary>The saved-log format, byte for byte. No ETW here: tested without admin rights.</summary>
internal static class DiagLogFormat
{
    public const byte DbgPrintOpcode = 0x20;

    /// <summary>
    /// Message of a DbgPrint event payload: ULONG ComponentId, ULONG Level, ANSI text, NUL.
    /// Anything else is read as its longest printable run and the caller counts it
    /// (and samples it), so a wrong guess about the layout shows up in the summary.
    /// </summary>
    public static ReadOnlySpan<byte> ExtractMessage(ReadOnlySpan<byte> payload, byte opcode, out bool fallback,
                                                    out uint component, out uint level)
    {
        component = uint.MaxValue; level = uint.MaxValue;
        if (opcode == DbgPrintOpcode && payload.Length >= 9)
        {
            var text = payload[8..];
            int nul = text.IndexOf((byte)0);
            if (nul >= 0)
            {
                component = BitConverter.ToUInt32(payload[..4]);
                level = BitConverter.ToUInt32(payload.Slice(4, 4));
                fallback = false;
                return text[..nul];
            }
        }
        fallback = true;
        int bestStart = 0, bestLen = 0;
        for (int i = 0; i < payload.Length;)
        {
            int j = i;
            while (j < payload.Length && (payload[j] >= 0x20 && payload[j] < 0x7F || payload[j] == 9 || payload[j] == 10 || payload[j] == 13)) j++;
            if (j - i > bestLen) { bestStart = i; bestLen = j - i; }
            i = j + 1;
        }
        return bestLen >= 4 ? payload.Slice(bestStart, bestLen) : ReadOnlySpan<byte>.Empty;
    }

    private static readonly byte[] Palm = { (byte)'p', (byte)'a', (byte)'l', (byte)'m' };

    /// <summary>"palm" anywhere, ASCII case-insensitive: the DebugView filter the logs were saved with.</summary>
    public static bool ContainsPalm(ReadOnlySpan<byte> msg)
    {
        for (int i = 0; i + 4 <= msg.Length; i++)
        {
            if ((msg[i] | 0x20) == 'p' && (msg[i + 1] | 0x20) == 'a' && (msg[i + 2] | 0x20) == 'l' && (msg[i + 3] | 0x20) == 'm')
                return true;
        }
        return false;
    }

    /// <summary>Trailing CR/LF dropped (DebugView does); an inner CR, LF or TAB would break the columns, so it becomes a space.</summary>
    public static byte[] CleanMessage(ReadOnlySpan<byte> msg)
    {
        int n = msg.Length;
        while (n > 0 && (msg[n - 1] == (byte)'\n' || msg[n - 1] == (byte)'\r')) n--;
        var r = msg[..n].ToArray();
        for (int i = 0; i < r.Length; i++) if (r[i] == 9 || r[i] == 10 || r[i] == 13) r[i] = 32;
        return r;
    }

    private static readonly byte[] SystemCol = Encoding.ASCII.GetBytes("\tSystem\t");
    private static readonly byte[] LineEnd = { 9, 13, 10 };

    /// <summary>Upper bound of one formatted line for a text of <paramref name="textLen"/> bytes.</summary>
    public static int MaxLineBytes(int textLen) => textLen + 64;

    /// <summary>DebugView: %08d TAB seconds with 8 decimals TAB System TAB text TAB CR LF. No BOM anywhere.</summary>
    public static int FormatLine(Span<byte> dst, long seq, double sec, ReadOnlySpan<byte> text)
    {
        int n = 0;
        seq.TryFormat(dst, out int w, "D8", CultureInfo.InvariantCulture); n += w;
        dst[n++] = 9;
        if (!(sec >= 0) || double.IsInfinity(sec)) sec = 0;       // never a sign, NaN or exponent
        sec.TryFormat(dst[n..], out w, "F8", CultureInfo.InvariantCulture); n += w;
        SystemCol.CopyTo(dst[n..]); n += SystemCol.Length;
        text.CopyTo(dst[n..]); n += text.Length;
        LineEnd.CopyTo(dst[n..]); n += LineEnd.Length;
        return n;
    }

    /// <summary>Which of our drivers wrote a line, for the counts. 3 = anything else (Wacom's PalmRejection... lines).</summary>
    public static int DriverOf(ReadOnlySpan<byte> text)
    {
        if (text.StartsWith("PalmRejFilter"u8)) return 0;
        if (text.StartsWith("PalmRejPipeline"u8)) return 1;
        if (text.StartsWith("PalmRawUsbTap"u8)) return 2;
        return 3;
    }

    public static readonly string[] DriverNames = { "PalmRejFilter", "PalmRejPipeline", "PalmRawUsbTap", "기타(와콤 등)" };

    /// <summary>
    /// "0.1.17 진단 로그 2026-09-24 15-30-12.log": the installed version first, like the
    /// user's own "0.1.14 실사용.log" (scripts pick files by that prefix). Without a
    /// known version: "PalmRej 진단 로그 2026-09-24 15-30-12.log".
    /// </summary>
    public static string FileNameFor(DateTime t, string? version = null)
    {
        string v = SafeVersion(version);
        return $"{(v.Length > 0 ? v : "PalmRej")} 진단 로그 {t:yyyy-MM-dd HH-mm-ss}.log";
    }

    /// <summary>
    /// Digits, dots, ASCII letters and '-' only (it goes into a file name), at most 20
    /// characters. 1.0.1-exp: the label stays, so an experimental build's log is not
    /// named like a plain 1.0.1 log ("1.0.1-exp 진단 로그 ...").
    /// </summary>
    public static string SafeVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "";
        var s = new string(v.Trim().Where(ch => ch is >= '0' and <= '9' or '.' or '-' or >= 'a' and <= 'z' or >= 'A' and <= 'Z').ToArray()).Trim('.', '-');
        return s.Length is > 0 and <= 20 ? s : "";
    }

    /// <summary>
    /// .NET's own error text (it can name a folder) made safe for the summary file
    /// people send: this user's profile folder becomes %USERPROFILE%, any other
    /// \Users\&lt;name&gt; becomes \Users\%USERNAME%, and the account, profile-folder and
    /// PC names anywhere else become %USERNAME% / %COMPUTERNAME% (as the release
    /// privacy scan looks for them: names of 5+ characters, or 2+ non-ASCII).
    /// Only what is written is masked; the stored text stays as it was.
    /// </summary>
    public static string MaskUserPath(string? text)
    {
        string profile = "", user = "", machine = "";
        try { profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
        try { user = Environment.UserName; } catch { }
        try { machine = Environment.MachineName; } catch { }
        return MaskUserPath(text, profile, user, machine);
    }

    internal static string MaskUserPath(string? text, string? profile, string? userName, string? machineName)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string s = text;
        const RegexOptions opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // 1. This user's profile folder, either slash, not when it runs on into a longer name.
        string p = (profile ?? "").Trim().TrimEnd('\\', '/');
        string leaf = "";
        if (p.Length >= 3 && p.IndexOfAny(new[] { '\\', '/' }) >= 0)
        {
            leaf = p.Substring(p.LastIndexOfAny(new[] { '\\', '/' }) + 1);
            string pat = string.Join(@"[\\/]", p.Split('\\', '/').Select(Regex.Escape)) + @"(?![^\\/:*?""<>|'\s])";
            s = Regex.Replace(s, pat, "%USERPROFILE%", opt);
        }

        // 2. Any other user folder (another account, an old path, a short name).
        s = Regex.Replace(s, @"(?<=^|[\\/])Users([\\/])([^\\/:*?""<>|'\r\n]+)", m =>
        {
            string name = m.Groups[2].Value;
            return name.Equals("Public", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Default User", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("%USERNAME%", StringComparison.Ordinal)
                ? m.Value
                : m.Value.Substring(0, m.Value.Length - name.Length) + "%USERNAME%";
        }, opt);

        // 3. The names on their own (DOMAIN\user, \\PC\share, "<name>의"). Anywhere,
        //    like the scan, so masked text passes it; the tokens put in above are skipped.
        foreach (var (name, token) in new[] { (userName, "%USERNAME%"), (leaf, "%USERNAME%"), (machineName, "%COMPUTERNAME%") })
        {
            string n = (name ?? "").Trim();
            bool scanWord = (n.Any(ch => ch > 0x7F) && n.Length >= 2) || n.Length >= 5;
            if (scanWord)
                s = Regex.Replace(s, @"(?<tok>%(?:USERPROFILE|USERNAME|COMPUTERNAME)%)|" + Regex.Escape(n),
                                  m => m.Groups["tok"].Success ? m.Value : token, opt);
        }
        return s;
    }

    /// <summary>
    /// A line written into the .log itself when ETW reports a loss, so an analysis of
    /// the log alone cannot mistake ETW's gap for the driver's. It starts with
    /// "PalmRejLog " (compare_capture.pl skips it) and carries "palm", so a re-filter keeps it.
    /// </summary>
    public static byte[] LossMarker(string detail) => Encoding.ASCII.GetBytes("PalmRejLog ETW-LOST " + detail);
    public static bool IsMarker(ReadOnlySpan<byte> text) => text.StartsWith("PalmRejLog "u8);

    /// <summary>A file error in words the window can show: never .NET's English text.</summary>
    public static string FileErrorText(Exception ex)
    {
        int code = ex.HResult & 0xFFFF;
        return ex switch
        {
            UnauthorizedAccessException => $"이 폴더에는 저장할 수 없습니다. '위치 바꾸기'로 다른 폴더를 고르세요 (코드 {code})",
            DirectoryNotFoundException => $"저장할 폴더가 없습니다. '위치 바꾸기'로 다른 폴더를 고르세요 (코드 {code})",
            PathTooLongException => $"폴더 경로가 너무 깁니다. 바탕화면 같은 짧은 곳을 고르세요 (코드 {code})",
            IOException when code is 112 or 39 => $"디스크 공간이 부족합니다 (코드 {code})",
            IOException when code is 80 => $"같은 이름의 파일이 이미 있습니다. 잠시 뒤 다시 해 보세요 (코드 {code})",
            _ => $"파일을 만들거나 쓰지 못했습니다. 다른 폴더를 골라 보세요 (코드 {code})",
        };
    }

    /// <summary>"…\PalmRej 로그 2026-09-24 15-30-12 요약.txt" beside the log. Not .log, so *.log tools skip it.</summary>
    public static string SummaryPathFor(string logPath) =>
        Path.Combine(Path.GetDirectoryName(logPath) ?? "", Path.GetFileNameWithoutExtension(logPath) + " 요약.txt");

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}시간 {t.Minutes:D2}분 {t.Seconds:D2}초"
                          : $"{t.Minutes}분 {t.Seconds:D2}초";

    public static string Size(long bytes) =>
        bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB"
        : bytes >= 1L << 20 ? (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
        : (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
}

internal enum DiagLogError { NeedAdmin, OtherWindow, NoSlot, NoSpace, FileError, Other }

internal sealed class DiagLogException : Exception
{
    public readonly DiagLogError Kind;
    public readonly uint Code;
    public DiagLogException(DiagLogError kind, uint code, string? detail = null)
        : base(detail ?? $"{kind} ({code})") { Kind = kind; Code = code; }

    /// <summary>(headline, what to do) in the words the window shows.</summary>
    public (string Title, string Detail) UserText() => Kind switch
    {
        DiagLogError.NeedAdmin => ("관리자 권한이 필요합니다",
            "프로그램을 마우스 오른쪽 버튼으로 누르고 '관리자 권한으로 실행'을 고르세요"),
        DiagLogError.OtherWindow => ("이미 기록하고 있습니다",
            "다른 창에서 진단 로그를 기록하고 있습니다. 그 창에서 '멈추고 저장'을 누르세요"),
        DiagLogError.NoSlot => ("Windows 의 기록 자리가 모두 찼습니다",
            "다른 기록 프로그램을 닫거나 컴퓨터를 다시 시작한 뒤 해 보세요"),
        DiagLogError.NoSpace => ("저장할 공간이 부족합니다",
            "500MB 이상 비어 있는 곳이 필요합니다. '위치 바꾸기'로 다른 드라이브를 고르세요"),
        DiagLogError.FileError => ("파일을 만들지 못했습니다", Message),   // Message is DiagLogFormat.FileErrorText
        // Only reached with admin rights (NeedAdmin is decided before ETW is touched),
        // so "run as administrator" would be advice the user cannot follow.
        _ when Code == 5 => ("기록을 시작하지 못했습니다",
            "Windows 나 보안 프로그램이 막았습니다. 이 문구를 그대로 알려 주세요 (코드 5)"),
        _ => ("기록을 시작하지 못했습니다", $"이 문구를 그대로 알려 주세요 (코드 {Code})"),
    };
}

/// <summary>
/// One capture: an ETW system-logger session with the DBGPRINT flag, read in
/// real time, "palm" lines written in DebugView's format, and a summary file
/// beside the log. Start, then Stop (from a worker thread: it waits for ETW).
/// </summary>
internal sealed class DiagLogCapture : IDisposable
{
    public const string SessionName = "PalmRej Diagnostic Log";
    public const string MutexName = @"Global\PalmRejDiagnosticLog";

    internal static readonly Guid DbgPrintGuid = new("13976d09-a327-438c-950b-7f03192815c7");   // wmiguid.h DbgPrintGuid, WMI class Debugger
    internal static readonly Guid LostEventGuid = new("6a399ae0-4bc6-4de9-870b-3657f8947e7e");  // WMI class RT_LostEvent
    internal static readonly Guid EventTraceGuid = new("68fdd900-4a3e-11d1-84f4-0000f80464e3"); // header / rundown

    // Session sizing: ~18k DbgPrint/s while touching, 26k/s peak on this PC
    // (Debug Print Filter\DEFAULT=15 lets every driver's plain DbgPrint through).
    public uint BufferSizeKb = 128;
    public uint BufferCount = 512;          // 64 MB nonpaged, preallocated
    public uint FlushTimerSec = 1;
    public double ReorderWindowSec = 3.0;   // FlushTimer + 2 s
    public long MinFreeBytes = 500L << 20;  // stops by itself below this
    public string AppLabel = "";            // for the summary: which program wrote the log

    public readonly string LogPath;
    public readonly string SummaryPath;

    // ------------------------------------------------------------ counts ----
    // Written by the ETW thread (and the reorder release under _heapLock),
    // read by the window and the summary. 64-bit reads are atomic on x64.
    public long DbgPrintEvents;       // every DbgPrint received; DebugView's first column counts these
    public long PalmEvents;           // passed the palm filter
    public long LinesWritten;
    public long BytesQueued;
    public readonly long[] DriverLines = new long[4];
    public long FallbackParses;
    public long LateEvents;
    public double MaxLateMs;
    public long OtherEvents;
    public long RtLostEvents, RtLostBuffers, RtLostFiles;
    public long CallbackErrors;
    public string? LastCallbackError;
    public int MaxMessageLength;
    public long Lines510;
    public uint SessionEventsLost, SessionRealTimeBuffersLost, SessionLogBuffersLost, SessionNumberOfBuffers, SessionBufferSizeKb;
    public uint ClockType;
    public bool CleanedOrphan;
    public DateTime StartedLocal, EndedLocal;
    public string StopReason = "";
    /// <summary>Korean, for the window. The .NET text goes to WriteErrorDetail (summary only).</summary>
    public string? WriteError;
    public string? WriteErrorDetail;
    public volatile bool LowDisk;
    /// <summary>ProcessTrace returned without us asking (session stopped from outside).</summary>
    public volatile bool SessionEnded;
    /// <summary>DbgPrints that came after the file was closed (Stop gave up waiting): not in the file.</summary>
    public long EventsAfterClose;
    /// <summary>Chunks of formatted lines thrown away because the writer had already finished.</summary>
    public long DroppedChunks;
    /// <summary>False when Stop gave up waiting for ProcessTrace: events may have come after the file closed.</summary>
    public bool Drained = true;
    public string? StopError;
    public long LossMarkers;
    /// <summary>Installed PalmRej version (from the driver package), for the file name and the summary.</summary>
    public string InstalledVersion = "";
    public string PrivilegeNote = "-";

    /// <summary>
    /// Did ETW itself drop anything? The counters are in different units (events,
    /// whole buffers, notices) and probably report the same loss twice, so they are
    /// never added up: only "any of them is not zero" means something.
    /// </summary>
    public bool AnyLoss => SessionEventsLost != 0 || SessionRealTimeBuffersLost != 0 || SessionLogBuffersLost != 0
                           || RtLostEvents != 0 || RtLostBuffers != 0 || RtLostFiles != 0;

    /// <summary>Is anything missing from the file, for any reason? What the window's "빠진 부분" row shows.</summary>
    public bool Incomplete => IncompleteReasons().Count > 0;

    public List<string> IncompleteReasons()
    {
        var r = new List<string>();
        if (AnyLoss) r.Add("기록이 너무 많아 ETW 가 일부를 버렸습니다");
        if (CallbackErrors > 0) r.Add($"이벤트 {CallbackErrors:N0}개를 읽다가 오류가 났습니다");
        if (!Drained || EventsAfterClose > 0 || DroppedChunks > 0)
            r.Add($"저장이 끝난 뒤 도착한 줄이 있습니다 (이벤트 {EventsAfterClose:N0}개, 조각 {DroppedChunks:N0}개)");
        if (WriteError != null) r.Add("파일에 쓰다가 문제가 생겼습니다: " + WriteError);
        if (StopError != null) r.Add("멈추다가 오류가 났습니다");
        return r;
    }
    /// <summary>True when the window should stop the capture now (disk nearly full, file write failed, session gone).</summary>
    public bool NeedsStop => !_stopping && (LowDisk || WriteError != null || SessionEnded);
    public bool Running => _started && !_stopping;

    private readonly object _statLock = new();
    private readonly Dictionary<(uint, uint), long[]> _byComponent = new();
    private readonly List<string> _unknownSamples = new();
    private readonly List<string> _firstDbgPrints = new();
    private readonly List<string> _lossLog = new();

    // ------------------------------------------------------------- state ----
    private IntPtr _logfile, _loggerNamePtr;
    private ulong _consumer = Native.INVALID_PROCESSTRACE_HANDLE;
    private Thread? _processThread, _writerThread, _monitorThread;
    private uint _processTraceResult = uint.MaxValue;
    private Native.EventRecordCallback? _callback;   // kept alive: ETW holds only the raw pointer
    private long _t0;                                // FILETIME just before StartTrace
    private double _tickToSec = 1e-7;                // FILETIME units (no RAW_TIMESTAMP: ETW converts)
    private volatile bool _stopping, _started;
    private readonly ManualResetEventSlim _monitorStop = new();

    private readonly object _heapLock = new();
    private readonly PriorityQueue<Pending, Key> _heap = new();
    private long _arrival, _maxTs = long.MinValue, _lastWrittenTs = long.MinValue, _lastArrivalWall, _seq;
    private readonly BlockingCollection<byte[]> _chunks = new();
    private byte[] _chunk = new byte[64 * 1024];
    private int _chunkLen;
    private readonly byte[] _copy = new byte[4096];
    private FileStream? _file;

    private readonly record struct Key(long Ts, long Arrival) : IComparable<Key>
    {
        public int CompareTo(Key o) => Ts != o.Ts ? Ts.CompareTo(o.Ts) : Arrival.CompareTo(o.Arrival);
    }
    private readonly record struct Pending(long Ts, byte[]? Text, bool Marker);

    public DiagLogCapture(string logPath)
    {
        LogPath = logPath;
        SummaryPath = DiagLogFormat.SummaryPathFor(logPath);
    }

    /// <summary>The capture running in this process, for the crash and exit hooks.</summary>
    private static DiagLogCapture? _current;
    private static int _hooksInstalled;

    public static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public const int StartMutexWaitMs = 3000;

    /// <summary>Free space where the log goes. True when it cannot be told (a network folder): the monitor still watches.</summary>
    public static bool EnoughSpace(string path, long minBytes)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return true;
            return new DriveInfo(root).AvailableFreeSpace >= minBytes;
        }
        catch { return true; }
    }

    // =================================================== start / stop ====

    /// <summary>
    /// Stops a session left behind by a copy that was killed. Only when no running
    /// copy holds the mutex: a second window must not end the first one's capture.
    /// Does nothing without admin rights.
    /// </summary>
    public static bool CleanupOrphan()
    {
        if (!IsAdmin()) return false;
        try
        {
            using var m = new Mutex(false, MutexName);
            bool owned;
            try { owned = m.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned) return false;
            // Start waits up to StartMutexWaitMs for this, so a click right after launch is not refused.
            try { return StopByName(out _) == Native.ERROR_SUCCESS; }
            finally { m.ReleaseMutex(); }
        }
        catch { return false; }
    }

    /// <summary>
    /// Starts the capture or throws DiagLogException; nothing is left running then.
    /// Without admin rights it throws NeedAdmin before touching ETW or the disk.
    /// Everything that owns the session runs on one thread: it takes the
    /// cross-process mutex (a Mutex belongs to the thread that took it, and is
    /// abandoned if the process is killed - which is how the next copy tells an
    /// orphan from a live capture), cleans an orphan, starts the session, then
    /// sits in ProcessTrace.
    /// </summary>
    public void Start()
    {
        if (!IsAdmin()) throw new DiagLogException(DiagLogError.NeedAdmin, Native.ERROR_ACCESS_DENIED);
        if (!EnoughSpace(LogPath, MinFreeBytes)) throw new DiagLogException(DiagLogError.NoSpace, 112);

        FileStream file;
        try
        {
            file = new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20);
        }
        catch (Exception ex)
        {
            throw new DiagLogException(DiagLogError.FileError, (uint)(ex.HResult & 0xFFFF), DiagLogFormat.FileErrorText(ex));
        }
        _file = file;
        StartedLocal = DateTime.Now;
        // The writer owns its own reference: FinishWriting may give up on it (process
        // exit) without pulling the file out from under a write in progress.
        _writerThread = new Thread(() => WriterLoop(file)) { IsBackground = true, Name = "DiagLog writer" };
        _writerThread.Start();

        var started = new ManualResetEventSlim();
        Exception? startError = null;
        _processThread = new Thread(() =>
        {
            Mutex? mutex = null;
            bool owned = false;
            try
            {
                mutex = new Mutex(false, MutexName);
                // A short wait, not 0: the orphan cleanup that runs at launch holds
                // the mutex for a moment, and a click right then is not "another window".
                try { owned = mutex.WaitOne(StartMutexWaitMs); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new DiagLogException(DiagLogError.OtherWindow, 0);

                CleanedOrphan = StopByName(out _) == Native.ERROR_SUCCESS;
                // Harmless if not needed; some setups refuse a system logger without it.
                PrivilegeNote = Native.EnablePrivilege("SeSystemProfilePrivilege", out uint perr) ? "켬" : $"켜지 못함 (코드 {perr})";
                uint rc = StartSession();
                if (rc == Native.ERROR_ALREADY_EXISTS) { StopByName(out _); rc = StartSession(); }
                if (rc != Native.ERROR_SUCCESS) throw new DiagLogException(MapError(rc), rc);
                OpenConsumer();
            }
            catch (Exception ex)
            {
                startError = ex;
                if (owned) { try { StopByName(out _); } catch { } }   // only ours: never a live capture of another copy
                if (owned) mutex!.ReleaseMutex();
                mutex?.Dispose();
                started.Set();
                return;
            }
            _started = true;
            started.Set();
            try
            {
                _processTraceResult = Native.ProcessTrace(new[] { _consumer }, 1, IntPtr.Zero, IntPtr.Zero);
            }
            catch { /* the result code stays at MaxValue */ }
            finally
            {
                if (!_stopping) SessionEnded = true;
                try { StopByName(out _); } catch { }   // ProcessTrace can end on its own: never leave the session behind
                mutex.ReleaseMutex();
                mutex.Dispose();
            }
        }) { IsBackground = true, Name = "DiagLog ETW owner" };
        _processThread.Start();
        started.Wait();
        if (startError != null)
        {
            _stopping = true;
            FinishWriting();
            try { File.Delete(LogPath); } catch { }
            FreeNative();
            throw startError as DiagLogException ?? new DiagLogException(DiagLogError.Other, 0, startError.Message);
        }

        _current = this;
        InstallHooks();
        WriteSummary(final: false);
        _monitorThread = new Thread(MonitorLoop) { IsBackground = true, Name = "DiagLog monitor" };
        _monitorThread.Start();
    }

    private uint StartSession()
    {
        IntPtr props = Native.NewProperties();
        try
        {
            Marshal.StructureToPtr(Guid.NewGuid(), props + Native.P_WnodeGuid, false);  // a system logger: any GUID but SystemTraceControlGuid
            Marshal.WriteInt32(props, Native.P_WnodeClientContext, 1);                 // QPC
            Marshal.WriteInt32(props, Native.P_BufferSize, (int)BufferSizeKb);
            Marshal.WriteInt32(props, Native.P_MinimumBuffers, (int)BufferCount);
            Marshal.WriteInt32(props, Native.P_MaximumBuffers, (int)BufferCount);
            Marshal.WriteInt32(props, Native.P_LogFileMode, (int)(Native.EVENT_TRACE_REAL_TIME_MODE | Native.EVENT_TRACE_SYSTEM_LOGGER_MODE));
            Marshal.WriteInt32(props, Native.P_FlushTimer, (int)FlushTimerSec);
            Marshal.WriteInt32(props, Native.P_EnableFlags, unchecked((int)(Native.EVENT_TRACE_FLAG_DBGPRINT | Native.EVENT_TRACE_FLAG_NO_SYSCONFIG)));
            Marshal.WriteInt32(props, Native.P_LogFileNameOffset, 0);                   // real time only, no .etl

            Native.GetSystemTimePreciseAsFileTime(out _t0);   // before StartTrace: no event can be older
            uint rc = Native.StartTraceW(out _, SessionName, props);
            if (rc == Native.ERROR_SUCCESS)
            {
                SessionNumberOfBuffers = (uint)Marshal.ReadInt32(props, Native.P_NumberOfBuffers);
                SessionBufferSizeKb = (uint)Marshal.ReadInt32(props, Native.P_BufferSize);
            }
            return rc;
        }
        finally { Marshal.FreeHGlobal(props); }
    }

    private void OpenConsumer()
    {
        _callback = OnEvent;
        _logfile = Marshal.AllocHGlobal(Native.LogfileSize);
        Native.Zero(_logfile, Native.LogfileSize);
        _loggerNamePtr = Marshal.StringToHGlobalUni(SessionName);
        Marshal.WriteIntPtr(_logfile, Native.L_LoggerName, _loggerNamePtr);
        // No RAW_TIMESTAMP: EVENT_RECORD.TimeStamp comes as FILETIME, whatever the clock.
        Marshal.WriteInt32(_logfile, Native.L_ProcessTraceMode, (int)(Native.PROCESS_TRACE_MODE_REAL_TIME | Native.PROCESS_TRACE_MODE_EVENT_RECORD));
        Marshal.WriteIntPtr(_logfile, Native.L_EventRecordCallback, Marshal.GetFunctionPointerForDelegate(_callback));
        _consumer = Native.OpenTraceW(_logfile);
        if (_consumer == Native.INVALID_PROCESSTRACE_HANDLE)
            throw new DiagLogException(DiagLogError.Other, (uint)Marshal.GetLastWin32Error());
        ClockType = (uint)Marshal.ReadInt32(_logfile, Native.L_HdrReservedFlags);
        _lastArrivalWall = Stopwatch.GetTimestamp();
    }

    private int _stopOnce;

    /// <summary>
    /// Normal stop, from a worker thread (it can wait several seconds). STOP makes
    /// the session hand over its buffers, ProcessTrace drains them and returns;
    /// then the reorder queue is emptied into the file and the summary is final.
    /// </summary>
    public void Stop(string reason)
    {
        if (Interlocked.Exchange(ref _stopOnce, 1) != 0) return;
        _stopping = true;
        StopReason = reason;
        bool drained = false;
        try
        {
            uint rc = StopByName(out var st);
            if (rc == Native.ERROR_SUCCESS || rc == Native.ERROR_MORE_DATA) TakeLossCounters(st, final: true);
            drained = _processThread == null || _processThread.Join(TimeSpan.FromSeconds(20));
            if (!drained && _consumer != Native.INVALID_PROCESSTRACE_HANDLE)
            {
                Native.CloseTrace(_consumer);               // ERROR_CTX_CLOSE_PENDING: ends after queued buffers
                _consumer = Native.INVALID_PROCESSTRACE_HANDLE;   // closed once, never twice
                drained = _processThread!.Join(TimeSpan.FromSeconds(5));
            }
            if (_consumer != Native.INVALID_PROCESSTRACE_HANDLE) { Native.CloseTrace(_consumer); _consumer = Native.INVALID_PROCESSTRACE_HANDLE; }
        }
        catch (Exception ex) { StopError = ex.GetType().Name + ": " + ex.Message; }
        Drained = drained;
        try { _monitorStop.Set(); _monitorThread?.Join(3000); } catch { }
        try { FinishWriting(); } catch (Exception ex) { StopError ??= ex.GetType().Name + ": " + ex.Message; }
        EndedLocal = DateTime.Now;
        WriteSummary(final: true);
        if (drained) FreeNative();                          // a thread still in ProcessTrace may still read them
        if (ReferenceEquals(_current, this)) _current = null;
    }

    /// <summary>Process exit or crash: the session must not outlive us. Whatever is queued is written.</summary>
    public void EmergencyStop(string reason)
    {
        if (Interlocked.Exchange(ref _stopOnce, 1) != 0) return;
        _stopping = true;
        StopReason = reason;
        try { StopByName(out var st); TakeLossCounters(st, final: true); } catch { }
        try { Drained = _processThread == null || _processThread.Join(2000); } catch { Drained = false; }
        try { FinishWriting(2000); } catch { }
        EndedLocal = DateTime.Now;
        try { WriteSummary(final: true); } catch { }
    }

    public void Dispose() { if (_started) Stop("정리"); }

    private static void InstallHooks()
    {
        if (Interlocked.Exchange(ref _hooksInstalled, 1) != 0) return;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _current?.EmergencyStop("앱 종료");
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _current?.EmergencyStop("앱 오류");
    }

    private int _finished;
    private bool _closed;   // under _heapLock: nothing more goes into the file

    internal void FinishWriting(int joinMs = Timeout.Infinite)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        lock (_heapLock) { ReleaseUpTo(long.MaxValue); FlushChunk(); _closed = true; }
        _chunks.CompleteAdding();
        bool joined = _writerThread == null || _writerThread.Join(joinMs);
        // Only once the writer is gone: disposing under a write it is still doing
        // is how the old code crashed. If it is not gone (process exit, slow
        // disk), the process ending closes the file.
        if (joined)
        {
            try { _file?.Dispose(); } catch { }
            _file = null;
        }
    }

    private void FreeNative()
    {
        if (_logfile != IntPtr.Zero) { Marshal.FreeHGlobal(_logfile); _logfile = IntPtr.Zero; }
        if (_loggerNamePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_loggerNamePtr); _loggerNamePtr = IntPtr.Zero; }
    }

    internal struct SessionCounters { public uint EventsLost, RealTimeBuffersLost, LogBuffersLost, NumberOfBuffers; }

    internal static uint StopByName(out SessionCounters st) => Control(Native.EVENT_TRACE_CONTROL_STOP, out st);

    private static uint Control(uint code, out SessionCounters st)
    {
        st = default;
        IntPtr props = Native.NewProperties();
        try
        {
            uint rc = Native.ControlTraceW(0, SessionName, props, code);
            st.EventsLost = (uint)Marshal.ReadInt32(props, Native.P_EventsLost);
            st.RealTimeBuffersLost = (uint)Marshal.ReadInt32(props, Native.P_RealTimeBuffersLost);
            st.LogBuffersLost = (uint)Marshal.ReadInt32(props, Native.P_LogBuffersLost);
            st.NumberOfBuffers = (uint)Marshal.ReadInt32(props, Native.P_NumberOfBuffers);
            return rc;
        }
        finally { Marshal.FreeHGlobal(props); }
    }

    /// <summary>
    /// For a StartTrace failure. The process is already known to be elevated here,
    /// so ACCESS_DENIED is NOT "need admin" (that advice could not be followed): it
    /// falls to Other, which shows the code.
    /// </summary>
    internal static DiagLogError MapError(uint rc) => rc switch
    {
        Native.ERROR_NO_SYSTEM_RESOURCES => DiagLogError.NoSlot,
        Native.ERROR_ALREADY_EXISTS => DiagLogError.OtherWindow,
        _ => DiagLogError.Other,
    };

    /// <summary>Session counters only grow; a rise is noted with the time it was seen.</summary>
    /// <summary>
    /// Session counters only grow; a rise is noted with the time it was seen, and a
    /// marker line goes into the .log at that moment (the counters are polled every
    /// 2 s, so the marker sits up to 2 s after the loss itself).
    /// </summary>
    internal void TakeLossCounters(SessionCounters st, bool final)
    {
        string? marker = null;
        lock (_statLock)
        {
            long de = st.EventsLost - (long)SessionEventsLost, dr = st.RealTimeBuffersLost - (long)SessionRealTimeBuffersLost,
                 dl = st.LogBuffersLost - (long)SessionLogBuffersLost;
            if (de > 0 || dr > 0 || dl > 0)
            {
                double t = (DateTime.Now - StartedLocal).TotalSeconds;
                string what = $"events=+{Math.Max(0, de)} rtBuffers=+{Math.Max(0, dr)} logBuffers=+{Math.Max(0, dl)}";
                if (_lossLog.Count < 200)
                    _lossLog.Add($"t={t.ToString("F1", CultureInfo.InvariantCulture)}초 세션 {what}{(final ? " (멈출 때)" : "")}");
                marker = $"source=session {what} (polled every 2 s: the loss is up to 2 s before this line)";
            }
            SessionEventsLost = Math.Max(SessionEventsLost, st.EventsLost);
            SessionRealTimeBuffersLost = Math.Max(SessionRealTimeBuffersLost, st.RealTimeBuffersLost);
            SessionLogBuffersLost = Math.Max(SessionLogBuffersLost, st.LogBuffersLost);
            if (st.NumberOfBuffers != 0) SessionNumberOfBuffers = st.NumberOfBuffers;
        }
        if (marker != null) AddMarker(NowTicks(), marker);
    }

    /// <summary>The current time on the events' clock (FILETIME), or the newest event's in the offline tests.</summary>
    private long NowTicks()
    {
        if (_testSink != null) return _maxTs == long.MinValue ? 0 : _maxTs;
        Native.GetSystemTimePreciseAsFileTime(out long now);
        return now;
    }

    internal void AddMarker(long ts, string detail) => Add(ts, DiagLogFormat.LossMarker(detail), marker: true);

    /// <summary>Every 2 s the loss counters, every 5 s the free space, every 10 s the summary.</summary>
    private void MonitorLoop()
    {
        var sw = Stopwatch.StartNew();
        long nextQuery = 2000, nextDisk = 0, nextSummary = 10000;
        while (!_monitorStop.Wait(250))
        {
            try
            {
                long now = sw.ElapsedMilliseconds;
                if (now >= nextQuery)
                {
                    nextQuery = now + 2000;
                    if (Control(Native.EVENT_TRACE_CONTROL_QUERY, out var st) == Native.ERROR_SUCCESS) TakeLossCounters(st, final: false);
                }
                if (now >= nextDisk)
                {
                    nextDisk = now + 5000;
                    string? root = Path.GetPathRoot(Path.GetFullPath(LogPath));
                    if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace < MinFreeBytes) LowDisk = true;
                }
                if (now >= nextSummary)
                {
                    nextSummary = now + 10000;
                    WriteSummary(final: false);
                }
            }
            catch { /* the monitor is advisory; the capture goes on */ }
        }
    }

    // ====================================================== consumer ====

    private static (long, long) KeyOf(Guid g)
    {
        var b = g.ToByteArray();
        return (BitConverter.ToInt64(b, 0), BitConverter.ToInt64(b, 8));
    }
    private static readonly (long A, long B) KDbgPrint = KeyOf(DbgPrintGuid), KLost = KeyOf(LostEventGuid), KEventTrace = KeyOf(EventTraceGuid);

    internal void OnEvent(IntPtr rec)
    {
        // An exception must never leave this method: it would tear the process down.
        try
        {
            long ts = Marshal.ReadInt64(rec, Native.R_TimeStamp);
            long g0 = Marshal.ReadInt64(rec, Native.R_ProviderId), g1 = Marshal.ReadInt64(rec, Native.R_ProviderId + 8);
            byte opcode = Marshal.ReadByte(rec, Native.R_Opcode);
            int len = (ushort)Marshal.ReadInt16(rec, Native.R_UserDataLength);
            IntPtr data = Marshal.ReadIntPtr(rec, Native.R_UserData);
            if (len > _copy.Length) len = _copy.Length;
            if (len > 0 && data != IntPtr.Zero) Marshal.Copy(data, _copy, 0, len); else len = 0;
            var payload = new ReadOnlySpan<byte>(_copy, 0, len);

            if (g0 == KDbgPrint.A && g1 == KDbgPrint.B)
            {
                DbgPrintEvents++;
                var msg = DiagLogFormat.ExtractMessage(payload, opcode, out bool fallback, out uint comp, out uint level);
                if (fallback) { FallbackParses++; Sample(_unknownSamples, "DbgPrint (해석 실패)", rec, payload); }
                if (_firstDbgPrints.Count < 3) Sample(_firstDbgPrints, "DbgPrint", rec, payload);
                bool keep = DiagLogFormat.ContainsPalm(msg);
                Count(comp, level, keep);
                if (keep) { PalmEvents++; Add(ts, DiagLogFormat.CleanMessage(msg)); }
                else Add(ts, null);
            }
            else if (g0 == KLost.A && g1 == KLost.B)
            {
                if (opcode == 32) RtLostEvents++; else if (opcode == 33) RtLostBuffers++; else RtLostFiles++;
                lock (_statLock)
                    if (_lossLog.Count < 200)
                        _lossLog.Add($"t={ToSec(ts).ToString("F1", CultureInfo.InvariantCulture)}초 RT_LostEvent 종류 {opcode} ({(opcode == 32 ? "이벤트" : opcode == 33 ? "버퍼" : "파일")})");
                AddMarker(ts, $"source=RT_LostEvent kind={(opcode == 32 ? "event" : opcode == 33 ? "buffer" : "file")} opcode={opcode}");
            }
            else
            {
                OtherEvents++;
                if (!(g0 == KEventTrace.A && g1 == KEventTrace.B)) Sample(_unknownSamples, "모르는 이벤트", rec, payload);
            }
        }
        catch (Exception ex)
        {
            CallbackErrors++;
            LastCallbackError = ex.GetType().Name + ": " + ex.Message;
        }
    }

    private void Count(uint comp, uint level, bool kept)
    {
        lock (_statLock)
        {
            if (!_byComponent.TryGetValue((comp, level), out var c))
            {
                if (_byComponent.Count >= 64) return;
                _byComponent[(comp, level)] = c = new long[2];
            }
            c[0]++;
            if (kept) c[1]++;
        }
    }

    private void Sample(List<string> into, string what, IntPtr rec, ReadOnlySpan<byte> payload)
    {
        lock (_statLock)
        {
            if (into.Count >= 8) return;
            var hdr = new byte[Native.EventRecordSize];
            Marshal.Copy(rec, hdr, 0, hdr.Length);
            var guid = new Guid(new ReadOnlySpan<byte>(hdr, Native.R_ProviderId, 16));
            into.Add($"{what}: provider {guid} opcode {hdr[Native.R_Opcode]} version {hdr[Native.R_Version]} " +
                     $"flags 0x{BitConverter.ToUInt16(hdr, 4):X4} len {payload.Length}\r\n" +
                     $"  header {Convert.ToHexString(hdr)}\r\n" +
                     $"  data   {Convert.ToHexString(payload[..Math.Min(payload.Length, 256)])}");
        }
    }

    // ======================================================= reorder ====
    // ETW hands over per-CPU buffers at different moments, so events arrive a
    // little out of order. Every DbgPrint (the filtered ones as a bare time) goes
    // into a min-heap and leaves it once it is ReorderWindowSec older than the
    // newest seen. The sequence number counts all of them, like DebugView's.

    /// <summary><paramref name="text"/> null = a DbgPrint the filter dropped: it still takes a number.</summary>
    internal void Add(long ts, byte[]? text, bool marker = false)
    {
        lock (_heapLock)
        {
            if (_closed)
            {
                if (!marker) EventsAfterClose++;           // too late for the file; the verdict says so
                return;
            }
            _heap.Enqueue(new Pending(ts, text, marker), new Key(ts, _arrival++));
            _lastArrivalWall = Stopwatch.GetTimestamp();
            if (ts > _maxTs)
            {
                _maxTs = ts;
                ReleaseUpTo(_maxTs - SecToTicks(ReorderWindowSec));
            }
        }
    }

    /// <summary>Called by the writer thread: after a quiet spell everything queued goes out.</summary>
    private void ReleaseIfIdle()
    {
        lock (_heapLock)
        {
            if (_heap.Count > 0)
            {
                double idle = (Stopwatch.GetTimestamp() - _lastArrivalWall) / (double)Stopwatch.Frequency;
                if (idle > ReorderWindowSec + FlushTimerSec) ReleaseUpTo(long.MaxValue);
            }
            FlushChunk();
        }
    }

    private long SecToTicks(double s) => (long)(s / _tickToSec);
    private double ToSec(long ts) => Math.Max(0, (ts - _t0) * _tickToSec);

    private void ReleaseUpTo(long limit)
    {
        while (_heap.TryPeek(out var p, out var k) && k.Ts <= limit)
        {
            _heap.Dequeue();
            _seq++;
            if (p.Text == null) continue;
            long ts = p.Ts;
            if (ts < _lastWrittenTs)
            {
                // Older than a line already in the file. Written at the last line's
                // time so the time column never goes back (several tools stop there).
                if (p.Marker) { AppendLine(_seq, ToSec(_lastWrittenTs), p.Text); LinesWritten++; LossMarkers++; continue; }
                LateEvents++;
                MaxLateMs = Math.Max(MaxLateMs, (_lastWrittenTs - ts) * _tickToSec * 1000);
                ts = _lastWrittenTs;
            }
            else _lastWrittenTs = ts;
            AppendLine(_seq, ToSec(ts), p.Text);
            LinesWritten++;
            if (p.Marker) { LossMarkers++; continue; }
            DriverLines[DiagLogFormat.DriverOf(p.Text)]++;
            if (p.Text.Length > MaxMessageLength) MaxMessageLength = p.Text.Length;
            if (p.Text.Length >= 510) Lines510++;
        }
    }

    private void AppendLine(long seq, double sec, byte[] text)
    {
        int need = DiagLogFormat.MaxLineBytes(text.Length);
        if (_chunkLen + need > _chunk.Length) FlushChunk();
        if (need > _chunk.Length) _chunk = new byte[need];
        _chunkLen += DiagLogFormat.FormatLine(_chunk.AsSpan(_chunkLen), seq, sec, text);
    }

    private void FlushChunk()
    {
        if (_chunkLen == 0) return;
        if (!_chunks.IsAddingCompleted)
        {
            _chunks.Add(_chunk[.._chunkLen]);
            BytesQueued += _chunkLen;
        }
        else DroppedChunks++;                                    // cannot happen while Add checks _closed; counted in case
        _chunkLen = 0;
    }

    private void SetWriteError(Exception ex)
    {
        WriteErrorDetail ??= ex.GetType().Name + ": " + ex.Message;
        WriteError ??= DiagLogFormat.FileErrorText(ex);
    }

    /// <param name="file">Its own reference, never _file: FinishWriting may drop that one.</param>
    private void WriterLoop(FileStream file)
    {
        long lastFlush = Stopwatch.GetTimestamp(), lastIdleCheck = lastFlush;
        while (!_chunks.IsCompleted)
        {
            try
            {
                if (_chunks.TryTake(out var c, 100) && WriteError == null)
                    file.Write(c, 0, c.Length);
                if (!_stopping && Stopwatch.GetTimestamp() - lastIdleCheck > Stopwatch.Frequency / 4)
                {
                    ReleaseIfIdle();                             // also hands over the partly filled chunk
                    lastIdleCheck = Stopwatch.GetTimestamp();
                }
                if (WriteError == null && Stopwatch.GetTimestamp() - lastFlush > Stopwatch.Frequency)
                {
                    file.Flush();                                // a crash loses at most ~1 s + the reorder window
                    lastFlush = Stopwatch.GetTimestamp();
                }
            }
            catch (Exception ex)
            {
                SetWriteError(ex);                               // keep draining so nothing blocks; the window reacts
            }
        }
        try { if (WriteError == null) file.Flush(); } catch (Exception ex) { SetWriteError(ex); }
    }

    // ======================================================= summary ====

    private readonly object _summaryLock = new();

    /// <summary>Beside the log, UTF-8 with BOM (Notepad and PowerShell 5.1 read the Korean), CRLF.</summary>
    public void WriteSummary(bool final)
    {
        lock (_summaryLock)
        {
            try { File.WriteAllText(SummaryPath, SummaryText(final), new UTF8Encoding(true)); }
            catch { /* the log itself matters more; the window shows the numbers too */ }
        }
    }

    public string SummaryText(bool final)
    {
        var ic = CultureInfo.InvariantCulture;
        var end = final ? EndedLocal : DateTime.Now;
        var dur = end - StartedLocal;
        long received = DbgPrintEvents, written = LinesWritten;
        var s = new StringBuilder();
        void L(string x = "") => s.Append(x).Append("\r\n");

        L("PalmRej 진단 로그 요약");
        L($"상태={(final ? "끝남" : "기록 중")}");
        L($"로그파일={Path.GetFileName(LogPath)}");
        L($"시작={StartedLocal:yyyy-MM-dd HH:mm:ss}");
        L(final ? $"끝={EndedLocal:yyyy-MM-dd HH:mm:ss}" : $"갱신={end:yyyy-MM-dd HH:mm:ss}");
        L($"기록시간={DiagLogFormat.Duration(dur)} ({dur.TotalSeconds.ToString("F1", ic)}초)");
        if (final) L($"멈춘이유={StopReason}");
        L($"프로그램={AppLabel}");
        L($"설치된PalmRej={(InstalledVersion.Length > 0 ? InstalledVersion : "-")}");
        L($"Windows={Environment.OSVersion.Version}");
        L();
        // The verdict names every reason the file could be short, not only ETW's.
        L("[판정]");
        var reasons = IncompleteReasons();
        if (final && written == 0) reasons.Insert(0, "palm 이 든 줄이 하나도 오지 않았습니다");
        if (LowDisk) reasons.Add("디스크 공간이 모자라 일찍 멈췄습니다");
        if (SessionEnded) reasons.Add("Windows 가 기록을 일찍 멈췄습니다 (멈춘이유 참고)");
        if (reasons.Count == 0) L(final ? "빠진 것 없음" : "지금까지 빠진 것 없음");
        else
        {
            L(AnyLoss || CallbackErrors > 0 || !Drained || EventsAfterClose > 0 || DroppedChunks > 0 || WriteError != null
                ? "빠진 부분 있음. 이 로그에는 없는 줄이 있습니다. 같은 동작을 한 번 더 기록해 주세요."
                : "주의할 점 있음");
            foreach (var r in reasons) L("- " + r);
        }
        if (FallbackParses > 0) L($"주의: 구조를 해석하지 못한 이벤트 {FallbackParses:N0}개 (아래 16진수 참고)");
        // .NET's text can name the folder (C:\Users\<name>\...): masked here, where it is written.
        if (WriteErrorDetail != null) L($"쓰기오류원문={DiagLogFormat.MaskUserPath(WriteErrorDetail)}");
        if (StopError != null) L($"멈춤오류원문={DiagLogFormat.MaskUserPath(StopError)}");
        L();
        L("[줄 수]");
        L("필터=palm (대소문자 무시. DebugView 에서 palm 필터를 건 것과 같습니다)");
        L($"받은DbgPrint={received}");
        L($"palm줄={PalmEvents}");
        L($"기록한줄={written} (이 중 유실표시줄 {LossMarkers})");
        L($"뺀줄={received - PalmEvents}");
        for (int i = 0; i < 4; i++) L($"줄.{DiagLogFormat.DriverNames[i]}={DriverLines[i]}");
        L($"파일크기={BytesQueued} ({DiagLogFormat.Size(BytesQueued)})");
        L($"가장긴메시지={MaxMessageLength}자, 510자이상줄={Lines510}");
        L($"늦게온줄={LateEvents} (시간 칸이 줄지 않도록 앞 줄 시각으로 기록), 최대지연={MaxLateMs.ToString("F1", ic)}ms");
        L();
        L("[유실] (단위가 서로 달라 더하지 않습니다. 같은 유실이 두 곳에 함께 잡힐 수 있습니다)");
        L($"세션.EventsLost={SessionEventsLost} (이벤트 수)");
        L($"세션.RealTimeBuffersLost={SessionRealTimeBuffersLost} (버퍼 수. 버퍼 하나는 {SessionBufferSizeKb}KB, DbgPrint 수백~천여 개)");
        L($"세션.LogBuffersLost={SessionLogBuffersLost} (버퍼 수)");
        L($"RT_LostEvent 알림 이벤트={RtLostEvents}회 버퍼={RtLostBuffers}회 파일={RtLostFiles}회");
        L($"저장뒤도착={EventsAfterClose}개, 버린조각={DroppedChunks}, 끝까지받음={(Drained ? "예" : "아니오")}");
        L($"유실표시줄={LossMarkers} (로그 안의 'PalmRejLog ETW-LOST' 줄)");
        lock (_statLock)
        {
            if (_lossLog.Count == 0) L("유실구간=없음");
            else foreach (var x in _lossLog) L("유실구간: " + x);
        }
        L();
        L("[세션]");
        L($"이름={SessionName}");
        L($"버퍼={BufferSizeKb}KB x {BufferCount} (돌려받은 값 {SessionBufferSizeKb}KB x {SessionNumberOfBuffers})");
        L($"FlushTimer={FlushTimerSec}초, 재정렬창={ReorderWindowSec.ToString("F1", ic)}초");
        L($"시계종류(ReservedFlags)={ClockType} (1=QPC)");
        L($"이전세션정리={(CleanedOrphan ? "예 (앞서 비정상 종료로 남은 세션을 멈춤)" : "아니오")}");
        L($"SeSystemProfilePrivilege={PrivilegeNote}");
        L($"ProcessTrace결과={(_processTraceResult == uint.MaxValue ? "-" : _processTraceResult.ToString(ic))}");
        L();
        L("[이벤트 해석]");
        L($"해석실패={FallbackParses}");
        L($"콜백오류={CallbackErrors}{(LastCallbackError != null ? " 마지막: " + DiagLogFormat.MaskUserPath(LastCallbackError) : "")}");
        L($"기타이벤트={OtherEvents}");
        lock (_statLock)
        {
            foreach (var kv in _byComponent.OrderByDescending(k => k.Value[0]).Take(20))
                L($"Component={(kv.Key.Item1 == uint.MaxValue ? "?" : kv.Key.Item1.ToString(ic))} Level={(kv.Key.Item2 == uint.MaxValue ? "?" : kv.Key.Item2.ToString(ic))} 받음={kv.Value[0]} 기록={kv.Value[1]}");
            L();
            L("[첫 DbgPrint 이벤트 16진수] (이벤트 구조 확인용)");
            if (_firstDbgPrints.Count == 0) L("없음");
            foreach (var x in _firstDbgPrints) L(x);
            L();
            L("[모르는 이벤트 16진수] (처음 8개까지)");
            if (_unknownSamples.Count == 0) L("없음");
            foreach (var x in _unknownSamples) L(x);
        }
        L();
        L("[안내]");
        L("- 로그 형식은 DebugView 저장 파일과 같습니다: 번호 TAB 초 TAB System TAB 메시지 TAB 줄바꿈(CRLF), BOM 없음.");
        L("- 첫 칸 번호가 건너뛴 만큼은 palm 필터로 뺀 줄입니다. 유실이 아닙니다.");
        L("- ETW 가 줄을 버리면 로그 안에 'PalmRejLog ETW-LOST' 줄이 그 시각에 들어갑니다. 그 근처의 빈 곳은 드라이버가 아니라 기록이 놓친 것입니다.");
        L("- 두 번째 칸의 초는 기록을 시작한 때부터 잰 시간입니다.");
        return s.ToString();
    }

    // ------------------------------------------------- offline test hook ----
    /// <summary>A capture that never starts ETW: events go in through OnEvent/Add, lines come out in <paramref name="sink"/>.</summary>
    internal static DiagLogCapture ForTest(Stream sink, double windowSec, double tickToSec = 1e-7)
    {
        var c = new DiagLogCapture(Path.Combine(Path.GetTempPath(), "unused.log")) { ReorderWindowSec = windowSec };
        c._t0 = 0;
        c._tickToSec = tickToSec;
        c._testSink = sink;
        c.StartedLocal = DateTime.Now;
        return c;
    }
    private Stream? _testSink;
    internal void TestFinish()
    {
        lock (_heapLock) { ReleaseUpTo(long.MaxValue); FlushChunk(); }
        while (_chunks.TryTake(out var c)) _testSink!.Write(c, 0, c.Length);
    }
    /// <summary>The real writer thread on a real file, for the emergency-stop test.</summary>
    internal void StartWriterForTest(FileStream fs)
    {
        _file = fs;
        _writerThread = new Thread(() => WriterLoop(fs)) { IsBackground = true, Name = "DiagLog writer (test)" };
        _writerThread.Start();
    }
    internal Thread? WriterThreadForTest => _writerThread;
    internal void TestPump()
    {
        lock (_heapLock) FlushChunk();
        while (_chunks.TryTake(out var c)) _testSink!.Write(c, 0, c.Length);
    }

    // ======================================================== native ====

    internal static class Native
    {
        public const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        public const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        public const uint EVENT_TRACE_SYSTEM_LOGGER_MODE = 0x02000000;
        public const uint EVENT_TRACE_FLAG_DBGPRINT = 0x00040000;
        public const uint EVENT_TRACE_FLAG_NO_SYSCONFIG = 0x10000000;
        public const uint EVENT_TRACE_CONTROL_QUERY = 0, EVENT_TRACE_CONTROL_STOP = 1;
        public const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100, PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        public const ulong INVALID_PROCESSTRACE_HANDLE = ulong.MaxValue;
        public const uint ERROR_SUCCESS = 0, ERROR_ACCESS_DENIED = 5, ERROR_ALREADY_EXISTS = 183, ERROR_MORE_DATA = 234,
                          ERROR_NO_SYSTEM_RESOURCES = 1450, ERROR_WMI_INSTANCE_NOT_FOUND = 4201;

        // EVENT_TRACE_PROPERTIES, x64 (offsets printed by etw\layout.c against SDK 10.0.28000 headers)
        public const int PropsSize = 120, P_WnodeBufferSize = 0, P_WnodeGuid = 24, P_WnodeClientContext = 40, P_WnodeFlags = 44,
            P_BufferSize = 48, P_MinimumBuffers = 52, P_MaximumBuffers = 56, P_LogFileMode = 64,
            P_FlushTimer = 68, P_EnableFlags = 72, P_NumberOfBuffers = 80, P_FreeBuffers = 84, P_EventsLost = 88,
            P_BuffersWritten = 92, P_LogBuffersLost = 96, P_RealTimeBuffersLost = 100,
            P_LogFileNameOffset = 112, P_LoggerNameOffset = 116;
        public const int PropsTotal = PropsSize + 2 * 1024 * 2 + 16;   // room for both names (1024 WCHAR each)

        // EVENT_TRACE_LOGFILEW, x64
        public const int LogfileSize = 448, L_LoggerName = 8, L_ProcessTraceMode = 28,
            L_HdrReservedFlags = 120 + 272, L_EventRecordCallback = 424;

        // EVENT_RECORD, x64
        public const int EventRecordSize = 112, R_TimeStamp = 16, R_ProviderId = 24, R_Version = 42, R_Opcode = 45,
            R_UserDataLength = 86, R_UserData = 96;

        public static IntPtr NewProperties()
        {
            IntPtr p = Marshal.AllocHGlobal(PropsTotal);
            Zero(p, PropsTotal);
            Marshal.WriteInt32(p, P_WnodeBufferSize, PropsTotal);
            Marshal.WriteInt32(p, P_WnodeFlags, unchecked((int)WNODE_FLAG_TRACED_GUID));
            Marshal.WriteInt32(p, P_LoggerNameOffset, PropsSize);
            Marshal.WriteInt32(p, P_LogFileNameOffset, PropsSize + 2048);  // StartSession sets 0 (no file)
            return p;
        }

        public static void Zero(IntPtr p, int n) { for (int i = 0; i < n; i++) Marshal.WriteByte(p, i, 0); }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void EventRecordCallback(IntPtr eventRecord);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint StartTraceW(out ulong traceId, string instanceName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint ControlTraceW(ulong traceId, string? instanceName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern ulong OpenTraceW(IntPtr logfile);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        public static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        public static extern uint CloseTrace(ulong traceHandle);

        [DllImport("kernel32.dll")]
        public static extern void GetSystemTimePreciseAsFileTime(out long fileTime);

        // ---- one privilege in this process's own token (nothing system-wide) ----
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20, TOKEN_QUERY = 0x8, SE_PRIVILEGE_ENABLED = 2;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges1 { public uint Count; public long Luid; public uint Attributes; }   // 16 bytes, LUID at 4

        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges1 state, uint len, IntPtr prev, IntPtr retLen);

        /// <summary>
        /// Enables a privilege the token already holds (an admin token holds
        /// SeSystemProfilePrivilege, disabled). False with 1300 when it does not hold it.
        /// </summary>
        public static bool EnablePrivilege(string name, out uint error)
        {
            error = 0;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            { error = (uint)Marshal.GetLastWin32Error(); return false; }
            try
            {
                if (!LookupPrivilegeValueW(null, name, out long luid)) { error = (uint)Marshal.GetLastWin32Error(); return false; }
                var tp = new TokenPrivileges1 { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                bool ok = AdjustTokenPrivileges(token, false, ref tp, (uint)Marshal.SizeOf<TokenPrivileges1>(), IntPtr.Zero, IntPtr.Zero);
                error = (uint)Marshal.GetLastWin32Error();   // 1300 ERROR_NOT_ALL_ASSIGNED even when ok is true
                return ok && error == 0;
            }
            finally { CloseHandle(token); }
        }
    }
}
