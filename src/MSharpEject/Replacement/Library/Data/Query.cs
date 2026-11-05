using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Platform.Entities.Data
{
    public class DatabaseQuery : IDatabaseQuery
    {
        readonly List<AssociationInclusion> include = new List<AssociationInclusion>();
        IDataProvider provider;

        internal DatabaseQuery(Type entityType) => EntityType = entityType;

        protected static IDatabase Database => Context.Current.Database();

        public IDataProvider Provider => provider ?? (provider = Database.GetProvider(EntityType));

        public Type EntityType { get; }

        public List<ICriterion> Criteria { get; } = new List<ICriterion>();

        public IEnumerable<AssociationInclusion> Include => include;

        public Dictionary<string, object> Parameters { get; } = new Dictionary<string, object>();

        public int PageStartIndex { get; set; }

        public int? PageSize { get; set; }

        public int? TakeTop { get; set; }

        public string[] Columns { get; set; }

        public List<OrderByPart> OrderByParts { get; } = new List<OrderByPart>();

        public string AliasPrefix { get; set; }

        public string MapColumn(string propertyName) => Provider.MapColumn(propertyName);

        public string Column(string propertyName, string alias = null) => MapColumn(propertyName);

        IDatabaseQuery IDatabaseQuery.Include(string associationProperty)
        {
            var parts = associationProperty.Split('.');
            var property = EntityType.GetProperty(parts[0]) ?? throw new ArgumentException($"{EntityType.Name} has no property {parts[0]}.");
            var inclusion = include.FirstOrDefault(i => i.Association == property) ?? AddInclusion(property);
            if (parts.Length > 1) inclusion.IncludeNestedAssociation(string.Join(".", parts.Skip(1)));
            return this;
        }

        AssociationInclusion AddInclusion(PropertyInfo property)
        {
            var inclusion = AssociationInclusion.Create(property);
            include.Add(inclusion);
            return inclusion;
        }

        IDatabaseQuery IDatabaseQuery.Include(IEnumerable<string> associationProperties)
        {
            foreach (var p in associationProperties) ((IDatabaseQuery)this).Include(p);
            return this;
        }

        public IDatabaseQuery Where(params ICriterion[] criteria)
        {
            Criteria.AddRange(criteria);
            return this;
        }

        public IDatabaseQuery WhereIn(string myField, IDatabaseQuery subquery, string targetField) => WhereSubquery(myField, subquery, targetField, "IN");

        public IDatabaseQuery WhereNotIn(string myField, IDatabaseQuery subquery, string targetField) => WhereSubquery(myField, subquery, targetField, "NOT IN");

        IDatabaseQuery WhereSubquery(string myField, IDatabaseQuery subquery, string targetField, string op)
        {
            subquery.AliasPrefix = "Subq" + Guid.NewGuid().ToString("N").Substring(0, 6) + "_";
            var sql = subquery.Provider.GenerateSelectCommand(subquery, subquery.Column(targetField));
            Criteria.Add(new DirectDatabaseCriterion($"{Column(myField)} {op} ({sql})", subquery.Parameters));
            return this;
        }

        public IDatabaseQuery OrderBy(string property, bool descending)
        {
            OrderByParts.Clear();
            OrderByParts.Add(new OrderByPart { Property = property, Descending = descending });
            return this;
        }

        public IDatabaseQuery ThenBy(string property, bool descending)
        {
            OrderByParts.Add(new OrderByPart { Property = property, Descending = descending });
            return this;
        }

        public IDatabaseQuery OrderBy(string property) => OrderBy(property, false);

        public IDatabaseQuery Top(int rows)
        {
            TakeTop = rows;
            return this;
        }

        public IDatabaseQuery Select(params string[] columns)
        {
            Columns = columns;
            return this;
        }

        public IDatabaseQuery CloneFor(Type type)
        {
            var clone = new DatabaseQuery(type) { PageStartIndex = PageStartIndex, PageSize = PageSize, TakeTop = TakeTop, AliasPrefix = AliasPrefix, Columns = Columns };
            clone.Criteria.AddRange(Criteria);
            clone.OrderByParts.AddRange(OrderByParts);
            foreach (var p in Parameters) clone.Parameters[p.Key] = p.Value;
            return clone;
        }

        public async Task<IEntity[]> GetList()
        {
            if (Criteria.None() && PageSize == null && TakeTop == null && OrderByParts.None() && Database.Cache is { } cache && cache.IsCacheable(EntityType)
                && cache.GetList(EntityType) is System.Collections.IEnumerable cached)
                return cached.Cast<IEntity>().ToArray();

            var result = (await Provider.GetList(this)).ToArray();
            foreach (var inclusion in include) await inclusion.LoadAssociations(this, result);

            // Unordered queries come back sorted by the entity's CompareTo (ToString by default), unless [SkipAutoSort].
            if (OrderByParts.None() && !SkipAutoSortAttribute.HasAttribute(EntityType) && PageSize == null)
                result = result.OrderBy(e => e).ToArray();

            if (Database.Cache is { } c && c.IsCacheable(EntityType))
            {
                foreach (var item in result) c.Add(item);
                if (Criteria.None() && PageSize == null && TakeTop == null && OrderByParts.None()) c.AddList(EntityType, result);
            }
            return result;
        }

        public Task<int> Count() => Provider.Count(this);

        public async Task<bool> Any() => await Clone(1).Count() > 0;

        public async Task<bool> None() => !await Any();

        DatabaseQuery Clone(int top)
        {
            var clone = (DatabaseQuery)CloneFor(EntityType);
            clone.TakeTop = top;
            return clone;
        }

        public async Task<IEntity> FirstOrDefault()
        {
            if (OrderByParts.Any()) return (await Clone(1).GetList()).FirstOrDefault();
            return (await GetList()).FirstOrDefault();
        }

        public async Task<IEntity> WithMax(string property) => (await ((DatabaseQuery)CloneFor(EntityType)).OrderBy(property, true).Top(1).GetList()).FirstOrDefault();

        public async Task<IEntity> WithMin(string property) => (await ((DatabaseQuery)CloneFor(EntityType)).OrderBy(property, false).Top(1).GetList()).FirstOrDefault();

        public Task<object> Aggregate(AggregateFunction function, string propertyName) => Provider.Aggregate(this, function, propertyName);

        public async Task<TOutput?> Aggregate<TOutput>(AggregateFunction function, Expression property) where TOutput : struct
        {
            var name = PropertyName(property);
            var result = await Aggregate(function, name);
            if (result == null || result is DBNull) return null;
            return (TOutput)Convert.ChangeType(result, typeof(TOutput));
        }

        internal static string PropertyName(Expression expression)
        {
            if (expression is LambdaExpression lambda) expression = lambda.Body;
            if (expression is UnaryExpression unary) expression = unary.Operand;
            if (expression is MemberExpression member) return member.GetDatabaseColumnPath();
            throw new ArgumentException("Expected a property: " + expression);
        }
    }

    public class DatabaseQuery<TEntity> : DatabaseQuery, IDatabaseQuery<TEntity> where TEntity : IEntity
    {
        public DatabaseQuery() : base(typeof(TEntity)) { }

        public IDatabaseQuery<TEntity> Where(Expression<Func<TEntity, bool>> criteria)
        {
            if (criteria == null) return this;
            Criteria.AddRange(new CriteriaExtractor<TEntity>(criteria, throwOnNonConversion: true).Extract());
            return this;
        }

        public new async Task<TEntity[]> GetList() => (await base.GetList()).Cast<TEntity>().ToArray();

        public new async Task<TEntity> FirstOrDefault() => (TEntity)await base.FirstOrDefault();

        public new IDatabaseQuery<TEntity> OrderBy(string property) => (IDatabaseQuery<TEntity>)base.OrderBy(property);

        public new IDatabaseQuery<TEntity> Top(int rows) => (IDatabaseQuery<TEntity>)base.Top(rows);

        public new IDatabaseQuery<TEntity> Where(params ICriterion[] criteria) => (IDatabaseQuery<TEntity>)base.Where(criteria);

        public new IDatabaseQuery<TEntity> Select(params string[] columns) => (IDatabaseQuery<TEntity>)base.Select(columns);

        public IDatabaseQuery<TEntity> ThenBy(Expression<Func<TEntity, object>> property, bool descending = false) => (IDatabaseQuery<TEntity>)base.ThenBy(PropertyName(property), descending);

        public IDatabaseQuery<TEntity> OrderByDescending(Expression<Func<TEntity, object>> property) => OrderBy(property, true);

        public IDatabaseQuery<TEntity> OrderBy(Expression<Func<TEntity, object>> property, bool descending = false) => (IDatabaseQuery<TEntity>)base.OrderBy(PropertyName(property), descending);

        public IDatabaseQuery<TEntity> ThenByDescending(Expression<Func<TEntity, object>> property) => ThenBy(property, true);

        public IDatabaseQuery<TEntity> Include(Expression<Func<TEntity, object>> property) => (IDatabaseQuery<TEntity>)((IDatabaseQuery)this).Include(PropertyName(property));

        public new async Task<TEntity> WithMax(string property) => (TEntity)await base.WithMax(property);

        public new async Task<TEntity> WithMin(string property) => (TEntity)await base.WithMin(property);

        public Task<TEntity> WithMax(Expression<Func<TEntity, object>> property) => WithMax(PropertyName(property));

        public Task<TEntity> WithMin(Expression<Func<TEntity, object>> property) => WithMin(PropertyName(property));

        public Task<TOutput?> Aggregate<TProperty, TOutput>(AggregateFunction function, Expression<Func<TEntity, TProperty?>> property) where TProperty : struct where TOutput : struct =>
            Aggregate<TOutput>(function, property);

        public Task<TProperty?> Max<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Max, property);
        public Task<TProperty?> Max<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Max, property);
        public Task<TProperty?> Min<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Min, property);
        public Task<TProperty?> Min<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Min, property);
        public Task<TProperty?> Sum<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Sum, property);
        public Task<TProperty?> Sum<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Sum, property);
        public Task<decimal?> Sum(Expression<Func<TEntity, int?>> property) => Aggregate<decimal>(AggregateFunction.Sum, property);
        public Task<TProperty?> Average<TProperty>(Expression<Func<TEntity, TProperty?>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Average, property);
        public Task<TProperty?> Average<TProperty>(Expression<Func<TEntity, TProperty>> property) where TProperty : struct => Aggregate<TProperty>(AggregateFunction.Average, property);
        public Task<decimal?> Average(Expression<Func<TEntity, int?>> property) => Aggregate<decimal>(AggregateFunction.Average, property);

        public async Task<bool> Contains(TEntity item) => item != null && (await GetList()).Contains(item);

        public async Task<bool> ContainsAny(params TEntity[] items) => (await GetList()).Intersect(items).Any();

        public Task<bool> Any(Expression<Func<TEntity, bool>> criteria) => ((DatabaseQuery<TEntity>)CloneFor(typeof(TEntity)).ToTyped()).Where(criteria).Any();
    }

    static class QueryCloneExtensions
    {
        internal static IDatabaseQuery ToTyped(this IDatabaseQuery query)
        {
            var typed = (DatabaseQuery)Activator.CreateInstance(typeof(DatabaseQuery<>).MakeGenericType(query.EntityType));
            typed.Criteria.AddRange(query.Criteria);
            typed.OrderByParts.AddRange(query.OrderByParts);
            foreach (var p in query.Parameters) typed.Parameters[p.Key] = p.Value;
            typed.PageStartIndex = query.PageStartIndex;
            typed.PageSize = query.PageSize;
            typed.TakeTop = query.TakeTop;
            return typed;
        }
    }

}

namespace Platform.Entities
{
    using Platform.Entities.Data;

    public static class DatabaseQueryExtensions
    {
        public static T Page<T>(this T query, int startIndex, int pageSize) where T : IDatabaseQuery
        {
            query.PageStartIndex = startIndex;
            query.PageSize = pageSize;
            return query;
        }

        public static T OrderBy<T>(this T query, string property, bool descending = false) where T : DatabaseQuery
        {
            ((IDatabaseQuery)query).OrderBy(property, descending);
            return query;
        }

        public static T OrderByDescending<T>(this T query, string property) where T : DatabaseQuery => OrderBy(query, property, true);

        public static T ThenBy<T>(this T query, string property, bool descending = false) where T : DatabaseQuery
        {
            ((IDatabaseQuery)query).ThenBy(property, descending);
            return query;
        }

        public static T ThenByDescending<T>(this T query, string property) where T : DatabaseQuery => ThenBy(query, property, true);

        public static T Where<T>(this T query, string sqlCriteria) where T : DatabaseQuery
        {
            query.Criteria.Add(new DirectDatabaseCriterion(sqlCriteria));
            return query;
        }

        public static T Where<T>(this T query, IEnumerable<ICriterion> criteria) where T : IDatabaseQuery
        {
            query.Criteria.AddRange(criteria);
            return query;
        }

        public static T Where<T>(this T query, IEnumerable<Criterion> criteria) where T : IDatabaseQuery
        {
            query.Criteria.AddRange(criteria);
            return query;
        }

        static T In<T>(T query, string myField, IDatabaseQuery subquery, string targetField, bool not) where T : IDatabaseQuery
        {
            if (not) ((IDatabaseQuery)query).WhereNotIn(myField, subquery, targetField);
            else ((IDatabaseQuery)query).WhereIn(myField, subquery, targetField);
            return query;
        }

        public static T WhereIn<T>(this T query, string myField, IDatabaseQuery subquery, string targetField) where T : IDatabaseQuery => In(query, myField, subquery, targetField, false);

        public static T WhereIn<T>(this T query, IDatabaseQuery subquery, string targetField) where T : IDatabaseQuery => In(query, "ID", subquery, targetField, false);

        public static IDatabaseQuery<T> WhereIn<T>(this IDatabaseQuery<T> query, Expression<Func<T, object>> property, IDatabaseQuery subquery, string targetField) where T : IEntity =>
            In(query, DatabaseQuery.PropertyName(property), subquery, targetField, false);

        public static IDatabaseQuery<T> WhereIn<T, K>(this IDatabaseQuery<T> query, Expression<Func<T, object>> property, IDatabaseQuery<K> subquery, Expression<Func<K, object>> targetProperty) where T : IEntity where K : IEntity =>
            In(query, DatabaseQuery.PropertyName(property), subquery, DatabaseQuery.PropertyName(targetProperty), false);

        public static T WhereIn<T, K>(this T query, string property, IDatabaseQuery<K> subquery, Expression<Func<K, object>> targetProperty) where T : IDatabaseQuery where K : IEntity =>
            In(query, property, subquery, DatabaseQuery.PropertyName(targetProperty), false);

        public static T WhereIn<T, K>(this T query, IDatabaseQuery<K> subquery, Expression<Func<K, object>> targetProperty) where T : IDatabaseQuery where K : IEntity =>
            In(query, "ID", subquery, DatabaseQuery.PropertyName(targetProperty), false);

        public static T WhereNotIn<T>(this T query, string myField, IDatabaseQuery subquery, string targetField) where T : IDatabaseQuery => In(query, myField, subquery, targetField, true);

        public static T WhereNotIn<T>(this T query, IDatabaseQuery subquery, string targetField) where T : IDatabaseQuery => In(query, "ID", subquery, targetField, true);

        public static IDatabaseQuery<T> WhereNotIn<T>(this IDatabaseQuery<T> query, Expression<Func<T, object>> property, IDatabaseQuery subquery, string targetField) where T : IEntity =>
            In(query, DatabaseQuery.PropertyName(property), subquery, targetField, true);

        public static IDatabaseQuery<T> WhereNotIn<T, K>(this IDatabaseQuery<T> query, Expression<Func<T, object>> property, IDatabaseQuery<K> subquery, Expression<Func<K, object>> targetProperty) where T : IEntity where K : IEntity =>
            In(query, DatabaseQuery.PropertyName(property), subquery, DatabaseQuery.PropertyName(targetProperty), true);

        public static T WhereNotIn<T, K>(this T query, string property, IDatabaseQuery<K> subquery, Expression<Func<K, object>> targetProperty) where T : IDatabaseQuery where K : IEntity =>
            In(query, property, subquery, DatabaseQuery.PropertyName(targetProperty), true);

        public static T WhereNotIn<T, K>(this T query, IDatabaseQuery<K> subquery, Expression<Func<K, object>> targetProperty) where T : IDatabaseQuery where K : IEntity =>
            In(query, "ID", subquery, DatabaseQuery.PropertyName(targetProperty), true);
    }
}
