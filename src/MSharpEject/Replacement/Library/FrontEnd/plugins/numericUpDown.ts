export default class NumbericUpDown {
    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new NumbericUpDown($(e)));
    }

    constructor(private input: JQuery) { this.enable(); }

    private enable() {
        const number = (name: string) => { const v = parseFloat(this.input.attr(name)); return isNaN(v) ? null : v; };
        this.input.spinedit({ minimum: number("data-val-range-min"), maximum: number("data-val-range-max"), step: 1 });
    }
}
