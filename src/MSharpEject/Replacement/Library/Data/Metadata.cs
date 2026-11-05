using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;

namespace Platform.Entities.Data
{
    public class PropertyData : IPropertyData
    {
        public bool IsCustomPrimaryKey { get; internal set; }
        public string Name { get; internal set; }
        public string ParameterName { get; internal set; }
        public PropertyInfo PropertyInfo { get; internal set; }
        public Type NonGenericType { get; internal set; }
        public bool IsAutoNumber { get; internal set; }
        public bool IsDeleted { get; internal set; }
        public bool IsOriginalId { get; internal set; }
        public bool IsDefaultId { get; internal set; }
        public bool IsComputed { get; internal set; }
        public Type AssociateType { get; internal set; }
        public IPropertyAccessor Accessor { get; internal set; }
        public bool IsUserDefined { get; internal set; }
    }

    sealed class ReflectionPropertyAccessor : IPropertyAccessor
    {
        readonly PropertyInfo property;
        readonly Type target;

        public ReflectionPropertyAccessor(PropertyInfo property)
        {
            this.property = property;
            target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }

        public object Get(IEntity entity) => property.GetValue(entity);

        public void Set(IEntity entity, IDataReader reader, int index) => Set(entity, reader.IsDBNull(index) ? null : reader.GetValue(index));

        public void Set(IEntity entity, object value)
        {
            if (!property.CanWrite) return;
            property.SetValue(entity, Convert(value, target, property.PropertyType));
        }

        internal static object Convert(object value, Type target, Type declared)
        {
            if (value == null || value is DBNull) return declared.IsValueType && Nullable.GetUnderlyingType(declared) == null ? Activator.CreateInstance(declared) : null;
            if (target.IsInstanceOfType(value)) return value;
            if (target.IsEnum) return value is string s ? Enum.Parse(target, s, ignoreCase: true) : Enum.ToObject(target, value);
            if (target == typeof(Guid)) return value is byte[] bytes ? new Guid(bytes) : Guid.Parse(value.ToString());
            if (target == typeof(Blob)) return new Blob(value.ToString());
            if (target == typeof(TimeSpan)) return value is DateTime d ? d.TimeOfDay : TimeSpan.Parse(value.ToString());
            if (target == typeof(DateTimeOffset) && value is DateTime dt) return new DateTimeOffset(dt);
            return System.Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    sealed class SoftDeleteAccessor : IPropertyAccessor
    {
        public object Get(IEntity entity) => entity is Entity e && SoftDeleteAttribute.IsMarked(e);

        public void Set(IEntity entity, IDataReader reader, int index) => Set(entity, reader.IsDBNull(index) ? null : reader.GetValue(index));

        public void Set(IEntity entity, object value)
        {
            if (!(entity is Entity e)) return;
            if (value != null && !(value is DBNull) && System.Convert.ToBoolean(value)) SoftDeleteAttribute.MarkDeleted(e);
            else SoftDeleteAttribute.UnMark(e);
        }
    }

    sealed class OriginalIdAccessor : IPropertyAccessor
    {
        public object Get(IEntity entity) => entity.GetType().GetProperty("OriginalId")?.GetValue(entity) ?? entity.GetId();

        public void Set(IEntity entity, IDataReader reader, int index) { }

        public void Set(IEntity entity, object value) { }
    }

    public class DataProviderMetaData : IDataProviderMetaData
    {
        static readonly ConcurrentDictionary<Type, DataProviderMetaData> All = new ConcurrentDictionary<Type, DataProviderMetaData>();

        internal static DataProviderMetaData For(Type type) => All.GetOrAdd(type, t => new DataProviderMetaData(t));

        DataProviderMetaData(Type type)
        {
            Type = type;
            var declaredName = type.GetCustomAttribute<TableNameAttribute>(inherit: false)?.TableName;
            TableName = declaredName ?? type.Name.ToPlural();
            Schema = SchemaAttribute.GetSchema(type);
            TableAlias = TableName;
            IsSoftDeleteEnabled = SoftDeleteAttribute.IsEnabled(type, inherit: false) || (SoftDeleteAttribute.IsEnabled(type) && BaseClassTypes(type).Length == 0);
            Properties = BuildProperties().ToArray();
        }

        public string TableName { get; }
        public string TableAlias { get; }
        public string Schema { get; }
        public IPropertyData[] Properties { get; }
        public Type Type { get; }
        public bool IsSoftDeleteEnabled { get; }
        public string IdColumnName => "ID";

        public Type[] BaseClassTypesInOrder => BaseClassTypes(Type);

        public Type[] DrivedClassTypes => EntityFinder.GetDomainEntityTypes().Where(t => t.BaseType == Type).ToArray();

        public IDataProviderMetaData[] BaseClassesInOrder => BaseClassTypesInOrder.Select(For).ToArray();

        public IDataProviderMetaData[] DrivedClasses => DrivedClassTypes.Select(For).ToArray();

        public IEnumerable<IPropertyData> UserDefienedProperties => Properties.Where(p => p.IsUserDefined);

        public IEnumerable<IPropertyData> UserDefienedAndIdAndDeletedProperties => Properties.Where(p => p.IsUserDefined || p.IsDefaultId || p.IsDeleted);

        public IEnumerable<IPropertyData> UserDefienedAndDeletedProperties => Properties.Where(p => p.IsUserDefined || p.IsDeleted);

        public IEnumerable<IPropertyData> AssociateProperties => Properties.Where(p => p.AssociateType != null);

        public bool HasAutoNumber => Properties.Any(p => p.IsAutoNumber);

        public IPropertyData AutoNumberProperty => Properties.FirstOrDefault(p => p.IsAutoNumber && p.IsUserDefined) ?? Properties.FirstOrDefault(p => p.IsAutoNumber);

        internal string FullTableName => string.IsNullOrEmpty(Schema) ? TableName : Schema + "." + TableName;

        static Type[] BaseClassTypes(Type type)
        {
            var result = new List<Type>();
            for (var b = type.BaseType; b != null && typeof(IEntity).IsAssignableFrom(b) && !b.IsAbstract && b.Namespace != typeof(IEntity).Namespace; b = b.BaseType)
                result.Insert(0, b);
            return result.ToArray();
        }

        IEnumerable<PropertyData> BuildProperties()
        {
            var idByDatabase = IdByDatabaseAttribute.IsIdAssignedByDatabase(Type, inherit: true);
            foreach (var p in Type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!IsPersistent(p)) continue;
                var associate = typeof(IEntity).IsAssignableFrom(p.PropertyType) ? p.PropertyType : null;
                yield return new PropertyData
                {
                    Name = p.Name,
                    ParameterName = p.Name,
                    PropertyInfo = p,
                    NonGenericType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType,
                    IsAutoNumber = AutoNumberAttribute.IsAutoNumber(p),
                    IsComputed = ComputedColumnAttribute.IsComputedColumn(p),
                    IsCustomPrimaryKey = PrimaryKeyAttribute.IsPrimaryKey(p),
                    AssociateType = associate,
                    IsUserDefined = true,
                    Accessor = new ReflectionPropertyAccessor(p),
                };
            }

            var id = Type.GetProperty("ID");
            yield return new PropertyData
            {
                Name = "ID", ParameterName = "ID", PropertyInfo = id, NonGenericType = id?.PropertyType,
                IsDefaultId = true, IsAutoNumber = idByDatabase, Accessor = id == null ? null : new ReflectionPropertyAccessor(id),
            };

            if (IsSoftDeleteEnabled)
                yield return new PropertyData { Name = ".Deleted", ParameterName = "_Deleted", IsDeleted = true, NonGenericType = typeof(bool), Accessor = new SoftDeleteAccessor() };

            yield return new PropertyData { Name = "OriginalId", ParameterName = "OriginalId", IsOriginalId = true, PropertyInfo = Type.GetProperty("OriginalId"), Accessor = new OriginalIdAccessor() };
        }

        static bool IsPersistent(PropertyInfo p)
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead || !p.CanWrite || p.SetMethod?.IsPublic != true) return false;
            if (p.Name == "ID" || p.Name == "OriginalId") return false;
            if (p.GetCustomAttribute<PersistentAttribute>(inherit: true) is { } persistent) return persistent.IsPersistent;
            if (CalculatedAttribute.IsCalculated(p)) return false;
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (typeof(IEntity).IsAssignableFrom(type)) return false;
            if (type != typeof(string) && type != typeof(byte[]) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return false;
            return true;
        }
    }

    public static class DataProviderExtensions
    {
        public static string GetTableTemplate(this IDataProviderMetaData @this, ISqlCommandGenerator sqlCommandGenerator) =>
            $"{(string.IsNullOrEmpty(@this.Schema) ? @this.TableName : @this.Schema + "." + @this.TableName)} AS {sqlCommandGenerator.SafeId(@this.TableAlias)}";

        public static IEnumerable<IPropertyData> GetPropertiesForFillData(this IDataProviderMetaData @this) =>
            @this.UserDefienedAndIdAndDeletedProperties;

        public static IEnumerable<IPropertyData> GetPropertiesForInsert(this IDataProviderMetaData @this) =>
            @this.UserDefienedAndIdAndDeletedProperties.Where(p => !p.IsAutoNumber && !p.IsComputed);
    }
}
