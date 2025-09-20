using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Tromby.Core.Persona;
using Tromby.Core.Scheduling;

namespace Tromby.UI.ViewModels;

/// <summary>
/// Settings view model exposing configurable policies and scheduler parameters.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IPersonaManager _personaManager;
    private readonly IScheduler _scheduler;
    private readonly TimeSpan _defaultQualityStart;
    private readonly TimeSpan _defaultQualityEnd;
    private readonly int _defaultOverride;
    private readonly string _defaultMode;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public SettingsViewModel(IPersonaManager personaManager, IScheduler scheduler)
    {
        _personaManager = personaManager;
        _scheduler = scheduler;
        PersonaPresets = new ObservableCollection<string>(_personaManager.GetPresets());
        _defaultQualityStart = SchedulerReflection.GetTimeSpan(_scheduler, "QualityStart", TimeSpan.FromHours(8));
        _defaultQualityEnd = SchedulerReflection.GetTimeSpan(_scheduler, "QualityEnd", TimeSpan.FromHours(18));
        _defaultOverride = SchedulerReflection.GetInt32(_scheduler, "OverrideMinutes", 0);

        _qualityStart = _defaultQualityStart;
        _qualityEnd = _defaultQualityEnd;
        _overrideMinutes = _defaultOverride;
        _defaultMode = _scheduler.ActiveMode;
        _selectedMode = _defaultMode;
        _selectedPersona = PersonaPresets.FirstOrDefault() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_selectedPersona))
        {
            _personaManager.SetPersona(_selectedPersona);
        }

        ApplySchedulerCommand = new AsyncRelayCommand(ApplySchedulerAsync);
        ResetSchedulerCommand = new RelayCommand(ResetScheduler);
    }

    /// <summary>
    /// Gets the available persona presets.
    /// </summary>
    public ObservableCollection<string> PersonaPresets { get; }

    /// <summary>
    /// Gets available mode options.
    /// </summary>
    public string[] ModeOptions { get; } = { "Qualité", "Éco", "Auto" };

    /// <summary>
    /// Command to persist scheduler changes.
    /// </summary>
    public IAsyncRelayCommand ApplySchedulerCommand { get; }

    /// <summary>
    /// Command to reset scheduler to defaults.
    /// </summary>
    public IRelayCommand ResetSchedulerCommand { get; }

    [ObservableProperty]
    private string _selectedMode = "Qualité";

    [ObservableProperty]
    private TimeSpan _qualityStart;

    [ObservableProperty]
    private TimeSpan _qualityEnd;

    [ObservableProperty]
    private double _overrideMinutes;

    [ObservableProperty]
    private string _selectedPersona = string.Empty;

    partial void OnSelectedModeChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _scheduler.SwitchMode(value);
        }
    }

    partial void OnSelectedPersonaChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _personaManager.SetPersona(value);
        }
    }

    private async Task ApplySchedulerAsync()
    {
        SchedulerReflection.SetTimeSpan(_scheduler, "QualityStart", QualityStart);
        SchedulerReflection.SetTimeSpan(_scheduler, "QualityEnd", QualityEnd);
        SchedulerReflection.SetInt32(_scheduler, "OverrideMinutes", (int)Math.Clamp(OverrideMinutes, 0, int.MaxValue));
        _scheduler.SwitchMode(SelectedMode);
        WeakReferenceMessenger.Default.Send(new SchedulerUpdatedMessage());
        await Task.CompletedTask;
    }

    private void ResetScheduler()
    {
        QualityStart = _defaultQualityStart;
        QualityEnd = _defaultQualityEnd;
        OverrideMinutes = _defaultOverride;
        SelectedMode = _defaultMode;
    }
}

internal static class SchedulerReflection
{
    public static TimeSpan GetTimeSpan(IScheduler scheduler, string propertyName, TimeSpan fallback)
    {
        var property = scheduler.GetType().GetProperty(propertyName);
        if (property is not null && property.PropertyType == typeof(TimeSpan))
        {
            var value = property.GetValue(scheduler);
            if (value is TimeSpan timeSpan)
            {
                return timeSpan;
            }
        }

        return fallback;
    }

    public static void SetTimeSpan(IScheduler scheduler, string propertyName, TimeSpan value)
    {
        var property = scheduler.GetType().GetProperty(propertyName);
        if (property is not null && property.PropertyType == typeof(TimeSpan) && property.CanWrite)
        {
            property.SetValue(scheduler, value);
        }
    }

    public static int GetInt32(IScheduler scheduler, string propertyName, int fallback)
    {
        var property = scheduler.GetType().GetProperty(propertyName);
        if (property is not null && property.PropertyType == typeof(int))
        {
            var value = property.GetValue(scheduler);
            if (value is int number)
            {
                return number;
            }
        }

        return fallback;
    }

    public static void SetInt32(IScheduler scheduler, string propertyName, int value)
    {
        var property = scheduler.GetType().GetProperty(propertyName);
        if (property is not null && property.PropertyType == typeof(int) && property.CanWrite)
        {
            property.SetValue(scheduler, value);
        }
    }
}

internal sealed class SchedulerUpdatedMessage
{
}
