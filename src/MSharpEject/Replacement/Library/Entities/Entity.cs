using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Platform.Entities
{
    public interface IEntity : IComparable
    {
        Task Validate();

        object GetId();

        IEntity Clone();

        void InvalidateCachedReferences();

        bool IsNew { get; }
    }

    public interface IEntity<T> : IEntity
    {
        T ID { get; set; }
    }

    public interface IOriginalIdHolder
    {
        void SetOriginalId();
    }

    public interface IHierarchy : IEntity
    {
        IHierarchy GetParent();

        IEnumerable<IHierarchy> GetChildren();

        string Name { get; }
    }

    public interface ISortable : IEntity
    {
        int Order { get; set; }
    }

    public enum SaveMode { Update = 0, Insert = 1 }

    [Flags]
    public enum SaveBehaviour
    {
        Default = 1,
        BypassValidation = 2,
        BypassSaving = 4,
        BypassSaved = 8,
        BypassLogging = 16,
        BypassAll = BypassValidation | BypassSaving | BypassSaved | BypassLogging,
    }

    [Flags]
    public enum DeleteBehaviour
    {
        Default = 1,
        BypassDeleting = 2,
        BypassDeleted = 4,
        BypassLogging = 8,
        BypassAll = BypassDeleting | BypassDeleted | BypassLogging,
    }

    public class SaveEventArgs : EventArgs
    {
        public SaveEventArgs(SaveMode mode) => Mode = mode;

        public SaveMode Mode { get; }
    }

    public class DeleteEventArgs : EventArgs { }

    public class GlobalSaveEventArgs : SaveEventArgs
    {
        public IEntity Entity;

        public GlobalSaveEventArgs(IEntity entity, SaveMode mode) : base(mode) => Entity = entity;
    }

    public class GlobalDeleteEventArgs : DeleteEventArgs
    {
        public IEntity Entity;

        public GlobalDeleteEventArgs(IEntity entity) => Entity = entity;

        public Type EntityType => Entity?.GetType();

        public object EnityID => Entity?.GetId();
    }

    public static class GlobalEntityEvents
    {
        public static event AwaitableEventHandler<CancelEventArgs> InstanceSaving;
        public static event AwaitableEventHandler<GlobalSaveEventArgs> InstanceSaved;
        public static event AwaitableEventHandler<CancelEventArgs> InstanceDeleting;
        public static event AwaitableEventHandler<EventArgs> InstanceValidating;
        public static event AwaitableEventHandler<GlobalDeleteEventArgs> InstanceDeleted;

        public static Task OnInstanceSaved(GlobalSaveEventArgs args) => InstanceSaved.Raise(args);

        internal static Task OnInstanceSaving(IEntity entity, CancelEventArgs args) => InstanceSaving.Raise(args);

        internal static Task OnInstanceDeleting(IEntity entity, CancelEventArgs args) => InstanceDeleting.Raise(args);

        internal static Task OnInstanceValidating(IEntity entity, EventArgs args) => InstanceValidating.Raise(args);

        internal static Task OnInstanceDeleted(GlobalDeleteEventArgs args) => InstanceDeleted.Raise(args);
    }

    public class ValidationException : Exception
    {
        public ValidationException() { }

        public ValidationException(string messageFormat, params object[] arguments) : base(string.Format(messageFormat, arguments)) { }

        public ValidationException(string message) : base(message) { }

        public ValidationException(string message, Exception inner) : base(message, inner) { }

        public string InvalidPropertyName { get; set; }

        public bool IsMessageTranslated { get; set; }
    }

    public sealed class EntityServices
    {
        public EntityServices() { }
    }

    public abstract class Entity : IEntity
    {
        public Entity _ClonedFrom;
        public Entity UpdatedClone;
        public static readonly EntityServices Services = new EntityServices();

        internal bool IsSaved;
        internal bool IsImmutable;

        protected Entity() => Initialize();

        public event AwaitableEventHandler Loaded;
        public event AwaitableEventHandler<CancelEventArgs> Saving;
        public event AwaitableEventHandler Validating;
        public event AwaitableEventHandler<SaveEventArgs> Saved;
        public event AwaitableEventHandler<CancelEventArgs> Deleting;
        public event AwaitableEventHandler Deleted;

        public static IDatabase Database => Context.Current.Database();

        public virtual bool IsNew => !IsSaved;

        public virtual bool IsMarkedSoftDeleted => SoftDeleteAttribute.IsMarked(this);

        public bool IsStale => UpdatedClone != null;

        protected virtual void Initialize() { }

        public abstract object GetId();

        public virtual void InvalidateCachedReferences() { }

        public virtual string ToString(string format) => ToString();

        protected virtual Task ValidateProperties() => Task.CompletedTask;

        public virtual async Task Validate()
        {
            await OnValidating(EventArgs.Empty);
            await ValidateProperties();
        }

        public virtual IEntity Clone()
        {
            var clone = (Entity)MemberwiseClone();
            clone._ClonedFrom = this;
            clone.UpdatedClone = null;
            return clone;
        }

        protected virtual Task OnLoaded() => Loaded.Raise();

        protected virtual async Task OnSaving(CancelEventArgs e)
        {
            await Saving.Raise(e);
            await GlobalEntityEvents.OnInstanceSaving(this, e);
        }

        protected virtual async Task OnValidating(EventArgs e)
        {
            await Validating.Raise();
            await GlobalEntityEvents.OnInstanceValidating(this, e);
        }

        protected virtual async Task OnSaved(SaveEventArgs e)
        {
            await Saved.Raise(e);
            await GlobalEntityEvents.OnInstanceSaved(new GlobalSaveEventArgs(this, e.Mode));
        }

        protected virtual async Task OnDeleting(CancelEventArgs e)
        {
            await Deleting.Raise(e);
            await GlobalEntityEvents.OnInstanceDeleting(this, e);
        }

        protected virtual async Task OnDeleted(EventArgs e)
        {
            await Deleted.Raise();
            await GlobalEntityEvents.OnInstanceDeleted(new GlobalDeleteEventArgs(this));
        }

        internal Task RaiseLoaded() => OnLoaded();
        internal Task RaiseSaving(CancelEventArgs e) => OnSaving(e);
        internal Task RaiseValidating(EventArgs e) => OnValidating(e);
        internal Task RaiseSaved(SaveEventArgs e) => OnSaved(e);
        internal Task RaiseDeleting(CancelEventArgs e) => OnDeleting(e);
        internal Task RaiseDeleted(EventArgs e) => OnDeleted(e);

        public override int GetHashCode() => GetId()?.GetHashCode() ?? 0;

        public override bool Equals(object @object) => @object is Entity other && Equals(other);

        public abstract bool Equals(Entity @object);

        public static bool operator ==(Entity left, object right) => ReferenceEquals(left, null) ? right == null : left.Equals(right);

        public static bool operator !=(Entity left, object right) => !(left == right);

        public static bool operator ==(Entity left, Entity right) => ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.Equals(right);

        public static bool operator !=(Entity left, Entity right) => !(left == right);

        public virtual int CompareTo(object other) => other == null ? 1 : string.Compare(ToString(), other.ToString(), StringComparison.CurrentCulture);
    }

    public abstract class Entity<T> : Entity, IEntity<T>, IOriginalIdHolder
    {
        public Entity() { }

        public virtual T ID { get; set; }

        public virtual T OriginalId { get; internal set; }

        public override object GetId() => ID;

        void IOriginalIdHolder.SetOriginalId() => OriginalId = ID;

        public override int GetHashCode() => ID?.GetHashCode() ?? 0;

        public override bool Equals(Entity other) =>
            other is Entity<T> typed && !ReferenceEquals(other, null) && EqualityComparer<T>.Default.Equals(ID, typed.ID) && EntityFinder.GetRoot(GetType()) == EntityFinder.GetRoot(other.GetType());
    }

    public abstract class GuidEntity : Entity<Guid>
    {
        Guid? id;

        public static Func<Type, Guid> NewIdGenerator = _ => Guid.NewGuid();

        // Assigned on first read, so an entity has an ID before it's saved.
        public override Guid ID
        {
            get => id ?? (id = NewIdGenerator(GetType())).Value;
            set => id = value;
        }

        public override IEntity Clone() => base.Clone();

        public override bool Equals(Entity other) => base.Equals(other);

        public override bool Equals(object other) => other is Entity entity ? Equals(entity) : false;

        public override int GetHashCode() => ID.GetHashCode();

        public static bool operator ==(GuidEntity entity, Guid? id) => entity?.ID == id;
        public static bool operator !=(GuidEntity entity, Guid? id) => !(entity == id);
        public static bool operator ==(GuidEntity entity, Guid id) => entity?.ID == id;
        public static bool operator !=(GuidEntity entity, Guid id) => !(entity == id);
        public static bool operator ==(Guid? id, GuidEntity entity) => entity == id;
        public static bool operator !=(Guid? id, GuidEntity entity) => !(entity == id);
        public static bool operator ==(Guid id, GuidEntity entity) => entity == id;
        public static bool operator !=(Guid id, GuidEntity entity) => !(entity == id);
    }

    public class IntEntity : Entity<int>
    {
        public static Func<Type, Task<int>> NewIdGenerator;

        public override int ID { get; set; }

        public override bool Equals(Entity other) => base.Equals(other);

        public override bool Equals(object other) => other is Entity entity ? Equals(entity) : false;

        public override int GetHashCode() => ID.GetHashCode();

        public static bool operator ==(IntEntity entity, int? id) => entity?.ID == id;
        public static bool operator !=(IntEntity entity, int? id) => !(entity == id);
        public static bool operator ==(IntEntity entity, int id) => entity?.ID == id;
        public static bool operator !=(IntEntity entity, int id) => !(entity == id);
        public static bool operator ==(int? id, IntEntity entity) => entity == id;
        public static bool operator !=(int? id, IntEntity entity) => !(entity == id);
        public static bool operator ==(int id, IntEntity entity) => entity == id;
        public static bool operator !=(int id, IntEntity entity) => !(entity == id);
    }

    public abstract class StringEntity : Entity<string>
    {
        public StringEntity() { }

        public override bool Equals(Entity other) => base.Equals(other);

        public override bool Equals(object other) => other is Entity entity ? Equals(entity) : false;

        public override int GetHashCode() => ID?.GetHashCode() ?? 0;

        public static bool operator ==(StringEntity entity, string id) => entity?.ID == id;
        public static bool operator !=(StringEntity entity, string id) => !(entity == id);
        public static bool operator ==(string id, StringEntity entity) => entity == id;
        public static bool operator !=(string id, StringEntity entity) => !(entity == id);
    }

    public static class EntityStateExtensions
    {
        public static bool IsImmutable(this EntityServices @this, IEntity entity) => entity is Entity e && e.IsImmutable;

        public static void MarkImmutable(this EntityServices @this, IEntity entity)
        {
            if (entity is Entity e) e.IsImmutable = true;
        }

        public static void SetSaved(this EntityServices @this, IEntity entity)
        {
            if (entity is Entity e) e.IsSaved = true;
        }

        public static void SetOriginalId(this EntityServices @this, IEntity entity) => (entity as IOriginalIdHolder)?.SetOriginalId();

        public static T CloneAsNew<T>(this EntityServices @this, T entity, Action<T> changes = null) where T : Entity
        {
            var clone = (T)entity.Clone();
            clone.IsSaved = false;
            clone._ClonedFrom = null;
            if (clone is GuidEntity guid) guid.ID = GuidEntity.NewIdGenerator(clone.GetType());
            changes?.Invoke(clone);
            return clone;
        }
    }
}

namespace Platform.Entities.Data
{
    public interface IEntityLoadedInterceptor
    {
        Task Process(IEntity entity);
    }

    public interface IEntitySavingInterceptor
    {
        Task Process(IEntity entity);
    }

    public static class EntityRaiseEventExtensions
    {
        public static Task RaiseOnDeleting(this EntityServices @this, IEntity record, CancelEventArgs args) => (record as Entity)?.RaiseDeleting(args) ?? Task.CompletedTask;

        public static Task RaiseOnValidating(this EntityServices @this, IEntity record, EventArgs args) => (record as Entity)?.RaiseValidating(args) ?? Task.CompletedTask;

        public static Task RaiseOnDeleted(this EntityServices @this, IEntity record) => (record as Entity)?.RaiseDeleted(EventArgs.Empty) ?? Task.CompletedTask;

        public static Task RaiseOnLoaded(this EntityServices @this, IEntity record) => (record as Entity)?.RaiseLoaded() ?? Task.CompletedTask;

        public static Task RaiseOnSaving(this EntityServices @this, IEntity record, CancelEventArgs e) => (record as Entity)?.RaiseSaving(e) ?? Task.CompletedTask;

        public static Task RaiseOnSaved(this EntityServices @this, IEntity record, SaveEventArgs e) => (record as Entity)?.RaiseSaved(e) ?? Task.CompletedTask;
    }
}
