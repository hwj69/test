using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using Tromby.Infra;
using Tromby.UI.Components;
using Tromby.UI.ViewModels;

namespace Tromby.UI.Views;

/// <summary>
/// Primary overlay window using CompactOverlay presenter and handling global hotkeys.
/// </summary>
public sealed partial class ShellWindow : Window, IDisposable
{
    private readonly ShellViewModel _viewModel;
    private readonly ILogger<ShellWindow> _logger;
    private readonly MicaBackdrop _micaBackdrop = new();
    private WindowId _windowId;
    private AppWindow? _appWindow;
    private bool _disposed;
    private bool _isVisible = true;
    private TaskbarBadgeService? _taskbarBadge;

    private const int HOTKEY_TOGGLE_MODE = 0xB001;
    private const int HOTKEY_TOGGLE_VISIBILITY = 0xB002;
    private const int HOTKEY_DEMO_RESERVE = 0xB003;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(HWND hWnd, int id, HOT_KEY_MODIFIERS fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(HWND hWnd, int id);

    /// <summary>
    /// Creates the overlay shell window.
    /// </summary>
    public ShellViewModel ViewModel => _viewModel;

    public ShellWindow(ShellViewModel viewModel, ILogger<ShellWindow> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        DataContext = viewModel;
        Activated += OnActivated;
        Closed += OnClosed;
        AppWindow.SetIcon("Assets/Icons/tromby.ico");
        TryApplyBackdrop();
        InitializeCompactOverlay();
        WeakReferenceMessenger.Default.Register<SchedulerUpdatedMessage>(this, (_, __) =>
        {
            _viewModel.RefreshBadges();
            UpdateTaskbarBadge();
        });
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void TryApplyBackdrop()
    {
        try
        {
            SystemBackdrop = _micaBackdrop;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mica backdrop unavailable, falling back to default background.");
        }
    }

    private void InitializeCompactOverlay()
    {
        try
        {
            var window = GetAppWindow();
            if (window.Presenter.Kind != AppWindowPresenterKind.CompactOverlay)
            {
                window.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            }
            window.IsShownInSwitchers = false;
            if (window.Presenter is CompactOverlayPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
            }
            EnsureTopMost();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enable CompactOverlay presenter.");
        }
    }

    private AppWindow GetAppWindow()
    {
        if (_appWindow is not null)
        {
            return _appWindow;
        }

        _windowId = Win32Interop.GetWindowIdFromWindow(this.GetWindowHandle());
        _appWindow = AppWindow.GetFromWindowId(_windowId);
        return _appWindow;
    }

    private void EnsureTopMost()
    {
        var hwnd = new HWND(this.GetWindowHandle());
        PInvoke.SetWindowPos(hwnd, PInvoke.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.CodeActivated)
        {
            HwndExtensions.EnsureHook(this);
            RegisterGlobalHotkeys();
            EnsureTaskbarBadge();
            UpdateTaskbarBadge();
        }
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        Dispose();
    }

    private void RegisterGlobalHotkeys()
    {
        var hwnd = new HWND(this.GetWindowHandle());
        if (!RegisterHotKey(hwnd, HOTKEY_TOGGLE_MODE, HOT_KEY_MODIFIERS.MOD_ALT, (uint)VirtualKey.VK_Q))
        {
            _logger.LogWarning("Unable to register global hotkey Alt+Q.");
        }
        else
        {
            _logger.LogInformation("Global hotkey Alt+Q registered.");
        }

        if (!RegisterHotKey(hwnd, HOTKEY_TOGGLE_VISIBILITY, HOT_KEY_MODIFIERS.MOD_CONTROL, (uint)VirtualKey.VK_SPACE))
        {
            _logger.LogWarning("Unable to register global hotkey Ctrl+Space.");
        }
        else
        {
            _logger.LogInformation("Global hotkey Ctrl+Space registered.");
        }

        if (!RegisterHotKey(hwnd, HOTKEY_DEMO_RESERVE, HOT_KEY_MODIFIERS.MOD_ALT, (uint)VirtualKey.VK_D))
        {
            _logger.LogWarning("Unable to register global hotkey Alt+D.");
        }
        else
        {
            _logger.LogInformation("Global hotkey Alt+D registered.");
        }

        HwndExtensions.MessageReceived += OnWindowMessage;
    }

    private void OnWindowMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.MessageId != PInvoke.WM_HOTKEY)
        {
            return;
        }

        if (e.WParam == HOTKEY_TOGGLE_MODE)
        {
            _logger.LogInformation("Alt+Q pressed, toggling Qualité/Éco override.");
            _viewModel.ToggleModeOverride();
            UpdateTaskbarBadge();
        }
        else if (e.WParam == HOTKEY_TOGGLE_VISIBILITY)
        {
            _logger.LogInformation("Ctrl+Space pressed, toggling overlay visibility.");
            ToggleVisibility();
        }
        else if (e.WParam == HOTKEY_DEMO_RESERVE)
        {
            _logger.LogInformation("Alt+D pressed, executing demo budget reservation.");
            if (_viewModel.DemoReserveCommand.CanExecute(null))
            {
                _viewModel.DemoReserveCommand.Execute(null);
                UpdateTaskbarBadge();
            }
        }
    }

    private void ToggleVisibility()
    {
        try
        {
            var window = GetAppWindow();
            if (_isVisible)
            {
                window.Hide();
                _isVisible = false;
            }
            else
            {
                window.Show();
                _isVisible = true;
                EnsureTopMost();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle overlay visibility.");
        }
    }

    private void EnsureTaskbarBadge()
    {
        if (_taskbarBadge is not null)
        {
            return;
        }

        try
        {
            var handle = this.GetWindowHandle();
            if (handle != IntPtr.Zero)
            {
                _taskbarBadge = new TaskbarBadgeService(handle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Taskbar badge service unavailable.");
        }
    }

    private void UpdateTaskbarBadge()
    {
        if (_taskbarBadge is null)
        {
            return;
        }

        try
        {
            _taskbarBadge.Update(_viewModel.CurrentQualityMode, _viewModel.BudgetPercent);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to update taskbar badge state.");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.ModeBadge) or nameof(ShellViewModel.BudgetBadge) or nameof(ShellViewModel.BudgetPercent) or nameof(ShellViewModel.CurrentQualityMode))
        {
            UpdateTaskbarBadge();
        }
    }

    private void UnregisterGlobalHotkeys()
    {
        var hwnd = new HWND(this.GetWindowHandle());
        UnregisterHotKey(hwnd, HOTKEY_TOGGLE_MODE);
        UnregisterHotKey(hwnd, HOTKEY_TOGGLE_VISIBILITY);
        UnregisterHotKey(hwnd, HOTKEY_DEMO_RESERVE);
        HwndExtensions.MessageReceived -= OnWindowMessage;
    }

    private void OnOnboardingDismissed(object sender, OnboardingDismissedEventArgs e)
    {
        _viewModel.HandleOnboardingDismissal(e.DontShowAgain, e.ConsentRequested);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterGlobalHotkeys();
        _micaBackdrop.Dispose();
        Activated -= OnActivated;
        Closed -= OnClosed;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _taskbarBadge?.Dispose();
        _taskbarBadge = null;
        WeakReferenceMessenger.Default.Unregister<SchedulerUpdatedMessage>(this);
        GC.SuppressFinalize(this);
    }
}

namespace Tromby.UI.ViewModels
{
    public partial class ShellViewModel
    {
        public string ModeBadge
        {
            get
            {
                var now = DateTime.Now;
                var prefersQuality = !string.Equals(_scheduler.ActiveMode, "Éco", StringComparison.OrdinalIgnoreCase);
                var qualityActive = _scheduler.ComputeEffectiveMode(prefersQuality, _scheduler.QualityStart, _scheduler.QualityEnd, now);
                var label = qualityActive ? "Qualité" : "Éco";
                if (string.Equals(_scheduler.ActiveMode, "Auto", StringComparison.OrdinalIgnoreCase))
                {
                    label += " (Auto)";
                }

                if (_scheduler.OverrideMsRemaining() > 0)
                {
                    label += " • Override";
                }

                return label;
            }
        }

        public string BudgetBadge => BuildBudgetBadge();

        public int BudgetPercent => _lastBudgetSnapshot?.DayPercent ?? 0;

        public QualityMode CurrentQualityMode => _lastBudgetMode;

        public void ToggleModeOverride()
        {
            var remaining = _scheduler.OverrideMsRemaining();
            if (remaining > 0)
            {
                _scheduler.ManualOverride(0);
                _logger.LogInformation("Manual override cleared.");
            }
            else
            {
                var minutes = _scheduler.OverrideMinutes > 0 ? _scheduler.OverrideMinutes : 5;
                _scheduler.ManualOverride(minutes);
                _logger.LogInformation("Manual override engaged for {Minutes} minutes.", minutes);
            }

            RefreshBadges();
        }

        public void HandleOnboardingDismissal(bool dontShowAgain, bool consentRequested)
        {
            ShowOnboarding = false;
            if (dontShowAgain)
            {
                _logger.LogInformation("Onboarding dismissed with 'do not show again'.");
            }

            if (consentRequested)
            {
                var policyHash = _policyManager.CurrentPolicyHash;
                _consentService.StoreConsentAsync(policyHash, _cts.Token).FireAndForget(_logger);
                _logger.LogInformation("Consent stored for policy hash {Hash}.", policyHash);
            }
        }

        public void RefreshBadges()
        {
            ActiveMode = _scheduler.ActiveMode;
            UpdateBudgetBadge();
            OnPropertyChanged(nameof(ModeBadge));
            OnPropertyChanged(nameof(BudgetBadge));
            OnPropertyChanged(nameof(BudgetPercent));
            OnPropertyChanged(nameof(CurrentQualityMode));
        }
    }
}
