using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace EnhancedSnap.Settings.Controls;

/// <summary>
/// A <see cref="SettingsCard"/> that acts as one big button. Use it instead of <c>IsClickEnabled="True"</c>, which leaves
/// screen readers and voice access with a button they can't press.
/// <para>
/// The toolkit's peer (8.2.251219) offers itself as the Invoke pattern of a clickable card without implementing
/// <see cref="IInvokeProvider"/>, so XAML answers every UI Automation Invoke with E_NOINTERFACE. Mouse and keyboard don't
/// go through it. <see cref="Microsoft.UI.Xaml.Controls.Primitives.ButtonBase"/> has no way to raise its Click from code,
/// so this card has its own <see cref="Click"/>, raised for input and for Invoke alike.
/// </para>
/// </summary>
public sealed partial class ClickableSettingsCard : SettingsCard
{
    public ClickableSettingsCard()
    {
        IsClickEnabled = true;
        base.Click += (_, e) => Click?.Invoke(this, e);
    }

    /// <summary>The card was activated by mouse, touch, keyboard, gamepad or UI Automation.</summary>
    public new event RoutedEventHandler? Click;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed partial class Peer(ClickableSettingsCard card) : SettingsCardAutomationPeer(card), IInvokeProvider
    {
        public void Invoke()
        {
            if (!IsEnabled())
            {
                throw new ElementNotEnabledException();
            }

            if (ListenerExists(AutomationEvents.InvokePatternOnInvoked))
            {
                RaiseAutomationEvent(AutomationEvents.InvokePatternOnInvoked);
            }

            card.Click?.Invoke(card, new RoutedEventArgs());
        }

        // Automation clients keep seeing the same kind of element as a plain card.
        protected override string GetClassNameCore() => nameof(SettingsCard);
    }
}
