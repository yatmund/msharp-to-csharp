export default class CustomRadio {
    private static handledClassName = "handled";

    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new CustomRadio($(e)));
    }

    constructor(private input: JQuery) { this.enable(); }

    private enable() {
        if (this.input.is("." + CustomRadio.handledClassName) || this.input.closest(".as-buttons-input").length) return;
        this.input.addClass(CustomRadio.handledClassName);
        const helper = $('<div class="radio-helper"></div>');
        this.input.after(helper);
        helper.on("click", () => { this.input.click(); this.input.focus(); });
    }
}
