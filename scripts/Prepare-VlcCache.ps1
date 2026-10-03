[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$NativeDirectory,
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'VLC x64 cache generation requires 64-bit PowerShell.' }
$taskNative = [IO.Path]::GetFullPath($NativeDirectory)
foreach ($taskName in @('libvlc.dll', 'libvlccore.dll', 'plugins')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskNative $taskName))) { throw "Missing VLC publish file: $taskName" }
}
for ($taskAncestor = $taskNative; $taskAncestor; $taskAncestor = [IO.Path]::GetDirectoryName($taskAncestor)) {
    if ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "VLC cache directory contains a link: $taskAncestor"
    }
}
$taskCache = Join-Path $taskNative 'plugins/plugins.dat'
if ($VerifyOnly -and -not (Test-Path -LiteralPath $taskCache -PathType Leaf)) { throw 'plugins.dat is missing for verification.' }

if (-not ('NexusVlcCacheNative' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
public static class NexusVlcCacheNative
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr library, string name);
    [DllImport("kernel32")] private static extern bool FreeLibrary(IntPtr library);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Create(int count, IntPtr options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreatePlayer(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Release(IntPtr instance);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Version();
    private static T Export<T>(IntPtr library, string name) where T : class
    {
        var pointer = GetProcAddress(library, name);
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("Missing VLC export: " + name);
        return Marshal.GetDelegateForFunctionPointer(pointer, typeof(T)) as T;
    }
    public sealed class Result
    {
        public string NativeVersion;
        public double LibVlcMs;
        public double PlayerMs;
        public int LoadedPluginLibraries;
        public bool MmDeviceLoaded;
    }
    public static Result Run(string directory, bool generate)
    {
        IntPtr core = IntPtr.Zero, library = IntPtr.Zero, instance = IntPtr.Zero, player = IntPtr.Zero, argv = IntPtr.Zero;
        string[] options = generate
            ? new[] { "--no-osd", "--aout=mmdevice", "--mmdevice-backend=wasapi", "--audio-resampler=speex_resampler", "--reset-plugins-cache" }
            : new[] { "--no-osd", "--aout=mmdevice", "--mmdevice-backend=wasapi", "--audio-resampler=speex_resampler" };
        var strings = new IntPtr[options.Length];
        Release releaseInstance = null, releasePlayer = null;
        try
        {
            // Resolve dependencies in this native directory and Windows System32.
            core = LoadLibraryEx(Path.Combine(directory, "libvlccore.dll"), IntPtr.Zero, 0x100 | 0x800);
            library = LoadLibraryEx(Path.Combine(directory, "libvlc.dll"), IntPtr.Zero, 0x100 | 0x800);
            if (core == IntPtr.Zero || library == IntPtr.Zero) throw new InvalidOperationException("Cannot load VLC native libraries: " + Marshal.GetLastWin32Error());
            var create = Export<Create>(library, "libvlc_new");
            var createPlayer = Export<CreatePlayer>(library, "libvlc_media_player_new");
            releaseInstance = Export<Release>(library, "libvlc_release");
            releasePlayer = Export<Release>(library, "libvlc_media_player_release");
            argv = Marshal.AllocHGlobal(IntPtr.Size * options.Length);
            for (int i = 0; i < options.Length; i++)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(options[i] + "\0");
                strings[i] = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, strings[i], bytes.Length);
                Marshal.WriteIntPtr(argv, i * IntPtr.Size, strings[i]);
            }
            var watch = Stopwatch.StartNew();
            instance = create(options.Length, argv);
            if (instance == IntPtr.Zero) throw new InvalidOperationException("VLC cache initialization failed.");
            var result = new Result { LibVlcMs = watch.Elapsed.TotalMilliseconds };
            watch.Restart();
            player = createPlayer(instance);
            if (player == IntPtr.Zero) throw new InvalidOperationException("VLC player initialization failed.");
            result.PlayerMs = watch.Elapsed.TotalMilliseconds;
            result.NativeVersion = Marshal.PtrToStringAnsi(Export<Version>(library, "libvlc_get_version")());
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                    if (module.FileName.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && module.ModuleName.EndsWith("_plugin.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        result.LoadedPluginLibraries++;
                        if (module.ModuleName.Equals("libmmdevice_plugin.dll", StringComparison.OrdinalIgnoreCase)) result.MmDeviceLoaded = true;
                    }
            return result;
        }
        finally
        {
            if (player != IntPtr.Zero) releasePlayer(player);
            if (instance != IntPtr.Zero) releaseInstance(instance);
            foreach (var text in strings) if (text != IntPtr.Zero) Marshal.FreeHGlobal(text);
            if (argv != IntPtr.Zero) Marshal.FreeHGlobal(argv);
            if (library != IntPtr.Zero) FreeLibrary(library);
            if (core != IntPtr.Zero) FreeLibrary(core);
        }
    }
}
'@
}
$taskPreviousPluginPath = $env:VLC_PLUGIN_PATH
try {
    # Limit cache generation to this publish directory; never reset another VLC installation's cache.
    $env:VLC_PLUGIN_PATH = $null
    $taskResult = [NexusVlcCacheNative]::Run($taskNative.TrimEnd('\', '/'), -not $VerifyOnly)
}
finally { $env:VLC_PLUGIN_PATH = $taskPreviousPluginPath }
if (-not (Test-Path -LiteralPath $taskCache -PathType Leaf) -or (Get-Item -LiteralPath $taskCache).Length -eq 0) {
    throw 'VLC did not generate a nonempty plugins.dat. Publishing stopped.'
}
if ($VerifyOnly -and ($taskResult.LoadedPluginLibraries -gt 10 -or -not $taskResult.MmDeviceLoaded)) {
    throw "VLC still loaded $($taskResult.LoadedPluginLibraries) plugin libraries. Cache verification failed."
}
[pscustomobject]@{
    cache = $taskCache; cacheBytes = (Get-Item -LiteralPath $taskCache).Length;
    generated = -not $VerifyOnly; nativeVersion = $taskResult.NativeVersion;
    libvlcMs = [Math]::Round($taskResult.LibVlcMs, 2); playerMs = [Math]::Round($taskResult.PlayerMs, 2);
    loadedPluginLibraries = $taskResult.LoadedPluginLibraries; mmDeviceLoaded = $taskResult.MmDeviceLoaded
} | ConvertTo-Json
