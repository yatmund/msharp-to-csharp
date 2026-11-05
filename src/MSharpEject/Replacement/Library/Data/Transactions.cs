using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using IsolationLevel = System.Data.IsolationLevel;

namespace Platform.Entities.Data
{
    /// <summary>
    /// A database transaction that follows the async flow. Nested Required scopes join the outer one; only the root commits,
    /// and only if every scope in it called Complete().
    /// </summary>
    public class DbTransactionScope : ITransactionScope
    {
        static readonly AsyncLocal<DbTransactionScope> current = new AsyncLocal<DbTransactionScope>();
        readonly Dictionary<string, (DbConnection Connection, DbTransaction Transaction)> connections = new Dictionary<string, (DbConnection, DbTransaction)>();
        readonly List<Action> completedHandlers = new List<Action>();
        readonly IsolationLevel isolationLevel;
        readonly DbTransactionScope previous;
        readonly DbTransactionScope owner;
        bool completed, aborted;

        public DbTransactionScope() : this(IsolationLevel.ReadCommitted) { }

        public DbTransactionScope(DbTransactionScopeOption scopeOption) : this(IsolationLevel.ReadCommitted, scopeOption) { }

        public DbTransactionScope(IsolationLevel isolationLevel, DbTransactionScopeOption scopeOption = DbTransactionScopeOption.Required)
        {
            this.isolationLevel = isolationLevel;
            previous = current.Value;
            if (scopeOption == DbTransactionScopeOption.Suppress)
            {
                current.Value = null;
                owner = null;
                return;
            }
            owner = scopeOption == DbTransactionScopeOption.Required && previous != null ? previous.owner ?? previous : null;
            current.Value = this;
        }

        public Guid ID { get; } = Guid.NewGuid();

        public static DbTransactionScope Current { get => current.Value; set => current.Value = value; }

        public static DbTransactionScope Root { get => current.Value?.owner ?? current.Value; set => current.Value = value; }

        public static DbTransactionScope Parent { get => current.Value?.previous; set { } }

        DbTransactionScope RootScope => owner ?? this;

        internal async Task<IDbConnection> GetConnection(string connectionString, Func<Task<IDbConnection>> create)
        {
            var root = RootScope;
            if (root.connections.TryGetValue(connectionString, out var existing)) return existing.Connection;
            var connection = (DbConnection)await create();
            root.connections[connectionString] = (connection, connection.BeginTransaction(isolationLevel));
            return connection;
        }

        internal DbTransaction GetTransaction(DbConnection connection) =>
            RootScope.connections.Values.FirstOrDefault(c => c.Connection == connection).Transaction;

        public void OnTransactionCompleted(Action eventHandler) => RootScope.completedHandlers.Add(eventHandler);

        public void Complete() => completed = true;

        public void Dispose()
        {
            current.Value = previous;
            if (owner != null)
            {
                if (!completed) owner.aborted = true;
                return;
            }

            var commit = completed && !aborted;
            foreach (var (connection, transaction) in connections.Values)
            {
                try
                {
                    if (commit) transaction.Commit();
                    else transaction.Rollback();
                }
                finally
                {
                    transaction.Dispose();
                    connection.Dispose();
                }
            }
            connections.Clear();
            if (commit) foreach (var handler in completedHandlers) handler();
        }
    }

    public class TransactionScopeWrapper : ITransactionScope
    {
        readonly TransactionScope scope;

        public TransactionScopeWrapper(TransactionScope scope) => this.scope = scope;

        public Guid ID { get; } = Guid.NewGuid();

        public void Complete() => scope.Complete();

        public void Dispose() => scope.Dispose();
    }

    public static class TransactionExtensions
    {
        public static TransactionScope CreateScope(this System.Transactions.IsolationLevel @this) => @this.CreateScope(TransactionScopeOption.Required);

        public static TransactionScope CreateScope(this System.Transactions.IsolationLevel @this, TransactionScopeOption scopeOption) =>
            new TransactionScope(scopeOption, new TransactionOptions { IsolationLevel = @this }, TransactionScopeAsyncFlowOption.Enabled);
    }

    public interface IReferenceData
    {
        Task Create();
    }

    public interface IDatabaseServer
    {
        string GetDatabaseName();
        string GetDataSource();
        bool Exists(string tempDatabaseName, string fullName);
        void ClearConnectionPool();
        void Delete(string database);
        void Execute(string script, string database = null);
    }

    public abstract class DatabaseServer : IDatabaseServer
    {
        public abstract void Delete(string database);

        public abstract void Execute(string script, string database = null);

        public abstract bool Exists(string database, string filePath);

        public abstract void ClearConnectionPool();

        static DbConnectionStringBuilder Builder() => new DbConnectionStringBuilder { ConnectionString = DataAccess.GetCurrentConnectionString() };

        public string GetDataSource() => Builder().TryGetValue("Data Source", out var v) || Builder().TryGetValue("Server", out v) ? v?.ToString() : null;

        public string GetDatabaseName() => Builder().TryGetValue("Initial Catalog", out var v) || Builder().TryGetValue("Database", out v) ? v?.ToString() : null;
    }
}
