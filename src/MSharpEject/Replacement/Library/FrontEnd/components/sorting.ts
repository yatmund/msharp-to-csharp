import Url from "./url";
import ServerInvoker from "../mvc/serverInvoker";

export default class Sorting implements IService {
    constructor(private url: Url, private serverInvoker: ServerInvoker) { }

    enableDragSort(selector: JQuery) {
        selector.each((_: number, e: any) => this.DragSort($(e)));
    }

    enablesetSortHeaderClass(selector: JQuery) {
        selector.each((_: number, e: any) => this.setSortHeaderClass($(e)));
    }

    enableAjaxSorting(selector: JQuery) {
        selector.off("click.ajax-sorting").on("click.ajax-sorting", (e: JQueryEventObject) => this.AjaxSorting(e));
    }

    private AjaxSorting(event: JQueryEventObject) {
        event.stopImmediatePropagation();
        const button = $(event.currentTarget);
        const module = button.closest("[data-module]");
        module.find("[name='s']").val(button.attr("data-sort"));
        module.find("input[name='p']").val("1");
        this.serverInvoker.invokeWithAjax(event, button.attr("formaction") || this.url.current());
        return false;
    }

    setSortHeaderClass(thead: JQuery) {
        const current = $("#Current-Sort").val();
        if (!current) return;
        const descending = current.endsWith(".DESC");
        const name = current.replace(".DESC", "");
        const header = thead.filter(`[data-sort='${name}']`);
        if (!header.length) return;
        header.addClass(descending ? "sort-descending" : "sort-ascending");
        header.append("<i></i>");
    }

    private DragSort(container: JQuery) {
        const isTable = container.is("tbody");
        container.sortable({
            handle: "[data-sort-item]",
            containment: "parent",
            axis: "y",
            tolerance: "pointer",
            scroll: true,
            items: isTable ? "> tr" : "> .r-grid-row",
            helper: (e: any, ui: any) => { ui.children().each((_: number, c: any) => $(c).width($(c).width())); return ui; },
            start: () => { },
            stop: (e: any, ui: any) => {
                const item = ui.item.find("[data-sort-item]").addBack("[data-sort-item]").first();
                const next = ui.item.next().find("[data-sort-item]").addBack("[data-sort-item]").first().attr("data-sort-item");
                const url = this.url.addQuery(item.attr("data-sort-action"), "drop-before", next || "");
                this.serverInvoker.invokeWithAjax({ currentTarget: item, preventDefault() { } }, url);
            },
        });
    }
}
