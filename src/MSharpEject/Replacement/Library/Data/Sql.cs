using System;
using System.Collections.Generic;
using System.Linq;

namespace Platform.Entities.Data
{
    public abstract class SqlCommandGenerator : ISqlCommandGenerator
    {
        public abstract string SafeId(string objectName);

        public abstract string UnescapeId(string id);

        public abstract string GenerateSelectCommand(IDatabaseQuery iquery, string tables, string fields);

        public abstract string GeneratePagination(IDatabaseQuery query);

        public virtual string GenerateSort(IDatabaseQuery query)
        {
            var parts = query.OrderByParts.Select(p => query.Column(p.Property) + (p.Descending ? " DESC" : "")).ToList();
            return parts.Count == 0 ? string.Empty : " ORDER BY " + string.Join(", ", parts);
        }

        public virtual string GenerateWhere(IDatabaseQuery query)
        {
            var meta = DataProviderMetaData.For(query.EntityType.IsInterface ? query.EntityType : query.EntityType);
            var context = new SqlConversionContext { Query = query, Type = query.EntityType, Alias = query.AliasPrefix + meta.TableAlias, ToSafeId = SafeId };
            var conditions = new List<string> { $"{context.Alias}.{SafeId("ID")} IS NOT NULL" };
            conditions.AddRange(query.Criteria.Select(c => SqlOf(c, context)));

            var softDelete = meta.BaseClassesInOrder.Cast<DataProviderMetaData>().Concat(new[] { meta }).FirstOrDefault(m => m.IsSoftDeleteEnabled);
            if (softDelete != null && SoftDeleteAttribute.RequiresSoftdeleteQuery(query.EntityType))
            {
                var name = query.AliasPrefix + softDelete.TableAlias + "__Deleted";
                query.Parameters[name] = false;
                conditions.Add($"{query.AliasPrefix}{softDelete.TableAlias}.{SafeId(".Deleted")} = @{name}");
            }
            return " WHERE " + string.Join(" AND ", conditions);
        }

        internal string SqlOf(ICriterion criterion, SqlConversionContext context) => new Generator(this, context.Query).Generate(criterion);

        sealed class Generator : DatabaseCriterionSqlGenerator
        {
            readonly SqlCommandGenerator owner;

            public Generator(SqlCommandGenerator owner, IDatabaseQuery query) : base(query) => this.owner = owner;

            protected override string ToSafeId(string id) => owner.SafeId(id);

            protected override string UnescapeId(string id) => owner.UnescapeId(id);
        }

        public string GetFullTableName(IDataProviderMetaData metaData) =>
            string.IsNullOrEmpty(metaData.Schema) ? metaData.TableName : metaData.Schema + "." + metaData.TableName;

        public virtual string GenerateUpdateCommand(IDataProviderMetaData metaData)
        {
            var sets = metaData.UserDefienedAndDeletedProperties.Where(p => !p.IsAutoNumber && !p.IsComputed).Select(p => $"{SafeId(p.Name)} = @{p.ParameterName}");
            return $"UPDATE {GetFullTableName(metaData)} SET {string.Join(", ", sets)} WHERE {metaData.IdColumnName} = @OriginalId";
        }

        public virtual string GenerateInsertCommand(IDataProviderMetaData metaData) => GetInsertCommandTemplate(metaData);

        protected virtual string GetInsertCommandTemplate(IDataProviderMetaData metaData)
        {
            var properties = metaData.GetPropertiesForInsert().ToArray();
            var output = metaData.HasAutoNumber ? $" OUTPUT [INSERTED].{metaData.AutoNumberProperty.Name}" : "";
            return $"INSERT INTO {GetFullTableName(metaData)} ({string.Join(", ", properties.Select(p => SafeId(p.Name)))}){output} VALUES ({string.Join(", ", properties.Select(p => "@" + p.ParameterName))})";
        }

        public virtual string GenerateDeleteCommand(IDataProviderMetaData metaData) => $"DELETE FROM {GetFullTableName(metaData)} WHERE {metaData.IdColumnName} = @Id";
    }

    /// <summary>Criterion to SQL, matching Olive's shapes: "IsNot" also matches NULL, HasValue excludes empty strings.</summary>
    public abstract class DatabaseCriterionSqlGenerator
    {
        readonly IDatabaseQuery query;

        protected DatabaseCriterionSqlGenerator(IDatabaseQuery query) => this.query = query;

        public DatabaseCriterionSqlGenerator(DatabaseQuery query) : this((IDatabaseQuery)query) { }

        protected abstract string ToSafeId(string id);

        protected abstract string UnescapeId(string id);

        public string Generate(ICriterion criterion)
        {
            switch (criterion)
            {
                case DirectDatabaseCriterion direct:
                    foreach (var p in direct.Parameters) query.Parameters[p.Key.TrimStart('@')] = p.Value;
                    return direct.SqlCriteria;
                case BinaryCriterion binary:
                    return $"({Generate(binary.Left)} {binary.Operator} {Generate(binary.Right)})";
                case Criterion c when c.SqlCondition != null:
                    return c.SqlCondition;
                case DynamicValueCriterion dynamic:
                    return dynamic.ToSql(new SqlConversionContext { Query = query, ToSafeId = ToSafeId });
            }

            var column = query.Column(criterion.PropertyName);
            var name = ParameterName(column, criterion.PropertyName);
            object Value() => criterion is Criterion c2 ? c2.ValueFor(new SqlConversionContext { Query = query, ToSafeId = ToSafeId }) : criterion.Value;

            switch (criterion.FilterFunction)
            {
                case FilterFunction.Null: return $"{column} IS NULL";
                case FilterFunction.NotNull: return $"{column} IS NOT NULL";
                case FilterFunction.IsNot:
                    query.Parameters[name] = Value();
                    return $"( {column} <> @{name} OR {column} Is NULL )";
                case FilterFunction.In:
                case FilterFunction.NotIn:
                    var items = (criterion.Value as System.Collections.IEnumerable)?.Cast<object>().ToArray() ?? new object[0];
                    if (items.Length == 0) return criterion.FilterFunction == FilterFunction.In ? "1 = 0" : "1 = 1";
                    var names = items.Select((item, i) => { var n = name + "_" + i; query.Parameters[n] = item; return "@" + n; });
                    return $"{column} {criterion.FilterFunction.GetDatabaseOperator()} ({string.Join(", ", names)})";
                default:
                    query.Parameters[name] = Value();
                    return $"{column} {criterion.FilterFunction.GetDatabaseOperator()} @{name}";
            }
        }

        // Named after the table that owns the column, e.g. Employees_Name when querying Managers.
        string ParameterName(string column, string property)
        {
            var dot = column.IndexOf('.');
            var alias = dot > 0 ? column.Substring(0, dot) : query.AliasPrefix + DataProviderMetaData.For(query.EntityType).TableAlias;
            var baseName = (alias + "_" + property).Replace(".", "_");
            var name = baseName;
            for (var i = 2; query.Parameters.ContainsKey(name); i++) name = baseName + "_" + i;
            return name;
        }
    }
}
