using System.Windows.Input;

namespace AxisApp.Controls;

public partial class PageHeaderBar : ContentView
{
    public static readonly BindableProperty HeaderTitleProperty =
        BindableProperty.Create(nameof(HeaderTitle), typeof(string), typeof(PageHeaderBar), string.Empty);

    public static readonly BindableProperty ShowOverflowProperty =
        BindableProperty.Create(nameof(ShowOverflow), typeof(bool), typeof(PageHeaderBar), false);

    public static readonly BindableProperty OverflowCommandProperty =
        BindableProperty.Create(nameof(OverflowCommand), typeof(ICommand), typeof(PageHeaderBar));

    public string HeaderTitle
    {
        get => (string)GetValue(HeaderTitleProperty);
        set => SetValue(HeaderTitleProperty, value);
    }

    public bool ShowOverflow
    {
        get => (bool)GetValue(ShowOverflowProperty);
        set => SetValue(ShowOverflowProperty, value);
    }

    public ICommand? OverflowCommand
    {
        get => (ICommand?)GetValue(OverflowCommandProperty);
        set => SetValue(OverflowCommandProperty, value);
    }

    public PageHeaderBar()
    {
        InitializeComponent();
    }

    private async void OnBackTapped(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
