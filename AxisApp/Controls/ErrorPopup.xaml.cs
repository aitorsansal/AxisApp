namespace AxisApp.Controls;

/// <summary>A real modal overlay for BaseViewModel.ErrorMessage, replacing the plain red-Label
/// pattern every page used before — that label was easy to miss (it rendered wherever it happened
/// to fit on each page's layout, not necessarily near whatever action triggered it). Deliberately a
/// hand-built scrim + card, not CommunityToolkit.Maui's Popup control — that package's Close()/
/// Page.ShowPopupAsync() API doesn't exist in the version this app is pinned to (13.0.0), the same
/// reason BaseViewModel's remarks give for not using it in the first place. This mirrors
/// AddExpensePage's existing receipt-preview overlay (scrim BoxView + centered Border) instead.
///
/// Message is TwoWay by default so a page only has to write Message="{Binding ErrorMessage}" —
/// dismissing (scrim tap or OK) writes "" straight back onto the ViewModel's ErrorMessage, the same
/// value RunSafeAsync itself resets to at the start of every command.</summary>
public partial class ErrorPopup : ContentView
{
    public static readonly BindableProperty MessageProperty = BindableProperty.Create(
        nameof(Message), typeof(string), typeof(ErrorPopup), string.Empty, BindingMode.TwoWay);

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ErrorPopup()
    {
        InitializeComponent();
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => Dismiss();

    private void OnDismissClicked(object? sender, EventArgs e) => Dismiss();

    private void Dismiss() => Message = string.Empty;
}
