using Avalonia.Styling;

namespace MSharpEject.Gui;

public static class ThemeSetting
{
    public static readonly string[] Names = ["System", "Light", "Dark"];

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "msharp-eject", "theme.txt");

    public static string Load(string file)
    {
        try
        {
            var name = File.ReadAllText(file).Trim();
            return Names.Contains(name) ? name : "System";
        }
        catch (IOException) { return "System"; }
        catch (UnauthorizedAccessException) { return "System"; }
    }

    public static void Save(string file, string name)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, name);
        }
        catch (IOException) { } // Losing the preference isn't worth an error dialog.
        catch (UnauthorizedAccessException) { }
    }

    public static ThemeVariant ToVariant(string name) => name switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };
}
