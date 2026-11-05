using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Platform
{
    public static partial class Extensions
    {
        public delegate void ItemHandler<in T>(T arg);

        public static bool HasAny<TSource>(this IEnumerable<TSource> @this) => @this != null && @this.Any();

        public static bool None<T>(this IEnumerable<T> @this) => !@this.HasAny();

        public static bool None<T>(this IEnumerable<T> @this, Func<T, bool> criteria) => !@this.Any(criteria);

        public static bool None<T>(this IEnumerable<T> @this, Func<T, int, bool> predicate) => !@this.Where(predicate).Any();

        public static bool HasMany<T>(this IEnumerable<T> @this) => @this != null && @this.Skip(1).Any();

        public static bool IsSingle<T>(this IEnumerable<T> @this) => @this != null && @this.Take(2).Count() == 1;

        public static bool IsSingle<T>(this IEnumerable<T> @this, Func<T, bool> criteria) => @this.Where(criteria).IsSingle();

        public static bool IsSingle<T>(this IEnumerable<T> list, out T first) where T : class
        {
            var items = list.Take(2).ToList();
            first = items.Count == 1 ? items[0] : null;
            return items.Count == 1;
        }

        public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T> @this) => @this ?? Enumerable.Empty<T>();

        public static IEnumerable OrEmpty(this IEnumerable @this) => @this ?? Enumerable.Empty<object>();

        public static IEnumerable<T> Or<T>(this IEnumerable<T> @this, IEnumerable<T> valueIfEmpty) => @this.HasAny() ? @this : valueIfEmpty;

        static string Text<T>(T item) => item == null ? "{NULL}" : item.ToString();

        public static string ToString<T>(this IEnumerable<T> @this, string separator) =>
            @this == null ? null : string.Join(separator, @this.Select(Text));

        public static string ToString<T>(this IEnumerable<T> @this, string separator, string lastSeparator)
        {
            if (@this == null) return null;
            var items = @this.Select(Text).ToList();
            if (items.Count < 2) return string.Join(separator, items);
            return string.Join(separator, items.Take(items.Count - 1)) + lastSeparator + items.Last();
        }

        public static string ToString(this IEnumerable @this, string separator) => @this == null ? "{NULL}" : @this.Cast<object>().ToString(separator);

        public static string ToLinesString<T>(this IEnumerable<T> @this) => @this.ToString(Environment.NewLine);

        public static string ToHtmlLines<T>(this IEnumerable<T> @this) => @this.ToString("<br/>");

        public static bool Lacks<T>(this IEnumerable<T> @this, T item) => !@this.Contains(item);

        // Enumerable.Contains named outright: inside this namespace the call would bind back to this overload.
        public static bool Contains<T>(this IEnumerable<T> @this, T? item) where T : struct => item.HasValue && Enumerable.Contains(@this, item.Value);

        public static bool Lacks<T>(this IEnumerable<T> @this, T? item) where T : struct => !@this.Contains(item);

        public static bool LacksAny<T>(this IEnumerable<T> @this, IEnumerable<T> items) => !@this.ContainsAll(items);

        public static bool LacksAll<T>(this IEnumerable<T> @this, IEnumerable<T> items) => !@this.ContainsAny(items.ToArray());

        public static bool ContainsAll<T>(this IEnumerable<T> @this, IEnumerable<T> items) => items.All(@this.Contains);

        public static bool ContainsAny<T>(this IEnumerable<T> @this, params T[] items) => items.Any(@this.Contains);

        public static bool Intersects<T>(this IEnumerable<T> @this, IEnumerable<T> otherList) => @this.Intersect(otherList).Any();

        public static bool Intersects<T>(this IEnumerable<T> @this, params T[] items) => @this.Intersect(items).Any();

        public static bool IsSubsetOf<T>(this IEnumerable<T> @this, IEnumerable<T> target) => @this.All(target.Contains);

        public static bool IsEquivalentTo<T>(this IEnumerable<T> @this, IEnumerable<T> other)
        {
            var mine = @this.OrEmpty().ToList();
            var theirs = other.OrEmpty().ToList();
            return mine.Count == theirs.Count && mine.All(theirs.Contains) && theirs.All(mine.Contains);
        }

        public static bool AreItemsUnique<T>(this IEnumerable<T> @this)
        {
            var seen = new HashSet<T>();
            return @this == null || @this.All(seen.Add);
        }

        public static bool AreItemsUnique<T>(this IEnumerable<string> @this, bool caseSensitive)
        {
            var seen = new HashSet<string>(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            return @this == null || @this.All(seen.Add);
        }

        public static bool IsAnyOf<T>(this T @this, params T[] items) => items.Contains(@this);

        public static bool IsNoneOf<T>(this T @this, params T[] items) => !items.Contains(@this);

        public static bool IsAnyOf<T>(this T? @this, IEnumerable<T> items) where T : struct => @this.HasValue && items.Contains(@this.Value);

        public static bool IsAnyOf(this int @this, IEnumerable<int> items) => items.Contains(@this);

        public static bool IsAnyOf(this Guid @this, IEnumerable<Guid> items) => items != null && items.Contains(@this);

        public static bool IsAnyOf(this Guid @this, IEnumerable<Guid?> items) => items != null && items.Contains(@this);

        public static bool IsAnyOf(this Guid @this, params Guid?[] items) => items.Contains(@this);

        public static bool IsAnyOf(this Guid? @this, IEnumerable<Guid> items) => items != null && @this.HasValue && items.Contains(@this.Value);

        public static bool IsAnyOf(this Guid? @this, IEnumerable<Guid?> items) => items != null && items.Contains(@this);

        public static bool IsAnyOf(this Guid? @this, params Guid?[] items) => items.Contains(@this);

        public static bool ContainsAny(this IEnumerable<Guid> @this, params Guid?[] ids) => ids.Any(id => id.HasValue && @this.Contains(id.Value));

        public static bool Contains(this IEnumerable<string> @this, string instance, bool caseSensitive) =>
            @this.Any(i => string.Equals(i, instance, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        public static bool Lacks(this IEnumerable<string> @this, string instance, bool caseSensitive) => !@this.Contains(instance, caseSensitive);

        public static bool LacksAll(this IEnumerable<string> @this, IEnumerable<string> items, bool caseSensitive) => items.All(i => @this.Lacks(i, caseSensitive));

        public static string GetElementAfter(this IEnumerable<string> @this, string item, bool caseSensitive)
        {
            var list = @this.ToList();
            var index = list.FindIndex(i => string.Equals(i, item, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            return index < 0 || index == list.Count - 1 ? null : list[index + 1];
        }

        public static string GetElementBefore(this IEnumerable<string> @this, string item, bool caseSensitive)
        {
            var list = @this.ToList();
            var index = list.FindIndex(i => string.Equals(i, item, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            return index <= 0 ? null : list[index - 1];
        }

        public static T GetElementAfter<T>(this IEnumerable<T> @this, T item) where T : class
        {
            var list = @this.ToList();
            var index = list.IndexOf(item);
            return index < 0 || index == list.Count - 1 ? null : list[index + 1];
        }

        public static T GetElementBefore<T>(this IEnumerable<T> @this, T item) where T : class
        {
            var list = @this.ToList();
            var index = list.IndexOf(item);
            return index <= 0 ? null : list[index - 1];
        }

        public static int IndexOf<T>(this IEnumerable<T> @this, T element)
        {
            var index = 0;
            foreach (var item in @this)
            {
                if (Equals(item, element)) return index;
                index++;
            }
            return -1;
        }

        public static int IndexOf<T>(this IEnumerable<T> @this, Func<T, bool> criteria)
        {
            var index = 0;
            foreach (var item in @this)
            {
                if (criteria(item)) return index;
                index++;
            }
            return -1;
        }

        public static IEnumerable<T> Except<T>(this IEnumerable<T> list, Func<T, bool> criteria) => list.Where(i => !criteria(i));

        public static IEnumerable<T> Except<T>(this IEnumerable<T> @this, T item) => @this.Where(i => !Equals(i, item));

        public static IEnumerable<T> Except<T>(this IEnumerable<T> list, params T[] items) => list.Where(i => !items.Contains(i));

        public static IEnumerable<T> Except<T>(this IEnumerable<T> list, HashSet<T> items) => list.Where(i => !items.Contains(i));

        public static IEnumerable<T> Except<T>(this IEnumerable<T> @this, List<T> itemsToExclude) => @this.Where(i => !itemsToExclude.Contains(i));

        public static IEnumerable<T> Except<T>(this IEnumerable<T> @this, IEnumerable<T> itemsToExclude, bool alsoDistinct = false)
        {
            var result = @this.Where(i => !itemsToExclude.Contains(i));
            return alsoDistinct ? result.Distinct() : result;
        }

        public static IEnumerable<string> Except(this IEnumerable<string> @this, IEnumerable<string> itemsToExclude) => @this.Where(i => !itemsToExclude.Contains(i));

        public static IEnumerable<char> Except(this IEnumerable<char> @this, IEnumerable<char> itemsToExclude) => @this.Where(i => !itemsToExclude.Contains(i));

        public static IEnumerable<T> Except<T>(this IEnumerable<T> @this, Type excludedType) => @this.Where(i => i == null || !excludedType.IsInstanceOfType(i));

        public static IEnumerable<T> ExceptNull<T>(this IEnumerable<T> @this) => @this.Where(i => i != null);

        public static IEnumerable<int> ExceptNull(this IEnumerable<int?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        public static IEnumerable<double> ExceptNull(this IEnumerable<double?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        public static IEnumerable<decimal> ExceptNull(this IEnumerable<decimal?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        public static IEnumerable<bool> ExceptNull(this IEnumerable<bool?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        public static IEnumerable<DateTime> ExceptNull(this IEnumerable<DateTime?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        public static IEnumerable<TimeSpan> ExceptNull(this IEnumerable<TimeSpan?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        public static IEnumerable<Guid> ExceptNull(this IEnumerable<Guid?> @this) => @this.Where(i => i.HasValue).Select(i => i.Value);

        // Olive keeps the default items, not the others, and throws on a null item; kept for identical behaviour.
        public static IEnumerable<T> ExceptDefault<T>(this IEnumerable<T> @this) => @this.Where(i => i.Equals(default(T)));

        public static IEnumerable<T> ExceptFirst<T>(this IEnumerable<T> @this, int count = 1) => @this.Skip(count);

        public static IEnumerable<T> ExceptLast<T>(this IEnumerable<T> @this, int count = 1)
        {
            var list = @this.ToList();
            return list.Take(Math.Max(0, list.Count - count));
        }

        public static IEnumerable<T> ExceptAt<T>(this IEnumerable<T> @this, params int[] indices) => @this.Where((_, i) => !indices.Contains(i));

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> @this, T item) => @this.Concat(new[] { item });

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> @this, params IEnumerable<T>[] otherLists) =>
            otherLists.Aggregate(@this, (all, other) => Enumerable.Concat(all, other));

        public static IEnumerable<T> Union<T>(this IEnumerable<T> @this, params IEnumerable<T>[] otherLists) =>
            otherLists.Aggregate(@this, (all, other) => Enumerable.Union(all, other));

        public static IEnumerable<T> Union<T>(this IEnumerable<T> @this, params T[] otherItems) => Enumerable.Union(@this, otherItems);

        public static IEnumerable<T> Prepend<T>(this IEnumerable<T> @this, IEnumerable<T> prefix) => Enumerable.Concat(prefix, @this);

        public static IEnumerable<T> Prepend<T>(this IEnumerable<T> @this, params T[] prefix) => Enumerable.Concat(prefix, @this);

        public static IEnumerable<T> Distinct<T, TResult>(this IEnumerable<T> @this, Func<T, TResult> selector)
        {
            var seen = new HashSet<TResult>();
            foreach (var item in @this)
                if (seen.Add(selector(item))) yield return item;
        }

        public static IEnumerable<IEnumerable<T>> Chop<T>(this IEnumerable<T> @this, int chopSize)
        {
            if (@this == null)
            {
                yield return new List<T>();
                yield break;
            }
            var batch = new List<T>(chopSize);
            foreach (var item in @this)
            {
                batch.Add(item);
                if (batch.Count == chopSize)
                {
                    yield return batch;
                    batch = new List<T>(chopSize);
                }
            }
            if (batch.Count > 0) yield return batch;
        }

        public static T WithMax<T, TKey>(this IEnumerable<T> @this, Func<T, TKey> keySelector) =>
            @this.HasAny() ? @this.OrderByDescending(keySelector).First() : default(T);

        public static T WithMin<T, TKey>(this IEnumerable<T> @this, Func<T, TKey> keySelector) =>
            @this.HasAny() ? @this.OrderBy(keySelector).First() : default(T);

        public static R MaxOrDefault<T, R>(this IEnumerable<T> @this, Func<T, R> expression) => @this.HasAny() ? @this.Max(expression) : default(R);

        public static R MinOrDefault<T, R>(this IEnumerable<T> @this, Func<T, R> expression) => @this.HasAny() ? @this.Min(expression) : default(R);

        public static T FirstOrDefault<T>(this ICollection<T> @this, Func<T, bool> selector) => Enumerable.FirstOrDefault(@this, selector);

        public static void Do<T>(this IEnumerable<T> @this, ItemHandler<T> action)
        {
            if (@this == null) return;
            foreach (var item in @this) action(item);
        }

        public static void Do<T>(this IEnumerable<T> @this, Action<T, int> action)
        {
            if (@this == null) return;
            var index = 0;
            foreach (var item in @this) action(item, index++);
        }

        public static async Task Do<T>(this IEnumerable<T> @this, Func<T, Task> func)
        {
            if (@this == null) return;
            foreach (var item in @this) await func(item);
        }

        public static void AddRange<T>(this IList<T> @this, IEnumerable<T> items)
        {
            if (items == null) return;
            foreach (var item in items.ToList()) @this.Add(item);
        }

        public static void AddRange<T>(this HashSet<T> @this, IEnumerable<T> items)
        {
            if (items == null) return;
            foreach (var item in items.ToList()) @this.Add(item);
        }

        public static void Remove<T>(this IList<T> @this, IEnumerable<T> itemsToRemove)
        {
            if (itemsToRemove == null) return;
            foreach (var item in itemsToRemove.ToList()) @this.Remove(item);
        }

        public static void RemoveWhere<T>(this IList<T> @this, Func<T, bool> selector)
        {
            foreach (var item in @this.Where(selector).ToList()) @this.Remove(item);
        }

        public static void RemoveNulls<T>(this IList<T> @this) => @this.RemoveWhere(i => i == null);

        public static TValue GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> @this, TKey key) =>
            key != null && @this.TryGetValue(key, out var value) ? value : default(TValue);

        public static TValue TryGet<TKey, TValue>(this IDictionary<TKey, TValue> @this, TKey key) => @this.GetOrDefault(key);

        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dic, TKey key, Func<TValue> valueProvider)
        {
            if (dic.TryGetValue(key, out var value)) return value;
            return dic[key] = valueProvider();
        }

        public static bool LacksKey<TKey, TValue>(this IDictionary<TKey, TValue> @this, TKey key) => !@this.ContainsKey(key);

        public static void RemoveWhere<TKey, TValue>(this IDictionary<TKey, TValue> @this, Func<KeyValuePair<TKey, TValue>, bool> selector)
        {
            foreach (var pair in @this.Where(selector).ToList()) @this.Remove(pair.Key);
        }

        public static bool Lacks(this IDictionary @this, object key) => !@this.Contains(key);

        public static T? NullIfDefault<T>(this T value, T defaultValue = default(T)) where T : struct =>
            value.Equals(defaultValue) ? (T?)null : value;
    }
}
