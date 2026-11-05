using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Platform.Entities
{
    public interface IParameterFactory
    {
        IDataParameter CreateParameter(string name, object value, DbType? dbType);
    }
}

namespace Platform.Entities.Data
{
    public class DataAccessOptions
    {
        internal readonly List<(Type Connection, ISqlCommandGenerator Generator, IParameterFactory Parameters)> Registrations = new List<(Type, ISqlCommandGenerator, IParameterFactory)>();

        public DataAccessOptions Add<TConnection, TSqlCommandGenerator>(IParameterFactory parameterFactory = null)
            where TConnection : DbConnection where TSqlCommandGenerator : ISqlCommandGenerator, new()
        {
            Registrations.Add((typeof(TConnection), new TSqlCommandGenerator(), parameterFactory));
            return this;
        }
    }

    public static class DataAccessExtensions
    {
        public static IServiceCollection AddDataAccess(this IServiceCollection @this, Action<DataAccessOptions> options)
        {
            var settings = new DataAccessOptions();
            options(settings);
            foreach (var r in settings.Registrations) DataAccess.Register(r.Connection, r.Generator, r.Parameters);
            return @this;
        }

        public static ICache Cache(this IDatabase database) => database.Cache;
    }

    public class DataAccess
    {
        static readonly ConcurrentDictionary<Type, (ISqlCommandGenerator Generator, IParameterFactory Parameters)> Registered =
            new ConcurrentDictionary<Type, (ISqlCommandGenerator, IParameterFactory)>();

        public static string GetCurrentConnectionString() => new ConnectionStringProvider().GetConnectionString();

        public static void Register(Type connectionType, ISqlCommandGenerator sqlCommandGenerator, IParameterFactory parameterFactory) =>
            Registered[connectionType] = (sqlCommandGenerator, parameterFactory);

        public static IDataAccess Create(Type connectionType, string connectionString = null)
        {
            if (!Registered.TryGetValue(connectionType, out var registration))
                throw new InvalidOperationException($"No data access is registered for {connectionType.Name}. Call services.AddDataAccess(x => x.SqlServer()).");
            var accessType = typeof(DataAccess<>).MakeGenericType(connectionType);
            return (IDataAccess)Activator.CreateInstance(accessType, Context.Current.GetOptionalService<IDatabaseProviderConfig>(), registration.Generator, connectionString, registration.Parameters);
        }

        public static IDataAccess Create<TConnection>(string connectionString = null) where TConnection : IDbConnection => Create(typeof(TConnection), connectionString);

        public static IDataAccess Create(string connectionType = null, string connectionString = null)
        {
            var type = string.IsNullOrEmpty(connectionType)
                ? Registered.Keys.FirstOrDefault() ?? throw new InvalidOperationException("No data access is registered. Call services.AddDataAccess(x => x.SqlServer()).")
                : Registered.Keys.FirstOrDefault(k => k.FullName == connectionType || k.Name == connectionType) ?? Type.GetType(connectionType, throwOnError: true);
            return Create(type, connectionString);
        }
    }

    public class DataAccess<TConnection> : DataAccess, IDataAccess where TConnection : DbConnection, new()
    {
        readonly ISqlCommandGenerator sqlCommandGenerator;
        readonly IParameterFactory parameterFactory;
        readonly string connectionString;

        public DataAccess(IDatabaseProviderConfig providerConfig, ISqlCommandGenerator sqlCommandGenerator, string connectionString = null, IParameterFactory parameterFactory = null)
        {
            this.sqlCommandGenerator = sqlCommandGenerator;
            this.connectionString = connectionString;
            this.parameterFactory = parameterFactory;
        }

        string ConnectionString => connectionString ?? GetCurrentConnectionString() ?? throw new InvalidOperationException("No connection string is configured (ConnectionStrings:Default).");

        public ISqlCommandGenerator GetSqlCommandGenerator() => sqlCommandGenerator;

        public async Task<IDbConnection> CreateConnection()
        {
            var connection = new TConnection { ConnectionString = ConnectionString };
            await connection.OpenAsync();
            return connection;
        }

        public async Task<IDbConnection> GetOrCreateConnection() =>
            DbTransactionScope.Current is { } scope ? await scope.GetConnection(ConnectionString, CreateConnection) : await CreateConnection();

        async Task<T> Run<T>(string command, CommandType commandType, IDataParameter[] @params, Func<DbCommand, Task<T>> execute)
        {
            var scope = DbTransactionScope.Current;
            var connection = (DbConnection)await GetOrCreateConnection();
            var watch = Stopwatch.StartNew();
            try
            {
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = command;
                    cmd.CommandType = commandType;
                    if (DatabaseContext.Current?.CommandTimeout is { } timeout) cmd.CommandTimeout = timeout;
                    cmd.Transaction = scope?.GetTransaction(connection);
                    foreach (var p in @params ?? new IDataParameter[0]) cmd.Parameters.Add(p);
                    var result = await execute(cmd);
                    cmd.Parameters.Clear();
                    return result;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in running SQL command: {command}", ex);
            }
            finally
            {
                DataAccessProfiler.Record(command, watch.Elapsed.TotalMilliseconds);
                if (scope == null) connection.Dispose();
            }
        }

        public async Task<int> ExecuteNonQuery(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params)
        {
            var result = await Run(command, commandType, @params, cmd => cmd.ExecuteNonQueryAsync());
            DatabaseStateChangeCommand.Raise(command, commandType, @params);
            return result;
        }

        // Buffered, so the connection can close before the caller reads.
        public Task<IDataReader> ExecuteReader(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params) =>
            Run(command, commandType, @params, async cmd =>
            {
                var table = new DataTable();
                using (var reader = await cmd.ExecuteReaderAsync()) table.Load(reader);
                return (IDataReader)table.CreateDataReader();
            });

        public async Task<T> ExecuteScalar<T>(string commandText, CommandType commandType = CommandType.Text, params IDataParameter[] @params)
        {
            var result = await ExecuteScalar(commandText, commandType, @params);
            if (result == null || result is DBNull) return default(T);
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)(target.IsInstanceOfType(result) ? result : Convert.ChangeType(result, target));
        }

        public Task<object> ExecuteScalar(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params) =>
            Run(command, commandType, @params, cmd => cmd.ExecuteScalarAsync());

        public Task<DataTable> ExecuteQuery(string databaseQuery, CommandType commandType = CommandType.Text, params IDataParameter[] @params) =>
            Run(databaseQuery, commandType, @params, async cmd =>
            {
                var table = new DataTable();
                using (var reader = await cmd.ExecuteReaderAsync()) table.Load(reader);
                return table;
            });

        public async Task<int> ExecuteBulkNonQueries(CommandType commandType, List<KeyValuePair<string, IDataParameter[]>> commands)
        {
            var total = 0;
            foreach (var command in commands) total += await ExecuteNonQuery(command.Key, commandType, command.Value);
            return total;
        }

        public IDataParameter CreateParameter(string name, object value) => CreateParameter(name, value, null);

        public IDataParameter CreateParameter(string name, object value, DbType? dbType)
        {
            value = ToDatabaseValue(value);
            if (parameterFactory != null) return parameterFactory.CreateParameter(name, value, dbType);
            using (var connection = new TConnection())
            using (var cmd = connection.CreateCommand())
            {
                var parameter = cmd.CreateParameter();
                parameter.ParameterName = name.StartsWith("@") ? name : "@" + name;
                parameter.Value = value;
                if (dbType.HasValue) parameter.DbType = dbType.Value;
                return parameter;
            }
        }

        internal static object ToDatabaseValue(object value)
        {
            switch (value)
            {
                case null: return DBNull.Value;
                case Enum e: return Convert.ToInt32(e);
                case Blob blob: return blob.IsEmpty() ? (object)DBNull.Value : blob.FileName;
                case IEntity entity: return entity.GetId() ?? DBNull.Value;
                case DateTime date: return date.GetSqlSafeValue();
                default: return value;
            }
        }
    }

    public class DatabaseStateChangeCommand
    {
        public static event Action<DatabaseStateChangeCommand> ExecutedChangeCommand;

        public string CommandText { get; private set; }

        public CommandType CommandType { get; private set; }

        public IDataParameter[] Params { get; private set; }

        internal static void Raise(string command, CommandType type, IDataParameter[] @params) =>
            ExecutedChangeCommand?.Invoke(new DatabaseStateChangeCommand { CommandText = command, CommandType = type, Params = @params });
    }

    public class DataAccessProfiler
    {
        static readonly ConcurrentDictionary<string, ConcurrentBag<double>> Timings = new ConcurrentDictionary<string, ConcurrentBag<double>>();

        static bool Enabled => Context.Current.GetOptionalService<IDatabaseProviderConfig>()?.Configuration.Profile ?? false;

        internal static void Record(string command, double milliseconds)
        {
            try
            {
                if (!Enabled) return;
            }
            catch (InvalidOperationException) { return; }
            Timings.GetOrAdd(command, _ => new ConcurrentBag<double>()).Add(milliseconds);
        }

        public static void Reset() => Timings.Clear();

        public static ReportRow[] GenerateReport(bool snapshot = false)
        {
            var result = Timings.Select(t =>
            {
                var values = t.Value.OrderBy(v => v).ToArray();
                return new ReportRow
                {
                    Command = t.Key,
                    Calls = values.Length,
                    Total = values.Sum(),
                    Average = values.Average(),
                    Median = values[values.Length / 2],
                    Longest = values.Last(),
                };
            }).OrderByDescending(r => r.Total).ToArray();
            if (!snapshot) Reset();
            return result;
        }

        public class ReportRow
        {
            public string Command { get; internal set; }
            public int Calls { get; internal set; }
            public double Total { get; internal set; }
            public double Average { get; internal set; }
            public double Median { get; internal set; }
            public double Longest { get; internal set; }
        }
    }
}
