using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Tromby.Infra;

/// <summary>
/// Manages taskbar progress and overlay icons for the Tromby overlay window.
/// </summary>
public sealed class TaskbarBadgeService : IDisposable
{
    private readonly nint _hwnd;
    private readonly ITaskbarList3 _taskbar;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskbarBadgeService"/> class.
    /// </summary>
    /// <param name="hwnd">Window handle owning the taskbar button.</param>
    public TaskbarBadgeService(nint hwnd)
    {
        _hwnd = hwnd;
        _taskbar = (ITaskbarList3)new CTaskbarList();
        _taskbar.HrInit();
    }

    /// <summary>
    /// Updates taskbar progress and overlay icon based on the provided mode and budget percentage.
    /// </summary>
    /// <param name="mode">Current quality mode.</param>
    /// <param name="percent">Budget usage percentage.</param>
    public void Update(QualityMode mode, int percent)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TaskbarBadgeService));
        }

        percent = Math.Clamp(percent, 0, 100);

        var flag = TBPFLAG.TBPF_NORMAL;
        if (percent >= 95)
        {
            flag = TBPFLAG.TBPF_ERROR;
        }
        else if (percent >= 80)
        {
            flag = TBPFLAG.TBPF_PAUSED;
        }

        _taskbar.SetProgressState(_hwnd, flag);
        _taskbar.SetProgressValue(_hwnd, (ulong)percent, 100);

        var overlay = CreateLetterIcon(mode == QualityMode.Qualite ? 'Q' : 'E');
        try
        {
            _taskbar.SetOverlayIcon(_hwnd, overlay, mode == QualityMode.Qualite ? "Qualité" : "Éco");
        }
        finally
        {
            if (overlay != 0)
            {
                PInvoke.DestroyIcon(new HICON(overlay));
            }
        }
    }

    /// <summary>
    /// Clears taskbar progress and overlay icon.
    /// </summary>
    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        _taskbar.SetProgressState(_hwnd, TBPFLAG.TBPF_NOPROGRESS);
        _taskbar.SetOverlayIcon(_hwnd, 0, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        Marshal.FinalReleaseComObject(_taskbar);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static nint CreateLetterIcon(char letter)
    {
        try
        {
            using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using var brush = new SolidBrush(letter == 'Q' ? Color.FromArgb(0, 120, 215) : Color.FromArgb(0, 153, 51));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using var font = new Font("Segoe UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.DrawString(letter.ToString(), font, brush, new RectangleF(0, 0, 32, 32), format);

            var iconHandle = bitmap.GetHicon();
            return iconHandle;
        }
        catch
        {
            return 0;
        }
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();
        void AddTab(nint hwnd);
        void DeleteTab(nint hwnd);
        void ActivateTab(nint hwnd);
        void SetActiveAlt(nint hwnd);
    }

    [ComImport]
    [Guid("602D4995-B13A-429b-A66E-1935E44F4317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList2 : ITaskbarList
    {
        new void HrInit();
        new void AddTab(nint hwnd);
        new void DeleteTab(nint hwnd);
        new void ActivateTab(nint hwnd);
        new void SetActiveAlt(nint hwnd);
        void MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3 : ITaskbarList2
    {
        new void HrInit();
        new void AddTab(nint hwnd);
        new void DeleteTab(nint hwnd);
        new void ActivateTab(nint hwnd);
        new void SetActiveAlt(nint hwnd);
        new void MarkFullscreenWindow(nint hwnd, bool fFullscreen);
        void SetProgressValue(nint hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(nint hwnd, TBPFLAG tbpFlags);
        void RegisterTab(nint hwndTab, nint hwndMDI);
        void UnregisterTab(nint hwndTab);
        void SetTabOrder(nint hwndTab, nint hwndInsertBefore);
        void SetTabActive(nint hwndTab, nint hwndMDI, uint dwReserved);
        void ThumbBarAddButtons(nint hwnd, uint cButtons, nint pButtons);
        void ThumbBarUpdateButtons(nint hwnd, uint cButtons, nint pButtons);
        void ThumbBarSetImageList(nint hwnd, nint himl);
        void SetOverlayIcon(nint hwnd, nint hIcon, [MarshalAs(UnmanagedType.LPWStr)] string? pszDescription);
        void SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(nint hwnd, nint prcClip);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private sealed class CTaskbarList
    {
    }

    private enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }
}
