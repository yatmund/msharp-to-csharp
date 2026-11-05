import Config from "../config";
import { ModalHelper } from "../components/modal";

export default abstract class dateTimePickerBase {
    protected abstract controlType: string;
    protected abstract format: string;

    constructor(protected input: JQuery, private modalHelper: ModalHelper) { }

    protected abstract modifyOptions(options: any): void;

    show() {
        if (window.isMobile || this.input.attr("type") === "date") return;
        this.input.attr("data-autofocus", "disabled");
        if (this.input.attr("data-control") === "calendar") {
            alert("Don't know how to handle date control of calendar");
            return;
        }
        const options: any = {
            format: this.format, useCurrent: false, showTodayButton: true,
            icons: {
                today: "fas fa-calendar-check", clear: "fas fa-eraser", time: "fas fa-clock", date: "fas fa-calendar-alt",
                up: "fas fa-chevron-up", down: "fas fa-chevron-down", next: "fas fa-chevron-right", previous: "fas fa-chevron-left",
            },
            keepInvalid: !this.input.is("[data-change-action]"), locale: Config.DATE_LOCALE,
        };
        this.modifyOptions(options);
        this.input.datetimepicker(options);
        this.input.on("dp.show", () => this.modalHelper.expandToFitPicker(this.input));
    }
}
