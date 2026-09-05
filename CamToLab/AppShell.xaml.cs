namespace MauiJohnWick1
{
    public partial class AppShell : Shell
    {
        public const string DisplayLabPreferenceKey = "DisplayLabValues";

        public AppShell()
        {
            InitializeComponent();
            // RGB is the default; the preference is only used after the user changes the switch.
            DisplayLabSwitch.IsToggled = Preferences.Get(DisplayLabPreferenceKey, false);
        }

        private void OnDisplayLabToggled(object? sender, ToggledEventArgs e)
        {
            // Keep the display preference synchronized with the main page.
            Preferences.Set(DisplayLabPreferenceKey, e.Value);
            if (CurrentPage is MainPage mainPage)
                mainPage.RefreshDisplayMode();
        }

        private async void OnPickImageClicked(object? sender, EventArgs e)
        {
            if (CurrentPage is MainPage mainPage)
            {
                await mainPage.PickImageAsync();
                FlyoutIsPresented = false;
            }
        }

        private async void OnLoadTestCardClicked(object? sender, EventArgs e)
        {
            if (CurrentPage is MainPage mainPage)
            {
                await mainPage.LoadTestCardAsync();
                FlyoutIsPresented = false;
            }
        }
    }
}
