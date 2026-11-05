export default class Grid implements IService {
    enableColumn(element: any) {
        element.off("click.apply-columns").on("click.apply-columns", (e: JQueryEventObject) => this.applyColumns(e));
    }

    enableToggle(element: any) {
        element.off("change.select-all").on("change.select-all", (e: JQueryEventObject) => this.enableSelectAllToggle(e));
    }

    private enableHlightRow(element: any) {
        element.closest("tr").addClass("highlighted");
    }

    enableSelectCol(selector: JQuery) {
        selector.each((_: number, e: any) => this.enableSelectColumns($(e)));
    }

    private applyColumns(event: JQueryEventObject) {
        const button = $(event.currentTarget);
        const container = button.parents(".select-cols");
        const boxes = container.find(":checkbox");
        if (boxes.filter(":checked").length) return;
        $("<input type='checkbox' checked='checked'/>").attr({ name: boxes.first().attr("name"), value: "-" }).hide().appendTo(container);
    }

    private enableSelectColumns(container: JQuery) {
        const toggle = container.find(".select-grid-cols .group-control");
        toggle.off("click.select-cols").on("click.select-cols", () => container.find(".select-cols").toggle());
    }

    private enableSelectAllToggle(event: JQueryEventObject) {
        const trigger = $(event.currentTarget);
        const checked = trigger.prop("checked");
        trigger.closest("table").find("td.select-row > input:checkbox").prop("checked", checked);
    }

    private highlightRow(element: any) { this.enableHlightRow($(element)); }

    mergeActionButtons() {
        $("table tr > .actions-merge, .r-grid .r-grid-row > .actions-merge").parent().each((_: number, row: any) => {
            const cells = $(row).children(".actions-merge");
            if (cells.length < 2) return;
            const first = cells.first();
            cells.not(first).each((__: number, c: any) => { first.append(" ").append($(c).contents()); $(c).remove(); });
        });
    }
}
