#pragma warning disable CS0618 // ComplexTypeModelBinder is obsolete but still shipped; Olive's binder derives from it.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Platform.Entities;

namespace Platform.Mvc
{
    public interface IViewModel { }

    public interface IViewComponent { }

    public enum BlobViewModelAction { New = 0, Removed = 1, Unchanged = 2 }

    public class BlobViewModel
    {
        public BlobViewModelAction Action { get; set; }
        public string TempFileId { get; set; }
        public string Filename { get; set; }
        public string Url { get; set; }
        public bool HasValue => !IsEmpty;
        public bool IsEmpty { get; set; }

        public static BlobViewModel From(Blob blob) => blob == null || blob.IsEmpty()
            ? new BlobViewModel()
            : new BlobViewModel { Filename = blob.FileName, Url = blob.Url(), IsEmpty = false };

        public async Task<Blob> ToBlob()
        {
            switch (Action)
            {
                case BlobViewModelAction.Removed: return Blob.Empty();
                case BlobViewModelAction.New:
                    return await Context.Current.GetService<IFileRequestService>().Bind(TempFileId);
                default: return Blob.Unchanged();
            }
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class CopyDataAttribute : Attribute
    {
        public bool CanCopy;

        public CopyDataAttribute(bool canCopy) => CanCopy = canCopy;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class CustomBoundAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class FromRequestAttribute : Attribute
    {
        public FromRequestAttribute(string name) => Name = name;

        public string Name { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class HasDefaultAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class KeepWhiteSpaceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class LocalizedDateAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class MasterDetailsAttribute : Attribute
    {
        public MasterDetailsAttribute(string prefix) => Prefix = prefix;

        public string Prefix { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class BindingControllerAttribute : Attribute
    {
        public BindingControllerAttribute(Type type) => Type = type;

        public Type Type { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class RemoteDataProviderAttribute : Attribute
    {
        public readonly Type EntityType;

        public RemoteDataProviderAttribute(Type entityType) => EntityType = entityType;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class PushExpireClientCacheAttribute : Attribute { }

    /// <summary>Marks view model methods to run at a stage of binding: [OnPreBinding], [OnPreBound], [OnBound].</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class BaseModelBindAttribute : Attribute
    {
        public static IEnumerable<Action> DiscoverBindMethods(ControllerContext cContext, object model, Type attributeType, ConcurrentDictionary<Type, List<MethodInfo>> cache)
        {
            if (model == null) yield break;
            var methods = cache.GetOrAdd(model.GetType(), t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.IsDefined(attributeType, inherit: true)).ToList());
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                yield return () =>
                {
                    var args = parameters.Select(p => p.ParameterType.IsInstanceOfType(cContext?.HttpContext) ? (object)cContext.HttpContext : null).ToArray();
                    var result = method.Invoke(model, args);
                    if (result is Task task) task.GetAwaiter().GetResult();
                };
            }
        }

        public static void Enqueue(ControllerContext cContext, object model, string actionsKey, Type attributeType, ConcurrentDictionary<Type, List<MethodInfo>> cache)
        {
            var items = cContext.HttpContext.Items;
            if (!(items[actionsKey] is List<Action> list)) items[actionsKey] = list = new List<Action>();
            list.AddRange(DiscoverBindMethods(cContext, model, attributeType, cache));
        }

        public static void SetRoot(ModelBindingContext model, string rootKey)
        {
            if (model.HttpContext.Items[rootKey] == null) model.HttpContext.Items[rootKey] = model.ModelMetadata;
        }

        public static void InvokeAllForRoot(ModelBindingContext model, string rootKey, string actionsKey)
        {
            if (model.HttpContext.Items[rootKey] != model.ModelMetadata) return;
            if (model.HttpContext.Items[actionsKey] is List<Action> list)
            {
                foreach (var action in list) action();
                list.Clear();
            }
            model.HttpContext.Items.Remove(rootKey);
        }
    }

    public class OnPreBindingAttribute : BaseModelBindAttribute { }

    public class OnPreBoundAttribute : BaseModelBindAttribute { }

    public class OnBoundAttribute : BaseModelBindAttribute { }

    public class BindAttributeRunner
    {
        readonly Controller rootController;
        readonly IViewModel[] models;

        public BindAttributeRunner(Controller rootController, IViewModel[] models)
        {
            this.rootController = rootController;
            this.models = models;
        }

        // Hooks are controller methods taking the view model, run after binding and validation.
        public static async Task Run(ActionExecutingContext context)
        {
            if (!(context.Controller is Controller controller)) return;
            foreach (var model in context.ActionArguments.Values.OfType<IViewModel>()) await Bind(model, controller);
        }

        public static async Task Bind(IViewModel item, Controller controller)
        {
            foreach (var stage in new[] { typeof(OnPreBindingAttribute), typeof(OnPreBoundAttribute), typeof(OnBoundAttribute) })
                foreach (var method in Hooks(controller.GetType(), stage))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].ParameterType.IsInstanceOfType(item)) continue;
                    if (method.Invoke(controller, new object[] { item }) is Task task) await task;
                }
        }

        static IEnumerable<MethodInfo> Hooks(Type controller, Type stage) => Cache.GetOrAdd((controller, stage), k =>
            k.Item1.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.IsDefined(k.Item2, inherit: true)).ToList());

        static readonly ConcurrentDictionary<(Type, Type), List<MethodInfo>> Cache = new ConcurrentDictionary<(Type, Type), List<MethodInfo>>();
    }

    public class EntityModelBinder : IModelBinder
    {
        static readonly ConcurrentDictionary<Type, Func<string, Task<IEntity>>> CustomParsers = new ConcurrentDictionary<Type, Func<string, Task<IEntity>>>();
        readonly Type type;

        public EntityModelBinder(Type type) => this.type = type;

        public static void RegisterReadableTextParser<TEntity>(Func<string, Task<GuidEntity>> binder) where TEntity : GuidEntity =>
            CustomParsers[typeof(TEntity)] = async text => await binder(text);

        public static void RegisterCustomParser<TEntity>(Func<string, Task<IEntity>> binder) where TEntity : IEntity => CustomParsers[typeof(TEntity)] = binder;

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            if (string.IsNullOrEmpty(value) || value == "null")
            {
                bindingContext.Result = ModelBindingResult.Success(null);
                return;
            }
            var entity = CustomParsers.TryGetValue(type, out var parser) && !Guid.TryParse(value, out _)
                ? await parser(value)
                : await Context.Current.Database().GetOrDefault(value, type);
            bindingContext.Result = ModelBindingResult.Success(entity);
        }
    }

    // Re-implements IModelBinder: the base BindModelAsync isn't virtual, and the post-binding hooks must run after it.
    public class OliveModelBinder : ComplexTypeModelBinder, IModelBinder
    {
        public OliveModelBinder(IDictionary<ModelMetadata, IModelBinder> propertyBinders, ILoggerFactory loggerFactory) : base(propertyBinders, loggerFactory) { }

        // Unposted List<T> properties come back empty, not null.
        async Task IModelBinder.BindModelAsync(ModelBindingContext bindingContext)
        {
            await base.BindModelAsync(bindingContext);
            var model = bindingContext.Result.IsModelSet ? bindingContext.Result.Model : null;
            if (model == null) return;
            foreach (var property in model.GetType().GetProperties().Where(p => p.CanWrite && p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(List<>)))
                if (property.GetValue(model) == null) property.SetValue(model, Activator.CreateInstance(property.PropertyType));
        }

        protected override Task BindProperty(ModelBindingContext bindingContext)
        {
            var property = bindingContext.ModelMetadata.ContainerType?.GetProperty(bindingContext.ModelMetadata.PropertyName ?? "");
            var fromRequest = property?.GetCustomAttribute<FromRequestAttribute>();
            if (fromRequest != null)
            {
                var value = bindingContext.HttpContext.Request.Param(fromRequest.Name);
                if (value != null)
                {
                    bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value, value);
                    bindingContext.Result = ModelBindingResult.Success(ViewModelServices.Convert(value, bindingContext.ModelType).GetAwaiter().GetResult());
                    return Task.CompletedTask;
                }
            }
            return base.BindProperty(bindingContext);
        }
    }

    public class OliveBinderProvider : IModelBinderProvider
    {
        public IModelBinder GetBinder(ModelBinderProviderContext context)
        {
            var type = context.Metadata.ModelType;
            if (typeof(IEntity).IsAssignableFrom(type)) return new EntityModelBinder(type);
            if (type == typeof(ListSortExpression)) return new ListSortExpressionBinder();
            if (type == typeof(ColumnSelection)) return new ColumnSelectionBinder();
            if (type == typeof(OptionalBooleanFilter)) return new OptionalBooleanFilterBinder();
            if (type == typeof(BlobViewModel)) return new BlobViewModelBinder();
            if (type == typeof(DateTime) || type == typeof(DateTime?)) return new DateTimeBinder();
            if (type == typeof(bool) || type == typeof(bool?)) return new BoolBinder();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) && context.Metadata.ElementMetadata?.IsComplexType == false)
                return new SimpleListBinder(type.GetGenericArguments()[0]);
            if (typeof(IViewModel).IsAssignableFrom(type) && context.Metadata.IsComplexType && !context.Metadata.IsCollectionType)
            {
                var binders = context.Metadata.Properties.ToDictionary(p => p, p => context.CreateBinder(p));
                return new OliveModelBinder(binders, (ILoggerFactory)context.Services.GetService(typeof(ILoggerFactory)));
            }
            return null;
        }
    }

    public class ListSortExpression
    {
        public ListSortExpression(IViewModel container) => Container = container;

        public ListSortExpression(IViewModel container, string expression) : this(container)
        {
            if (string.IsNullOrEmpty(expression)) return;
            Descending = expression.EndsWith(".DESC", StringComparison.OrdinalIgnoreCase);
            Expression = Descending ? expression.Substring(0, expression.Length - 5) : expression;
        }

        public string Expression { get; set; }
        public bool Descending { get; set; }
        public string Prefix { get; set; }
        public bool UseAjaxPost { get; set; }
        public IViewModel Container { get; set; }

        public IEnumerable<TSource> Apply<TSource, TSort>(IEnumerable<TSource> items, Func<TSource, TSort> expression) =>
            Descending ? items.OrderByDescending(expression) : items.OrderBy(expression);

        public async Task<IEnumerable<TSource>> Apply<TSource, TSort>(IEnumerable<TSource> items, Func<TSource, Task<TSort>> expression)
        {
            var keyed = new List<(TSource Item, TSort Key)>();
            foreach (var item in items) keyed.Add((item, await expression(item)));
            return (Descending ? keyed.OrderByDescending(k => k.Key) : keyed.OrderBy(k => k.Key)).Select(k => k.Item);
        }

        public IEnumerable<T> Apply<T>(IEnumerable<T> items)
        {
            if (string.IsNullOrEmpty(Expression)) return items;
            var property = typeof(T).GetProperty(Expression);
            if (property == null) return items;
            return Descending ? items.OrderByDescending(i => property.GetValue(i)) : items.OrderBy(i => property.GetValue(i));
        }

        public override string ToString() => string.IsNullOrEmpty(Expression) ? string.Empty : Expression + (Descending ? ".DESC" : "");

        public string Url(string sortExpression)
        {
            var next = sortExpression == Expression && !Descending ? sortExpression + ".DESC" : sortExpression;
            var request = Context.Current.Request();
            var key = (Prefix ?? "") + "s";
            var query = request.Query.Where(q => !q.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                .Select(q => q.Key + "=" + Uri.EscapeDataString(q.Value)).Concat(new[] { key + "=" + Uri.EscapeDataString(next) });
            return request.Path + "?" + string.Join("&", query);
        }
    }

    public class ListSortExpressionBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            var parsed = new ListSortExpression(null, value);
            var result = bindingContext.Model as ListSortExpression ?? parsed;
            result.Expression = parsed.Expression;
            result.Descending = parsed.Descending;
            bindingContext.Result = ModelBindingResult.Success(result);
            return Task.CompletedTask;
        }
    }

    public class ColumnSelection
    {
        readonly string prefix;
        string cookieKey;

        public ColumnSelection(string prefix = null) => this.prefix = prefix;

        public List<string> Options { get; set; } = new List<string>();
        public List<string> Default { get; set; } = new List<string>();

        public string CookieKey
        {
            get => cookieKey ?? Context.Current.Http()?.GetRouteValue("action") + "." + prefix + ".Columns";
            set => cookieKey = value;
        }
        internal List<string> Selected { get; set; }

        public IEnumerable<string> Current => Selected ?? Default;

        public bool Contains(string column) => Current.Contains(column);

        public IEnumerable<SelectListItem> GetListOptions() => Options.Select(o => new SelectListItem(o.ToLiteralFromPascalCase(), o, Selected?.Contains(o) == true));
    }

    public class ColumnSelectionBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            var result = bindingContext.Model as ColumnSelection ?? new ColumnSelection();
            var selected = (value ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var cookies = bindingContext.HttpContext.Response.Cookies;
            if (selected.Any() && selected.All(result.Options.Contains))
            {
                result.Selected = selected;
                cookies.Append(result.CookieKey, string.Join("|", selected), new CookieOptions { Expires = DateTimeOffset.Now.AddYears(10), HttpOnly = true, Path = "/" });
            }
            else cookies.Delete(result.CookieKey);
            bindingContext.Result = ModelBindingResult.Success(result);
            return Task.CompletedTask;
        }
    }

    // Parses in the request culture, query string included. An unparsable DateTime? binds to null with no error.
    sealed class DateTimeBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            if (string.IsNullOrWhiteSpace(value)) return Task.CompletedTask;
            if (DateTime.TryParse(value, out var date)) bindingContext.Result = ModelBindingResult.Success(date);
            else if (bindingContext.ModelType == typeof(DateTime?)) bindingContext.Result = ModelBindingResult.Success(null);
            else
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                    bindingContext.ModelMetadata.ModelBindingMessageProvider.AttemptedValueIsInvalidAccessor(value, bindingContext.ModelMetadata.GetDisplayName()));
                bindingContext.Result = ModelBindingResult.Failed();
            }
            return Task.CompletedTask;
        }
    }

    // Checkbox pairs post "true,false"; the first value wins. An unparsable bool? binds to null with no error.
    sealed class BoolBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            if (value == null) return Task.CompletedTask;
            if (bool.TryParse(value, out var result)) bindingContext.Result = ModelBindingResult.Success(result);
            else if (bindingContext.ModelType == typeof(bool?)) bindingContext.Result = ModelBindingResult.Success(null);
            else
            {
                bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                    bindingContext.ModelMetadata.ModelBindingMessageProvider.AttemptedValueIsInvalidAccessor(value, bindingContext.ModelMetadata.GetDisplayName()));
                bindingContext.Result = ModelBindingResult.Failed();
            }
            return Task.CompletedTask;
        }
    }

    sealed class SimpleListBinder : IModelBinder
    {
        readonly Type itemType;

        public SimpleListBinder(Type itemType) => this.itemType = itemType;

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var values = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).Values.Where(v => !string.IsNullOrEmpty(v)).ToList();
            if (values.Count == 0) return Task.CompletedTask;
            var converter = System.ComponentModel.TypeDescriptor.GetConverter(itemType);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
            foreach (var value in values)
            {
                try { list.Add(converter.ConvertFromString(null, System.Globalization.CultureInfo.CurrentCulture, value)); }
                catch (Exception)
                {
                    bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, bindingContext.ModelMetadata.ModelBindingMessageProvider.ValueIsInvalidAccessor(value));
                    bindingContext.Result = ModelBindingResult.Failed();
                    return Task.CompletedTask;
                }
            }
            bindingContext.Result = ModelBindingResult.Success(list);
            return Task.CompletedTask;
        }
    }

    // The upload control posts {name}_Action, {name}_TempFileId and so on.
    sealed class BlobViewModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            string Value(string field) => bindingContext.ValueProvider.GetValue(bindingContext.ModelName + "_" + field).FirstValue;
            var fields = new[] { "Action", "TempFileId", "Filename", "Url", "IsEmpty" };
            if (fields.All(f => Value(f) == null)) return Task.CompletedTask;
            bindingContext.Result = ModelBindingResult.Success(new BlobViewModel
            {
                Action = Enum.TryParse<BlobViewModelAction>(Value("Action"), out var action) ? action : BlobViewModelAction.New,
                TempFileId = Value("TempFileId"),
                Filename = Value("Filename"),
                Url = Value("Url"),
                // Inverted, as Olive binds it.
                IsEmpty = !(bool.TryParse(Value("IsEmpty"), out var empty) && empty),
            });
            return Task.CompletedTask;
        }
    }

    sealed class OptionalBooleanFilterBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName).FirstValue;
            if (value != null) bindingContext.Result = ModelBindingResult.Success(OptionalBooleanFilter.Parse(value));
            return Task.CompletedTask;
        }
    }

    public class OptionalBooleanFilter
    {
        public readonly bool? Value;
        public static readonly OptionalBooleanFilter Null = new OptionalBooleanFilter(null);
        public static readonly OptionalBooleanFilter True = new OptionalBooleanFilter(true);
        public static readonly OptionalBooleanFilter False = new OptionalBooleanFilter(false);

        OptionalBooleanFilter(bool? value) => Value = value;

        public override string ToString() => Value?.ToString() ?? "Null";

        public static OptionalBooleanFilter Parse(string text) => text?.ToLowerInvariant() switch
        {
            "true" or "yes" => True,
            "false" or "no" => False,
            "null" => Null,
            _ => null,
        };

        public static implicit operator OptionalBooleanFilter(bool? value) => value == null ? Null : value.Value ? True : False;

        public static implicit operator bool?(OptionalBooleanFilter value) => value?.Value;

        public override int GetHashCode() => Value.GetHashCode();

        public override bool Equals(object obj) => obj is OptionalBooleanFilter other && other.Value == Value;
    }

    public class MenuItem
    {
        public MenuItem(string key, string url)
        {
            Key = key;
            Url = url;
        }

        public string Key { get; set; }
        public string Url { get; set; }

        public bool MatchesCurrentUrl() => string.Equals(Url, Context.Current.Request()?.ToPathAndQuery(), StringComparison.OrdinalIgnoreCase);

        public bool MatchesCurrentUrlWithoutQuery() => string.Equals(Url?.Split('?')[0], Context.Current.Request()?.Path.Value, StringComparison.OrdinalIgnoreCase);
    }
}

namespace Platform
{
    using Platform.Mvc;

    public static class ViewModelServices
    {
        public static async Task<T> To<T>(this IEntity model, string sourcePrefix = null, string targetPrefix = null) where T : IViewModel
        {
            var result = Activator.CreateInstance<T>();
            await CopyData(model, result, sourcePrefix, targetPrefix);
            return result;
        }

        public static Task CopyDataTo(this IViewModel @this, IEntity to, string sourcePrefix = null, string targetPrefix = null) => CopyData(@this, to, sourcePrefix, targetPrefix);

        public static Task CopyDataTo(this IEntity @this, IViewModel to, string sourcePrefix = null, string targetPrefix = null) => CopyData(@this, to, sourcePrefix, targetPrefix);

        public static async Task CopyDataTo<TItem>(this Task<TItem> @this, IViewModel to, string sourcePrefix = null, string targetPrefix = null) where TItem : IEntity =>
            await CopyData(await @this, to, sourcePrefix, targetPrefix);

        /// <summary>Copies same-named public properties, converting between entities and IDs, blobs and blob view models, and simple types.</summary>
        public static async Task CopyData(object from, object to, string sourcePrefix = null, string targetPrefix = null)
        {
            if (from == null || to == null) return;
            var sources = from.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
            foreach (var target in to.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite && p.GetIndexParameters().Length == 0))
            {
                if (target.GetCustomAttribute<CopyDataAttribute>()?.CanCopy == false) continue;
                var name = target.Name;
                if (targetPrefix != null) { if (!name.StartsWith(targetPrefix)) continue; name = name.Substring(targetPrefix.Length); }
                if (!sources.TryGetValue((sourcePrefix ?? "") + name, out var source)) continue;
                if (source.GetCustomAttribute<CopyDataAttribute>()?.CanCopy == false) continue;

                var value = source.GetValue(from);
                if (target.PropertyType == typeof(Blob) && value is BlobViewModel blobModel)
                {
                    var blob = await blobModel.ToBlob();
                    if (blob.IsUnchanged()) continue;
                    value = blob;
                }
                else value = await Convert(value, target.PropertyType);
                target.SetValue(to, value);
            }
        }

        public static async Task<object> Convert(object source, Type target)
        {
            if (source == null || (source is string empty && empty.Length == 0 && target != typeof(string)))
                return target.IsValueType && Nullable.GetUnderlyingType(target) == null ? Activator.CreateInstance(target) : null;
            if (target.IsInstanceOfType(source)) return source;

            var underlying = Nullable.GetUnderlyingType(target) ?? target;
            if (typeof(IEntity).IsAssignableFrom(underlying)) return await Context.Current.Database().GetOrDefault(source, underlying);
            if (source is IEntity entity)
            {
                if (underlying == typeof(string)) return entity.GetId()?.ToString();
                return await Convert(entity.GetId(), target);
            }
            if (underlying == typeof(BlobViewModel) && source is Blob blob) return BlobViewModel.From(blob);
            if (underlying == typeof(Blob) && source is BlobViewModel model) return await model.ToBlob();
            if (underlying == typeof(string)) return source.ToString();
            if (underlying.IsEnum) return source is string s ? Enum.Parse(underlying, s, ignoreCase: true) : Enum.ToObject(underlying, source);
            if (underlying == typeof(Guid)) return Guid.Parse(source.ToString());

            if (typeof(IEnumerable).IsAssignableFrom(target) && source is IEnumerable items && target.IsGenericType)
            {
                var itemType = target.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
                foreach (var item in items) list.Add(await Convert(item, itemType));
                if (target.IsArray) { var array = Array.CreateInstance(itemType, list.Count); list.CopyTo(array, 0); return array; }
                return list;
            }
            return System.Convert.ChangeType(source, underlying, System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    public class DatabaseFilters<T> : List<Criterion> where T : IEntity
    {
        public void Add(Expression<Func<T, bool>> criterion) => AddRange(new CriteriaExtractor<T>(criterion, throwOnNonConversion: true).Extract());
    }
}
