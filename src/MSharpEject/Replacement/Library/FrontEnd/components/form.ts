import Url from "./url";
import Validate from "./validate";
import Waiting from "./waiting";
import AjaxRedirect from "../mvc/ajaxRedirect";

export default class Form implements IService {
    constructor(private url: Url, private validate: Validate, private waiting: Waiting, private ajaxRedirect: AjaxRedirect) { }

    protected currentRequestUrlProvider: (() => string) = () => window.location.pathname + window.location.search;

    enableDefaultButtonKeyPress(selector: JQuery) {
        selector.off("keypress.default-button").on("keypress.default-button", (e: JQueryEventObject) => this.DefaultButtonKeyPress(e));
    }

    enablecleanUpNumberField(selector: JQuery) {
        selector.off("blur.cleanup-number").on("blur.cleanup-number", (e: JQueryEventObject) => this.cleanUpNumberField($(e.currentTarget)));
    }

    enablesubmitCleanGet(selector: JQuery) {
        selector.off("submit.clean-up").on("submit.clean-up", (e: JQueryEventObject) => this.submitCleanGet(e));
    }

    private getCleanFormData(form: JQuery): JQuerySerializeArrayElement[] {
        const result: JQuerySerializeArrayElement[] = [];
        const groups: { [name: string]: string[] } = {};
        form.serializeArray().forEach((i: JQuerySerializeArrayElement) => (groups[i.name] = groups[i.name] || []).push(i.value));
        for (const name of Object.keys(groups)) {
            const values = groups[name].filter(v => v !== "");
            if (this.ignoreFormDataInput(name, values)) continue;
            result.push({ name, value: values.join("|") });
        }
        return result;
    }

    protected ignoreFormDataInput(inputName: string, values: string[]): boolean {
        return values.length === 0 || inputName === "__RequestVerificationToken";
    }

    cleanJson(str: any): string {
        return str.replace(/(\s*?{\s*?|\s*?,\s*?)(['"])?([a-zA-Z0-9]+)(['"])?:/g, '$1"$3":');
    }

    getPostData(trigger: JQuery): JQuerySerializeArrayElement[] {
        const form = trigger.closest("[data-module]");
        const data: JQuerySerializeArrayElement[] = form.find(":input").serializeArray();
        form.find("select[multiple]").filter((_: number, s: any) => !$(s).val() || !$(s).val().length).each((_: number, s: any) => data.push({ name: $(s).attr("name"), value: "" }));
        data.push({ name: "subFormIndex", value: "-1" });
        data.push({ name: "current.request.url", value: this.currentRequestUrlProvider() });
        return data;
    }

    private DefaultButtonKeyPress(event: JQueryEventObject) {
        if (event.which !== 13) return true;
        const button = $(event.currentTarget).closest("[data-module]").find("[default-button]:first");
        if (!button.length) return true;
        event.preventDefault();
        button.focus().click();
        return false;
    }

    private cleanUpNumberField(input: JQuery) {
        const value = input.val();
        if (value) input.val(value.replace(/[^\d.\-]/g, ""));
    }

    private submitCleanGet(event: JQueryEventObject) {
        const form = $(event.currentTarget);
        if (form.is("[data-redirect=ajax]") === false && form.attr("target")) return true;
        event.preventDefault();
        const query = this.getCleanFormData(form).map(i => i.name + "=" + encodeURIComponent(i.value)).join("&");
        const action = form.attr("action") || window.location.pathname;
        const url = query ? action + (action.indexOf("?") >= 0 ? "&" : "?") + query : action;
        if (form.is("[data-redirect=ajax]")) this.ajaxRedirect.go(url, form, false, false, true);
        else window.location.href = url;
        return false;
    }
}
