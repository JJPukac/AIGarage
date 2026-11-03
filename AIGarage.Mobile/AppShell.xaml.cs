namespace AIGarage.Mobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage page)
    {
        InitializeComponent();
        GarageContent.Content = page;
    }
}
