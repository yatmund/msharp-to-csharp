import Url from "./url";
import ServerInvoker from "../mvc/serverInvoker";

export default class Paging implements IService {
    constructor(private url: Url, private serverInvoker: ServerInvoker) { }

    enableOnSizeChanged(selector: JQuery) {
        selector.off("change.pagination-size").on("change.pagination-size", (e: JQueryEventObject) => this.onSizeChanged(e));
    }

    enableWithAjax(selector: JQuery) {
        selector.off("click.ajax-paging").on("click.ajax-paging", (e: JQueryEventObject) => this.withAjax(e));
    }

    private onSizeChanged(event: JQueryEventObject) {
        this.serverInvoker.invokeWithAjax(event, $(event.currentTarget).closest("form").attr("action"));
    }

    private withAjax(event: JQueryEventObject) {
        event.stopImmediatePropagation();
        const button = $(event.currentTarget);
        const page = button.attr("data-pagination");
        button.closest("[data-module]").find("[name='p']").val(page.split("=").pop());
        this.serverInvoker.invokeWithAjax(event, button.attr("formaction") || this.url.current());
        return false;
    }
}
