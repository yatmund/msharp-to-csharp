using System.Text;

namespace MSharpEject.Eject;

public abstract record Change(string Path, string Reason)
{
    public abstract string Action { get; }
}

public record EditFile(string Path, string Reason, string NewText, Encoding Encoding) : Change(Path, Reason)
{
    public override string Action => "Edit";
}

public record DeleteFile(string Path, string Reason) : Change(Path, Reason)
{
    public override string Action => "Delete file";
}

public record DeleteDirectory(string Path, string Reason) : Change(Path, Reason)
{
    public override string Action => "Delete folder";
}

public static class ChangeApplier
{
    public static void Apply(string repo, IReadOnlyList<Change> changes, RunLog log)
    {
        // Edits first: a later delete may remove a folder an edited file used to sit next to.
        foreach (var change in changes.OfType<EditFile>())
        {
            FileText.Write(System.IO.Path.Combine(repo, change.Path), change.NewText, change.Encoding);
            log.Detail($"Edited {change.Path}: {change.Reason}");
        }
        foreach (var change in changes.OfType<DeleteFile>())
        {
            var path = System.IO.Path.Combine(repo, change.Path);
            if (File.Exists(path)) File.Delete(path);
            log.Detail($"Deleted {change.Path}: {change.Reason}");
        }
        foreach (var change in changes.OfType<DeleteDirectory>())
        {
            var path = System.IO.Path.Combine(repo, change.Path);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            log.Detail($"Deleted folder {change.Path}: {change.Reason}");
        }
    }
}
