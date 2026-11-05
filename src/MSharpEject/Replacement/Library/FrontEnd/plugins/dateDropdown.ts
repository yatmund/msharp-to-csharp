import Config from "../config";

export default class DateDropdown {
    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new DateDropdown($(e)));
    }

    constructor(private input: JQuery) { this.enable(); }

    private enable() {
        this.input.combodate({ format: Config.DATE_FORMAT, template: "DD / MMM / YYYY", minYear: 1985, maxYear: new Date().getFullYear(), smartDays: true, firstItem: "name" });
    }
}
