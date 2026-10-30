using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MSharpEject.Gui;

namespace MSharpEject.Tests;

public class HeadlessApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class MainWindowTests
{
    static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessApp)));

    static readonly string ThemeFile = Path.Combine(TestLog.Directory, "theme-" + Guid.NewGuid().ToString("N") + ".txt");

    static Task<T> OnUiThread<T>(Func<Task<T>> action) => Session.Value.Dispatch(action, CancellationToken.None);

    [Test]
    public async Task Starts_with_save_and_filter_disabled()
    {
        var (save, filter, run) = await OnUiThread(() =>
        {
            var window = new MainWindow();
            window.Show();
            return Task.FromResult((
                window.FindControl<Button>("SaveButton")!.IsEnabled,
                window.FindControl<DockPanel>("ResultsPanel")!.IsVisible,
                window.FindControl<Button>("RunButton")!.IsEnabled));
        });

        await Assert.That(save).IsFalse();
        await Assert.That(filter).IsFalse();
        await Assert.That(run).IsTrue();
    }

    [Test]
    public async Task Missing_folder_shows_message_and_runs_nothing()
    {
        var (status, result) = await OnUiThread(async () =>
        {
            var window = new MainWindow(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid())) { LogDirectory = TestLog.Directory, ThemeFile = ThemeFile };
            window.Show();
            await window.RunAuditAsync();
            return (window.FindControl<TextBlock>("StatusText")!.Text, window.Result);
        });

        await Assert.That(status).IsEqualTo("Choose a folder that exists.");
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Audit_fills_tabs_and_filter_narrows_them()
    {
        using var repo = new TempDir();
        repo.Write("Domain/Domain.csproj", """<Project><ItemGroup><PackageReference Include="Olive" Version="2.1.340" /></ItemGroup></Project>""");
        repo.Write("M#/Model/Domain/Contact.cs", "class Contact : EntityType { }");
        repo.Write("Website/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:6.0-nanoserver-1809");
        repo.Write("Build.bat", "@echo off");

        var view = await OnUiThread(async () =>
        {
            var window = new MainWindow(repo.Path) { LogDirectory = TestLog.Directory, ThemeFile = ThemeFile };
            window.Show();
            window.FindControl<CheckBox>("SemanticCheck")!.IsChecked = false;
            await window.RunAuditAsync();
            Dispatcher.UIThread.RunJobs();

            var before = (string)window.FindControl<TabItem>("BlockersTab")!.Header!;
            window.FindControl<TextBox>("FilterBox")!.Text = "docker";
            window.ApplyFilter();
            return new
            {
                Before = before,
                After = (string)window.FindControl<TabItem>("BlockersTab")!.Header!,
                Packages = (string)window.FindControl<TabItem>("PackagesTab")!.Header!,
                Stats = (List<Stat>)window.FindControl<ItemsControl>("StatsList")!.ItemsSource!,
                Welcome = window.FindControl<StackPanel>("WelcomePanel")!.IsVisible,
                SaveEnabled = window.FindControl<Button>("SaveButton")!.IsEnabled,
                OpenLogEnabled = window.FindControl<Button>("OpenLogButton")!.IsEnabled,
                RunVisible = window.FindControl<Button>("RunButton")!.IsVisible,
                CancelVisible = window.FindControl<Button>("CancelButton")!.IsVisible,
                Progress = window.FindControl<ProgressBar>("ProgressBar")!.Value,
                Step = window.FindControl<TextBlock>("StepText")!.Text,
                LogBox = window.FindControl<TextBox>("LogBox")!.Text,
                window.LastLogPath,
            };
        });

        await Assert.That(view.Before).IsEqualTo("Deployment blockers (2)");
        await Assert.That(view.After).IsEqualTo("Deployment blockers (1/2)");
        await Assert.That(view.Packages).IsEqualTo("Packages (0/1)");
        await Assert.That(view.Stats[0]).IsEqualTo(new Stat("Geeks packages", "1", true));
        await Assert.That(view.Welcome).IsFalse();
        await Assert.That(view.SaveEnabled).IsTrue();
        await Assert.That(view.OpenLogEnabled).IsTrue();
        await Assert.That(view.RunVisible).IsTrue();
        await Assert.That(view.CancelVisible).IsFalse();
        await Assert.That(view.Progress).IsEqualTo(1);
        await Assert.That(view.Step).IsEqualTo("Done");
        await Assert.That(view.LogBox).Contains("Scanning for deployment blockers");
        await Assert.That(RunLogTests.ReadShared(view.LastLogPath!)).Contains("Auditing " + Path.GetFullPath(repo.Path));
    }

    [Test]
    [Timeout(300_000)]
    [NotInParallel("theme")]
    public async Task Eject_previews_then_applies(CancellationToken _)
    {
        using var repo = FakeRepo.Create();
        await FakeRepo.CommitAllAsync(repo.Path);

        var view = await OnUiThread(async () =>
        {
            var window = new MainWindow(repo.Path) { LogDirectory = TestLog.Directory, ThemeFile = ThemeFile };
            window.Show();
            window.FindControl<CheckBox>("VerifyCheck")!.IsChecked = false;
            window.FindControl<CheckBox>("SemanticCheck")!.IsChecked = false;

            window.SetTheme("Dark");
            window.Width = 1200;
            window.Height = 760;
            await window.PreviewAsync("eject");
            Dispatcher.UIThread.RunJobs();
            var shot = Path.Combine(AppContext.BaseDirectory, "TestResults", "eject-preview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(shot)!);
            using (var file = File.Create(shot)) window.CaptureRenderedFrame()!.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            window.SetTheme("System");
            var preview = new
            {
                Banner = window.FindControl<TextBlock>("OutcomeText")!.Text,
                Apply = window.FindControl<Button>("ApplyButton")!.IsVisible,
                Changes = (string)window.FindControl<TabItem>("ChangesTab")!.Header!,
                MSharpStillThere = Directory.Exists(Path.Combine(repo.Path, "M#")),
            };

            await window.ApplyAsync();
            return new
            {
                Preview = preview,
                Reaudited = window.Result != null,
                BannerStillShown = window.FindControl<Border>("OutcomeBanner")!.IsVisible,
                Banner = window.FindControl<TextBlock>("OutcomeText")!.Text,
                Apply = window.FindControl<Button>("ApplyButton")!.IsVisible,
                Problem = window.FindControl<Border>("OutcomeBanner")!.Classes.Contains("problem"),
            };
        });

        await Assert.That(view.Preview.Banner).StartsWith("Eject M# will make 4 changes");
        await Assert.That(view.Preview.Apply).IsTrue();
        await Assert.That(view.Preview.Changes).IsEqualTo("Changes (4)");
        await Assert.That(view.Preview.MSharpStillThere).IsTrue();
        await Assert.That(view.Banner).StartsWith("Eject M# applied 4 changes. Not verified");
        await Assert.That(view.Apply).IsFalse();
        await Assert.That(view.Problem).IsFalse();
        await Assert.That(view.Reaudited).IsTrue();
        await Assert.That(view.BannerStillShown).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(repo.Path, "M#"))).IsFalse();
    }

    [Test]
    public async Task Eject_problem_shows_in_banner_without_apply()
    {
        using var repo = new TempDir();
        repo.Write("Domain/Domain.csproj", "<Project />");

        var (banner, apply, problem) = await OnUiThread(async () =>
        {
            var window = new MainWindow(repo.Path) { LogDirectory = TestLog.Directory, ThemeFile = ThemeFile };
            window.Show();
            await window.PreviewAsync("eject");
            return (window.FindControl<TextBlock>("OutcomeText")!.Text, window.FindControl<Button>("ApplyButton")!.IsVisible,
                window.FindControl<Border>("OutcomeBanner")!.Classes.Contains("problem"));
        });

        await Assert.That(banner).Contains("nothing to eject");
        await Assert.That(apply).IsFalse();
        await Assert.That(problem).IsTrue();
    }

    [Test]
    public async Task Starts_on_welcome_panel()
    {
        var (welcome, results) = await OnUiThread(() =>
        {
            var window = new MainWindow { ThemeFile = ThemeFile };
            window.Show();
            return Task.FromResult((window.FindControl<StackPanel>("WelcomePanel")!.IsVisible, window.FindControl<DockPanel>("ResultsPanel")!.IsVisible));
        });

        await Assert.That(welcome).IsTrue();
        await Assert.That(results).IsFalse();
    }

    [Test]
    public async Task Theme_choice_applies_and_persists()
    {
        var file = Path.Combine(TestLog.Directory, "theme-" + Guid.NewGuid().ToString("N") + ".txt");

        var (variant, reopened) = await OnUiThread(() =>
        {
            var window = new MainWindow { ThemeFile = file };
            window.Show();
            window.FindControl<ComboBox>("ThemeBox")!.SelectedIndex = 2;
            var applied = Application.Current!.RequestedThemeVariant;

            var second = new MainWindow { ThemeFile = file };
            second.Show();
            var index = second.FindControl<ComboBox>("ThemeBox")!.SelectedIndex;
            second.SetTheme("System");
            return Task.FromResult((applied, index));
        });

        await Assert.That(variant).IsEqualTo(Avalonia.Styling.ThemeVariant.Dark);
        await Assert.That(reopened).IsEqualTo(2);
    }

    [Test]
    [Arguments("Light")]
    [Arguments("Dark")]
    [NotInParallel("theme")]
    public async Task Renders_after_an_audit(string theme)
    {
        using var repo = new TempDir();
        repo.Write("Domain/Domain.csproj", """<Project><ItemGroup><PackageReference Include="Olive" Version="2.1.340" /><PackageReference Include="Olive.Mvc" Version="3.1.150" /></ItemGroup></Project>""");
        repo.Write("M#/Model/Domain/Contact.cs", "class Contact : EntityType { } class User : EntityType { }");
        repo.Write("M#/UI/Modules/ContactsList.cs", "class ContactsList : ListModule<Contact> { }");
        repo.Write("Website/Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:6.0-nanoserver-1809");
        repo.Write("Build.bat", "call msharp-build");
        var output = Path.Combine(AppContext.BaseDirectory, "TestResults", $"main-window-{theme.ToLowerInvariant()}.png");

        var size = await OnUiThread(async () =>
        {
            var window = new MainWindow(repo.Path) { LogDirectory = TestLog.Directory, ThemeFile = ThemeFile, Width = 1200, Height = 760 };
            window.Show();
            window.SetTheme(theme);
            window.FindControl<CheckBox>("SemanticCheck")!.IsChecked = false;
            await window.RunAuditAsync();
            window.FindControl<TabControl>("Tabs")!.SelectedItem = window.FindControl<TabItem>("BlockersTab");
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame()!;
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var file = File.Create(output)) frame.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            return frame.PixelSize;
        });

        await Assert.That(size.Width).IsGreaterThan(0);
        await Assert.That(File.Exists(output)).IsTrue();
    }
}
