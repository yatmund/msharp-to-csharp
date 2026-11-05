using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Configuration;

namespace Platform.Entities.Data
{
    public class DatabaseConfig
    {
        public List<ProviderMapping> Providers { get; set; } = new List<ProviderMapping>();

        public bool Profile { get; set; }

        public CacheConfig Cache { get; set; } = new CacheConfig();

        public TransactionConfig Transaction { get; set; } = new TransactionConfig();

        public class CacheConfig
        {
            public bool ConcurrencyAware { get; set; }
            public bool Enabled { get; set; } = true;
            public bool PerRequest { get; set; }
            public string Mode { get; set; }
        }

        public class TransactionConfig
        {
            public string Type { get; set; }
            public bool EnforceForSave { get; set; }
        }

        public class ProviderMapping
        {
            public string AssemblyName { get; set; }
            public string TypeName { get; set; }
            public string ProviderFactoryType { get; set; }
            public string ConnectionStringKey { get; set; }
            public string ConnectionString { get; set; }
            public string SqlClient { get; set; }
            public Assembly Assembly { get; set; }
            public Type Type { get; set; }

            public Assembly GetAssembly() => Assembly ?? (Assembly = string.IsNullOrEmpty(AssemblyName) ? null : Assembly.Load(new AssemblyName(AssemblyName)));

            public Type GetMappedType()
            {
                if (Type != null) return Type;
                if (string.IsNullOrEmpty(TypeName)) return null;
                return Type = GetAssembly()?.GetType(TypeName) ?? System.Type.GetType(TypeName);
            }
        }
    }

    public interface IConnectionStringProvider
    {
        string GetConnectionString(string key = "Default");
    }

    public class ConnectionStringProvider : IConnectionStringProvider
    {
        public virtual string GetConnectionString(string key = "Default") =>
            DatabaseContext.Current?.ConnectionString ?? Config.Get("ConnectionStrings:" + key);
    }

    /// <summary>Overrides the connection string, and optionally the command timeout, for the code inside it.</summary>
    public class DatabaseContext : IDisposable
    {
        static readonly AsyncLocal<DatabaseContext> current = new AsyncLocal<DatabaseContext>();
        readonly DatabaseContext previous;

        public DatabaseContext(string connectionString)
        {
            ConnectionString = connectionString;
            previous = current.Value;
            current.Value = this;
        }

        public string ConnectionString { get; set; }

        public int? CommandTimeout { get; set; }

        public static DatabaseContext Current { get => current.Value; set => current.Value = value; }

        public void Dispose() => current.Value = previous;
    }

    public interface IDatabaseProviderConfig
    {
        IDataProvider GetProvider(Type type);
        IDataProvider TryGetProvider(Type type);
        IDataAccess GetAccess(string connectionString = null);
        void RegisterDataProvider(Type entityType, IDataProvider dataProvider);
        List<IDataProvider> ResolveDataProviders(Type baseType);
        void Configure();
        IEnumerable<Assembly> GetRegisteredAssemblies();
        DatabaseConfig Configuration { get; }
    }

    public class DatabaseProviderConfig : IDatabaseProviderConfig
    {
        readonly ConcurrentDictionary<Type, IDataProvider> providers = new ConcurrentDictionary<Type, IDataProvider>();
        readonly ConcurrentDictionary<Assembly, IDataProviderFactory> factories = new ConcurrentDictionary<Assembly, IDataProviderFactory>();
        readonly List<IDataProviderFactory> typeFactories = new List<IDataProviderFactory>();
        bool configured;

        public DatabaseProviderConfig() => Configuration = Platform.Config.Bind<DatabaseConfig>("Database");

        public DatabaseConfig Configuration { get; }

        public void Configure()
        {
            if (configured) return;
            configured = true;
            foreach (var mapping in Configuration.Providers) RegisterDataProviderFactory(mapping);
        }

        public void RegisterDataProviderFactory(DatabaseConfig.ProviderMapping factoryInfo)
        {
            IDataProviderFactory factory;
            if (!string.IsNullOrEmpty(factoryInfo.ProviderFactoryType))
            {
                var type = Type.GetType(factoryInfo.ProviderFactoryType) ?? factoryInfo.GetAssembly()?.GetType(factoryInfo.ProviderFactoryType)
                    ?? throw new InvalidOperationException("Could not find the data provider factory type " + factoryInfo.ProviderFactoryType);
                factory = (IDataProviderFactory)Activator.CreateInstance(type, factoryInfo);
            }
            else factory = new DataProviderFactory(factoryInfo);

            if (factoryInfo.GetMappedType() != null) typeFactories.Add(factory);
            else if (factoryInfo.GetAssembly() is { } assembly) factories[assembly] = factory;
        }

        public void RegisterDataProvider(Type entityType, IDataProvider dataProvider) => providers[entityType] = dataProvider;

        public IEnumerable<Assembly> GetRegisteredAssemblies() => factories.Keys;

        public IDataAccess GetAccess(string connectionString = null) => DataAccess.Create(connectionString: connectionString);

        public IDataProvider GetProvider(Type type) => TryGetProvider(type) ?? throw new InvalidOperationException($"There is no registered data provider for {type.FullName}.");

        public IDataProvider TryGetProvider(Type type)
        {
            Configure();
            return providers.GetOrAdd(type, t =>
            {
                if (t.IsInterface) return new InterfaceDataProvider(t);
                var factory = typeFactories.FirstOrDefault(f => (f as DataProviderFactory)?.Relevant(t) == true)
                    ?? (factories.TryGetValue(t.Assembly, out var f) ? f : null)
                    ?? (factories.IsEmpty ? factories.GetOrAdd(t.Assembly, a => new DataProviderFactory(new DatabaseConfig.ProviderMapping { Assembly = a })) : null);
                return factory?.GetProvider(t);
            });
        }

        public List<IDataProvider> ResolveDataProviders(Type baseType) =>
            EntityFinder.FindPossibleTypes(baseType, mustFind: false).Where(t => !t.IsAbstract).Select(TryGetProvider).Where(p => p != null).ToList();
    }
}
