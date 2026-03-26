using Microsoft.JSInterop;

namespace Intersect.Client.Web;

/// <summary>
/// Centralized debug logging for the web client.
/// Enable via browser console: IntersectDebug.enable()
/// Disable via browser console: IntersectDebug.disable()
/// All output goes to console.log so it can be filtered in browser devtools.
/// </summary>
public static class WebDebugLog
{
    /// <summary>
    /// When true, all Log() calls emit to the browser console.
    /// Toggled at runtime via JS interop (IntersectDebug.enable/disable).
    /// Default: true on first load so startup diagnostics are captured.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Log a debug message to the browser console (via Console.WriteLine → console.log).
    /// No-op when Enabled is false.
    /// </summary>
    public static void Log(string category, string message)
    {
        if (!Enabled) return;
        Console.WriteLine($"[WEB:{category}] {message}");
    }

    /// <summary>
    /// Always log, regardless of Enabled flag. For critical errors/warnings.
    /// </summary>
    public static void Warn(string category, string message)
    {
        Console.Error.WriteLine($"[WEB:{category}] WARNING: {message}");
    }

    [JSInvokable]
    public static void SetDebugLogging(bool enabled)
    {
        Enabled = enabled;
    }

    [JSInvokable]
    public static bool GetDebugLogging()
    {
        return Enabled;
    }
}
