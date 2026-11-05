using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Platform.Entities
{
    [AttributeUsage(AttributeTargets.Property)]
    public class AutoNumberAttribute : Attribute
    {
        public static bool IsAutoNumber(PropertyInfo property) => property.IsDefined(typeof(AutoNumberAttribute), inherit: true);
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class BridgColumnAttribute : Attribute
    {
        public BridgColumnAttribute(string name, int type)
        {
            ColumnName = name;
            Type = type;
        }

        public string ColumnName { get; set; }

        public int Type { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class BridgTableAttribute : Attribute
    {
        public BridgTableAttribute(string name) => TableName = name;

        public string TableName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class CacheObjectsAttribute : Attribute
    {
        readonly bool enabled;

        public CacheObjectsAttribute(bool enabled) => this.enabled = enabled;

        public static bool? IsEnabled(Type type) => type.GetCustomAttribute<CacheObjectsAttribute>(inherit: true)?.enabled;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class CalculatedAttribute : Attribute
    {
        public static bool IsCalculated(PropertyInfo property) => property.IsDefined(typeof(CalculatedAttribute), inherit: true);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
    public class ComputedColumnAttribute : Attribute
    {
        public static bool IsComputedColumn(PropertyInfo property) => property.IsDefined(typeof(ComputedColumnAttribute), inherit: true);

        public static bool IsComputedColumn(Type type) => type.IsDefined(typeof(ComputedColumnAttribute), inherit: true);
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class CustomDataConverterAttribute : Attribute
    {
        public CustomDataConverterAttribute(string className) => ClassName = className;

        internal string ClassName { get; }

        public static string GetClassName(PropertyInfo property) => property.GetCustomAttribute<CustomDataConverterAttribute>(inherit: true)?.ClassName;
    }

    public class EnumDataConverterAttribute : CustomDataConverterAttribute
    {
        public EnumDataConverterAttribute(string enumName) : base($"Platform.Entities.EnumValueConverter<{enumName}>") { }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class DateOnlyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class EncryptedPropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class HasDataAccessClass : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class HasKnownDatabaseAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class IdByDatabaseAttribute : Attribute
    {
        public static bool IsIdAssignedByDatabase(Type type, bool inherit = false) => type.IsDefined(typeof(IdByDatabaseAttribute), inherit);
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ImmutableAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class InverseOfAttribute : Attribute
    {
        public InverseOfAttribute(string association) => Association = association;

        public string Association { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class LazyLoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = true)]
    public class LogEventsAttribute : Attribute
    {
        public LogEventsAttribute(bool shouldLog) : this(shouldLog, shouldLog, shouldLog, shouldLog, shouldLog, shouldLog) { }

        public LogEventsAttribute(bool insertAction = true, bool insertData = true, bool updateAction = true, bool updateData = true, bool deleteAction = true, bool deleteData = true)
        {
            InsertAction = insertAction;
            InsertData = insertData;
            UpdateAction = updateAction;
            UpdateData = updateData;
            DeleteAction = deleteAction;
            DeleteData = deleteData;
        }

        public bool InsertAction { get; }
        public bool InsertData { get; }
        public bool UpdateAction { get; }
        public bool UpdateData { get; }
        public bool DeleteAction { get; }
        public bool DeleteData { get; }

        public static LogEventsAttribute For(IEntity instance) => For(instance.GetType());

        public static LogEventsAttribute For(Type type) => type.GetCustomAttribute<LogEventsAttribute>(inherit: true);

        public static bool ShouldLog(PropertyInfo property) => property.GetCustomAttribute<LogEventsAttribute>(inherit: true)?.UpdateData ?? true;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ManyToManyAttribute : Attribute
    {
        public bool Lazy { get; set; }

        public static IEnumerable<PropertyInfo> GetManyToManyProperties(Type type) => GetManyToManyProperties(type, null);

        public static IEnumerable<PropertyInfo> GetManyToManyProperties(Type type, bool? lazy) =>
            type.GetProperties().Where(p => p.GetCustomAttribute<ManyToManyAttribute>(inherit: true) is { } a && (lazy == null || a.Lazy == lazy));
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class NeedsCastingDatabaseValueAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class NotAssociationAttribute : Attribute
    {
        public static bool Marked(PropertyInfo property) => property.IsDefined(typeof(NotAssociationAttribute), inherit: true);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
    public class PersistentAttribute : Attribute
    {
        public PersistentAttribute(bool isPersistent) => IsPersistent = isPersistent;

        internal bool IsPersistent { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class PrimaryKeyAttribute : Attribute
    {
        public static bool IsPrimaryKey(PropertyInfo property) => property.IsDefined(typeof(PrimaryKeyAttribute), inherit: true);
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ReferencedTypeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class SchemaAttribute : Attribute
    {
        public SchemaAttribute(string schema) => Schema = schema;

        public string Schema { get; }

        public static string GetSchema(Type entityType) => entityType.GetCustomAttribute<SchemaAttribute>(inherit: true)?.Schema;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class SecureFileAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class SkipAutoSortAttribute : Attribute
    {
        public static bool HasAttribute(Type type) => type.IsDefined(typeof(SkipAutoSortAttribute), inherit: true);
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class SoftDeleteAttribute : Attribute
    {
        static readonly ConcurrentDictionary<Type, bool> Enabled = new ConcurrentDictionary<Type, bool>();
        static readonly ConditionalWeakTable<Entity> Marked = new ConditionalWeakTable<Entity>();

        public static bool IsEnabled(Type type) => IsEnabled(type, inherit: true);

        public static bool IsEnabled(Type type, bool inherit) =>
            inherit ? Enabled.GetOrAdd(type, t => t.IsDefined(typeof(SoftDeleteAttribute), inherit: true)) : type.IsDefined(typeof(SoftDeleteAttribute), inherit: false);

        public static bool RequiresSoftdeleteQuery<T>() => RequiresSoftdeleteQuery(typeof(T));

        public static bool RequiresSoftdeleteQuery(Type type) => IsEnabled(type) && !Context.ShouldByPassSoftDelete();

        public static void MarkDeleted(Entity entity) => Marked.Set(entity);

        public static void UnMark(Entity entity) => Marked.Remove(entity);

        public static bool IsMarked(Entity entity) => Marked.Contains(entity);

        public class Context : IDisposable
        {
            static readonly AsyncLocal<Context> current = new AsyncLocal<Context>();
            readonly Context previous;
            readonly bool bypassSoftdelete;

            public Context(bool bypassSoftdelete)
            {
                this.bypassSoftdelete = bypassSoftdelete;
                previous = current.Value;
                current.Value = this;
            }

            public static Context Current { get => current.Value; set => current.Value = value; }

            public static bool ShouldByPassSoftDelete() => current.Value?.bypassSoftdelete ?? false;

            public void Dispose() => current.Value = previous;
        }

        sealed class ConditionalWeakTable<T> where T : class
        {
            readonly System.Runtime.CompilerServices.ConditionalWeakTable<T, object> table = new System.Runtime.CompilerServices.ConditionalWeakTable<T, object>();
            static readonly object Flag = new object();

            public void Set(T item)
            {
                table.Remove(item);
                table.Add(item, Flag);
            }

            public void Remove(T item) => table.Remove(item);

            public bool Contains(T item) => table.TryGetValue(item, out _);
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class TableNameAttribute : Attribute
    {
        public TableNameAttribute(string tableName) => TableName = tableName;

        public string TableName { get; }

        public static string GetTableName(Type entityType) => entityType.GetCustomAttribute<TableNameAttribute>(inherit: false)?.TableName ?? entityType.Name;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class ToStringAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class TransientEntityAttribute : Attribute
    {
        public static bool IsTransient(Type type) => type.IsDefined(typeof(TransientEntityAttribute), inherit: true);
    }

    public static class EntityFinder
    {
        static readonly ConcurrentDictionary<string, Type> TypesByName = new ConcurrentDictionary<string, Type>();
        static Type[] domainTypes;

        internal static Type GetRoot(Type type)
        {
            while (type.BaseType != null && type.BaseType.IsAbstract == false && typeof(Entity).IsAssignableFrom(type.BaseType)) type = type.BaseType;
            return type;
        }

        public static IEnumerable<Type> GetDomainEntityTypes() =>
            domainTypes ?? (domainTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && (a == typeof(IEntity).Assembly || a.GetReferencedAssemblies().Any(r => r.Name == typeof(IEntity).Assembly.GetName().Name)))
                .SelectMany(a => { try { return a.GetExportedTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); } })
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IEntity).IsAssignableFrom(t))
                .ToArray());

        public static Type GetEntityType(string typeFullName) =>
            TypesByName.GetOrAdd(typeFullName, name => GetDomainEntityTypes().FirstOrDefault(t => t.FullName == name || t.AssemblyQualifiedName == name));

        public static IEnumerable<Type> FindPossibleTypes(Type baseType, bool mustFind)
        {
            var result = SearchForPossibleTypes(baseType, mustFind).ToArray();
            if (mustFind && result.Length == 0) throw new ArgumentException($"No type found implementing {baseType.FullName}.");
            return result;
        }

        public static IEnumerable<Type> SearchForPossibleTypes(Type baseType, bool mustFind) =>
            GetDomainEntityTypes().Where(baseType.IsAssignableFrom);

        public static void ResetCache()
        {
            TypesByName.Clear();
            domainTypes = null;
        }
    }
}
