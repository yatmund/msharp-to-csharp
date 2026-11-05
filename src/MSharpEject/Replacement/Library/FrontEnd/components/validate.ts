import "jquery-validate-unobtrusive";
import Alert from "./alert";
import ResponseProcessor from "../mvc/responseProcessor";

export default class Validate implements IService {
    private tooltipOptions: TooltipOption = {};
    private static cssInjected = false;

    constructor(private alert: Alert, private responseProcessor: ResponseProcessor) { }

    private injectCss() {
        if (Validate.cssInjected) return;
        Validate.cssInjected = true;
        $("<style>.validation-icon-wrapper{position:relative;display:block}.validation-error-icon{position:absolute;right:8px;top:50%;transform:translateY(-50%);color:#dc3545}"
            + ".validation-error-bubble{display:none;position:absolute;z-index:10;left:0;bottom:100%;background:#dc3545;color:#fff;padding:2px 6px;border-radius:3px;font-size:85%}"
            + ".validation-error-bubble.visible{display:block}</style>").appendTo("head");
    }

    configure() {
        this.injectCss();
        const methods = $.validator.methods;
        const format = "DD/MM/YYYY";
        methods.date = function (value: string, element: any) {
            return this.optional(element) || !window.moment || moment(value, format).isValid();
        };
        $.validator.setDefaults({ ignore: ":hidden:not([data-validate-hidden])" });
    }

    initialize() {
        this.responseProcessor.subformChanged.handle(data => this.reloadRules(data.trigger.parents("form")));
    }

    setTooltipOptions(options: TooltipOption) { this.tooltipOptions = options; }

    validateForm(trigger: JQuery): boolean {
        if (!this.needsValidation(trigger)) return true;
        const form = this.getForm(trigger);
        const validator = this.getValidator(trigger, form);
        this.extendValidatorSettings(validator, trigger);
        if (validator.form()) return true;
        this.handleMessageBoxStyle(validator, form, trigger);
        this.focusOnInvalid(validator, form, trigger);
        this.showAdditionalErrors(validator);
        this.handleInvalidForm(validator, form, trigger);
        return false;
    }

    reloadRules(form: JQuery) {
        form.removeData("validator").removeData("unobtrusiveValidation");
        $.validator.unobtrusive.parse(form);
        form.validate();
    }

    removeTooltipsRelatedTo(parent: JQuery) {
        parent.find(".validation-error-bubble").removeClass("visible");
        parent.find(".validation-error-icon").remove();
    }

    protected needsValidation(trigger: JQuery): boolean {
        return !trigger.is("[formnovalidate]") && trigger.closest("form").length > 0;
    }

    protected getForm(trigger: JQuery): JQuery { return trigger.closest("form"); }

    protected getValidator(trigger: JQuery, form: JQuery): Validator {
        $.validator.unobtrusive.parse(form);
        return form.validate();
    }

    private ensureWrapper(element: JQuery): JQuery {
        const existing = element.parent(".validation-icon-wrapper");
        if (existing.length) return existing;
        element.wrap('<div class="validation-icon-wrapper"></div>');
        return element.parent();
    }

    private showBubble(error: JQuery, element: JQuery) {
        const wrapper = this.ensureWrapper(element);
        if (!error.text()) return;
        const id = (element.attr("id") || element.attr("name")) + "-error";
        wrapper.find(".validation-error-bubble").remove();
        if (!wrapper.find(".validation-error-icon").length) wrapper.append('<i class="validation-error-icon fa fa-exclamation-circle"></i>');
        wrapper.append($("<div></div>").attr("id", id).addClass("validation-error-bubble").text(error.text()));
        element.off("focus.bubble blur.bubble")
            .on("focus.bubble", () => wrapper.find(".validation-error-bubble").addClass("visible"))
            .on("blur.bubble", () => wrapper.find(".validation-error-bubble").removeClass("visible"));
        element.attr("aria-describedby", id);
    }

    private hideBubble(element: JQuery) {
        const wrapper = this.ensureWrapper(element);
        wrapper.find(".validation-error-icon, .validation-error-bubble").remove();
    }

    protected extendValidatorSettings(validator: Validator, trigger: JQuery) {
        validator.settings.errorClass = "error";
        validator.settings.errorPlacement = (error: JQuery, element: JQuery) => this.showBubble(error, element);
        validator.settings.success = (label: JQuery, element: any) => this.hideBubble($(element));
    }

    protected focusOnInvalid(validator: Validator, form: JQuery, trigger: JQuery) {
        if (!validator.errorList.length) return;
        const first = $(validator.errorList[0].element);
        first.focus();
        first.parent().find(".validation-error-bubble").addClass("visible");
    }

    protected showAdditionalErrors(_validator: Validator) { }

    protected handleMessageBoxStyle(validator: Validator, form: JQuery, trigger: JQuery) {
        if (!form.is("[data-validation-style*=message-box]")) return;
        this.alert.alert(validator.errorList.map((e: any) => e.message).join("<br />\n"));
    }

    protected handleInvalidForm(validator: Validator, form: JQuery, trigger: JQuery) { }
}
