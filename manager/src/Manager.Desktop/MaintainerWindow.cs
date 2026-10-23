using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScheduleI.Manager;

namespace ScheduleI.Manager.Desktop;

public partial class MainWindow
{
    private RegistryReview? review;
    private ReviewEvidence? evidence;
    private readonly TextBox reviewUrl = new() { MinWidth = 320 };
    private readonly TextBox observationSeconds = new() { Text = "90", Width = 70 };
    private readonly CheckBox unverifiedVersion = new() { Content = "Allow testing an unverified game version", Foreground = Brushes.White, Margin = new(0, 12, 0, 12) };
    private readonly TextBlock reviewStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGoldenrodYellow };
    private readonly TextBlock reviewBanner = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGoldenrodYellow };
    private readonly Button cancelSuite = new() { Content = "Cancel launch suite", Visibility = Visibility.Collapsed };
    private CancellationTokenSource? suiteCancellation;
    private static string ActiveReviewPath => Path.Combine(ManagerPaths.Root, "active-review.json");

    private void RestoreMaintainerReview()
    {
        if (!File.Exists(ActiveReviewPath)) return;
        var saved = JsonFiles.Read<RegistryReview>(ActiveReviewPath);
        var restored = RegistryClient.ForReview(saved);
        registry.Dispose(); registry = restored; review = saved;
        evidence = new ReviewEvidence(saved);
        evidence.Record("Pinned review resumed");
        reviewUrl.Text = saved.PullRequestUrl;
        LibraryPath.IsReadOnly = true;
        ShowReviewDetails();
    }
    private void ShowReviewDetails()
    {
        if (review == null || evidence == null) return;
        reviewBanner.Text = $"MAINTAINER · PR #{review.Number} · {review.Commit} · version override {(MaintainerCompatibility.AllowUnverifiedGameVersion ? "ON — UNVERIFIED" : "OFF")}";
        reviewStatus.Text = $"Source: {review.Repository}\nCommit: {review.Commit}\nIndex SHA-256: {evidence.IndexSha256 ?? "not fetched in this session"}\nEvidence: {evidence.DirectoryPath}";
    }

    private void InitializeMaintainer()
    {
        Title = "Schedule I Mod Manager — Maintainer";
        ((TextBlock)((StackPanel)VersionLabel.Parent).Children[0]).Text = "MAINTAINER EDITION";
        LibraryPath.IsReadOnly = true;
        foreach (var button in ((DockPanel)LibraryPath.Parent).Children.OfType<Button>()) button.IsEnabled = false;
        var card = new Border { Style = (Style)FindResource("Card") };
        var panel = new StackPanel();
        card.Child = panel;
        panel.Children.Add(new TextBlock { Text = "Registry PR review", FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Paste a registry PR URL. Its current head commit is pinned; reopen the URL to review subsequent changes.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new(0, 8, 0, 12) });
        System.Windows.Automation.AutomationProperties.SetName(reviewUrl, "Registry pull request URL");
        panel.Children.Add(reviewUrl);
        var actions = new WrapPanel { Margin = new(0, 12, 0, 12) };
        var load = new Button { Content = "Open PR review" };
        load.Click += OpenReview;
        actions.Children.Add(load);
        var export = new Button { Content = "Open evidence folder" };
        export.Click += (_, _) => { if (evidence != null) Open(evidence.DirectoryPath); else StatusText.Text = "Open a PR review first."; };
        actions.Children.Add(export);
        var capture = new Button { Content = "Capture loader log" };
        capture.Click += async (_, _) => await Run(() =>
        {
            if (evidence == null || game == null) throw new IOException("Open a review and select a game first.");
            evidence.Record("Manual loader log capture", new { Game = game, LoaderLog = evidence.CaptureLoaderLog(game, "manual") });
        }, "Loader log captured in the evidence folder.");
        actions.Children.Add(capture);
        panel.Children.Add(actions);
        var manualNote = new TextBox { AcceptsReturn = true, MinHeight = 55, TextWrapping = TextWrapping.Wrap, ToolTip = "Manual gameplay result or combination tested" };
        System.Windows.Automation.AutomationProperties.SetName(manualNote, "Manual review note");
        panel.Children.Add(manualNote);
        var saveNote = new Button { Content = "Save manual note", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0, 8, 0, 12) };
        saveNote.Click += async (_, _) => await Run(() =>
        {
            if (evidence == null || string.IsNullOrWhiteSpace(manualNote.Text)) throw new IOException("Open a PR review and enter a note first.");
            evidence.Record("Manual review note", new { Game = game, Note = manualNote.Text });
            manualNote.Clear();
        }, "Manual note saved in the evidence report.");
        panel.Children.Add(saveNote);
        panel.Children.Add(reviewStatus);
        unverifiedVersion.Checked += ChangeVersionOverride;
        unverifiedVersion.Unchecked += ChangeVersionOverride;
        panel.Children.Add(unverifiedVersion);
        panel.Children.Add(new TextBlock { Text = "This bypasses only the game version claim. Backend, loader, runtime, checksum and file ownership checks still apply. Test runs use the selected game and its saves.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var suite = new WrapPanel { Margin = new(0, 14, 0, 8) };
        suite.Children.Add(new TextBlock { Text = "Observe each launch (seconds): ", VerticalAlignment = VerticalAlignment.Center });
        suite.Children.Add(observationSeconds);
        var runSuite = new Button { Content = "Run launch suite", Margin = new(10, 0, 0, 0) };
        runSuite.Click += RunLaunchSuite;
        suite.Children.Add(runSuite);
        panel.Children.Add(suite);
        panel.Children.Add(new TextBlock { Text = "Tests all downloaded mods: no mods, each alone, then cumulative sets. Each game process is closed after observation (forced if needed); original enabled mods are restored. Startup evidence does not certify gameplay or all possible combinations.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        ((StackPanel)SettingsPage.Content).Children.Insert(0, card);
        ((StackPanel)RegistryBanner.Parent).Children.Insert(0, new Border { Background = new SolidColorBrush(Color.FromRgb(59, 51, 34)), CornerRadius = new(8), Padding = new(12), Margin = new(0, 0, 0, 12), Child = reviewBanner });
        ((StackPanel)CancelSetupButton.Parent).Children.Add(cancelSuite);
        cancelSuite.Click += (_, _) => { suiteCancellation?.Cancel(); cancelSuite.IsEnabled = false; StatusText.Text = "Stopping the test process and restoring enabled mods…"; };
        Closing += (_, args) =>
        {
            if (suiteCancellation == null) return;
            args.Cancel = true;
            suiteCancellation.Cancel();
            StatusText.Text = "Wait for the launch suite to stop and restore enabled mods before closing.";
        };
        reviewBanner.Text = "MAINTAINER BUILD · Open Settings to load a registry PR. Public settings and libraries are separate.";
    }
    private async void OpenReview(object sender, RoutedEventArgs args) => await Run(async () =>
    {
        if (game != null)
        {
            GameDetector.RequireStopped(game);
            if (packages.Any(p => ModDeployment.Status(game, p) == "Enabled"))
                throw new IOException("Disable the current review's mods before changing PR commits.");
        }
        var candidate = await registry.ResolveReview(reviewUrl.Text);
        var nextEvidence = new ReviewEvidence(candidate);
        var next = RegistryClient.ForReview(candidate);
        IReadOnlyList<RegistryMod> fetched;
        try
        {
            fetched = await next.Fetch();
            nextEvidence.SaveIndex(next);
        }
        catch (Exception exception)
        {
            nextEvidence.SaveIndex(next);
            nextEvidence.Record("Review index rejected", new { Error = exception.Message });
            next.Dispose();
            throw new IOException($"PR could not be loaded. Evidence: {nextEvidence.DirectoryPath}. {exception.Message}", exception);
        }
        registry.Dispose(); registry = next;
        review = candidate; evidence = nextEvidence; availableMods = fetched;
        unverifiedVersion.IsChecked = false;
        MaintainerCompatibility.AllowUnverifiedGameVersion = false;
        settings.LibraryFolder = Path.Combine(review.SessionDirectory, "Library");
        LibraryPath.Text = settings.LibraryFolder;
        LibraryPath.IsReadOnly = true;
        JsonFiles.Write(ManagerSettings.FilePath, settings);
        JsonFiles.Write(ActiveReviewPath, review);
        RegistryBanner.Visibility = Visibility.Collapsed;
        RegistryRefreshButton.IsEnabled = true;
        ShowReviewDetails();
        RegistrySummary.Text = $"{fetched.Count} mod(s) · PR #{review.Number} · {review.Commit} (pinned)";
        Reload();
        BrowseNav.IsChecked = true;
    }, "PR index loaded. Download mods from this pinned review, then enable them in My mods.");

    private void ChangeVersionOverride(object sender, RoutedEventArgs args)
    {
        if (suiteCancellation != null) return;
        MaintainerCompatibility.AllowUnverifiedGameVersion = review != null && unverifiedVersion.IsChecked == true;
        if (review != null)
        {
            reviewBanner.Text = $"MAINTAINER · PR #{review.Number} · {review.Commit} · version override {(MaintainerCompatibility.AllowUnverifiedGameVersion ? "ON — UNVERIFIED" : "OFF")}";
            evidence?.Record("Game version override changed", new { Allowed = MaintainerCompatibility.AllowUnverifiedGameVersion });
        }
        Reload();
    }
    private async void RunLaunchSuite(object sender, RoutedEventArgs args)
    {
        using var cancellation = new CancellationTokenSource();
        suiteCancellation = cancellation;
        cancelSuite.Visibility = Visibility.Visible;
        cancelSuite.IsEnabled = true;
        try
        {
            await Run(async () =>
            {
                if (review == null || evidence == null) throw new IOException("Open a registry PR first.");
                if (!int.TryParse(observationSeconds.Text, out var seconds) || seconds is < 15 or > 600) throw new IOException("Observation must be 15–600 seconds.");
                SavePaths(); Reload(); await CheckRequirements();
                if (game == null || requirements?.Ready != true || restartRequired) throw new IOException("Select a game and resolve its requirements first.");
                var progress = new Progress<string>(message => StatusText.Text = message);
                await new MaintainerLaunchSuite().Run(game, packages, evidence, TimeSpan.FromSeconds(seconds), progress, cancellation.Token);
            }, "Launch suite finished; original enabled mods restored. Open the evidence report and inspect each observation.");
        }
        finally
        {
            suiteCancellation = null;
            cancelSuite.Visibility = Visibility.Collapsed;
            Reload();
        }
    }
}
