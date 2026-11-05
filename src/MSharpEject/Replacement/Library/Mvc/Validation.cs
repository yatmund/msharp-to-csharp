using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Localization;

namespace Platform.Mvc
{
    static class Condition
    {
        // The rule applies when the named boolean property on the same object is true (When) or false (Unless).
        internal static bool Applies(ValidationContext context, string propertyName, bool when)
        {
            var value = context.ObjectType.GetProperty(propertyName)?.GetValue(context.ObjectInstance);
            var flag = value is bool b ? b : value != null;
            return when ? flag : !flag;
        }
    }

    public class RequiredWhenAttribute : RequiredAttribute
    {
        readonly string propertyName;

        public RequiredWhenAttribute(string propertyName) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, true) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class RequiredUnlessAttribute : RequiredAttribute
    {
        readonly string propertyName;

        public RequiredUnlessAttribute(string propertyName) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, false) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class StringLengthWhenAttribute : StringLengthAttribute
    {
        readonly string propertyName;

        public StringLengthWhenAttribute(string propertyName, int maximumLength) : base(maximumLength) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, true) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class StringLengthUnlessAttribute : StringLengthAttribute
    {
        readonly string propertyName;

        public StringLengthUnlessAttribute(string propertyName, int maximumLength) : base(maximumLength) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, false) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class RangeWhenAttribute : RangeAttribute
    {
        readonly string propertyName;

        public RangeWhenAttribute(string propertyName, double minimum, double maximum) : base(minimum, maximum) => this.propertyName = propertyName;

        public RangeWhenAttribute(string propertyName, int minimum, int maximum) : base(minimum, maximum) => this.propertyName = propertyName;

        public RangeWhenAttribute(string propertyName, Type type, string minimum, string maximum) : base(type, minimum, maximum) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, true) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class RangeUnlessAttribute : RangeAttribute
    {
        readonly string propertyName;

        public RangeUnlessAttribute(string propertyName, double minimum, double maximum) : base(minimum, maximum) => this.propertyName = propertyName;

        public RangeUnlessAttribute(string propertyName, int minimum, int maximum) : base(minimum, maximum) => this.propertyName = propertyName;

        public RangeUnlessAttribute(string propertyName, Type type, string minimum, string maximum) : base(type, minimum, maximum) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, false) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class DataTypeWhenAttribute : DataTypeAttribute
    {
        readonly string propertyName;

        public DataTypeWhenAttribute(string propertyName, DataType dataType) : base(dataType) => this.propertyName = propertyName;

        public DataTypeWhenAttribute(string propertyName, string customDataType) : base(customDataType) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, true) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class DataTypeUnlessAttribute : DataTypeAttribute
    {
        readonly string propertyName;

        public DataTypeUnlessAttribute(string propertyName, DataType dataType) : base(dataType) => this.propertyName = propertyName;

        public DataTypeUnlessAttribute(string propertyName, string customDataType) : base(customDataType) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, false) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class TimeAttribute : ValidationAttribute, IClientModelValidator
    {
        public override bool IsValid(object value) =>
            value == null || value is TimeSpan || (value is string s && (s.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(s, @"^([01]?\d|2[0-3]):[0-5]\d(:[0-5]\d)?$")));

        public void AddValidation(ClientModelValidationContext context)
        {
            context.Attributes["data-val"] = "true";
            context.Attributes["data-val-time"] = FormatErrorMessage(context.ModelMetadata.GetDisplayName());
        }
    }

    public class TimeWhenAttribute : TimeAttribute
    {
        readonly string propertyName;

        public TimeWhenAttribute(string propertyName) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, true) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class TimeUnlessAttribute : TimeAttribute
    {
        readonly string propertyName;

        public TimeUnlessAttribute(string propertyName) => this.propertyName = propertyName;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext) =>
            Condition.Applies(validationContext, propertyName, false) ? base.IsValid(value, validationContext) : ValidationResult.Success;
    }

    public class OliveValidationAttributeAdapterProvider : IValidationAttributeAdapterProvider
    {
        readonly IValidationAttributeAdapterProvider fallback = new ValidationAttributeAdapterProvider();

        public IAttributeAdapter GetAttributeAdapter(ValidationAttribute attribute, IStringLocalizer stringLocalizer) =>
            fallback.GetAttributeAdapter(attribute, stringLocalizer);
    }
}
