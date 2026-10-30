using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MSharpEject.Audit;
using MSharpEject.DeOlive;
using MSharpEject.Eject;

namespace MSharpEject.Gui;

public partial class MainWindow : Window
{
    // The on-screen log is a convenience; the file has everything. Capping keeps the TextBox fast.
    const int MaxLogChars = 200_000;

    // Log lines and progress arrive from worker threads at any rate; the pump applies them in batches.
    readonly ConcurrentQueue<string> pendingLog = new();
    ProgressUpdate? pendingProgress;
    readonly DispatcherTimer pump = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly DispatcherTimer filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Stopwatch elapsed = new();
    readonly StringBuilder logText = new();
    CancellationTokenSource? cancel;
    string? previewCommand;

    public AuditResult? Result { get; private set; }
    public RunOutcome? Outcome { get; private set; }
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
        EjectButton.Click += async (_, _) => await PreviewAsync("eject");
        DeOliveButton.Click += async (_, _) => await PreviewAsync("de-olive");
        ApplyButton.Click += async (_, _) => await ApplyAsync();
        CancelButton.Click += (_, _) => Cancel();
        SaveButton.Click += async (_, _) => await SaveAsync();
        OpenLogButton.Click += async (_, _) => await OpenPathAsync(LastLogPath);
        FilterBox.TextChanged += (_, _) => { filterDebounce.Stop(); filterDebounce.Start(); };
        filterDebounce.Tick += (_, _) => { filterDebounce.Stop(); ShowTables(); };
        ChangesGrid.DoubleTapped += async (_, _) => await OpenRepoFileAsync((ChangesGrid.SelectedItem as ChangeRow)?.Path);
        BuildStepsGrid.DoubleTapped += async (_, _) => await OpenRepoFileAsync((BuildStepsGrid.SelectedItem as Finding)?.File);
        BlockersGrid.DoubleTapped += async (_, _) => await OpenRepoFileAsync((BlockersGrid.SelectedItem as Finding)?.File);
        pump.Tick += (_, _) => FlushUi();

        Opened += (_, _) => ThemeBox.SelectedIndex = Array.IndexOf(ThemeSetting.Names, ThemeSetting.Load(ThemeFile));
        ThemeBox.SelectionChanged += (_, _) => ApplyTheme(ThemeSetting.Names[Math.Max(0, ThemeBox.SelectedIndex)]);
        Closed += (_, _) =>
        {
            cancel?.Cancel();
            pump.Stop();
            filterDebounce.Stop();
        };
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

    /// <param name="keepOutcome">Refresh after an apply: keep its banner and change list on screen.</param>
    public Task RunAuditAsync(bool keepOutcome = false)
    {
        var semantic = SemanticCheck.IsChecked == true;
        return RunAsync("audit", async (repo, log, token) =>
        {
            var result = await Auditor.RunAsync(repo, semantic, log, token);
            return () =>
            {
                Result = result;
                previewCommand = null;
                StatsList.ItemsSource = ResultView.Stats(result);
                if (!keepOutcome)
                {
                    Outcome = null;
                    OutcomeBanner.IsVisible = ChangesTab.IsVisible = false;
                    if (Tabs.SelectedItem == ChangesTab || Tabs.SelectedItem == null) Tabs.SelectedItem = PackagesTab;
                }
                ShowTables();
                StatusText.Text = result.LoadErrors.Count == 0
                    ? $"Audited {result.Repo} in {elapsed.Elapsed:m\\:ss}."
                    : $"Audited with {result.LoadErrors.Count} load errors, so Olive counts may be low. Open the log for details.";
            };
        });
    }

    public Task PreviewAsync(string command) => RunCommandAsync(command, new RunOptions(DryRun: true));

    public async Task ApplyAsync()
    {
        if (previewCommand is not { } command) return;
        await RunCommandAsync(command, new RunOptions(DryRun: false, AllowDirty: AllowDirtyCheck.IsChecked == true, Verify: VerifyCheck.IsChecked == true));
        // Show what's left straight away; the change list and verdict stay visible.
        if (Outcome is { Applied: true }) await RunAuditAsync(keepOutcome: true);
    }

    Task RunCommandAsync(string command, RunOptions options) => RunAsync(command, async (repo, log, token) =>
    {
        var outcome = command == "eject"
            ? await Ejector.RunAsync(repo, options, log, token)
            : await DeOliver.RunAsync(repo, options, log, token);
        return () =>
        {
            Outcome = outcome;
            var banner = ResultView.Banner(command, outcome, options.DryRun);
            previewCommand = banner.CanApply ? command : null;
            OutcomeText.Text = banner.Text;
            OutcomeBanner.Classes.Set("ok", banner.Kind == BannerKind.Ok);
            OutcomeBanner.Classes.Set("problem", banner.Kind == BannerKind.Problem);
            OutcomeBanner.IsVisible = true;
            ApplyButton.IsVisible = banner.CanApply;
            ChangesTab.IsVisible = true;
            StatsList.ItemsSource = ResultView.OutcomeStats(outcome);
            Tabs.SelectedItem = ChangesTab;
            ShowTables();
            StatusText.Text = options.DryRun
                ? $"{ResultView.Title(command)} preview ready."
                : outcome.Applied ? $"{ResultView.Title(command)} applied." : StatusText.Text;
        };
    });

    /// <param name="work">Runs off the UI thread; returns the UI update to apply when done.</param>
    async Task RunAsync(string command, Func<string, RunLog, CancellationToken, Task<Action>> work)
    {
        var repo = RepoPath.Text?.Trim();
        if (string.IsNullOrEmpty(repo) || !Directory.Exists(repo))
        {
            StatusText.Text = "Choose a folder that exists.";
            return;
        }

        using var log = new RunLog(RunLog.DefaultPath(command, LogDirectory), command);
        log.Echo += pendingLog.Enqueue;
        log.Progressed += update => Volatile.Write(ref pendingProgress, update);
        LastLogPath = log.FilePath;
        using var source = new CancellationTokenSource();
        cancel = source;

        logText.Clear();
        LogBox.Text = "";
        SetBusy(true);
        StatusText.Text = "";

        try
        {
            var show = await Task.Run(() => work(repo, log, source.Token));
            FlushUi();
            show();
            ResultsPanel.IsVisible = true;
            WelcomePanel.IsVisible = false;
            StatsList.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            log.Warn("Cancelled.");
            StatusText.Text = command == "audit" ? "Cancelled." : "Cancelled. Files may be partly changed; git checkout -- . undoes everything.";
        }
        catch (Exception ex)
        {
            log.Error($"{command} failed", ex);
            StatusText.Text = $"{command} failed: {ex.Message} Open the log for details.";
            ResultsPanel.IsVisible = true;
            WelcomePanel.IsVisible = false;
            Tabs.SelectedItem = LogTab;
        }
        finally
        {
            cancel = null;
            FlushUi();
            SetBusy(false);
        }
    }

    void SetBusy(bool busy)
    {
        RunButton.IsVisible = EjectButton.IsVisible = DeOliveButton.IsVisible = !busy;
        CancelButton.IsVisible = busy;
        BrowseButton.IsEnabled = RepoPath.IsEnabled = SemanticCheck.IsEnabled = VerifyCheck.IsEnabled = AllowDirtyCheck.IsEnabled = ApplyButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy && Result != null;
        OpenLogButton.IsEnabled = LastLogPath != null;

        ProgressPanel.IsVisible = true;
        if (busy)
        {
            ProgressBar.Value = 0;
            StepText.Text = "Starting…";
            elapsed.Restart();
            pump.Start();
        }
        else
        {
            elapsed.Stop();
            pump.Stop();
        }
        ElapsedText.Text = elapsed.Elapsed.ToString(@"m\:ss");
    }

    /// <summary>Applies queued log lines and the latest progress. Runs on the UI thread.</summary>
    public void FlushUi()
    {
        ElapsedText.Text = elapsed.Elapsed.ToString(@"m\:ss");
        if (Interlocked.Exchange(ref pendingProgress, null) is { } progress)
        {
            ProgressBar.Value = progress.Fraction;
            StepText.Text = progress.Step;
        }

        if (pendingLog.IsEmpty) return;
        while (pendingLog.TryDequeue(out var line)) logText.AppendLine(line);
        if (logText.Length > MaxLogChars)
        {
            logText.Remove(0, logText.Length - MaxLogChars);
            logText.Insert(0, "… earlier lines are in the log file …" + Environment.NewLine);
        }
        LogBox.Text = logText.ToString();
        LogBox.CaretIndex = LogBox.Text.Length;
    }

    /// <summary>Applies the filter now instead of after the debounce.</summary>
    public void ApplyFilter()
    {
        filterDebounce.Stop();
        ShowTables();
    }

    void ShowTables()
    {
        var filter = FilterBox.Text;
        if (Outcome is { } o) Show(ChangesTab, ChangesGrid, "Changes", ResultView.ChangeRows(o), filter);
        if (Result is not { } r) return;

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

    Task OpenRepoFileAsync(string? relative) =>
        relative == null || RepoPath.Text is not { } repo ? Task.CompletedTask : OpenPathAsync(Path.Combine(repo, relative));

    async Task OpenPathAsync(string? path)
    {
        if (path != null && File.Exists(path)) await Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }
}
