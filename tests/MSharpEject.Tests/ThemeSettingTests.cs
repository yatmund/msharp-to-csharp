using Avalonia.Styling;
using MSharpEject.Gui;

namespace MSharpEject.Tests;

public class ThemeSettingTests
{
    [Test]
    public async Task Missing_or_garbage_file_means_system()
    {
        using var dir = new TempDir();

        await Assert.That(ThemeSetting.Load(Path.Combine(dir.Path, "none.txt"))).IsEqualTo("System");
        await Assert.That(ThemeSetting.Load(dir.Write("bad.txt", "Purple"))).IsEqualTo("System");
    }

    [Test]
    public async Task Save_then_load_round_trips()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "nested", "theme.txt");

        ThemeSetting.Save(file, "Dark");

        await Assert.That(ThemeSetting.Load(file)).IsEqualTo("Dark");
    }

    [Test]
    [Arguments("System", "Default")]
    [Arguments("Light", "Light")]
    [Arguments("Dark", "Dark")]
    public async Task ToVariant(string name, string expected) =>
        await Assert.That(ThemeSetting.ToVariant(name).Key.ToString()).IsEqualTo(expected);
}
