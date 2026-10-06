using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NexusExplorer.Infrastructure.Playback.Mpv;

internal static class MpvNative
{
    private const string Library = "nexus-libmpv";
    private static IntPtr _library;
    static MpvNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, (name, assembly, search) =>
        {
            if (name != Library) return IntPtr.Zero;
            var path = Path.Combine(AppContext.BaseDirectory, "native", "mpv", "win-x64", "libmpv-2.dll");
            if (!File.Exists(path)) throw new DllNotFoundException("mpv 原生库缺失：" + path);
            // One pinned library for the process. Never unload it while callbacks/core threads exist.
            return _library != IntPtr.Zero ? _library : _library = NativeLibrary.Load(path);
        });
    }
    internal enum Format { None, String, OsdString, Flag, Int64, Double, Node, Array, Map, ByteArray }
    internal enum EventId
    {
        None=0, Shutdown=1, LogMessage=2, SetPropertyReply=4, CommandReply=5,
        StartFile=6, EndFile=7, FileLoaded=8, ClientMessage=16, Seek=20,
        PlaybackRestart=21, PropertyChange=22, QueueOverflow=24, Hook=25
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Event { public int Id, Error; public ulong UserData; public IntPtr Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct Property { public IntPtr Name; public Format Format; public IntPtr Data; }
    [StructLayout(LayoutKind.Explicit)] internal struct Value
    {
        [FieldOffset(0)] public IntPtr Pointer;
        [FieldOffset(0)] public long Integer;
        [FieldOffset(0)] public double Number;
        [FieldOffset(0)] public int Flag;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Node { public Value Value; public Format Format; }
    [StructLayout(LayoutKind.Sequential)] internal struct NodeList { public int Count; public IntPtr Values, Keys; }
    [StructLayout(LayoutKind.Sequential)] internal struct EndFile { public int Reason, Error; public long EntryId, InsertId; public int InsertCount; }
    [StructLayout(LayoutKind.Sequential)] internal struct LogMessage { public IntPtr Prefix, Level, Text; public int LogLevel; }
    [StructLayout(LayoutKind.Sequential)] internal struct ClientMessage { public int Count; public IntPtr Args; }
    internal static string Text(IntPtr pointer) => pointer == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pointer) ?? "";
    internal static object? CopyNode(Node node, int depth = 0)
    {
        if (depth > 16) throw new InvalidDataException("mpv 属性层级过深。");
        switch (node.Format)
        {
            case Format.None: return null;
            case Format.String: return Text(node.Value.Pointer);
            case Format.Flag: return node.Value.Flag != 0;
            case Format.Int64: return node.Value.Integer;
            case Format.Double: return node.Value.Number;
            case Format.Array:
            case Format.Map:
                if (node.Value.Pointer == IntPtr.Zero) return null;
                var list = Marshal.PtrToStructure<NodeList>(node.Value.Pointer);
                if (list.Count < 0 || list.Count > 100000 || (list.Count > 0 && list.Values == IntPtr.Zero)) throw new InvalidDataException("mpv 属性数组无效。");
                var values = new List<object?>(list.Count);
                var map = new Dictionary<string, object?>();
                for (var i = 0; i < list.Count; i++)
                {
                    var value = CopyNode(Marshal.PtrToStructure<Node>(list.Values + i * Marshal.SizeOf<Node>()), depth + 1);
                    if (node.Format == Format.Map)
                    { if (list.Keys == IntPtr.Zero) throw new InvalidDataException("mpv 属性映射无效。"); map[Text(Marshal.ReadIntPtr(list.Keys, i * IntPtr.Size))] = value; }
                    else values.Add(value);
                }
                return node.Format == Format.Map ? map : values;
            default: return null;
        }
    }
    internal static object? CopyProperty(Property property) => property.Data == IntPtr.Zero ? null : property.Format switch
    {
        Format.String => Text(Marshal.ReadIntPtr(property.Data)),
        Format.Flag => Marshal.ReadInt32(property.Data) != 0,
        Format.Int64 => Marshal.ReadInt64(property.Data),
        Format.Double => Marshal.PtrToStructure<double>(property.Data),
        Format.Node => CopyNode(Marshal.PtrToStructure<Node>(property.Data)),
        _ => null
    };
    internal sealed class Arguments : IDisposable
    {
        private readonly List<IntPtr> _strings = new();
        public IntPtr Pointer { get; }
        internal Arguments(params string[] values)
        {
            Pointer = Marshal.AllocHGlobal((values.Length + 1) * IntPtr.Size);
            for (var i = 0; i < values.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(values[i] + "\0");
                var value = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, value, bytes.Length); _strings.Add(value);
                Marshal.WriteIntPtr(Pointer, i * IntPtr.Size, value);
            }
            Marshal.WriteIntPtr(Pointer, values.Length * IntPtr.Size, IntPtr.Zero);
        }
        public void Dispose() { foreach (var value in _strings) Marshal.FreeHGlobal(value); Marshal.FreeHGlobal(Pointer); }
    }
    internal static string? GetString(IntPtr handle, string name)
    {
        var value = GetPropertyString(handle, name);
        if (value == IntPtr.Zero) return null;
        try { return Text(value); } finally { Free(value); }
    }
    internal static void Check(int result, string operation) { if (result < 0) throw new InvalidOperationException($"mpv {operation}: {Text(ErrorString(result))} ({result})"); }
    [DllImport(Library, EntryPoint="mpv_create", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Create();
    [DllImport(Library, EntryPoint="mpv_initialize", CallingConvention=CallingConvention.Cdecl)] internal static extern int Initialize(IntPtr handle);
    [DllImport(Library, EntryPoint="mpv_client_api_version", CallingConvention=CallingConvention.Cdecl)] internal static extern uint ApiVersion();
    [DllImport(Library, EntryPoint="mpv_set_option_string", CallingConvention=CallingConvention.Cdecl)] internal static extern int SetOption(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, EntryPoint="mpv_command_async", CallingConvention=CallingConvention.Cdecl)] internal static extern int CommandAsync(IntPtr handle, ulong id, IntPtr arguments);
    [DllImport(Library, EntryPoint="mpv_get_property_string", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr GetPropertyString(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library, EntryPoint="mpv_observe_property", CallingConvention=CallingConvention.Cdecl)] internal static extern int Observe(IntPtr handle, ulong id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, Format format);
    [DllImport(Library, EntryPoint="mpv_unobserve_property", CallingConvention=CallingConvention.Cdecl)] internal static extern int Unobserve(IntPtr handle, ulong id);
    [DllImport(Library, EntryPoint="mpv_wait_event", CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr WaitEvent(IntPtr handle, double timeout);
    [DllImport(Library, EntryPoint="mpv_wakeup", CallingConvention=CallingConvention.Cdecl)] internal static extern void Wakeup(IntPtr handle);
    [DllImport(Library, EntryPoint="mpv_request_log_messages", CallingConvention=CallingConvention.Cdecl)] internal static extern int RequestLogs(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string level);
    [DllImport(Library, EntryPoint="mpv_terminate_destroy", CallingConvention=CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr handle);
    [DllImport(Library, EntryPoint="mpv_free", CallingConvention=CallingConvention.Cdecl)] private static extern void Free(IntPtr value);
    [DllImport(Library, EntryPoint="mpv_error_string", CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr ErrorString(int error);
}
