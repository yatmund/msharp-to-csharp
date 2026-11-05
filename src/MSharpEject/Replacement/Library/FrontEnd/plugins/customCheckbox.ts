export default class CustomCheckbox {
    private static handledClassName = "handled";

    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new CustomCheckbox($(e)));
    }

    constructor(private input: JQuery) { this.enable(); }

    private enable() {
        if (this.input.is("." + CustomCheckbox.handledClassName) || this.input.closest(".as-buttons-input").length) return;
        this.input.addClass(CustomCheckbox.handledClassName);
        const helper = $('<div class="checkbox-helper"></div>');
        this.input.after(helper);
        helper.on("click", () => { this.input.click(); this.input.focus(); });
    }
}
