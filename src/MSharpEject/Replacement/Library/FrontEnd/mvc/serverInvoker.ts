import Url from "../components/url";
import Waiting from "../components/waiting";
import Form from "../components/form";
import Validate from "../components/validate";
import ResponseProcessor from "./responseProcessor";
import Config from "../config";

export default class ServerInvoker implements IService {
    isAwaitingAjaxResponse = false;

    constructor(private url: Url, private validate: Validate, private waiting: Waiting, private form: Form, private responseProcessor: ResponseProcessor) { }

    enableInvokeWithAjax(selector: JQuery, event: string, attrName: string) {
        const name = event.split(".")[0];
        selector.off(event).on(event, (e: JQueryEventObject) => {
            const trigger = $(e.currentTarget);
            if (name === "click" && trigger.is("[formmethod=post]")) return true;
            return this.invokeWithAjax(e, trigger.attr(attrName));
        });
    }

    enableinvokeWithPost(selector: JQuery) {
        selector.off("click.formaction").on("click.formaction", (e: JQueryEventObject) => this.invokeWithPost(e));
    }

    private invokeWithPost(event: JQueryEventObject) {
        const trigger = $(event.currentTarget);
        if (!this.validate.validateForm(trigger)) return false;
        const form = $("<form method='post' style='display:none'></form>").attr("action", this.processActionUrl(trigger.attr("formaction"), trigger));
        this.form.getPostData(trigger).forEach(i => form.append($("<input type='hidden'>").attr("name", i.name).val(i.value)));
        form.appendTo("body").submit();
        return false;
    }

    private processActionUrl(url: string, trigger: JQuery): string {
        return this.url.effectiveUrlProvider(url, trigger);
    }

    invokeWithAjax(event: JQueryEventObject, actionUrl: string, syncCall: boolean = false): boolean {
        const trigger = $(event.currentTarget);
        const containerModule = trigger.closest("[data-module]");
        if (!this.validate.validateForm(trigger)) return false;
        const data = this.form.getPostData(trigger);
        data.push({ name: ".Requested-With", value: "XMLHttpRequest" });
        const context: IInvocationContext = { url: this.processActionUrl(actionUrl, trigger), trigger, containerModule };
        this.onInvocation(event, context);
        const token = this.waiting.show(trigger.is("[data-wait-screen]"), false);
        this.isAwaitingAjaxResponse = true;
        if (Config.DISABLE_BUTTONS_DURING_AJAX) trigger.attr("disabled", "disabled");
        $.ajax({
            url: context.url, type: trigger.attr("data-ajax-method") || "POST", xhrFields: { withCredentials: true }, async: !syncCall, data,
            success: (result: any) => {
                this.waiting.hide(token);
                this.responseProcessor.processAjaxResponse(result, containerModule, trigger, null);
                this.onInvocationProcessed(event, context);
            },
            error: (jqXHR: JQueryXHR, status: string, error: string) => { this.onAjaxResponseError(jqXHR, status, error); },
            complete: () => {
                this.isAwaitingAjaxResponse = false;
                if (Config.DISABLE_BUTTONS_DURING_AJAX) trigger.removeAttr("disabled");
                this.waiting.hide(token);
                this.onInvocationCompleted(event, context);
            },
        });
        return false;
    }

    protected onInvocation(event: JQueryEventObject, context: IInvocationContext) { }

    protected onInvocationProcessed(event: JQueryEventObject, context: IInvocationContext) { }

    protected onInvocationCompleted(event: JQueryEventObject, context: IInvocationContext) { }

    onAjaxResponseError = (jqXHR: JQueryXHR, status: string, error: string) => {
        this.waiting.hide();
        if (jqXHR.status === 401) { window.location.href = "/login?returnUrl=" + window.location.pathname + window.location.search; return; }
        alertify.alert(jqXHR.responseText || error);
    }

    protected showWaitingBar = () => { this.waiting.show(); }

    protected removeWaitingBar = () => { this.waiting.hide(); }
}
