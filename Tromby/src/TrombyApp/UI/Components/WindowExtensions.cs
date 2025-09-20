using System;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tromby.UI.Components;

/// <summary>
/// Extension helpers for WinUI <see cref="Window"/> interop.
/// </summary>
public static class WindowExtensions
{
    /// <summary>
    /// Gets the native handle for a WinUI window.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <returns>Native HWND.</returns>
    public static IntPtr GetWindowHandle(this Window window)
    {
        return WindowNative.GetWindowHandle(window);
    }
}

/// <summary>
/// Provides an event bridge for receiving window messages.
/// </summary>
public static class HwndExtensions
{
    /// <summary>
    /// Raised when a window message is received for the overlay handle.
    /// </summary>
    public static event EventHandler<WindowMessageEventArgs>? MessageReceived;

    private static bool _initialized;

    /// <summary>
    /// Initializes message hook for a given window handle.
    /// </summary>
    /// <param name="window">WinUI window to monitor.</param>
    public static void EnsureHook(Window window)
    {
        if (_initialized)
        {
            return;
        }

        var hwnd = new HWND(window.GetWindowHandle());
        WindowMessageMonitor.Start(hwnd, (msg) => MessageReceived?.Invoke(window, msg));
        _initialized = true;
    }
}

/// <summary>
/// Event arguments for window messages.
/// </summary>
public sealed class WindowMessageEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public WindowMessageEventArgs(uint messageId, int wParam, int lParam)
    {
        MessageId = messageId;
        WParam = wParam;
        LParam = lParam;
    }

    /// <summary>
    /// Gets the Windows message ID.
    /// </summary>
    public uint MessageId { get; }

    /// <summary>
    /// Gets the WParam payload.
    /// </summary>
    public int WParam { get; }

    /// <summary>
    /// Gets the LParam payload.
    /// </summary>
    public int LParam { get; }
}

internal static class WindowMessageMonitor
{
    /// <summary>
    /// Starts listening for window messages on the provided handle.
    /// </summary>
    public static void Start(HWND hwnd, Action<WindowMessageEventArgs> callback)
    {
        // TODO: Implement message hook using SubclassWndProc and ensure thread-safety.
    }
}
