using System.Reflection;
using System.Runtime.InteropServices;

namespace KHost.Screen.OpenAl;

/// <summary>
/// Minimal P/Invoke wrapper around an OpenAL library.
/// Tries bundled OpenAL Soft first (from the Silk.NET.OpenAL.Soft.Native package),
/// then falls back to system-installed libraries:
///   Windows : soft_oal.dll → openal32.dll
///   macOS   : libopenal.dylib → OpenAL.framework
///   Linux   : libopenal.so → libopenal.so.1
/// When no library is found, <see cref="IsLoaded"/> is false and callers
/// must not invoke any imported method.
/// </summary>
internal static class OpenAlNative
{
    private const string Lib = "KHost_OpenAL"; // dummy name resolved at runtime
    internal const int AL_FORMAT_MONO16 = 0x1101;
    internal const int AL_FORMAT_STEREO16 = 0x1103;
    internal const int AL_GAIN = 0x100A;
    internal const int AL_SOURCE_STATE = 0x1010;
    internal const int AL_PLAYING = 0x1012;
    internal const int AL_BUFFERS_QUEUED = 0x1015;
    internal const int AL_BUFFERS_PROCESSED = 0x1016;

    /// <summary>True when an OpenAL library was loaded successfully.</summary>
    internal static readonly bool IsLoaded;

    /// <summary>
    /// The exception thrown during static initialization, if any. Captured here so the
    /// first instance with an injected logger can surface it.
    /// </summary>
    internal static readonly Exception? LoadException;

    // The handle to the loaded library, used by the resolver.
    private static IntPtr _libHandle;

    static OpenAlNative()
    {
        try
        {
            var asm = typeof(OpenAlNative).Assembly;
            _libHandle = TryLoadAny(asm);
            if (_libHandle == IntPtr.Zero)
                return; // IsLoaded stays false

            NativeLibrary.SetDllImportResolver(asm, Resolve);
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            LoadException = ex;
            IsLoaded = false;
        }
    }

    /// <summary>
    /// Tries each candidate library name in order, returning the first that loads.
    /// </summary>
    private static IntPtr TryLoadAny(Assembly asm)
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? ["soft_oal", "openal32"]
            : OperatingSystem.IsMacOS()
                ? ["libopenal", "/System/Library/Frameworks/OpenAL.framework/OpenAL"]
                : ["libopenal", "libopenal.so.1"];

        foreach (string name in candidates)
        {
            if (NativeLibrary.TryLoad(name, asm, null, out IntPtr handle))
                return handle;
        }
        return IntPtr.Zero;
    }

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? path) =>
        name == Lib ? _libHandle : IntPtr.Zero;

    [DllImport(Lib, EntryPoint = "alcOpenDevice")]
    internal static extern IntPtr alcOpenDevice(string? name);

    [DllImport(Lib, EntryPoint = "alcCreateContext")]
    internal static extern IntPtr alcCreateContext(IntPtr device, IntPtr attrs);

    [DllImport(Lib, EntryPoint = "alcMakeContextCurrent")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool alcMakeContextCurrent(IntPtr ctx);

    [DllImport(Lib, EntryPoint = "alcDestroyContext")]
    internal static extern void alcDestroyContext(IntPtr ctx);

    [DllImport(Lib, EntryPoint = "alcCloseDevice")]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool alcCloseDevice(IntPtr device);

    [DllImport(Lib, EntryPoint = "alGenSources")]
    internal static extern void alGenSources(int n, out int source);

    [DllImport(Lib, EntryPoint = "alDeleteSources")]
    internal static extern void alDeleteSources(int n, ref int source);

    [DllImport(Lib, EntryPoint = "alGenBuffers")]
    internal static extern void alGenBuffers(int n, [Out] int[] bufs);

    [DllImport(Lib, EntryPoint = "alDeleteBuffers")]
    internal static extern void alDeleteBuffers(int n, [In] int[] bufs);

    [DllImport(Lib, EntryPoint = "alBufferData")]
    internal static extern void alBufferData(int bid, int fmt, byte[] data, int size, int freq);

    [DllImport(Lib, EntryPoint = "alSourceQueueBuffers")]
    internal static extern void alSourceQueueBuffers(int sid, int n, ref int bid);

    [DllImport(Lib, EntryPoint = "alSourceUnqueueBuffers")]
    internal static extern void alSourceUnqueueBuffers(int sid, int n, out int bid);

    [DllImport(Lib, EntryPoint = "alSourcePlay")]
    internal static extern void alSourcePlay(int sid);

    [DllImport(Lib, EntryPoint = "alSourceStop")]
    internal static extern void alSourceStop(int sid);

    [DllImport(Lib, EntryPoint = "alSourcef")]
    internal static extern void alSourcef(int sid, int param, float val);

    [DllImport(Lib, EntryPoint = "alGetSourcei")]
    internal static extern void alGetSourcei(int sid, int param, out int val);
}
