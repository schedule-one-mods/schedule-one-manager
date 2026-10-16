using System.Windows;
namespace ScheduleI.Manager.Desktop;
public partial class App : Application
{
    private System.Threading.Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        instance = new System.Threading.Mutex(true, @"Local\ScheduleIModManager", out var created);
        if (!created) { MessageBox.Show("Schedule I Mod Manager is already open.", "Schedule I Mod Manager"); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
