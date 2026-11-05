using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Platform.Entities
{
    public enum FilterFunction
    {
        Is = 0, IsNot = 1, Null = 2, NotNull = 3, Contains = 4, NotContains = 5, ContainsAll = 6, ContainsAny = 7,
        In = 8, NotIn = 9, BeginsWith = 10, EndsWith = 11, InRange = 12, LessThan = 13, LessThanOrEqual = 14, MoreThan = 15, MoreThanOrEqual = 16,
    }

    public enum BinaryOperator { OR = 0, AND = 1 }

    public static class FilterFunctionServices
    {
        public static string GetDatabaseOperator(this FilterFunction option)
        {
            switch (option)
            {
                case FilterFunction.Is: return "=";
                case FilterFunction.IsNot: return "<>";
                case FilterFunction.Null: return "IS NULL";
                case FilterFunction.NotNull: return "IS NOT NULL";
                case FilterFunction.Contains:
                case FilterFunction.BeginsWith:
                case FilterFunction.EndsWith: return "LIKE";
                case FilterFunction.NotContains: return "NOT LIKE";
                case FilterFunction.In: return "IN";
                case FilterFunction.NotIn: return "NOT IN";
                case FilterFunction.LessThan: return "<";
                case FilterFunction.LessThanOrEqual: return "<=";
                case FilterFunction.MoreThan: return ">";
                case FilterFunction.MoreThanOrEqual: return ">=";
                default: throw new NotSupportedException(option + " has no single database operator.");
            }
        }
    }

    public interface ICriterion
    {
        string ToSql(SqlConversionContext context);
        string PropertyName { get; }
        FilterFunction FilterFunction { get; set; }
        object Value { get; }
        string SqlCondition { get; }
    }

    public class Criterion : ICriterion
    {
        public Criterion(string propertyName, object value) : this(propertyName, value == null ? FilterFunction.Null : FilterFunction.Is, value) { }

        public Criterion(string propertyName, FilterFunction function, object value)
        {
            PropertyName = propertyName;
            FilterFunction = function;
            Value = value;
        }

        public Criterion(string propertyName, FilterFunction function, IEnumerable<Guid> ids) : this(propertyName, function, (object)ids.ToArray()) { }

        public Criterion(string propertyName, FilterFunction function, IEnumerable<string> ids) : this(propertyName, function, (object)ids.ToArray()) { }

        public Criterion(string propertyName, FilterFunction function, IEnumerable<int> ids) : this(propertyName, function, (object)ids.ToArray()) { }

        public Criterion(string propertyName, string function, object value) : this(propertyName, (FilterFunction)Enum.Parse(typeof(FilterFunction), function, ignoreCase: true), value) { }

        protected Criterion() { }

        public string PropertyName { get; protected set; }

        public string SqlCondition { get; protected set; }

        public FilterFunction FilterFunction { get; set; }

        public object Value { get; set; }

        public override string ToString() => SqlCondition ?? $"{PropertyName}|{FilterFunction}|{Value}";

        public static Criterion Parse(string criterionString)
        {
            var parts = criterionString.Split('|');
            if (parts.Length < 2) return FromSql(criterionString);
            return new Criterion(parts[0], parts[1], parts.Length > 2 ? string.Join("|", parts.Skip(2)) : null);
        }

        public static Criterion FromSql(string sqlCondition) => new Criterion { SqlCondition = sqlCondition };

        public static Criterion FromSql(string sqlCondition, string propertyName) => new Criterion { SqlCondition = sqlCondition, PropertyName = propertyName };

        public static Criterion From<T>(Expression<Func<T, bool>> criterion) where T : IEntity => From(criterion.Body);

        public static Criterion From(Expression expression)
        {
            switch (expression)
            {
                case BinaryExpression binary: return From(binary);
                case MethodCallExpression call: return From(call);
                case MemberExpression member: return From(member);
                case UnaryExpression unary: return From(unary);
                default: return null;
            }
        }

        public static Criterion From(MethodCallExpression expression, bool throwOnError = true)
        {
            var method = expression.Method.Name;
            var target = expression.Object as MemberExpression ?? (expression.Arguments.Count > 0 ? expression.Arguments[0] as MemberExpression : null);

            // x.Name.StartsWith("a"), x.Name.Contains("a"), x.Name.EndsWith("a")
            if (expression.Object is MemberExpression stringMember && IsEntityMember(stringMember) && expression.Arguments.Count >= 1)
            {
                var value = expression.Arguments[0].GetValue();
                switch (method)
                {
                    case "StartsWith": return new Criterion(stringMember.GetDatabaseColumnPath(), FilterFunction.BeginsWith, value);
                    case "EndsWith": return new Criterion(stringMember.GetDatabaseColumnPath(), FilterFunction.EndsWith, value);
                    case "Contains": return new Criterion(stringMember.GetDatabaseColumnPath(), FilterFunction.Contains, value);
                    case "Equals": return new Criterion(stringMember.GetDatabaseColumnPath(), FilterFunction.Is, value);
                }
            }

            // list.Contains(x.Prop) or x.Prop.IsAnyOf(list)
            if ((method == "Contains" || method == "IsAnyOf" || method == "IsNoneOf" || method == "Lacks") && expression.Arguments.Count >= 1)
            {
                var memberArg = expression.Arguments.Select(a => a as MemberExpression ?? (a as UnaryExpression)?.Operand as MemberExpression).FirstOrDefault(m => m != null && IsEntityMember(m));
                var listArg = expression.Object ?? expression.Arguments.FirstOrDefault(a => !(a is MemberExpression m && IsEntityMember(m)) && !((a as UnaryExpression)?.Operand is MemberExpression u && IsEntityMember(u)));
                if (memberArg != null && listArg != null && listArg.GetValue() is IEnumerable list && !(list is string))
                {
                    var values = list.Cast<object>().Select(v => v is IEntity e ? e.GetId() : v).ToArray();
                    return new Criterion(memberArg.GetDatabaseColumnPath(), method == "IsNoneOf" || method == "Lacks" ? FilterFunction.NotIn : FilterFunction.In, values);
                }
            }

            // x.Name.HasValue() / x.Name.IsEmpty()
            if (expression.Arguments.Count == 1 && expression.Arguments[0] is MemberExpression single && IsEntityMember(single))
            {
                var path = single.GetDatabaseColumnPath();
                if (method == "HasValue")
                    return new BinaryCriterion(new Criterion(path, FilterFunction.NotNull, (object)null), BinaryOperator.AND, new Criterion(path, FilterFunction.IsNot, ""));
                if (method == "IsEmpty")
                    return new BinaryCriterion(new Criterion(path, FilterFunction.Null, (object)null), BinaryOperator.OR, new Criterion(path, FilterFunction.Is, ""));
            }

            if (throwOnError) throw new NotSupportedException("Can't convert this to a database criterion: " + expression);
            return null;
        }

        public static Criterion From(MemberExpression expression)
        {
            // x.IsActive
            if (expression.Type == typeof(bool) && IsEntityMember(expression)) return new Criterion(expression.GetDatabaseColumnPath(), FilterFunction.Is, true);
            // x.Date.HasValue
            if (expression.Member.Name == "HasValue" && expression.Expression is MemberExpression nullable && IsEntityMember(nullable))
                return new Criterion(nullable.GetDatabaseColumnPath(), FilterFunction.NotNull, (object)null);
            return null;
        }

        public static Criterion From(UnaryExpression expression)
        {
            if (expression.NodeType != ExpressionType.Not) return From(expression.Operand);
            var inner = From(expression.Operand);
            if (inner == null) return null;
            switch (inner.FilterFunction)
            {
                case FilterFunction.Is: inner.FilterFunction = FilterFunction.IsNot; return inner;
                case FilterFunction.Null: inner.FilterFunction = FilterFunction.NotNull; return inner;
                case FilterFunction.NotNull: inner.FilterFunction = FilterFunction.Null; return inner;
                case FilterFunction.Contains: inner.FilterFunction = FilterFunction.NotContains; return inner;
                case FilterFunction.In: inner.FilterFunction = FilterFunction.NotIn; return inner;
                case FilterFunction.NotIn: inner.FilterFunction = FilterFunction.In; return inner;
                default: return null;
            }
        }

        public static Criterion From(BinaryExpression expression)
        {
            if (expression.NodeType == ExpressionType.AndAlso || expression.NodeType == ExpressionType.OrElse)
            {
                var left = From(expression.Left);
                var right = From(expression.Right);
                if (left == null || right == null) return null;
                return new BinaryCriterion(left, expression.NodeType == ExpressionType.AndAlso ? BinaryOperator.AND : BinaryOperator.OR, right);
            }

            var flipped = !(Strip(expression.Left) is MemberExpression l && IsEntityMember(l));
            var member = Strip(flipped ? expression.Right : expression.Left) as MemberExpression;
            if (member == null || !IsEntityMember(member)) return null;
            var value = (flipped ? expression.Left : expression.Right).GetValue();
            if (value is IEntity entity) value = entity.GetId();

            var function = expression.NodeType.ToFilterFunction(flipped);
            if (value == null && function == FilterFunction.Is) function = FilterFunction.Null;
            if (value == null && function == FilterFunction.IsNot) function = FilterFunction.NotNull;

            // As in Olive: x.OwnerId == id filters on the Owner association when the entity has one.
            var path = member.GetDatabaseColumnPath();
            if (path.EndsWith("Id") && member.Member.DeclaringType?.GetProperty(path.Substring(0, path.Length - 2)) is { } association
                && typeof(IEntity).IsAssignableFrom(association.PropertyType))
                path = association.Name;
            return new Criterion(path, function, value);
        }

        static Expression Strip(Expression e) => e is UnaryExpression u && u.NodeType == ExpressionType.Convert ? u.Operand : e;

        static bool IsEntityMember(MemberExpression member)
        {
            var root = member.Expression;
            while (root is MemberExpression parent) root = parent.Expression;
            while (root is UnaryExpression convert) root = convert.Operand;
            return root is ParameterExpression;
        }

        protected virtual bool NeedsParameter(SqlConversionContext context) => FilterFunction != FilterFunction.Null && FilterFunction != FilterFunction.NotNull;

        internal object ValueFor(SqlConversionContext context) => GetValue(context);

        protected virtual object GetValue(SqlConversionContext context)
        {
            switch (FilterFunction)
            {
                case FilterFunction.Contains:
                case FilterFunction.NotContains: return "%" + Value + "%";
                case FilterFunction.BeginsWith: return Value + "%";
                case FilterFunction.EndsWith: return "%" + Value;
                default: return Value is Enum ? Convert.ToInt32(Value) : Value;
            }
        }

        protected virtual string GetColumnName(SqlConversionContext context, string propertyName)
        {
            var column = context.Query?.MapColumn(propertyName) ?? context.ToSafeId(propertyName);
            return column;
        }

        public virtual string ToSql(SqlConversionContext context)
        {
            if (SqlCondition != null) return SqlCondition;
            var column = GetColumnName(context, PropertyName);
            if (!NeedsParameter(context)) return $"{column} {FilterFunction.GetDatabaseOperator()}";

            if (FilterFunction == FilterFunction.In || FilterFunction == FilterFunction.NotIn)
            {
                var items = (Value as IEnumerable)?.Cast<object>().ToArray() ?? new object[0];
                if (items.Length == 0) return FilterFunction == FilterFunction.In ? "1 = 0" : "1 = 1";
                var names = items.Select((item, i) => AddParameter(context, item, i)).ToArray();
                return $"{column} {FilterFunction.GetDatabaseOperator()} ({string.Join(", ", names)})";
            }

            return $"{column} {FilterFunction.GetDatabaseOperator()} {AddParameter(context, GetValue(context), null)}";
        }

        string AddParameter(SqlConversionContext context, object value, int? index)
        {
            var parameters = context.Query.Parameters;
            var name = "@" + Regex.Replace(PropertyName ?? "p", @"\W", "_") + (index?.ToString() ?? "");
            var unique = name;
            for (var i = 2; parameters.ContainsKey(unique.TrimStart('@')) && !Equals(parameters[unique.TrimStart('@')], value); i++) unique = name + "_" + i;
            parameters[unique.TrimStart('@')] = value is Enum ? Convert.ToInt32(value) : value;
            return unique;
        }
    }

    public class BinaryCriterion : Criterion
    {
        public BinaryCriterion(Criterion left, BinaryOperator opt, Criterion right)
        {
            Left = left;
            Operator = opt;
            Right = right;
        }

        public Criterion Left { get; set; }

        public Criterion Right { get; set; }

        public BinaryOperator Operator { get; set; }

        public override string ToSql(SqlConversionContext context) => $"({Left.ToSql(context)} {Operator} {Right.ToSql(context)})";

        public override string ToString() => $"({Left} {Operator} {Right})";

        public BinaryCriterion Or(BinaryCriterion left, BinaryCriterion right) => new BinaryCriterion(left, BinaryOperator.OR, right);

        public BinaryCriterion And(BinaryCriterion left, BinaryCriterion right) => new BinaryCriterion(left, BinaryOperator.AND, right);
    }

    public class DirectDatabaseCriterion : Criterion
    {
        public Dictionary<string, object> Parameters = new Dictionary<string, object>();

        public DirectDatabaseCriterion(string sqlCriteria) => SqlCriteria = sqlCriteria;

        public DirectDatabaseCriterion(string sqlCriteria, params Tuple<string, object>[] parameters) : this(sqlCriteria)
        {
            foreach (var p in parameters) Parameters[p.Item1] = p.Item2;
        }

        public DirectDatabaseCriterion(string sqlCriteria, Dictionary<string, object> parameters) : this(sqlCriteria) => Parameters = parameters ?? new Dictionary<string, object>();

        public DirectDatabaseCriterion(string sqlCriteria, object parameters) : this(sqlCriteria)
        {
            if (parameters != null)
                foreach (var p in parameters.GetType().GetProperties()) Parameters[p.Name] = p.GetValue(parameters);
        }

        public string SqlCriteria { get; set; }

        public new object Value { get; set; }

        public bool IsCacheSafe { get; set; }

        public new FilterFunction FilterFunction { get; set; }

        public override string ToString() => SqlCriteria;

        public override string ToSql(SqlConversionContext context)
        {
            foreach (var p in Parameters) context.Query.Parameters[p.Key.TrimStart('@')] = p.Value;
            return SqlCriteria;
        }

        public string MapSqlCriteria(Dictionary<string, string> propertyMappings)
        {
            var result = SqlCriteria;
            foreach (var mapping in propertyMappings) result = result.Replace($"{{{{{mapping.Key}}}}}", mapping.Value);
            return result;
        }
    }

    public class DynamicValueCriterion : Criterion
    {
        readonly string rightPropertyName;

        public DynamicValueCriterion(string leftPropertyName, FilterFunction function, string rightPropertyName) : base(leftPropertyName, function, (object)null) =>
            this.rightPropertyName = rightPropertyName;

        protected override object GetValue(SqlConversionContext context) => GetColumnName(context, rightPropertyName);

        protected override bool NeedsParameter(SqlConversionContext context) => false;

        public override string ToSql(SqlConversionContext context) =>
            $"{GetColumnName(context, PropertyName)} {FilterFunction.GetDatabaseOperator()} {GetColumnName(context, rightPropertyName)}";
    }

    public class CriteriaExtractor<T> where T : IEntity
    {
        readonly Expression<Func<T, bool>> criteria;
        readonly bool throwOnNonConversion;

        public List<Expression> NotConverted = new List<Expression>();

        public CriteriaExtractor(Expression<Func<T, bool>> criteria, bool throwOnNonConversion)
        {
            this.criteria = criteria;
            this.throwOnNonConversion = throwOnNonConversion;
        }

        public List<Criterion> Extract()
        {
            var result = new List<Criterion>();
            foreach (var part in SplitAnd(criteria.Body))
            {
                var converted = Criterion.From(part);
                if (converted != null) result.Add(converted);
                else if (throwOnNonConversion) throw new NotSupportedException("Can't convert this to a database criterion: " + part);
                else NotConverted.Add(part);
            }
            return result;
        }

        static IEnumerable<Expression> SplitAnd(Expression e) =>
            e is BinaryExpression b && b.NodeType == ExpressionType.AndAlso ? SplitAnd(b.Left).Concat(SplitAnd(b.Right)) : new[] { e };
    }

    public class AssociationSubQuery
    {
        public AssociationSubQuery(PropertyInfo property, string mappedSubquery)
        {
            Property = property;
            MappedSubquery = mappedSubquery;
        }

        public PropertyInfo Property { get; set; }
        public string ColumnName { get; set; }
        public string MappedSubquery { get; set; }
        public string TableAlias { get; set; }
        public string RecordIdExpression { get; set; }
        public string SelectClause { get; set; }

        public override string ToString() => MappedSubquery;
    }

    public class NestedCriteriaProcessor
    {
        public List<AssociationSubQuery> Queries = new List<AssociationSubQuery>();
        public PropertyInfo Property;
        public string TableAlias;

        public NestedCriteriaProcessor(Type declaringType, string[] parts)
        {
            var type = declaringType;
            foreach (var part in parts.Take(parts.Length - 1))
            {
                var association = type.GetProperty(part) ?? throw new ArgumentException($"{type.Name} has no property {part}.");
                Queries.Add(new AssociationSubQuery(association, null));
                type = association.PropertyType;
            }
            Property = type.GetProperty(parts.Last());
            TableAlias = string.Join("_", parts.Take(parts.Length - 1));
        }
    }

    public class QueryOption
    {
        public static FullTextSearchQueryOption FullTextSearch(string keyword, params string[] properties) =>
            new FullTextSearchQueryOption { Keyword = keyword, Properties = properties };

        public static FullTextSearchQueryOption FullTextSearch<T>(string keyword, params Expression<Func<T>>[] properties) =>
            new FullTextSearchQueryOption { Keyword = keyword, Properties = properties.Select(p => ((MemberExpression)p.Body).Member.Name).ToArray() };

        public static FullTextSearchQueryOption FullTextSearch<T>(string keyword, params Expression<Func<T, object>>[] properties) =>
            new FullTextSearchQueryOption { Keyword = keyword, Properties = properties.Select(p => ((p.Body as UnaryExpression)?.Operand as MemberExpression ?? (MemberExpression)p.Body).Member.Name).ToArray() };
    }

    public class FullTextSearchQueryOption : QueryOption
    {
        public string Keyword { get; set; }

        public IEnumerable<string> Properties { get; set; }
    }

    public class PagingQueryOption : QueryOption
    {
        public PagingQueryOption(string orderBy, int startIndex, int pageSize)
        {
            OrderBy = orderBy;
            PageStartIndex = startIndex;
            PageSize = pageSize;
        }

        public int PageStartIndex { get; }
        public int PageSize { get; }
        public string OrderBy { get; set; }
    }

    public class RangeQueryOption : QueryOption
    {
        internal RangeQueryOption(int from, int number)
        {
            From = from;
            Number = number;
        }

        public int From { get; }
        public int Number { get; }
    }

    public class SortQueryOption : QueryOption
    {
        public string Property { get; set; }
        public bool Descending { get; set; }
    }

    public class TakeTopQueryOption : QueryOption
    {
        public TakeTopQueryOption(int number) => Number = number;

        public int Number { get; }
    }

    public class WhereQueryOption : QueryOption
    {
        public WhereQueryOption() { }

        public WhereQueryOption(string sqlCriteria) => SqlCriteria = sqlCriteria;

        public string SqlCriteria { get; set; }
    }
}
