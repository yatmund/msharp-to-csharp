using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Platform.Entities.Data
{
    public class DataProvider : IDataProvider
    {
        public ICache Cache;
        readonly IDataAccess access;
        string connectionString;

        public DataProvider(Type type, ICache cache, IDataAccess access, ISqlCommandGenerator sqlCommandGenerator)
        {
            EntityType = type;
            Cache = cache;
            this.access = access;
            SqlCommandGenerator = sqlCommandGenerator;
            MetaData = DataProviderMetaData.For(type);
        }

        public static IDatabase Database => Context.Current.Database();

        public IDataAccess Access => connectionString == null ? access : DataAccess.Create(access.GetType().GetGenericArguments().FirstOrDefault() ?? typeof(object), connectionString);

        public Type EntityType { get; }

        public string ConnectionStringKey { get; set; }

        public string ConnectionString
        {
            get => connectionString ?? (ConnectionStringKey == null ? null : new ConnectionStringProvider().GetConnectionString(ConnectionStringKey));
            set => connectionString = value;
        }

        public ISqlCommandGenerator SqlCommandGenerator { get; }

        public IDataProviderMetaData MetaData { get; }

        DataProviderMetaData Meta => (DataProviderMetaData)MetaData;

        public string DeleteCommand => SqlCommandGenerator.GenerateDeleteCommand(MetaData);

        public string UpdateCommand => SqlCommandGenerator.GenerateUpdateCommand(MetaData);

        public string InsertCommand => SqlCommandGenerator.GenerateInsertCommand(MetaData);

        public Func<IEntity, Task> UpdateSelf => Save;

        IEnumerable<DataProviderMetaData> Hierarchy => Meta.BaseClassesInOrder.Cast<DataProviderMetaData>().Concat(new[] { Meta });

        // The root table plus every derived table, so a base-type query can load the most derived type.
        IEnumerable<DataProviderMetaData> LoadedTables
        {
            get
            {
                var root = Hierarchy.First();
                return new[] { root }.Concat(Descendants(root));
            }
        }

        static IEnumerable<DataProviderMetaData> Descendants(DataProviderMetaData meta) =>
            meta.DrivedClasses.Cast<DataProviderMetaData>().SelectMany(d => new[] { d }.Concat(Descendants(d)));

        public string GetFields() => string.Join(", ", LoadedTables.SelectMany(m => m.GetPropertiesForFillData().Select(p =>
            $"{m.TableAlias}.{SqlCommandGenerator.SafeId(p.Name)} as {m.TableAlias}_{p.Name.TrimStart('.')}")));

        public virtual string GetTables(string prefix = null)
        {
            var tables = LoadedTables.ToList();
            var root = tables[0];
            var result = $"{root.FullTableName} AS {SqlCommandGenerator.SafeId(prefix + root.TableAlias)}";
            foreach (var table in tables.Skip(1))
                result += $" LEFT OUTER JOIN {table.FullTableName} AS {SqlCommandGenerator.SafeId(prefix + table.TableAlias)} ON {SqlCommandGenerator.SafeId(prefix + table.TableAlias)}.[ID] = {SqlCommandGenerator.SafeId(prefix + root.TableAlias)}.[ID]";
            return result;
        }

        public virtual string MapColumn(string propertyName)
        {
            if (propertyName == "ID") return $"{Meta.TableAlias}.{SqlCommandGenerator.SafeId("ID")}";
            foreach (var table in Hierarchy.Reverse())
            {
                var property = table.Properties.FirstOrDefault(p => p.Name == propertyName && (p.IsUserDefined || p.IsDeleted));
                if (property != null) return $"{table.TableAlias}.{SqlCommandGenerator.SafeId(property.Name)}";
            }
            return $"{Meta.TableAlias}.{SqlCommandGenerator.SafeId(propertyName)}";
        }

        public virtual string MapSubquery(string path, string parent) => throw new NotSupportedException("Nested association criteria aren't supported: " + path);

        protected virtual string SafeId(string objectName) => SqlCommandGenerator.SafeId(objectName);

        public virtual string GenerateSelectCommand(IDatabaseQuery iquery) => GenerateSelectCommand(iquery, GetFields());

        public virtual string GenerateSelectCommand(IDatabaseQuery iquery, string fields) => SqlCommandGenerator.GenerateSelectCommand(iquery, GetTables(iquery.AliasPrefix), fields);

        public string GenerateCountCommand(IDatabaseQuery iquery) => $"SELECT Count(*) FROM {GetTables(iquery.AliasPrefix)}{SqlCommandGenerator.GenerateWhere(iquery)}";

        public string GenerateAggregateQuery(IDatabaseQuery query, AggregateFunction function, string propertyName)
        {
            var sqlFunction = function == AggregateFunction.Average ? "AVG" : function.ToString().ToUpperInvariant();
            return $"SELECT {sqlFunction}({query.Column(propertyName)}) FROM {GetTables(query.AliasPrefix)}{SqlCommandGenerator.GenerateWhere(query)}";
        }

        public virtual string GenerateWhere(DatabaseQuery query) => SqlCommandGenerator.GenerateWhere(query);

        public virtual string GenerateSort(DatabaseQuery query) => SqlCommandGenerator.GenerateSort(query);

        public IDataParameter[] GenerateParameters(Dictionary<string, object> parametersData) => parametersData.Select(GenerateParameter).ToArray();

        public virtual IDataParameter GenerateParameter(KeyValuePair<string, object> data) => Access.CreateParameter(data.Key, data.Value);

        public Task<IDataReader> ExecuteGetListReader(IDatabaseQuery query) =>
            Access.ExecuteReader(GenerateSelectCommand(query), CommandType.Text, GenerateParameters(query.Parameters));

        public virtual async Task<IEnumerable<IEntity>> GetList(IDatabaseQuery query)
        {
            var result = new List<IEntity>();
            using (var reader = await ExecuteGetListReader(query))
                while (reader.Read()) result.Add(Parse(reader));
            foreach (var entity in result) await Loaded(entity);
            return result;
        }

        public async Task<IEntity> Get(object objectID)
        {
            var query = new DatabaseQuery(EntityType);
            query.Criteria.Add(new Criterion("ID", FilterFunction.Is, objectID));
            var result = (await GetList(query)).FirstOrDefault();
            if (result == null) throw new DataException($"There is no {EntityType.Name} record with the ID of '{objectID}'.");
            return result;
        }

        static async Task Loaded(IEntity entity)
        {
            foreach (var interceptor in Context.Current.GetServices<IEntityLoadedInterceptor>()) await interceptor.Process(entity);
            await Entity.Services.RaiseOnLoaded(entity);
        }

        public virtual IEntity Parse(IDataReader reader)
        {
            var columns = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, i => i, StringComparer.OrdinalIgnoreCase);
            var actual = LoadedTables.Where(t => columns.TryGetValue(t.TableAlias + "_ID", out var i) && !reader.IsDBNull(i))
                .Select(t => t.Type).Where(t => EntityType.IsAssignableFrom(t)).LastOrDefault() ?? EntityType;
            var entity = (IEntity)Activator.CreateInstance(actual, nonPublic: true);
            foreach (var table in DataProviderMetaData.For(actual).BaseClassesInOrder.Cast<DataProviderMetaData>().Concat(new[] { DataProviderMetaData.For(actual) }))
                foreach (var property in table.GetPropertiesForFillData())
                    if (columns.TryGetValue(table.TableAlias + "_" + property.Name.TrimStart('.'), out var index))
                        property.Accessor?.Set(entity, reader, index);
            Entity.Services.SetSaved(entity);
            Entity.Services.SetOriginalId(entity);
            return entity;
        }

        public Task<int> Count(IDatabaseQuery query) =>
            Access.ExecuteScalar<int>(GenerateCountCommand(query), CommandType.Text, GenerateParameters(query.Parameters));

        public Task<object> Aggregate(IDatabaseQuery query, AggregateFunction function, string propertyName) =>
            Access.ExecuteScalar(GenerateAggregateQuery(query, function, propertyName), CommandType.Text, GenerateParameters(query.Parameters));

        public virtual async Task Save(IEntity record)
        {
            foreach (var interceptor in Context.Current.GetServices<IEntitySavingInterceptor>()) await interceptor.Process(record);
            var tables = DataProviderMetaData.For(record.GetType()).BaseClassesInOrder.Cast<DataProviderMetaData>().Concat(new[] { DataProviderMetaData.For(record.GetType()) }).ToList();
            if (record.IsNew)
                foreach (var table in tables) await Insert(record, table);
            else
                foreach (var table in tables) await Update(record, table);
        }

        async Task Insert(IEntity record, DataProviderMetaData table)
        {
            var parameters = table.GetPropertiesForInsert().Select(p => Access.CreateParameter(p.ParameterName, p.Accessor?.Get(record))).ToArray();
            var command = SqlCommandGenerator.GenerateInsertCommand(table);
            if (!table.HasAutoNumber)
            {
                await Access.ExecuteNonQuery(command, CommandType.Text, parameters);
                return;
            }
            var generated = await Access.ExecuteScalar(command, CommandType.Text, parameters);
            table.AutoNumberProperty.Accessor?.Set(record, generated);
        }

        async Task Update(IEntity record, DataProviderMetaData table)
        {
            var parameters = table.UserDefienedAndDeletedProperties.Where(p => !p.IsAutoNumber && !p.IsComputed)
                .Select(p => Access.CreateParameter(p.ParameterName, p.Accessor?.Get(record)))
                .Concat(new[] { Access.CreateParameter("OriginalId", OriginalId(record)) }).ToArray();
            var rows = await Access.ExecuteNonQuery(SqlCommandGenerator.GenerateUpdateCommand(table), CommandType.Text, parameters);
            if (rows == 0) throw new ConcurrencyException($"Failed to update the '{table.TableName}' table. There is no row with the ID of {OriginalId(record)}.");
        }

        static object OriginalId(IEntity record) => record.GetType().GetProperty("OriginalId")?.GetValue(record) ?? record.GetId();

        public virtual async Task Delete(IEntity record)
        {
            var meta = DataProviderMetaData.For(record.GetType());
            var tables = meta.BaseClassesInOrder.Cast<DataProviderMetaData>().Concat(new[] { meta }).Reverse();
            foreach (var table in tables)
                await Access.ExecuteNonQuery(SqlCommandGenerator.GenerateDeleteCommand(table), CommandType.Text, Access.CreateParameter("Id", record.GetId()));
        }

        public virtual async Task<IEnumerable<string>> ReadManyToManyRelation(IEntity instance, string property)
        {
            var bridge = EntityType.GetProperty(property)?.GetCustomAttribute<BridgTableAttribute>();
            if (bridge == null) return Enumerable.Empty<string>();
            var column = EntityType.GetProperty(property)?.GetCustomAttribute<BridgColumnAttribute>()?.ColumnName ?? property;
            var result = new List<string>();
            using (var reader = await Access.ExecuteReader($"SELECT {SafeId(column)} FROM {bridge.TableName} WHERE {SafeId(EntityType.Name)} = @ID", CommandType.Text, Access.CreateParameter("ID", instance.GetId())))
                while (reader.Read()) result.Add(reader.GetValue(0)?.ToString());
            return result;
        }

        public IDictionary<string, Tuple<string, string>> GetUpdatedValues(IEntity original, IEntity updated) =>
            new Audit.EntityProcessor().GetChanges(original, updated);

        public Task<int> ExecuteNonQuery(string command) => Access.ExecuteNonQuery(command);

        public Task<int> ExecuteNonQuery(string command, CommandType commandType, params IDataParameter[] @params) => Access.ExecuteNonQuery(command, commandType, @params);

        public Task<IDataReader> ExecuteReader(string command, CommandType commandType, params IDataParameter[] @params) => Access.ExecuteReader(command, commandType, @params);

        public Task<object> ExecuteScalar(string command) => Access.ExecuteScalar(command);

        public Task<object> ExecuteScalar(string command, CommandType commandType, params IDataParameter[] @params) => Access.ExecuteScalar(command, commandType, @params);

        public bool SupportValidationBypassing() => true;

        public virtual async Task BulkInsert(IEntity[] entities, int batchSize)
        {
            foreach (var entity in entities) await Save(entity);
        }

        public async Task BulkUpdate(IEntity[] entities, int batchSize)
        {
            foreach (var entity in entities) await Save(entity);
        }

        public static List<string> ExtractIds(string idsXml) =>
            string.IsNullOrEmpty(idsXml) ? new List<string>() : XElement.Parse("<r>" + idsXml + "</r>").Descendants().Select(e => e.Attribute("ID")?.Value ?? e.Value).ToList();

        public virtual DirectDatabaseCriterion GetAssociationInclusionCriteria(IDatabaseQuery masterQuery, PropertyInfo association)
        {
            var subquery = masterQuery.Provider.GenerateSelectCommand(masterQuery, masterQuery.Column(association.Name + "Id"));
            return new DirectDatabaseCriterion($"{MapColumn("ID")} IN ({subquery})", masterQuery.Parameters);
        }

        protected virtual string GenerateAssociationLoadingCriteria(string id, string uniqueItems, PropertyInfo association) =>
            $"{MapColumn("ID")} IN ({uniqueItems})";
    }

    /// <summary>Queries on an interface run against every entity type implementing it.</summary>
    public class InterfaceDataProvider : LimitedDataProvider
    {
        readonly Type interfaceType;

        public InterfaceDataProvider(Type interfaceType) => this.interfaceType = interfaceType;

        public override Type EntityType => interfaceType;

        IEnumerable<IDataProvider> Providers => Context.Current.GetService<IDatabaseProviderConfig>().ResolveDataProviders(interfaceType);

        public override async Task<int> Count(IDatabaseQuery query)
        {
            var total = 0;
            foreach (var provider in Providers) total += await provider.Count(query.CloneFor(provider.EntityType));
            return total;
        }

        public override async Task<IEnumerable<IEntity>> GetList(IDatabaseQuery query)
        {
            var result = new List<IEntity>();
            foreach (var provider in Providers) result.AddRange(await provider.GetList(query.CloneFor(provider.EntityType)));
            return result;
        }

        public override async Task<IEntity> Get(object objectID)
        {
            foreach (var provider in Providers)
            {
                var query = new DatabaseQuery(provider.EntityType);
                query.Criteria.Add(new Criterion("ID", FilterFunction.Is, objectID));
                var found = (await provider.GetList(query)).FirstOrDefault();
                if (found != null) return found;
            }
            throw new DataException($"There is no {interfaceType.Name} record with the ID of '{objectID}'.");
        }
    }

    public class DataProviderFactory : IDataProviderFactory
    {
        static readonly ConcurrentDictionary<Type, DataProvider> Providers = new ConcurrentDictionary<Type, DataProvider>();
        readonly DatabaseConfig.ProviderMapping mapping;
        readonly Type mappedType;

        public DataProviderFactory(Type type) => mappedType = type;

        public DataProviderFactory(DatabaseConfig.ProviderMapping mapping)
        {
            this.mapping = mapping;
            mappedType = mapping.GetMappedType();
        }

        public string ConnectionString => mapping?.ConnectionString ?? (mapping?.ConnectionStringKey == null ? null : new ConnectionStringProvider().GetConnectionString(mapping.ConnectionStringKey));

        public IDataAccess GetAccess() => string.IsNullOrEmpty(mapping?.SqlClient) ? DataAccess.Create(connectionString: ConnectionString) : DataAccess.Create(mapping.SqlClient, ConnectionString);

        public IDataProvider GetProvider(Type type)
        {
            var access = GetAccess();
            return GetOrCreate(type, Context.Current.GetService<ICache>(), access, access.GetSqlCommandGenerator());
        }

        protected virtual bool IsRelevant(Type type) => mappedType == null || mappedType.IsAssignableFrom(type);

        internal bool Relevant(Type type) => IsRelevant(type);

        public virtual bool SupportsPolymorphism() => true;

        public static DataProvider GetOrCreate(Type type, ICache cache, IDataAccess access, ISqlCommandGenerator sqlCommandGenerator) =>
            Providers.GetOrAdd(type, t => new DataProvider(t, cache, access, sqlCommandGenerator));
    }

    public class AssociationInclusion
    {
        readonly List<string> nested = new List<string>();

        public PropertyInfo Association { get; set; }

        public static AssociationInclusion Create(PropertyInfo association) => new AssociationInclusion { Association = association };

        public void IncludeNestedAssociation(string nestedAssociation) => nested.Add(nestedAssociation);

        public async Task LoadAssociations(DatabaseQuery query, IEnumerable<IEntity> mainObjects)
        {
            var idProperty = query.EntityType.GetProperty(Association.Name + "Id");
            if (idProperty == null) return;
            var ids = mainObjects.Select(o => idProperty.GetValue(o)).Where(id => id != null).Distinct().ToArray();
            if (ids.Length == 0) return;
            var associated = Context.Current.Database().Of(Association.PropertyType);
            associated.Where(new Criterion("ID", FilterFunction.In, ids));
            if (nested.Any()) associated.Include(nested);
            var loaded = (await associated.GetList()).ToDictionary(e => e.GetId());
            var cache = Context.Current.Database().Cache;
            foreach (var entity in loaded.Values) cache?.Add(entity);
        }
    }

    public class ConcurrencyException : ValidationException
    {
        public ConcurrencyException() { }

        public ConcurrencyException(string message) : base(message) { }
    }
}

namespace Platform.Entities
{
    using Platform.Entities.Data;

    public class LimitedDataProvider : IDataProvider
    {
        public virtual IDataAccess Access => throw new NotSupportedException();
        public virtual string ConnectionString { get; set; }
        public virtual string ConnectionStringKey { get; set; }
        public virtual Type EntityType => throw new NotSupportedException();

        public virtual Task<object> Aggregate(IDatabaseQuery query, AggregateFunction function, string propertyName) => throw new NotSupportedException();
        public virtual Task BulkInsert(IEntity[] entities, int batchSize) => throw new NotSupportedException();
        public virtual Task BulkUpdate(IEntity[] entities, int batchSize) => throw new NotSupportedException();
        public virtual Task<int> Count(IDatabaseQuery query) => throw new NotSupportedException();
        public virtual Task Delete(IEntity record) => throw new NotSupportedException();
        public virtual Task<int> ExecuteNonQuery(string command) => throw new NotSupportedException();
        public virtual Task<object> ExecuteScalar(string command) => throw new NotSupportedException();
        public virtual Task<IEntity> Get(object objectID) => throw new NotSupportedException();
        public virtual DirectDatabaseCriterion GetAssociationInclusionCriteria(IDatabaseQuery masterQuery, PropertyInfo association) => throw new NotSupportedException();
        public virtual Task<IEnumerable<IEntity>> GetList(IDatabaseQuery query) => throw new NotSupportedException();
        public virtual IDictionary<string, Tuple<string, string>> GetUpdatedValues(IEntity original, IEntity updated) => throw new NotSupportedException();
        public virtual string MapColumn(string propertyName) => throw new NotSupportedException();
        public virtual string MapSubquery(string path, string parent) => throw new NotSupportedException();
        public virtual Task<IEnumerable<string>> ReadManyToManyRelation(IEntity instance, string property) => throw new NotSupportedException();
        public virtual Task Save(IEntity record) => throw new NotSupportedException();
        public virtual bool SupportValidationBypassing() => false;
        public string GenerateSelectCommand(IDatabaseQuery iquery, string fields) => throw new NotSupportedException();
    }
}
