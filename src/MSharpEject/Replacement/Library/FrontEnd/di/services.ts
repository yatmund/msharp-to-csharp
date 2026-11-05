const names = ["Alert", "CrossDomainEvent", "Form", "Grid", "Grouping", "GroupingFactory", "LiteEvent", "MasterDetail", "Modal", "ModalHelper", "MainTag", "MainTagHelper",
    "Paging", "Sorting", "Url", "Validate", "Waiting", "AjaxRedirect", "WindowEx", "ResponseProcessor", "ServerInvoker", "StandardAction", "AutoComplete", "AutoCompleteFactory",
    "CkEditorFileManager", "CKEditorFileManagerFactory", "ConfirmBoxFactory", "ConfirmBox", "CustomCheckbox", "CustomRadio", "DateDropdown", "DatePicker", "DatePickerFactory",
    "DateTimePicker", "DateTimePickerFactory", "DateTimePickerBase", "FileUpload", "FileUploadFactory", "GlobalSearch", "HtmlEditor", "HtmlEditorFactory", "InstantSearch",
    "MultiSelect", "NumericUpDown", "PasswordStength", "SanityAdapter", "Select", "Slider", "SliderFactory", "SubMenu", "TimeControl", "TimeControlFactory", "UserHelp",
    "ServiceLocator", "GlobalSearchFactory", "TestingContext", "MultiSelectFactory"];

const Services: { [key: string]: string } = {};
names.forEach(n => Services[n] = n.charAt(0).toLowerCase() + n.slice(1));
export default Services;
