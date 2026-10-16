using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScheduleI.Manager;

namespace ScheduleI.Manager.Desktop;

internal sealed class LogWindow : Window
{
    private readonly ComboBox files = new() { MinWidth = 200, DisplayMemberPath = "Name" };
    private readonly TextBox text = new()
    {
        IsReadOnly = true, AcceptsReturn = true, AcceptsTab = true,
        FontFamily = new FontFamily("Consolas"), FontSize = 12,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top
    };
    private readonly TextBox location = new() { IsReadOnly = true, BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap };
    private readonly string primary;

    public LogWindow(Window owner, string path, bool related)
    {
        Owner = owner; primary = path;
        Title = "Setup and installer logs"; Width = 1000; Height = 650;
        MinWidth = 600; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = owner.Background; Foreground = owner.Foreground; Resources = owner.Resources;
        var panel = new DockPanel { Margin = new Thickness(14) };
        var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var refresh = new Button { Content = "Refresh", Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(refresh, Dock.Right); controls.Children.Add(refresh); controls.Children.Add(files);
        DockPanel.SetDock(controls, Dock.Top); panel.Children.Add(controls);
        DockPanel.SetDock(location, Dock.Bottom); panel.Children.Add(location); panel.Children.Add(text); Content = panel;
        void LoadFiles()
        {
            var selected = (files.SelectedValue as string) ?? primary;
            var paths = related ? SetupLogs.Related(primary) : new[] { primary };
            files.SelectedValuePath = "Path";
            files.ItemsSource = paths.Select(p => new { Name = Path.GetFileName(p), Path = p }).ToArray();
            files.SelectedValue = paths.Contains(selected) ? selected : primary;
        }
        void ReadSelected()
        {
            if (files.SelectedValue is not string selected) return;
            location.Text = selected;
            try { text.Text = SetupLogs.Read(selected); }
            catch (Exception exception) { text.Text = "Could not read this log: " + exception.Message; }
        }
        files.SelectionChanged += (_, _) => ReadSelected();
        refresh.Click += (_, _) => { LoadFiles(); ReadSelected(); };
        LoadFiles(); ReadSelected();
    }
}
