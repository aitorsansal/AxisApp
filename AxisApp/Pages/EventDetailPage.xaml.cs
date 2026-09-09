using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class EventDetailPage : ContentPage
{
    public EventDetailPage(EventDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
