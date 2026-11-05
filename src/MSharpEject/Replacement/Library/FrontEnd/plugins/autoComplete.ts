import Url from "../components/url";
import Form from "../components/form";
import ServerInvoker from "../mvc/serverInvoker";
import Config from "../config";

export class AutoCompleteFactory implements IService {
    constructor(private url: Url, private form: Form, private serverInvoker: ServerInvoker) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new AutoComplete($(e), this.url, this.form, this.serverInvoker).enable());
    }
}

export default class AutoComplete {
    private static customOptions: any;
    protected valueField: JQuery;
    private selectedItemOnEnter: any;

    static setOptions(options: any) { AutoComplete.customOptions = options; }

    constructor(public input: JQuery, private url: Url, private form: Form, private serverInvoker: ServerInvoker) { }

    enable() {
        if (this.input.is("[data-typeahead-enabled=true]")) return;
        this.input.attr("data-typeahead-enabled", true);
        this.valueField = $(`[name='${this.input.attr("name").replace(/\.Text$/, "")}']`);
        this.input.wrap('<span class="typehead-chevron-down"></span>').before('<i class="fas fa-chevron-down"></i>');
        this.input.parent().wrap('<div class="typeahead__container"></div>');
        this.input.on("change.autocomplete blur.autocomplete", () => this.clearValue());
        this.input.typeahead($.extend(true, this.getDefaultOptions(), AutoComplete.customOptions, this.getMandatoryOptions()));
    }

    private getMandatoryOptions() {
        const url = this.url.effectiveUrlProvider(this.input.attr("autocomplete-source"), this.input);
        return {
            source: {
                values: {
                    display: "Display", data: [{ Display: "", Text: "", Value: "" }],
                    ajax: (query: string) => ({ type: "POST", url, data: this.getPostData(), xhrFields: { withCredentials: true } }),
                },
            },
            callback: this.getMandatoryCallbacks(),
        };
    }

    private getMandatoryCallbacks() {
        return {
            onClickAfter: (node: JQuery, a: JQuery, item: any) => { this.itemSelected(item); },
            onPopulateSource: (node: JQuery, data: any[]) => { this.selectedItemOnEnter = data.length === 1 ? data[0] : null; return data; },
        };
    }

    protected getDefaultOptions(): any {
        return {
            maxItem: 0, minLength: 0, dynamic: true, searchOnFocus: true, debug: false, delay: Config.AUTOCOMPLETE_INPUT_DELAY, backdrop: false,
            correlativeTemplate: true, templateValue: "{{Text}}", emptyTemplate: "<div class='tt-suggestion'>Not found</div>",
        };
    }

    protected getPostData(): any {
        const data = this.toObject(this.form.getPostData(this.input));
        data[this.input.attr("name")] = "{{query}}";
        return data;
    }

    protected clearValue() {
        const text = this.input.val();
        if (!text || text !== this.input.data("selected-text")) this.valueField.val("");
    }

    protected itemSelected(item: any) {
        this.valueField.val(item ? item.Value : "");
        this.input.val(item ? item.Text : "").data("selected-text", item ? item.Text : "");
        if (this.input.attr("data-change-action")) this.serverInvoker.invokeWithAjax({ currentTarget: this.input, preventDefault() { } }, this.input.attr("data-change-action"));
    }

    protected toObject(arr: JQuerySerializeArrayElement[]): any {
        const result: any = {};
        arr.forEach(i => result[i.name] = i.value);
        return result;
    }
}
