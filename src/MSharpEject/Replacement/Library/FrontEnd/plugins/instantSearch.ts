export default class InstantSearch {
    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new InstantSearch($(e)));
    }

    constructor(private input: JQuery) { this.enable(); }

    private enable() {
        this.input.off("keyup.instant-search").on("keyup.instant-search", () => this.onChanged());
    }

    private onChanged() {
        const keyword = (this.input.val() || "").toLowerCase();
        const rows = this.input.closest("[data-module]").find(".grid > tbody > tr, .instant-search-item");
        rows.each((_: number, r: any) => { const row = $(r); row.toggle(row.text().toLowerCase().indexOf(keyword) >= 0); });
    }
}
