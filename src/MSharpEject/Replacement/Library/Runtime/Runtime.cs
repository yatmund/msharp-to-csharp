using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Platform
{
    public delegate void AwaitableEventHandler(AwaitableEvent ev);

    public delegate void AwaitableEventHandler<TArg>(AwaitableEvent<TArg> ev);

    /// <summary>Handlers register async work with <see cref="Do(Func{Task})"/>; the raiser awaits all of it.</summary>
    public class AwaitableEvent
    {
        internal readonly List<Func<Task>> Work = new List<Func<Task>>();

        public void Do(Func<Task> work) => Work.Add(work);
    }

    public class AwaitableEvent<TArgs>
    {
        internal readonly List<Func<Task>> Work = new List<Func<Task>>();

        public AwaitableEvent(TArgs args) => Args = args;

        public TArgs Args { get; }

        public void Do(Func<Task> work) => Work.Add(work);

        public void Do(Func<TArgs, Task> work) => Work.Add(() => work(Args));
    }

    public static partial class Extensions
    {
        public static async Task Raise(this AwaitableEventHandler @event)
        {
            if (@event == null) return;
            foreach (AwaitableEventHandler handler in @event.GetInvocationList())
            {
                var ev = new AwaitableEvent();
                handler(ev);
                foreach (var work in ev.Work) await work();
            }
        }

        public static async Task Raise<TArgs>(this AwaitableEventHandler<TArgs> @event, TArgs args)
        {
            if (@event == null) return;
            foreach (AwaitableEventHandler<TArgs> handler in @event.GetInvocationList())
            {
                var ev = new AwaitableEvent<TArgs>(args);
                handler(ev);
                foreach (var work in ev.Work) await work();
            }
        }
    }

    public class Context
    {
        static Context current;
        Func<IServiceProvider> scopeServiceProvider;
        IServiceProvider applicationServices;

        public static AwaitableEventHandler StartedUp;

        public static Context Current => current ?? throw new InvalidOperationException("Context.Initialize() hasn't been called.");

        public static Context Initialize(IServiceProvider applicationServices, Func<IServiceProvider> scopeServiceProvider)
        {
            current = new Context { applicationServices = applicationServices, scopeServiceProvider = scopeServiceProvider };
            return current;
        }

        public static Task OnStartedUp() => StartedUp.Raise();

        public IServiceProvider ServiceProvider => scopeServiceProvider?.Invoke() ?? applicationServices;

        public IConfiguration Config => GetService<IConfiguration>();

        public TService GetService<TService>() => ServiceProvider.GetRequiredService<TService>();

        public TService GetOptionalService<TService>() where TService : class => ServiceProvider?.GetService<TService>();

        public IEnumerable<TService> GetServices<TService>() => ServiceProvider.GetServices<TService>();
    }

    public static class Config
    {
        static IConfiguration configuration;

        static IConfiguration Configuration => configuration ?? (configuration = TryContextConfig());

        static IConfiguration TryContextConfig()
        {
            try { return Context.Current.Config; }
            catch (InvalidOperationException) { return new ConfigurationBuilder().Build(); }
        }

        public static void SetConfiguration(IConfiguration config) => configuration = config;

        public static IConfigurationSection GetSection(string key) => Configuration.GetSection(key);

        public static void Bind(string key, object instance) => Configuration.GetSection(key).Bind(instance);

        public static T Bind<T>(string key) where T : new()
        {
            var result = new T();
            Bind(key, result);
            return result;
        }

        public static string Get(string key) => Configuration[key];

        public static string Get(string key, string defaultValue) => Configuration[key] ?? defaultValue;

        public static string GetOrThrow(string key) => Configuration[key] ?? throw new InvalidOperationException($"Config value '{key}' is not specified.");

        public static T Get<T>(string key) => Get(key, default(T));

        public static T Get<T>(string key, T defaultValue)
        {
            var value = Configuration[key];
            if (string.IsNullOrEmpty(value)) return defaultValue;
            var type = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (type.IsEnum) return (T)Enum.Parse(type, value, ignoreCase: true);
            if (type == typeof(Guid)) return (T)(object)Guid.Parse(value);
            if (type == typeof(TimeSpan)) return (T)(object)TimeSpan.Parse(value);
            return (T)Convert.ChangeType(value, type, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, string> GetSubsection(string sectionKey, bool relativeKeys)
        {
            var section = Configuration.GetSection(sectionKey);
            return section.AsEnumerable(makePathsRelative: relativeKeys)
                .Where(p => p.Value != null && p.Key.Length > 0)
                .ToDictionary(p => p.Key, p => p.Value);
        }

        public static IConfiguration MergeEnvironmentVariables(this IConfiguration config)
        {
            foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                var key = variable.Key.ToString().Replace("__", ":");
                if (config[key] != null) config[key] = variable.Value?.ToString();
            }
            return config;
        }

        public static string GetSafeEnvironmentVariable(string key)
        {
            try { return Environment.GetEnvironmentVariable(key); }
            catch (System.Security.SecurityException) { return null; }
        }
    }

    public static class Log
    {
        static ILoggerFactory factory;

        public static ILoggerFactory Factory => factory ?? (factory = LoggerFactory.Create(_ => { }));

        public static void Init(Action<ILoggingBuilder> configure) => factory = LoggerFactory.Create(configure);

        public static void Init(ILoggerFactory factory) => Log.factory = factory;

        public static ILogger For(Type type) => Factory.CreateLogger(type);

        public static ILogger For<TType>() => For(typeof(TType));

        public static ILogger For(object useThis) => For(useThis?.GetType() ?? typeof(object));
    }

    public static class LogExtensions
    {
        public static void Error(this ILogger @this, string message) => @this.LogError(message);

        public static void Error(this ILogger @this, Exception ex, string message = null) => @this.LogError(ex, message ?? ex?.Message);

        public static void Warning(this ILogger @this, string message, object relatedObject = null, string userId = null, string userIp = null) =>
            @this.LogWarning(message);

        public static void Debug(this ILogger @this, string message, object relatedObject = null, string userId = null, string userIp = null) =>
            @this.LogDebug(message);

        public static void Info(this ILogger @this, string message, object relatedObject = null, string userId = null, string userIp = null) =>
            @this.LogInformation(message);

        public static void Audit(this ILogger @this, string message, object relatedObject = null, string userId = null, string userIp = null) =>
            @this.LogInformation(message);
    }

    public class LocalTime
    {
        static readonly AsyncLocal<Func<DateTime>> Overridden = new AsyncLocal<Func<DateTime>>();
        static Func<DateTime> redefined;

        public static Func<TimeZoneInfo> CurrentTimeZone = () => TimeZoneInfo.Local;

        static Func<DateTime> Clock => Overridden.Value ?? redefined;

        public static DateTime Now => Clock?.Invoke() ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CurrentTimeZone());

        public static DateTime UtcNow => Clock == null ? DateTime.UtcNow : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), CurrentTimeZone());

        public static DateTime Today => Now.Date;

        public static DateTime UtcToday => UtcNow.Date;

        public static bool IsRedefined => Clock != null;

        public static IDisposable Set(DateTime overriddenNow) => Set(() => overriddenNow);

        public static IDisposable Set(Func<DateTime> overriddenNow)
        {
            var previous = Overridden.Value;
            Overridden.Value = overriddenNow;
            return new Restore(() => Overridden.Value = previous);
        }

        public static void RedefineNow(Func<DateTime> overriddenNow) => redefined = overriddenNow;

        public static IDisposable Stop()
        {
            var frozen = Now;
            return Set(() => frozen);
        }

        public static void Add(TimeSpan time)
        {
            var now = Now.Add(time);
            RedefineNow(() => now);
        }

        public static void AddSeconds(double seconds) => Add(TimeSpan.FromSeconds(seconds));

        public static void AddMinutes(double minutes) => Add(TimeSpan.FromMinutes(minutes));

        public static void AddHours(double hours) => Add(TimeSpan.FromHours(hours));

        public static void AddDays(double days) => Add(TimeSpan.FromDays(days));

        sealed class Restore : IDisposable
        {
            readonly Action action;
            public Restore(Action action) => this.action = action;
            public void Dispose() => action();
        }
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class EscapeGCopAttribute : Attribute
    {
        public EscapeGCopAttribute(string reason = null) => Reason = reason;

        public string Reason { get; }
    }

    public class DevCommandsOptions
    {
        public DevCommandsOptions(IServiceCollection services) => Services = services;

        public IServiceCollection Services { get; }
    }

    public interface IDevCommand
    {
        bool IsEnabled();
        Task<string> Run();
        string Name { get; }
        string Title { get; }
    }

    public interface IMortal
    {
        bool IsDead();
    }

    public struct DisposableAwaitable<T> where T : IDisposable
    {
        public readonly Task<T> Task;

        public DisposableAwaitable(Task<T> task) => Task = task;

        public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter() => Task.GetAwaiter();
    }

    /// <summary>A lock usable across await: <c>using (await myLock.Lock()) { ... }</c>.</summary>
    public class AsyncLock
    {
        readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public DisposableAwaitable<IDisposable> Lock() => new DisposableAwaitable<IDisposable>(Acquire());

        async Task<IDisposable> Acquire()
        {
            await semaphore.WaitAsync();
            return new Releaser(semaphore);
        }

        sealed class Releaser : IDisposable
        {
            SemaphoreSlim semaphore;
            public Releaser(SemaphoreSlim semaphore) => this.semaphore = semaphore;
            public void Dispose() => Interlocked.Exchange(ref semaphore, null)?.Release();
        }
    }

    public class ProcessContext<T> : IDisposable
    {
        static readonly AsyncLocal<Dictionary<string, Stack<T>>> Contexts = new AsyncLocal<Dictionary<string, Stack<T>>>();
        readonly string key;

        public ProcessContext(T data) : this("default", data) { }

        public ProcessContext(string key, T data)
        {
            this.key = key;
            Data = data;
            var all = Contexts.Value ?? (Contexts.Value = new Dictionary<string, Stack<T>>());
            if (!all.TryGetValue(key, out var stack)) all[key] = stack = new Stack<T>();
            stack.Push(data);
        }

        public T Data { get; }

        public static T Current => GetCurrent("default");

        public static T GetCurrent(string key) =>
            Contexts.Value != null && Contexts.Value.TryGetValue(key, out var stack) && stack.Count > 0 ? stack.Peek() : default(T);

        public void Dispose()
        {
            if (Contexts.Value != null && Contexts.Value.TryGetValue(key, out var stack) && stack.Count > 0) stack.Pop();
        }
    }

    public class ProcessContext
    {
        public static ProcessContext<T> Create<T>(T contextObject) => new ProcessContext<T>(contextObject);

        public static ProcessContext<T> Create<T>(string key, T contextObject) => new ProcessContext<T>(key, contextObject);
    }
}
