using PhoneFolder.Desktop.Services;
using System.Windows;

namespace PhoneFolder.Desktop;

public partial class App : Application
{
    protected override async void OnExit(ExitEventArgs e)
    {
        await DefaultMediaSessionManager.DisposeAsync();
        base.OnExit(e);
    }
}
