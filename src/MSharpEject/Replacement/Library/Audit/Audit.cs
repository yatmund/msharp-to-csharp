using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Entities;

namespace Platform.Audit
{
    public interface IAudit
    {
        Task Log(IAuditEvent auditEvent);
        Task Log(string @event, string data, string group = null);
        Task Log(string @event, string data, IEntity item, string group = null);
        Task Log(string @event, string data, IEntity item, string userId, string userIp, string group = null);
        Task LogDelete(IEntity entity);
        Task LogInsert(IEntity entity);
        Task LogUpdate(IEntity entity);
    }

    public interface IAuditEvent : IEntity
    {
        DateTime Date { get; set; }
        string Event { get; set; }
        string ItemId { get; set; }
        string ItemType { get; set; }
        string ItemData { get; set; }
        string ItemGroup { get; set; }
        string UserId { get; set; }
        string UserIp { get; set; }
    }

    public interface IAuditLogger
    {
        IAuditEvent CreateInstance();
        Task Log(IAuditEvent auditEvent);
    }

    public interface IContextUserProvider
    {
        ClaimsPrincipal GetUser();
        string GetUserIP();
    }

    public class DefaultAudit : IAudit
    {
        readonly IConfiguration configuration;

        public DefaultAudit(IConfiguration configuration) => this.configuration = configuration;

        IAuditLogger Logger => Context.Current.GetOptionalService<IAuditLogger>();

        IContextUserProvider UserProvider => Context.Current.GetOptionalService<IContextUserProvider>();

        public virtual async Task LogInsert(IEntity entity)
        {
            var settings = LogEventsAttribute.For(entity);
            if (settings != null && !settings.InsertAction) return;
            await Log("Insert", settings == null || settings.InsertData ? EntityProcessor.GetDataXml(entity) : null, entity);
        }

        public virtual async Task LogUpdate(IEntity entity)
        {
            var settings = LogEventsAttribute.For(entity);
            if (settings != null && !settings.UpdateAction) return;
            var changes = settings == null || settings.UpdateData ? await EntityProcessor.GetChangesXml(entity) : null;
            if (changes == string.Empty) return;
            await Log("Update", changes, entity);
        }

        public virtual async Task LogDelete(IEntity entity)
        {
            var settings = LogEventsAttribute.For(entity);
            if (settings != null && !settings.DeleteAction) return;
            await Log("Delete", settings == null || settings.DeleteData ? EntityProcessor.GetDataXml(entity) : null, entity);
        }

        public virtual async Task Log(IAuditEvent auditEvent)
        {
            if (Logger is { } logger) await logger.Log(auditEvent);
        }

        public virtual Task Log(string @event, string data, string group = null) => Log(@event, data, null, group);

        public virtual Task Log(string @event, string data, IEntity item, string group = null)
        {
            var user = UserProvider;
            return Log(@event, data, item, user?.GetUser()?.Identity?.Name, user?.GetUserIP(), group);
        }

        public virtual async Task Log(string @event, string data, IEntity item, string userId, string userIp, string group = null)
        {
            if (Logger is not { } logger) return;
            var record = logger.CreateInstance();
            record.Date = LocalTime.Now;
            record.Event = @event;
            record.ItemData = data;
            record.ItemGroup = group;
            record.ItemId = item?.GetId()?.ToString();
            record.ItemType = item?.GetType().FullName;
            record.UserId = userId;
            record.UserIp = userIp;
            await logger.Log(record);
        }
    }

    public class EntityProcessor
    {
        static IEnumerable<PropertyInfo> LoggedProperties(IEntity entity) =>
            entity.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                .Where(p => !CalculatedAttribute.IsCalculated(p) && LogEventsAttribute.ShouldLog(p))
                .Where(p => p.Name != "ID" && p.Name != "OriginalId");

        public static async Task<string> GetChangesXml(IEntity entityBeingSaved)
        {
            if (entityBeingSaved.IsNew) return GetDataXml(entityBeingSaved);
            var original = await Context.Current.Database().Reload(entityBeingSaved);
            return ToChangeXml(new EntityProcessor().GetChanges(original, entityBeingSaved));
        }

        public static string GetDataXml(IEntity record) =>
            new XElement("Data", GetDataToLog(record).Select(p => new XElement(p.Key, p.Value))).ToString(SaveOptions.DisableFormatting);

        public static Dictionary<string, string> GetDataToLog(IEntity entity) =>
            LoggedProperties(entity).ToDictionary(p => p.Name, p => p.GetValue(entity)?.ToString());

        public static string ToChangeXml(IDictionary<string, Tuple<string, string>> changes)
        {
            if (changes.Count == 0) return string.Empty;
            return new XElement("Changes", changes.Select(c => new XElement(c.Key, new XAttribute("old", c.Value.Item1 ?? ""), new XAttribute("new", c.Value.Item2 ?? ""))))
                .ToString(SaveOptions.DisableFormatting);
        }

        public static async Task<IEntity> LoadItem(IAuditEvent applicationEvent, bool newItem = false)
        {
            var type = EntityFinder.GetEntityType(applicationEvent.ItemType);
            if (type == null) return null;
            if (!newItem) return await Context.Current.Database().GetOrDefault(applicationEvent.ItemId, type);
            return (IEntity)Activator.CreateInstance(type);
        }

        public virtual IDictionary<string, Tuple<string, string>> GetChanges(IEntity original, IEntity updated)
        {
            var result = new Dictionary<string, Tuple<string, string>>();
            if (original == null || updated == null) return result;
            foreach (var property in LoggedProperties(updated))
            {
                var before = property.GetValue(original)?.ToString();
                var after = property.GetValue(updated)?.ToString();
                if (before != after) result[property.Name] = Tuple.Create(before, after);
            }
            return result;
        }
    }
}

namespace Platform
{
    using Platform.Audit;

    public static class AuditExtensions
    {
        public static IServiceCollection AddDefaultAudit(this IServiceCollection @this) => @this.AddTransient<IAudit, DefaultAudit>();

        public static Task<string> NewChangesToHtml(this IAuditEvent applicationEvent) => ChangesToHtml(applicationEvent, "new");

        public static Task<string> OldChangesToHtml(this IAuditEvent applicationEvent) => ChangesToHtml(applicationEvent, "old");

        static Task<string> ChangesToHtml(IAuditEvent applicationEvent, string side)
        {
            if (string.IsNullOrEmpty(applicationEvent.ItemData)) return Task.FromResult(string.Empty);
            var xml = XElement.Parse(applicationEvent.ItemData);
            var html = new StringBuilder();
            foreach (var element in xml.Elements())
            {
                var value = element.Attribute(side)?.Value ?? (side == "new" ? element.Value : null);
                if (value != null) html.Append("<div class=\"prop\"><span class=\"key\">").Append(element.Name.LocalName.HtmlEncode()).Append("</span>: <span class=\"val\">").Append(value.HtmlEncode()).Append("</span></div>");
            }
            return Task.FromResult(html.ToString());
        }

        public static bool IsEntity(this PropertyInfo property, object obj) => typeof(IEntity).IsAssignableFrom(property.PropertyType);
    }
}
