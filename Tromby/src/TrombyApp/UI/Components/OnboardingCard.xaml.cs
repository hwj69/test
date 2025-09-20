using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tromby.UI.Components;

/// <summary>
/// Lightweight onboarding card displayed on first launch.
/// </summary>
public sealed partial class OnboardingCard : UserControl
{
    private bool _neverShow;

    /// <summary>
    /// Raised when the onboarding is dismissed.
    /// </summary>
    public event EventHandler<OnboardingDismissedEventArgs>? Dismissed;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public OnboardingCard()
    {
        InitializeComponent();
    }

    private void OnNeverShowChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            _neverShow = checkBox.IsChecked == true;
        }
    }

    private void OnConsentClick(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        Dismissed?.Invoke(this, new OnboardingDismissedEventArgs(_neverShow, true));
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        Visibility = Visibility.Collapsed;
        Dismissed?.Invoke(this, new OnboardingDismissedEventArgs(_neverShow, false));
    }
}

/// <summary>
/// Event payload for onboarding dismissal actions.
/// </summary>
public sealed class OnboardingDismissedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the event args.
    /// </summary>
    public OnboardingDismissedEventArgs(bool dontShowAgain, bool consentRequested)
    {
        DontShowAgain = dontShowAgain;
        ConsentRequested = consentRequested;
    }

    /// <summary>
    /// Gets a value indicating whether the onboarding should not reappear.
    /// </summary>
    public bool DontShowAgain { get; }

    /// <summary>
    /// Gets a value indicating whether the consent button was used.
    /// </summary>
    public bool ConsentRequested { get; }
}
