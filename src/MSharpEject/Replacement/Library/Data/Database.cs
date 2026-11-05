using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Audit;

namespace Platform.Entities.Data
{
    public class Database : IDatabase
    {
        static readonly ConcurrentDictionary<string, AsyncLock> SyncLocks = new ConcurrentDictionary<string, AsyncLock>();
        readonly IAudit audit;
        readonly IDatabaseProviderConfig providerConfig;

        public Database(IAudit audit, IDatabaseProviderConfig providerConfig, ICache cache)
        {
            this.audit = audit;
            this.providerConfig = providerConfig;
            Cache = cache;
        }

        public static IDatabase Instance => Context.Current.Database();

        public ICache Cache { get; }

        public event AwaitableEventHandler CacheRefreshed;

        public event AwaitableEventHandler<IEntity> Updated;

        public static AsyncLock GetSyncLock(string key) => SyncLocks.GetOrAdd(key, _ => new AsyncLock());

        public IDataProvider GetProvider(Type type) => providerConfig.GetProvider(type);

        public IDataProvider GetProvider(IEntity item) => GetProvider(item.GetType());

        public IDataProvider GetProvider<T>() where T : IEntity => GetProvider(typeof(T));

        public IDataAccess GetAccess(Type type) => GetProvider(type).Access;

        public IDataAccess GetAccess<TEntity>() where TEntity : IEntity => GetAccess(typeof(TEntity));

        public IDataAccess GetAccess<TConnection>(string connectionString = null) where TConnection : System.Data.Common.DbConnection, new() => DataAccess.Create<TConnection>(connectionString);

        public IDataAccess GetAccess(string connectionString = null) => providerConfig.GetAccess(connectionString);

        public IDatabaseQuery<TEntity> Of<TEntity>() where TEntity : IEntity => new DatabaseQuery<TEntity>();

        public IDatabaseQuery Of(Type type) => (IDatabaseQuery)Activator.CreateInstance(typeof(DatabaseQuery<>).MakeGenericType(type));

        public async Task Refresh()
        {
            Cache.ClearAll();
            await CacheRefreshed.Raise();
        }

        public bool AnyOpenTransaction() => DbTransactionScope.Current != null || System.Transactions.Transaction.Current != null;

        public ITransactionScope CreateTransactionScope(DbTransactionScopeOption option = DbTransactionScopeOption.Required) => new DbTransactionScope(option);

        public async Task EnlistOrCreateTransaction(Func<Task> func)
        {
            if (AnyOpenTransaction())
            {
                await func();
                return;
            }
            using (var scope = CreateTransactionScope())
            {
                await func();
                scope.Complete();
            }
        }

        // Get ------------------------------------------------------------

        public async Task<IEntity> Get(object entityID, Type objectType)
        {
            if (entityID == null) return null;
            if (Cache.IsCacheable(objectType) && Cache.Get(objectType, entityID.ToString()) is { } cached) return cached;
            var provider = GetProvider(objectType);
            var result = await provider.Get(Normalise(entityID, objectType));
            if (Cache.IsCacheable(objectType)) Cache.Add(result);
            return result;
        }

        static object Normalise(object id, Type type)
        {
            if (!(id is string text)) return id;
            var idType = type.GetProperty("ID")?.PropertyType;
            if (idType == typeof(Guid)) return Guid.Parse(text);
            if (idType == typeof(int)) return int.Parse(text);
            if (idType == typeof(long)) return long.Parse(text);
            return text;
        }

        public async Task<IEntity<Guid>> Get(Guid entityID, Type objectType) => (IEntity<Guid>)await Get((object)entityID, objectType);

        public async Task<T> Get<T>(string entityId) where T : IEntity => string.IsNullOrEmpty(entityId) ? default(T) : (T)await Get(entityId, typeof(T));
        public async Task<T> Get<T>(Guid id) where T : IEntity => (T)await Get((object)id, typeof(T));
        public async Task<T> Get<T>(Guid? id) where T : IEntity => id == null ? default(T) : (T)await Get((object)id.Value, typeof(T));
        public async Task<T> Get<T>(int? id) where T : IEntity<int> => id == null ? default(T) : (T)await Get((object)id.Value, typeof(T));
        public async Task<T> Get<T>(int id) where T : IEntity<int> => (T)await Get((object)id, typeof(T));
        public async Task<T> Get<T>(long? id) where T : IEntity<long> => id == null ? default(T) : (T)await Get((object)id.Value, typeof(T));
        public async Task<T> Get<T>(long id) where T : IEntity<long> => (T)await Get((object)id, typeof(T));
        public async Task<T> Get<T>(short? id) where T : IEntity<short> => id == null ? default(T) : (T)await Get((object)id.Value, typeof(T));
        public async Task<T> Get<T>(short id) where T : IEntity<short> => (T)await Get((object)id, typeof(T));
        public async Task<T> Get<T>(byte? id) where T : IEntity<byte> => id == null ? default(T) : (T)await Get((object)id.Value, typeof(T));
        public async Task<T> Get<T>(byte id) where T : IEntity<byte> => (T)await Get((object)id, typeof(T));

        public async Task<T> GetOrDefault<T>(object id) where T : IEntity => (T)await GetOrDefault(id, typeof(T));

        public async Task<IEntity> GetOrDefault(object id, Type type)
        {
            if (id == null || (id is string s && s.Length == 0)) return null;
            try { return await Get(id, type); }
            catch (System.Data.DataException) { return null; }
            catch (FormatException) { return null; }
        }

        public async Task<T> Reload<T>(T instance) where T : IEntity
        {
            if (instance == null) return default(T);
            Cache.Remove(instance);
            return (T)await GetProvider(instance).Get(instance.GetId());
        }

        public async Task<T> Parse<T>(string toString, bool caseSensitive = false) where T : IEntity =>
            (await GetList<T>()).FirstOrDefault(i => string.Equals(i.ToString(), toString, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        public Task<T[]> GetList<T>(Expression<Func<T, bool>> criteria = null) where T : IEntity => Of<T>().Where(criteria).GetList();

        public Task<T> FirstOrDefault<T>(Expression<Func<T, bool>> criteria) where T : IEntity => Of<T>().Where(criteria).FirstOrDefault();

        public Task<T> FirstOrDefault<T>() where T : IEntity => Of<T>().FirstOrDefault();

        public Task<bool> Any<T>() where T : IEntity => Of<T>().Any();

        public Task<bool> Any<T>(Expression<Func<T, bool>> criteria) where T : IEntity => Of<T>().Where(criteria).Any();

        public Task<bool> None<T>() where T : IEntity => Of<T>().None();

        public Task<bool> None<T>(Expression<Func<T, bool>> criteria) where T : IEntity => Of<T>().Where(criteria).None();

        public Task<int> Count<T>() where T : IEntity => Of<T>().Count();

        public Task<int> Count<T>(Expression<Func<T, bool>> criteria) where T : IEntity => Of<T>().Where(criteria).Count();

        public Task<IEnumerable<string>> ReadManyToManyRelation(IEntity instance, string property) => GetProvider(instance).ReadManyToManyRelation(instance, property);

        // Save -----------------------------------------------------------

        public async Task<T> Save<T>(T entity) where T : IEntity
        {
            await Save(entity, SaveBehaviour.Default);
            return entity;
        }

        public async Task Save(IEntity entity, SaveBehaviour behaviour)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (Entity.Services.IsImmutable(entity)) throw new ArgumentException("An immutable record must be cloned before any modifications can be applied on it.");

            if (!behaviour.HasFlag(SaveBehaviour.BypassSaving))
            {
                var args = new CancelEventArgs();
                await Entity.Services.RaiseOnSaving(entity, args);
                if (args.Cancel) return;
            }

            if (!behaviour.HasFlag(SaveBehaviour.BypassValidation)) await entity.Validate();

            var mode = entity.IsNew ? SaveMode.Insert : SaveMode.Update;
            if (mode == SaveMode.Update && !behaviour.HasFlag(SaveBehaviour.BypassLogging)) await audit.LogUpdate(entity);

            await EnlistOrCreateTransaction(async () =>
            {
                await GetProvider(entity).Save(entity);
                await SaveBlobs(entity);
            });

            Entity.Services.SetSaved(entity);
            Entity.Services.SetOriginalId(entity);
            Cache.Remove(entity);
            Cache.RemoveList(entity.GetType());
            if (entity is Entity e && e._ClonedFrom != null) e._ClonedFrom.UpdatedClone = e;

            if (mode == SaveMode.Insert && !behaviour.HasFlag(SaveBehaviour.BypassLogging)) await audit.LogInsert(entity);
            if (!behaviour.HasFlag(SaveBehaviour.BypassSaved)) await Entity.Services.RaiseOnSaved(entity, new SaveEventArgs(mode));
            await Updated.Raise(entity);
        }

        static async Task SaveBlobs(IEntity entity)
        {
            foreach (var property in entity.GetType().GetProperties().Where(p => p.PropertyType == typeof(Blob) && p.CanRead))
            {
                if (!(property.GetValue(entity) is Blob blob)) continue;
                if (entity is Entity owner && blob.OwnerId() == null) blob.Attach(owner, property.Name);
                await blob.Save();
            }
        }

        public async Task<IEnumerable<T>> Save<T>(List<T> records) where T : IEntity => await Save((IEnumerable<T>)records, SaveBehaviour.Default);

        public Task<IEnumerable<T>> Save<T>(T[] records) where T : IEntity => Save((IEnumerable<T>)records, SaveBehaviour.Default);

        public Task<IEnumerable<T>> Save<T>(IEnumerable<T> records) where T : IEntity => Save(records, SaveBehaviour.Default);

        public async Task<IEnumerable<T>> Save<T>(IEnumerable<T> records, SaveBehaviour behaviour) where T : IEntity
        {
            var list = records.ToList();
            await EnlistOrCreateTransaction(async () =>
            {
                foreach (var record in list) await Save(record, behaviour);
            });
            return list;
        }

        // Update clones the record, applies the change and saves the clone, so a cached instance never changes underneath its readers.
        public Task<T> Update<T>(T item, Action<T> action) where T : IEntity => Update(item, action, SaveBehaviour.Default);

        public Task<T> Update<T>(T item, Func<T, Task> action) where T : IEntity => Update(item, action, SaveBehaviour.Default);

        public Task<T> Update<T>(T item, Action<T> action, SaveBehaviour behaviour) where T : IEntity =>
            Update(item, x => { action(x); return Task.CompletedTask; }, behaviour);

        public async Task<T> Update<T>(T item, Func<T, Task> action, SaveBehaviour behaviour) where T : IEntity
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var clone = item.IsNew ? item : (T)item.Clone();
            await action(clone);
            await Save(clone, behaviour);
            return clone;
        }

        public Task<List<T>> Update<T>(IEnumerable<T> items, Action<T> action) where T : IEntity => Update(items, action, SaveBehaviour.Default);

        public Task<List<T>> Update<T>(IEnumerable<T> items, Func<T, Task> action) where T : IEntity => Update(items, action, SaveBehaviour.Default);

        public Task<List<T>> Update<T>(IEnumerable<T> items, Action<T> action, SaveBehaviour behaviour) where T : IEntity =>
            Update(items, x => { action(x); return Task.CompletedTask; }, behaviour);

        public async Task<List<T>> Update<T>(IEnumerable<T> items, Func<T, Task> action, SaveBehaviour behaviour) where T : IEntity
        {
            var result = new List<T>();
            await EnlistOrCreateTransaction(async () =>
            {
                foreach (var item in items.ToList()) result.Add(await Update(item, action, behaviour));
            });
            return result;
        }

        public async Task UpdateAll<T>(Action<T> change) where T : IEntity => await Update(await GetList<T>(), change);

        public async Task BulkInsert(Entity[] objects, int batchSize = 10, bool bypassValidation = false)
        {
            foreach (var group in objects.GroupBy(o => o.GetType()))
            {
                if (!bypassValidation) foreach (var item in group) await item.Validate();
                await GetProvider(group.Key).BulkInsert(group.Cast<IEntity>().ToArray(), batchSize);
                foreach (var item in group) Entity.Services.SetSaved(item);
                Cache.Remove(group.Key);
            }
        }

        public async Task BulkUpdate(Entity[] objects, int batchSize = 10, bool bypassValidation = false)
        {
            foreach (var group in objects.GroupBy(o => o.GetType()))
            {
                if (!bypassValidation) foreach (var item in group) await item.Validate();
                await GetProvider(group.Key).BulkUpdate(group.Cast<IEntity>().ToArray(), batchSize);
                Cache.Remove(group.Key);
            }
        }

        // Delete ---------------------------------------------------------

        public Task Delete(IEntity instance) => Delete(instance, DeleteBehaviour.Default);

        public async Task Delete(IEntity instance, DeleteBehaviour behaviour)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!behaviour.HasFlag(DeleteBehaviour.BypassDeleting))
            {
                var args = new CancelEventArgs();
                await Entity.Services.RaiseOnDeleting(instance, args);
                if (args.Cancel) return;
            }

            await EnlistOrCreateTransaction(async () =>
            {
                if (instance is Entity entity && SoftDeleteAttribute.IsEnabled(instance.GetType()) && !SoftDeleteAttribute.Context.ShouldByPassSoftDelete())
                {
                    SoftDeleteAttribute.MarkDeleted(entity);
                    await GetProvider(instance).Save(instance);
                }
                else await GetProvider(instance).Delete(instance);
            });

            Cache.Remove(instance);
            Cache.RemoveList(instance.GetType());
            if (!behaviour.HasFlag(DeleteBehaviour.BypassLogging)) await audit.LogDelete(instance);
            if (!behaviour.HasFlag(DeleteBehaviour.BypassDeleted)) await Entity.Services.RaiseOnDeleted(instance);
            await Updated.Raise(instance);
        }

        public async Task Delete<T>(IEnumerable<T> instances) where T : IEntity
        {
            var list = instances.ToList();
            await EnlistOrCreateTransaction(async () =>
            {
                foreach (var item in list) await Delete(item);
            });
        }

        public async Task DeleteAll<T>() where T : IEntity => await Delete(await GetList<T>());

        public async Task DeleteAll<T>(Expression<Func<T, bool>> criteria) where T : IEntity => await Delete(await GetList(criteria));
    }

    public interface ICacheProvider
    {
        IEntity Get(Type entityType, string id);
        void Add(IEntity entity);
        void Remove(IEntity entity);
        void Remove(Type type, bool invalidateCachedReferences = false);
        void AddList(Type type, IEnumerable list);
        void RemoveList(Type type);
        IEnumerable GetList(Type type);
        void ClearAll();
        bool IsUpdatedSince(IEntity instance, DateTime since);
        void UpdateRowVersion(IEntity entity);
    }

    // Registered transient like Olive, so the cache state is static.
    public class InMemoryCacheProvider : ICacheProvider
    {
        static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, IEntity>> items = new ConcurrentDictionary<Type, ConcurrentDictionary<string, IEntity>>();
        static readonly ConcurrentDictionary<Type, IEnumerable> lists = new ConcurrentDictionary<Type, IEnumerable>();
        static readonly ConcurrentDictionary<string, DateTime> rowVersions = new ConcurrentDictionary<string, DateTime>();

        static Type Root(Type type) => type.GetRootEntityType();

        public IEntity Get(Type entityType, string id) =>
            items.TryGetValue(Root(entityType), out var byId) && byId.TryGetValue(id, out var entity) && entityType.IsInstanceOfType(entity) ? entity : null;

        public void Add(IEntity entity)
        {
            if (entity?.GetId() == null) return;
            Entity.Services.MarkImmutable(entity);
            items.GetOrAdd(Root(entity.GetType()), _ => new ConcurrentDictionary<string, IEntity>())[entity.GetId().ToString()] = entity;
        }

        public void Remove(IEntity entity)
        {
            if (entity?.GetId() == null) return;
            if (items.TryGetValue(Root(entity.GetType()), out var byId)) byId.TryRemove(entity.GetId().ToString(), out _);
            RemoveList(entity.GetType());
        }

        public void Remove(Type type, bool invalidateCachedReferences = false)
        {
            if (items.TryRemove(Root(type), out var removed) && invalidateCachedReferences)
                foreach (var entity in removed.Values) entity.InvalidateCachedReferences();
            RemoveList(type);
        }

        public void AddList(Type type, IEnumerable list)
        {
            foreach (IEntity entity in list) Entity.Services.MarkImmutable(entity);
            lists[type] = list;
        }

        public void RemoveList(Type type)
        {
            foreach (var key in lists.Keys.Where(k => k.IsAssignableFrom(type) || type.IsAssignableFrom(k)).ToList()) lists.TryRemove(key, out _);
        }

        public IEnumerable GetList(Type type) => lists.TryGetValue(type, out var list) ? list : null;

        public void ClearAll()
        {
            items.Clear();
            lists.Clear();
        }

        public bool IsUpdatedSince(IEntity instance, DateTime since) =>
            rowVersions.TryGetValue(instance.GetFullIdentifierString(), out var version) && version > since;

        public void UpdateRowVersion(IEntity entity) => rowVersions[entity.GetFullIdentifierString()] = DateTime.UtcNow;
    }

    public class Cache : ICache
    {
        readonly ICacheProvider provider;
        readonly IDatabaseProviderConfig providerConfig;
        static readonly DateTime started = DateTime.UtcNow;

        public Cache(ICacheProvider provider, IDatabaseProviderConfig providerConfig)
        {
            this.provider = provider;
            this.providerConfig = providerConfig;
        }

        bool Enabled => providerConfig?.Configuration.Cache.Enabled ?? true;

        public DateTime? GetQueryTimestamp() => started;

        public void Add(IEntity entity)
        {
            if (entity != null && IsCacheable(entity.GetType())) provider.Add(entity);
        }

        public virtual void Remove(IEntity entity) => provider.Remove(entity);

        public virtual void Remove(Type type, bool invalidateCachedReferences = false) => provider.Remove(type, invalidateCachedReferences);

        public virtual void RemoveList(Type type) => provider.RemoveList(type);

        public virtual IEnumerable GetList(Type type) => IsCacheable(type) ? provider.GetList(type) : null;

        public void ClearAll() => provider.ClearAll();

        public void AddList(Type type, IEnumerable list)
        {
            if (IsCacheable(type)) provider.AddList(type, list);
        }

        public bool IsUpdatedSince(IEntity instance, DateTime since) => provider.IsUpdatedSince(instance, since);

        public void UpdateRowVersion(IEntity entity) => provider.UpdateRowVersion(entity);

        public virtual TEntity Get<TEntity>(object id) where TEntity : IEntity => id == null ? default(TEntity) : (TEntity)Get(typeof(TEntity), id.ToString());

        public IEntity Get(Type type, string id) => IsCacheable(type) ? provider.Get(type, id) : null;

        public bool IsCacheable(Type type) => CacheObjectsAttribute.IsEnabled(type) ?? Enabled;
    }

    public static class CacheExtensions
    {
        public static IServiceCollection AddDatabase(this IServiceCollection @this, IConfiguration config)
        {
            @this.AddSingleton<IDatabaseProviderConfig, DatabaseProviderConfig>();
            @this.AddTransient<ICacheProvider, InMemoryCacheProvider>();
            @this.AddTransient<ICache, Cache>();
            @this.AddSingleton<IDatabase, Database>();
            return @this;
        }
    }
}
