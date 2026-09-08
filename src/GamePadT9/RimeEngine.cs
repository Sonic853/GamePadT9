using System.Runtime.InteropServices;
using System.Text.Json;

namespace GamePadT9;

internal sealed record Settings(string InstallRoot, string Schema, string PrebuiltPath, string UserPath)
{
    public static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "gamepadt9.json")) || PortableRuntime.IsPortable(dir.FullName)) return dir.FullName;
        throw new FileNotFoundException("找不到 gamepadt9.json，请先运行 scripts/prepare.ps1。");
    }
    public static Settings Load(string root)
    {
        if (PortableRuntime.IsPortable(root)) return PortableRuntime.Prepare(root);
        var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(root, "gamepadt9.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("配置为空。");
        return value with { InstallRoot = Path.GetFullPath(value.InstallRoot, root), PrebuiltPath = Path.GetFullPath(value.PrebuiltPath, root), UserPath = Path.GetFullPath(value.UserPath, root) };
    }
}

internal sealed record Candidate(string Text, string Comment);
internal sealed record EngineView(string Preedit, int Page, bool LastPage, int Highlight, Candidate[] Candidates)
{
    public static EngineView Empty => new("", 0, true, 0, []);
}

// Layouts and C calling convention follow ../xiaobai-t9/librime/src/rime_api.h.
// These are in-process engine calls. No events are sent to the Windows keyboard queue.
internal sealed class RimeEngine : IDisposable
{
    private readonly List<nint> strings = [];
    private nuint session;
    private bool initialized;
    public EngineView View { get; private set; } = EngineView.Empty;
    public string PendingCommit { get; private set; } = "";
    public RimeEngine(Settings settings)
    {
        var dll = Path.Combine(settings.InstallRoot, "rime.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("找不到小白 T9 引擎。", dll);
        if (Environment.Is64BitProcess) throw new InvalidOperationException("已安装的小白 rime.dll 为 x86，请运行 win-x86 版本。");
        NativeLibrary.SetDllImportResolver(typeof(RimeEngine).Assembly,
            (name, _, _) => name == "rime.dll" ? LoadEngine(dll) : 0);
        Directory.CreateDirectory(settings.UserPath);
        Directory.CreateDirectory(Path.Combine(settings.UserPath, "logs"));
        Directory.CreateDirectory(Path.Combine(settings.UserPath, "build"));
        var traits = new Traits
        {
            DataSize = Marshal.SizeOf<Traits>() - sizeof(int),
            Shared = Utf8(Path.Combine(settings.InstallRoot, "data")),
            User = Utf8(settings.UserPath), Name = Utf8("GamePadT9"), Code = Utf8("gamepad_t9"),
            Version = Utf8("0.1"), AppName = Utf8("rime.gamepadt9"),
            LogLevel = 1, LogDir = Utf8(Path.Combine(settings.UserPath, "logs")),
            Prebuilt = Utf8(settings.PrebuiltPath), Staging = Utf8(Path.Combine(settings.UserPath, "build"))
        };
        try
        {
            Native.RimeSetup(ref traits);
            Native.RimeInitialize(ref traits); initialized = true;
            // Use the already-deployed original schema. Never deploy into the user's installation.
            session = Native.RimeCreateSession();
            if (session == 0 || Native.RimeSelectSchema(session, settings.Schema) == 0)
                throw new InvalidOperationException("无法加载原版小白方案，请查看 artifacts/rime-user/logs。");
            Native.RimeSetOption(session, "ascii_mode", 0);
            Refresh();
        }
        catch { Dispose(); throw; }
    }
    private static nint LoadEngine(string path)
    {
        var library = LoadLibraryEx(path, 0, 0x00000100 | 0x00001000); // DLL directory and default safe search locations.
        if (library == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法加载本机小白 T9 引擎或其依赖：" + path);
        return library;
    }
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    private nint Utf8(string value) { var p = Marshal.StringToCoTaskMemUTF8(value); strings.Add(p); return p; }
    public void InputRegion(int region)
    {
        if ((uint)region >= 9) throw new ArgumentOutOfRangeException(nameof(region));
        // Original xiaobai key_binder expects KP_* symbols; these remain inside librime.
        int[] codes = [7, 8, 9, 4, 5, 6, 1, 2, 3];
        Process(0xFFB0 + codes[region]);
    }
    public void Process(int key)
    {
        if (PendingCommit.Length != 0) throw new InvalidOperationException("尚有未确认的 TSF 提交，不能继续输入。");
        Native.RimeProcessKey(session, key, 0); Refresh();
    }
    public void Confirm(int? index = null)
    {
        if (PendingCommit.Length != 0 || View.Candidates.Length == 0) return;
        var selected = index ?? View.Highlight;
        if ((uint)selected >= View.Candidates.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (Native.RimeSelectCandidateOnCurrentPage(session, (nuint)selected) == 0)
            throw new InvalidOperationException("引擎拒绝选择候选。");
        Refresh();
    }
    public void AcknowledgeCommit() => PendingCommit = "";
    public void Clear()
    {
        Native.RimeClearComposition(session); PendingCommit = ""; Refresh();
    }
    private void Refresh()
    {
        var commit = new Commit { DataSize = Marshal.SizeOf<Commit>() - 4 };
        if (Native.RimeGetCommit(session, ref commit) != 0)
        {
            try { PendingCommit += Marshal.PtrToStringUTF8(commit.Text) ?? ""; }
            finally { Native.RimeFreeCommit(ref commit); }
        }
        var context = new Context { DataSize = Marshal.SizeOf<Context>() - 4 };
        if (Native.RimeGetContext(session, ref context) == 0) { View = EngineView.Empty; return; }
        try
        {
            if (context.Menu.Count is < 0 or > 128) throw new InvalidDataException("Rime 候选数量无效，可能是 ABI 不匹配。");
            var list = new Candidate[context.Menu.Count];
            for (var i = 0; i < list.Length; i++)
            {
                var candidate = Marshal.PtrToStructure<NativeCandidate>(context.Menu.Candidates + i * Marshal.SizeOf<NativeCandidate>());
                list[i] = new(Marshal.PtrToStringUTF8(candidate.Text) ?? "", Marshal.PtrToStringUTF8(candidate.Comment) ?? "");
            }
            View = new(Marshal.PtrToStringUTF8(context.Composition.Preedit) ?? "", context.Menu.Page,
                context.Menu.LastPage != 0, context.Menu.Highlight, list);
        }
        finally { Native.RimeFreeContext(ref context); }
    }
    public void Dispose()
    {
        if (session != 0) { Native.RimeDestroySession(session); session = 0; }
        if (initialized) { Native.RimeFinalize(); initialized = false; }
        foreach (var p in strings) Marshal.FreeCoTaskMem(p);
        strings.Clear();
    }
    [StructLayout(LayoutKind.Sequential)] private struct Traits
    {
        public int DataSize;
        public nint Shared, User, Name, Code, Version, AppName, Modules;
        public int LogLevel;
        public nint LogDir, Prebuilt, Staging;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Composition { public int Length, Cursor, Start, End; public nint Preedit; }
    [StructLayout(LayoutKind.Sequential)] private struct Menu { public int Size, Page, LastPage, Highlight, Count; public nint Candidates, SelectKeys; }
    [StructLayout(LayoutKind.Sequential)] private struct Context { public int DataSize; public Composition Composition; public Menu Menu; public nint Preview, Labels; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeCandidate { public nint Text, Comment, Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Commit { public int DataSize; public nint Text; }
    private static class Native
    {
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void RimeSetup(ref Traits traits);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void RimeInitialize(ref Traits traits);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void RimeFinalize();
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern nuint RimeCreateSession();
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeDestroySession(nuint session);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeSelectSchema(nuint session, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void RimeSetOption(nuint session, [MarshalAs(UnmanagedType.LPUTF8Str)] string option, int value);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeProcessKey(nuint session, int key, int mask);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeSelectCandidateOnCurrentPage(nuint session, nuint index);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void RimeClearComposition(nuint session);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeGetContext(nuint session, ref Context context);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeFreeContext(ref Context context);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeGetCommit(nuint session, ref Commit commit);
        [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int RimeFreeCommit(ref Commit commit);
    }
}
