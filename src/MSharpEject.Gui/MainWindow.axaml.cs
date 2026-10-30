using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MSharpEject.Audit;

namespace MSharpEject.Gui;

public partial class MainWindow : Window
{
    readonly DispatcherTimer elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch elapsed = new();
    CancellationTokenSource? cancel;

    public AuditResult? Result { get; private set; }
    public string? LastLogPath { get; private set; }

    /// <summary>Null means <see cref="RunLog.DefaultDirectory"/>.</summary>
    public string? LogDirectory { get; set; }

    public string ThemeFile { get; set; } = ThemeSetting.DefaultFile;

    public MainWindow() : this(null) { }

    public MainWindow(string? repo)
    {
        InitializeComponent();
        RepoPath.Text = repo;

        BrowseButton.Click += async (_, _) => await BrowseAsync();
        WelcomeBrowseButton.Click += async (_, _) => await BrowseAsync();
        RunButton.Click += async (_, _) => await RunAuditAsync();
        CancelButton.Click += (_, _) => Cancel();
        SaveButton.Click += async (_, _) => await SaveAsync();
        OpenLogButton.Click += async (_, _) => await OpenPathAsync(LastLogPath);
        FilterBox.TextChanged += (_, _) => ShowResult();
        BuildStepsGrid.DoubleTapped += async (_, _) => await OpenFindingAsync(BuildStepsGrid.SelectedItem as Finding);
        BlockersGrid.DoubleTapped += async (_, _) => await OpenFindingAsync(BlockersGrid.SelectedItem as Finding);
        elapsedTimer.Tick += (_, _) => ElapsedText.Text = elapsed.Elapsed.ToString(@"m\:ss");

        Opened += (_, _) => ThemeBox.SelectedIndex = Array.IndexOf(ThemeSetting.Names, ThemeSetting.Load(ThemeFile));
        ThemeBox.SelectionChanged += (_, _) => ApplyTheme(ThemeSetting.Names[Math.Max(0, ThemeBox.SelectedIndex)]);
    }

    public void SetTheme(string name)
    {
        ThemeBox.SelectedIndex = Array.IndexOf(ThemeSetting.Names, name);
        ApplyTheme(name);
    }

    void ApplyTheme(string name)
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = ThemeSetting.ToVariant(name);
        ThemeSetting.Save(ThemeFile, name);
    }

    async Task BrowseAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose an M# repository" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            RepoPath.Text = path;
            await RunAuditAsync();
        }
    }

    public void Cancel()
    {
        cancel?.Cancel();
        StepText.Text = "Cancelling…";
    }

    public async Task RunAuditAsync()
    {
        var repo = RepoPath.Text?.Trim();
        if (string.IsNullOrEmpty(repo) || !Directory.Exists(repo))
        {
            StatusText.Text = "Choose a folder that exists.";
            return;
        }

        var semantic = SemanticCheck.IsChecked == true;
        using var log = new RunLog(RunLog.DefaultPath("audit", LogDirectory), "audit");
        log.Echo += line => Dispatcher.UIThread.Post(() => AppendLog(line));
        log.Progressed += update => Dispatcher.UIThread.Post(() => ShowProgress(update));
        LastLogPath = log.FilePath;
        cancel = new CancellationTokenSource();

        LogBox.Text = "";
        SetBusy(true);
        StatusText.Text = semantic ? "Restoring and loading projects can take a few minutes." : "";

        try
        {
            Result = await Task.Run(() => Auditor.RunAsync(repo, semantic, log, cancel.Token));
            ShowResult();
            StatusText.Text = Result.LoadErrors.Count == 0
                ? $"Audited {Result.Repo} in {elapsed.Elapsed:m\\:ss}."
                : $"Audited with {Result.LoadErrors.Count} load errors, so Olive counts may be low. Open the log for details.";
        }
        catch (OperationCanceledException)
        {
            log.Warn("Cancelled.");
            StatusText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            log.Error("Audit failed", ex);
            StatusText.Text = "Audit failed: " + ex.Message + " Open the log for details.";
            Tabs.SelectedItem = LogTab;
        }
        finally
        {
            cancel.Dispose();
            cancel = null;
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        RunButton.IsVisible = !busy;
        CancelButton.IsVisible = busy;
        BrowseButton.IsEnabled = RepoPath.IsEnabled = SemanticCheck.IsEnabled = !busy;
        SaveButton.IsEnabled = FilterBox.IsEnabled = !busy && Result != null;
        OpenLogButton.IsEnabled = LastLogPath != null;

        ProgressPanel.IsVisible = true;
        if (busy)
        {
            ProgressBar.Value = 0;
            StepText.Text = "Starting…";
            elapsed.Restart();
            elapsedTimer.Start();
        }
        else
        {
            elapsed.Stop();
            elapsedTimer.Stop();
            ElapsedText.Text = elapsed.Elapsed.ToString(@"m\:ss");
        }
    }

    void ShowProgress(ProgressUpdate update)
    {
        ProgressBar.Value = update.Fraction;
        StepText.Text = update.Step;
    }

    void AppendLog(string line) => LogBox.Text += line + Environment.NewLine;

    void ShowResult()
    {
        if (Result is not { } r) return;
        var filter = FilterBox.Text;
        WelcomePanel.IsVisible = false;
        ResultsPanel.IsVisible = StatsList.IsVisible = true;
        StatsList.ItemsSource = ResultView.Stats(r);

        Show(PackagesTab, PackagesGrid, "Packages", r.Packages, filter);
        Show(DslTab, DslGrid, "M# definitions", ResultView.DslRows(r), filter);
        Show(GeneratedTab, GeneratedGrid, "Generated output", r.Generated, filter);
        GeneratedTab.Header = $"Generated output ({r.Generated.Sum(g => g.Files)} files)";
        Show(BuildStepsTab, BuildStepsGrid, "Build steps", r.BuildSteps, filter);
        Show(BlockersTab, BlockersGrid, "Deployment blockers", r.Blockers, filter);
        Show(SymbolsTab, SymbolsGrid, "Olive symbols", r.Symbols, filter);
    }

    static void Show<T>(TabItem tab, DataGrid grid, string name, List<T> all, string? filter)
    {
        var shown = ResultView.Filter(all, filter);
        grid.ItemsSource = shown;
        tab.Header = ResultView.Header(name, shown.Count, all.Count);
    }

    async Task SaveAsync()
    {
        if (Result == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save audit report",
            SuggestedFileName = "msharp-audit.md",
            DefaultExtension = "md",
            FileTypeChoices = [new FilePickerFileType("Markdown") { Patterns = ["*.md"] }],
        });
        if (file == null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(Auditor.ToMarkdown(Result));
        StatusText.Text = $"Saved {file.Name}.";
    }

    Task OpenFindingAsync(Finding? finding) =>
        Result == null || finding == null ? Task.CompletedTask : OpenPathAsync(Path.Combine(Result.Repo, finding.File));

    async Task OpenPathAsync(string? path)
    {
        if (path != null && File.Exists(path)) await Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }
}
