export default class ConfirmBoxFactory implements IService {
    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new ConfirmBox($(e)).enable());
    }
}

export class ConfirmBox {
    constructor(protected button: JQuery) { }

    enable() {
        this.button.off("click.confirm-question").bindFirst("click.confirm-question", (e: JQueryEventObject) => {
            e.stopImmediatePropagation();
            const text = this.button.attr("data-confirm-question");
            this.setButtonsLabel(this.button.attr("data-confirm-ok") || "OK", this.button.attr("data-confirm-cancel") || "Cancel");
            this.showConfirm(text, () => {
                this.button.off("click.confirm-question");
                this.button.trigger("click");
                this.enable();
            });
            return false;
        });
    }

    setButtonsLabel(ok: string, cancel: string) { alertify.set({ labels: { ok, cancel } }); }

    showConfirm(text: string, yesCallback: () => void) {
        alertify.confirm(text.replace(/\r/g, "<br />"), (ok: boolean) => { if (ok) yesCallback(); });
    }
}
