using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MSharpEject;

public record ProgressUpdate(double Fraction, string Step);

public sealed class RunLog : IDisposable
{
    readonly StreamWriter writer;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Lock gate = new();

    public string FilePath { get; }

    /// <summary>Lines meant for the user. Detail lines only go to the file.</summary>
    public event Action<string>? Echo;
    public event Action<ProgressUpdate>? Progressed;

    public RunLog(string filePath, string command)
    {
        FilePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // AutoFlush: a killed or crashed run still leaves a complete log.
        writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };

        var version = typeof(RunLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        Write("INFO", $"msharp-eject {version} {command}");
        Write("INFO", $"Started {DateTimeOffset.Now:O}");
        Write("INFO", $"OS {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, {RuntimeInformation.FrameworkDescription}");
        Write("INFO", $"Command line: {Environment.CommandLine}");
        Write("INFO", $"Working directory: {Environment.CurrentDirectory}");
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "msharp-eject", "logs");

    public static string DefaultPath(string command, string? directory = null) =>
        Path.Combine(directory ?? DefaultDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{command}.log");

    public void Info(string message)
    {
        Write("INFO", message);
        Echo?.Invoke(message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
        Echo?.Invoke("Warning: " + message);
    }

    public void Detail(string message) => Write("DETAIL", message);

    public void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex == null ? message : $"{message}\n{ex}");
        Echo?.Invoke("Error: " + (ex == null ? message : $"{message}: {ex.Message}"));
    }

    public void Progress(double fraction, string step)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        Info($"[{fraction,4:P0}] {step}");
        Progressed?.Invoke(new ProgressUpdate(fraction, step));
    }

    /// <summary>Logs how long the wrapped work took.</summary>
    public IDisposable Time(string step)
    {
        var started = clock.Elapsed;
        return new Timer(() => Detail($"{step}: {(clock.Elapsed - started).TotalSeconds:N1}s"));
    }

    void Write(string level, string message)
    {
        var prefix = $"[+{clock.Elapsed:hh\\:mm\\:ss\\.f}] {level,-6} ";
        lock (gate)
            foreach (var line in message.Split('\n'))
                writer.WriteLine(prefix + line.TrimEnd('\r'));
    }

    public void Dispose()
    {
        Write("INFO", $"Finished after {clock.Elapsed.TotalSeconds:N1}s");
        writer.Dispose();
    }

    sealed class Timer(Action done) : IDisposable
    {
        public void Dispose() => done();
    }
}
