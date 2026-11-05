using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Platform.Entities;

namespace Platform
{
    public static class EntitiesExtensions
    {
        public static bool IsAnyOf<T>(this T item, IEnumerable<T> options) where T : IEntity => options.Contains(item);

        public static bool IsNoneOf<T>(this T item, IEnumerable<T> options) where T : IEntity => !options.Contains(item);

        public static List<T> CloneAll<T>(this IEnumerable<T> list) where T : IEntity => list.Select(i => (T)i.Clone()).ToList();

        public static string GetFullIdentifierString(this IEntity entity) => entity == null ? null : entity.GetType().FullName + "/" + entity.GetId();

        public static async Task ValidateAll<T>(this IEnumerable<T> entities) where T : Entity
        {
            foreach (var entity in entities) await entity.Validate();
        }

        public static T OnlyWhen<T>(this T entity, Func<T, bool> criteria) where T : Entity => entity != null && criteria(entity) ? entity : null;

        public static IEnumerable<TId> IDs<TId>(this IEnumerable<IEntity<TId>> entities) => entities.Select(e => e.ID);

        public static FilterFunction ToFilterFunction(this ExpressionType type, bool flipped = false)
        {
            switch (type)
            {
                case ExpressionType.Equal: return FilterFunction.Is;
                case ExpressionType.NotEqual: return FilterFunction.IsNot;
                case ExpressionType.GreaterThan: return flipped ? FilterFunction.LessThan : FilterFunction.MoreThan;
                case ExpressionType.GreaterThanOrEqual: return flipped ? FilterFunction.LessThanOrEqual : FilterFunction.MoreThanOrEqual;
                case ExpressionType.LessThan: return flipped ? FilterFunction.MoreThan : FilterFunction.LessThan;
                case ExpressionType.LessThanOrEqual: return flipped ? FilterFunction.MoreThanOrEqual : FilterFunction.LessThanOrEqual;
                default: throw new NotSupportedException(type + " has no filter function.");
            }
        }

        /// <summary>Evaluates an expression with no lambda parameters in it, such as a captured local.</summary>
        public static object GetValue(this Expression @this)
        {
            switch (@this)
            {
                case ConstantExpression constant: return constant.Value;
                case MemberExpression member when member.Expression == null || member.Expression is ConstantExpression || member.Expression is MemberExpression:
                    var owner = member.Expression?.GetValue();
                    if (member.Member is FieldInfo field) return field.GetValue(owner);
                    if (member.Member is PropertyInfo property) return property.GetValue(owner);
                    break;
            }
            return Expression.Lambda(@this).Compile().DynamicInvoke();
        }

        public static PropertyInfo[] GetPropertiesInPath(this MemberExpression @this)
        {
            var result = new List<PropertyInfo>();
            for (Expression e = @this; e is MemberExpression m; e = m.Expression)
                if (m.Member is PropertyInfo p) result.Insert(0, p);
            return result.ToArray();
        }

        public static string GetDatabaseColumnPath(this MemberExpression @this) => string.Join(".", @this.GetPropertiesInPath().Select(p => p.Name));

        public static async Task<T> To<T>(this Guid? guid) where T : IEntity => guid == null ? default(T) : await guid.Value.To<T>();

        public static Task<T> To<T>(this Guid guid) where T : IEntity => Context.Current.Database().Get<T>(guid);

        public static bool IsAnyOf(this Guid? @this, params GuidEntity[] items) => @this.HasValue && items.Any(i => i?.ID == @this);

        public static bool IsAnyOf(this Guid? @this, IEnumerable<GuidEntity> items) => @this.HasValue && items.Any(i => i?.ID == @this);

        public static bool IsAnyOf(this Guid @this, params GuidEntity[] items) => items.Any(i => i?.ID == @this);

        public static bool IsAnyOf(this Guid @this, IEnumerable<GuidEntity> items) => items.Any(i => i?.ID == @this);

        public static string GetFullPath(this IHierarchy node) => node.GetFullPath(" > ");

        public static string GetFullPath(this IHierarchy hierarchy, string seperator) => string.Join(seperator, hierarchy.WithAllParents().Reverse().Select(n => n.Name));

        public static bool IsRootNode(this IHierarchy node) => node.GetParent() == null;

        public static IEnumerable<IHierarchy> WithAllChildren(this IHierarchy parent) => new[] { parent }.Concat(parent.GetAllChildren());

        public static IEnumerable<IHierarchy> GetAllChildren(this IHierarchy @this) =>
            @this.GetChildren().SelectMany(c => new[] { c }.Concat(c.GetAllChildren()));

        public static IEnumerable<IHierarchy> WithAllParents(this IHierarchy child) => new[] { child }.Concat(child.GetAllParents());

        public static IEnumerable<IHierarchy> GetAllParents(this IHierarchy child)
        {
            for (var parent = child.GetParent(); parent != null; parent = parent.GetParent()) yield return parent;
        }

        public static IEnumerable<T> WithAllParents<T>(this T child) where T : IHierarchy => ((IHierarchy)child).WithAllParents().Cast<T>();

        public static IEnumerable<T> GetAllParents<T>(this IHierarchy child) where T : IHierarchy => child.GetAllParents().Cast<T>();

        public static Type GetRootEntityType(this Type objectType) => EntityFinder.GetRoot(objectType);

        public static async Task<Blob> DownloadBlob(this Uri url, string cookieValue = null, int timeOutSeconds = 60)
        {
            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(timeOutSeconds) })
            {
                if (cookieValue != null) client.DefaultRequestHeaders.Add("Cookie", cookieValue);
                var data = await client.GetByteArrayAsync(url);
                return new Blob(data, System.IO.Path.GetFileName(url.LocalPath));
            }
        }

        public static IDatabase Database(this Context @this) => @this.GetService<IDatabase>();

        public static int? GetResultsToFetch(this IEnumerable<QueryOption> options) => options.OfType<TakeTopQueryOption>().FirstOrDefault()?.Number;

        static readonly DateTime SqlMinDate = new DateTime(1753, 1, 1);

        public static DateTime GetSqlSafeValue(this DateTime value) => value < SqlMinDate ? SqlMinDate : value;

        public static Task<IDataReader> ExecuteReader(this IDataAccess @this, string command, params object[] parameters) =>
            @this.ExecuteReader(command, CommandType.Text, Parameters(@this, parameters));

        public static Task<object> ExecuteScalar(this IDataAccess @this, string command, params object[] parameters) =>
            @this.ExecuteScalar(command, CommandType.Text, Parameters(@this, parameters));

        public static Task<int> ExecuteNonQuery(this IDataAccess @this, string command, params object[] parameters) =>
            @this.ExecuteNonQuery(command, CommandType.Text, Parameters(@this, parameters));

        static IDataParameter[] Parameters(IDataAccess access, object[] values) =>
            values.Select((v, i) => v as IDataParameter ?? access.CreateParameter("@p" + i, v ?? DBNull.Value)).ToArray();

        public static async Task<IEnumerable<T>> Select<T>(this Task<IDataReader> @this, Func<IDataReader, T> mapper)
        {
            var result = new List<T>();
            using (var reader = await @this)
                while (reader.Read()) result.Add(mapper(reader));
            return result;
        }

        public static async Task<IEnumerable<T>> SelectAsync<T>(this Task<IDataReader> @this, Func<IDataReader, Task<T>> mapper)
        {
            var result = new List<T>();
            using (var reader = await @this)
                while (reader.Read()) result.Add(await mapper(reader));
            return result;
        }
    }
}

namespace Platform.Entities
{
    public static class Sorter
    {
        public const int INCREMENT = 10;

        static IDatabase Database => Context.Current.Database();

        static async Task<ISortable[]> Siblings(ISortable item) =>
            (await Database.Of(item.GetType().GetRootEntityType()).GetList()).Cast<ISortable>().OrderBy(i => i.Order).ToArray();

        public static async Task<ISortable> FindItemAbove(ISortable item) => (await Siblings(item)).Where(i => i.Order < item.Order).LastOrDefault();

        public static async Task<ISortable> FindItemBelow(ISortable item) => (await Siblings(item)).FirstOrDefault(i => i.Order > item.Order);

        public static bool CanMoveUp(ISortable item) => FindItemAbove(item).GetAwaiter().GetResult() != null;

        public static bool CanMoveDown(ISortable item) => FindItemBelow(item).GetAwaiter().GetResult() != null;

        public static async Task MoveBefore(ISortable item, ISortable before, SaveBehaviour saveBehaviour = SaveBehaviour.Default)
        {
            var list = (await Siblings(item)).Except(new[] { item }).ToList();
            list.Insert(Math.Max(0, list.IndexOf(before)), item);
            await Renumber(list, saveBehaviour);
        }

        public static async Task MoveAfter(ISortable item, ISortable after, SaveBehaviour saveBehaviour = SaveBehaviour.Default)
        {
            var list = (await Siblings(item)).Except(new[] { item }).ToList();
            list.Insert(list.IndexOf(after) + 1, item);
            await Renumber(list, saveBehaviour);
        }

        public static async Task<bool> MoveUp(ISortable item, SaveBehaviour saveBehaviour = SaveBehaviour.Default)
        {
            var above = await FindItemAbove(item);
            if (above == null) return false;
            await MoveBefore(item, above, saveBehaviour);
            return true;
        }

        public static async Task<bool> MoveDown(ISortable item, SaveBehaviour saveBehaviour = SaveBehaviour.Default)
        {
            var below = await FindItemBelow(item);
            if (below == null) return false;
            await MoveAfter(item, below, saveBehaviour);
            return true;
        }

        public static async Task<bool> MoveFirst(ISortable item, SaveBehaviour saveBehaviour = SaveBehaviour.Default)
        {
            var first = (await Siblings(item)).FirstOrDefault();
            if (first == null || Equals(first, item)) return false;
            await MoveBefore(item, first, saveBehaviour);
            return true;
        }

        public static async Task<bool> MoveLast(ISortable item, SaveBehaviour saveBehaviour = SaveBehaviour.Default)
        {
            var last = (await Siblings(item)).LastOrDefault();
            if (last == null || Equals(last, item)) return false;
            await MoveAfter(item, last, saveBehaviour);
            return true;
        }

        public static async Task JustifyOrders(ISortable item, SaveBehaviour saveBehaviour = SaveBehaviour.Default) =>
            await Renumber((await Siblings(item)).ToList(), saveBehaviour);

        public static async Task<int> GetNewOrder(ISortable item)
        {
            var siblings = await Siblings(item);
            return siblings.Length == 0 ? INCREMENT : siblings.Max(i => i.Order) + INCREMENT;
        }

        static async Task Renumber(List<ISortable> items, SaveBehaviour saveBehaviour)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var order = (i + 1) * INCREMENT;
                if (items[i].Order == order) continue;
                await Database.Update(items[i], x => x.Order = order, saveBehaviour);
            }
        }
    }
}
