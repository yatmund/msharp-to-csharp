using System.Text;

namespace MSharpEject;

/// <summary>Reads and writes text keeping the file's BOM and line endings, so edits don't show up as whole-file diffs.</summary>
public static class FileText
{
    static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static (string Text, Encoding Encoding) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return (Utf8NoBom.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom ? Utf8Bom : Utf8NoBom);
    }

    public static void Write(string path, string text, Encoding encoding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text, encoding);
    }

    public static string NewLineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";
}
