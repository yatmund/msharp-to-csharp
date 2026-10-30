namespace MSharpEject.Tests;

sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("msharp-eject-").FullName;

    public string Write(string relative, string content)
    {
        var file = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
        return file;
    }

    public void Dispose()
    {
        try
        {
            // git marks its object files read-only, which blocks Directory.Delete on Windows.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException) { } // MSBuild nodes can still hold obj/ files.
        catch (UnauthorizedAccessException) { }
    }
}

static class TestLog
{
    public static readonly string Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msharp-eject-tests");

    public static RunLog Create() => new(RunLog.DefaultPath("test-" + Guid.NewGuid().ToString("N")[..8], Directory), "test");
}
