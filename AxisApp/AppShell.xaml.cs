using AxisApp.Pages;

namespace AxisApp;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Login/Groups are top-level routes registered as ShellContent above; these three are
        // pushed detail pages (take query parameters), registered here instead.
        Routing.RegisterRoute(AppConstants.Routes.GroupDetails, typeof(GroupDetailPage));
        Routing.RegisterRoute(AppConstants.Routes.Members, typeof(MembersPage));
        Routing.RegisterRoute(AppConstants.Routes.AddExpense, typeof(AddExpensePage));
        Routing.RegisterRoute(AppConstants.Routes.RecurringExpenses, typeof(RecurringExpensesPage));
        Routing.RegisterRoute(AppConstants.Routes.JoinGroup, typeof(JoinGroupPage));
        Routing.RegisterRoute(AppConstants.Routes.NewGroup, typeof(NewGroupPage));
        Routing.RegisterRoute(AppConstants.Routes.Profile, typeof(ProfilePage));
    }
}
