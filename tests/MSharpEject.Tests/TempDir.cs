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
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { } // MSBuild nodes can still hold obj/ files.
    }
}
