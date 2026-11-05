using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Platform.Entities
{
    public enum AggregateFunction { Max = 0, Min = 1, Sum = 2, Average = 3 }

    public enum DbTransactionScopeOption { Required = 0, RequiresNew = 1, Suppress = 2 }

    public interface ITransactionScope : IDisposable
    {
        void Complete();

        Guid ID { get; }
    }

    public interface ICache
    {
        void Add(IEntity entity);
        void Remove(IEntity entity);
        void Remove(Type type, bool invalidateCachedReferences = false);
        void RemoveList(Type type);
        IEnumerable GetList(Type type);
        void ClearAll();
        void AddList(Type type, IEnumerable list);
        bool IsUpdatedSince(IEntity instance, DateTime since);
        void UpdateRowVersion(IEntity entity);
        TEntity Get<TEntity>(object id) where TEntity : IEntity;
        IEntity Get(Type type, string id);
        DateTime? GetQueryTimestamp();
        bool IsCacheable(Type type);
    }

    public interface IDataAccess
    {
        Task<IDbConnection> GetOrCreateConnection();
        Task<IDbConnection> CreateConnection();
        Task<IDataReader> ExecuteReader(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params);
        Task<DataTable> ExecuteQuery(string databaseQuery, CommandType commandType = CommandType.Text, params IDataParameter[] @params);
        Task<int> ExecuteNonQuery(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params);
        Task<object> ExecuteScalar(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params);
        Task<T> ExecuteScalar<T>(string command, CommandType commandType = CommandType.Text, params IDataParameter[] @params);
        Task<int> ExecuteBulkNonQueries(CommandType commandType, List<KeyValuePair<string, IDataParameter[]>> commands);
        IDataParameter CreateParameter(string name, object value);
        IDataParameter CreateParameter(string name, object value, DbType? dbType);
        ISqlCommandGenerator GetSqlCommandGenerator();
    }

    public interface ISqlCommandGenerator
    {
        string SafeId(string objectName);
        string UnescapeId(string id);
        string GenerateSelectCommand(IDatabaseQuery iquery, string tables, string fields);
        string GenerateSort(IDatabaseQuery query);
        string GeneratePagination(IDatabaseQuery query);
        string GenerateWhere(IDatabaseQuery query);
        string GenerateUpdateCommand(IDataProviderMetaData metaData);
        string GenerateInsertCommand(IDataProviderMetaData metaData);
        string GenerateDeleteCommand(IDataProviderMetaData metaData);
    }

    public interface IDataProvider
    {
        Task<IEntity> Get(object objectID);
        Task Save(IEntity record);
        Task Delete(IEntity record);
        Task<IEnumerable<IEntity>> GetList(IDatabaseQuery query);
        DirectDatabaseCriterion GetAssociationInclusionCriteria(IDatabaseQuery masterQuery, PropertyInfo association);
        Task<int> Count(IDatabaseQuery query);
        Task<object> Aggregate(IDatabaseQuery query, AggregateFunction function, string propertyName);
        string MapColumn(string propertyName);
        string MapSubquery(string path, string parent);
        Task<IEnumerable<string>> ReadManyToManyRelation(IEntity instance, string property);
        IDictionary<string, Tuple<string, string>> GetUpdatedValues(IEntity original, IEntity updated);
        Task<int> ExecuteNonQuery(string command);
        Task<object> ExecuteScalar(string command);
        bool SupportValidationBypassing();
        Task BulkInsert(IEntity[] entities, int batchSize);
        Task BulkUpdate(IEntity[] entities, int batchSize);
        string GenerateSelectCommand(IDatabaseQuery iquery, string fields);
        IDataAccess Access { get; }
        Type EntityType { get; }
        string ConnectionString { get; set; }
        string ConnectionStringKey { get; set; }
    }

    public interface IDataProviderFactory
    {
        IDataProvider GetProvider(Type type);
        bool SupportsPolymorphism();
        IDataAccess GetAccess();
        string ConnectionString { get; }
    }

    public interface IPropertyAccessor
    {
        void Set(IEntity entity, IDataReader reader, int index);
        void Set(IEntity entity, object value);
        object Get(IEntity entity);
    }

    public interface IPropertyData
    {
        string Name { get; }
        string ParameterName { get; }
        bool IsAutoNumber { get; }
        bool IsDeleted { get; }
        bool IsUserDefined { get; }
        bool IsComputed { get; }
        Type AssociateType { get; }
        bool IsDefaultId { get; }
        IPropertyAccessor Accessor { get; }
        PropertyInfo PropertyInfo { get; }
        bool IsCustomPrimaryKey { get; }
    }

    public interface IDataProviderMetaData
    {
        IPropertyData[] Properties { get; }
        IEnumerable<IPropertyData> UserDefienedProperties { get; }
        IEnumerable<IPropertyData> UserDefienedAndIdAndDeletedProperties { get; }
        IEnumerable<IPropertyData> UserDefienedAndDeletedProperties { get; }
        bool IsSoftDeleteEnabled { get; }
        string IdColumnName { get; }
        string TableName { get; }
        string Schema { get; }
        string TableAlias { get; }
        IDataProviderMetaData[] BaseClassesInOrder { get; }
        bool HasAutoNumber { get; }
        IPropertyData AutoNumberProperty { get; }
        Type Type { get; }
        IDataProviderMetaData[] DrivedClasses { get; }
        IEnumerable<IPropertyData> AssociateProperties { get; }
    }

    public class OrderByPart
    {
        public string Property;
        public bool Descending;

        public override string ToString() => Property + (Descending ? " DESC" : "");
    }

    public class SqlConversionContext
    {
        public Type Type;
        public string Alias;
        public IDatabaseQuery Query;
        public Func<string, string> ToSafeId;
    }

    public interface IDatabaseQuery
    {
        string MapColumn(string propertyName);
        string Column(string propertyName, string alias = null);
        IDatabaseQuery Include(string associationProperty);
        IDatabaseQuery Include(IEnumerable<string> associationProperties);
        IDatabaseQuery Where(params ICriterion[] criteria);
        IDatabaseQuery WhereIn(string myField, IDatabaseQuery subquery, string targetField);
        IDatabaseQuery WhereNotIn(string myField, IDatabaseQuery subquery, string targetField);
        IDatabaseQuery OrderBy(string property, bool descending);
        IDatabaseQuery ThenBy(string property, bool descending);
        IDatabaseQuery Top(int rows);
        IDatabaseQuery OrderBy(string property);
        Task<IEntity> WithMax(string property);
        Task<IEntity> WithMin(string property);
        IDatabaseQuery CloneFor(Type entityType);
        Task<int> Count();
        Task<bool> Any();
        Task<bool> None();
        Task<IEntity[]> GetList();
        Task<IEntity> FirstOrDefault();
        IDatabaseQuery Select(params string[] columns);
        Type EntityType { get; }
        Dictionary<string, object> Parameters { get; }
        int PageStartIndex { get; set; }
        int? PageSize { get; set; }
        int? TakeTop { get; set; }
        string AliasPrefix { get; set; }
        IDataProvider Provider { get; }
        List<ICriterion> Criteria { get; }
        List<OrderByPart> OrderByParts { get; }
    }

    public interface IDatabaseQuery<TEntity> : IDatabaseQuery where TEntity : IEntity
    {
        IDatabaseQuery<TEntity> Where(Expression<Func<TEntity, bool>> criteria);
        new Task<TEntity[]> GetList();
        new Task<TEntity> FirstOrDefault();
        new IDatabaseQuery<TEntity> OrderBy(string property);
        new IDatabaseQuery<TEntity> Top(int rows);
        new IDatabaseQuery<TEntity> Where(params ICriterion[] criteria);
        new IDatabaseQuery<TEntity> Select(params string[] columns);
        IDatabaseQuery<TEntity> ThenBy(Expression<Func<TEntity, object>> property, bool descending = false);
        IDatabaseQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object>> property);
        IDatabaseQuery<TEntity> OrderBy(Expression<Func<TEntity, object>> property, bool descending = false);
        IDatabaseQuery<TEntity> ThenByDescending(Expression<Func<TEntity, object>> property);
        IDatabaseQuery<TEntity> Include(Expression<Func<TEntity, object>> property);
        new Task<TEntity> WithMax(string property);
        new Task<TEntity> WithMin(string property);
        Task<TEntity> WithMax(Expression<Func<TEntity, object>> property);
        Task<TEntity> WithMin(Expression<Func<TEntity, object>> property);
        Task<TOutput?> Aggregate<TProperty, TOutput>(AggregateFunction function, Expression<Func<TEntity, TProperty?>> property) where TProperty : struct where TOutput : struct;
        Task<TProperty?> Max<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct;
        Task<TProperty?> Max<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct;
        Task<TProperty?> Min<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct;
        Task<TProperty?> Min<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct;
        Task<TProperty?> Sum<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct;
        Task<TProperty?> Sum<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct;
        Task<decimal?> Sum(Expression<Func<TEntity, int?>> property);
        Task<TProperty?> Average<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct;
        Task<TProperty?> Average<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct;
        Task<decimal?> Average(Expression<Func<TEntity, int?>> property);
        Task<bool> Contains(TEntity item);
        Task<bool> ContainsAny(TEntity[] items);
        Task<bool> Any(Expression<Func<TEntity, bool>> criteria);
    }

    public interface IDatabase
    {
        Task Refresh();
        bool AnyOpenTransaction();
        Task EnlistOrCreateTransaction(Func<Task> action);
        Task<T> Parse<T>(string toString, bool caseSensitive = false) where T : IEntity;
        Task<IEnumerable<string>> ReadManyToManyRelation(IEntity instance, string property);
        Task<T> Reload<T>(T instance) where T : IEntity;
        Task<bool> Any<T>() where T : IEntity;
        Task<bool> Any<T>(Expression<Func<T, bool>> criteria) where T : IEntity;
        Task<bool> None<T>() where T : IEntity;
        Task<bool> None<T>(Expression<Func<T, bool>> criteria) where T : IEntity;
        Task<int> Count<T>(Expression<Func<T, bool>> criteria) where T : IEntity;
        Task Delete(IEntity instance);
        Task Delete(IEntity instance, DeleteBehaviour behaviour);
        Task Delete<T>(IEnumerable<T> instances) where T : IEntity;
        Task DeleteAll<T>() where T : IEntity;
        Task DeleteAll<T>(Expression<Func<T, bool>> criteria) where T : IEntity;
        Task UpdateAll<T>(Action<T> change) where T : IEntity;
        Task<T> FirstOrDefault<T>(Expression<Func<T, bool>> criteria) where T : IEntity;
        Task<T> FirstOrDefault<T>() where T : IEntity;
        Task<T> Get<T>(string entityId) where T : IEntity;
        Task<T> Get<T>(Guid id) where T : IEntity;
        Task<T> Get<T>(Guid? id) where T : IEntity;
        Task<T> Get<T>(int? id) where T : IEntity<int>;
        Task<T> Get<T>(int id) where T : IEntity<int>;
        Task<T> Get<T>(long? id) where T : IEntity<long>;
        Task<T> Get<T>(long id) where T : IEntity<long>;
        Task<T> Get<T>(short? id) where T : IEntity<short>;
        Task<T> Get<T>(short id) where T : IEntity<short>;
        Task<T> Get<T>(byte? id) where T : IEntity<byte>;
        Task<T> Get<T>(byte id) where T : IEntity<byte>;
        Task<IEntity<Guid>> Get(Guid entityID, Type objectType);
        Task<IEntity> Get(object entityID, Type objectType);
        Task<T> GetOrDefault<T>(object id) where T : IEntity;
        Task<IEntity> GetOrDefault(object id, Type type);
        Task<T[]> GetList<T>(Expression<Func<T, bool>> criteria = null) where T : IEntity;
        ITransactionScope CreateTransactionScope(DbTransactionScopeOption option = DbTransactionScopeOption.Required);
        IDataProvider GetProvider<T>() where T : IEntity;
        IDataProvider GetProvider(IEntity item);
        IDataProvider GetProvider(Type type);
        IDataAccess GetAccess(Type type);
        IDataAccess GetAccess<TEntity>() where TEntity : IEntity;
        IDataAccess GetAccess(string connectionString = null);
        Task<T> Save<T>(T entity) where T : IEntity;
        Task Save(IEntity entity, SaveBehaviour behaviour);
        Task<IEnumerable<T>> Save<T>(List<T> records) where T : IEntity;
        Task<List<T>> Update<T>(IEnumerable<T> items, Action<T> action) where T : IEntity;
        Task<List<T>> Update<T>(IEnumerable<T> items, Action<T> action, SaveBehaviour behaviour) where T : IEntity;
        Task<T> Update<T>(T item, Action<T> action) where T : IEntity;
        Task<T> Update<T>(T item, Action<T> action, SaveBehaviour behaviour) where T : IEntity;
        Task<List<T>> Update<T>(IEnumerable<T> items, Func<T, Task> action) where T : IEntity;
        Task<List<T>> Update<T>(IEnumerable<T> items, Func<T, Task> action, SaveBehaviour behaviour) where T : IEntity;
        Task<T> Update<T>(T item, Func<T, Task> action) where T : IEntity;
        Task<T> Update<T>(T item, Func<T, Task> action, SaveBehaviour behaviour) where T : IEntity;
        Task BulkInsert(Entity[] objects, int batchSize = 10, bool bypassValidation = false);
        Task BulkUpdate(Entity[] objects, int batchSize = 10, bool bypassValidation = false);
        Task<IEnumerable<T>> Save<T>(T[] records) where T : IEntity;
        Task<IEnumerable<T>> Save<T>(IEnumerable<T> records) where T : IEntity;
        Task<IEnumerable<T>> Save<T>(IEnumerable<T> records, SaveBehaviour behaviour) where T : IEntity;
        IDatabaseQuery<TEntity> Of<TEntity>() where TEntity : IEntity;
        IDatabaseQuery Of(Type type);
        ICache Cache { get; }
        event AwaitableEventHandler CacheRefreshed;
        event AwaitableEventHandler<IEntity> Updated;
    }

    public interface IValueConverter<T>
    {
        T ConvertTo(object value);
        object ConvertFrom(T value);
    }

    public interface EnumValueConverter { }

    public class EnumValueConverter<T> : EnumValueConverter, IValueConverter<T> where T : struct
    {
        public object ConvertFrom(T value) => Convert.ToInt32(value);

        public T ConvertTo(object value) =>
            value == null || value is DBNull ? default(T) : value is string s ? (T)Enum.Parse(typeof(T), s, ignoreCase: true) : (T)Enum.ToObject(typeof(T), value);
    }

    public interface ICachedReference
    {
        void Invalidate();
    }

    /// <summary>Caches the entity behind an association ID so repeated property reads don't hit the database.</summary>
    public class CachedReference<TId, TEntity> : ICachedReference where TId : struct where TEntity : Entity<TId>
    {
        TEntity value;
        TId? boundId;

        public TEntity Get(TId? id) => GetAsync(id).GetAwaiter().GetResult();

        public TEntity GetOrDefault(TId? id) => GetOrDefaultAsync(id).GetAwaiter().GetResult();

        public async Task<TEntity> GetAsync(TId? id)
        {
            if (id == null) return null;
            if (value != null && Equals(boundId, id)) return value;
            value = (TEntity)await Context.Current.Database().Get(id.Value, typeof(TEntity));
            boundId = id;
            return value;
        }

        public async Task<TEntity> GetOrDefaultAsync(TId? id)
        {
            if (id == null) return null;
            if (value != null && Equals(boundId, id)) return value;
            value = (TEntity)await Context.Current.Database().GetOrDefault(id.Value, typeof(TEntity));
            boundId = id;
            return value;
        }

        public void Bind(TEntity entity)
        {
            value = entity;
            boundId = entity?.ID;
        }

        public void Invalidate()
        {
            value = null;
            boundId = null;
        }
    }

    public class CachedReference<TEntity> : CachedReference<Guid, TEntity> where TEntity : GuidEntity { }
}
