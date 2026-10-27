using AxisApp.ViewModels;

namespace AxisApp.Pages;

public partial class SettlementPage : ContentPage
{
    private readonly SettlementViewModel viewModel;

    public SettlementPage(SettlementViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override bool OnBackButtonPressed() => viewModel.IsBusy || base.OnBackButtonPressed();
}
