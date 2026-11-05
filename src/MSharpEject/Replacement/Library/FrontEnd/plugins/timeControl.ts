import Config from "../config";
import { ModalHelper } from "../components/modal";

export class TimeControlFactory implements IService {
    constructor(private modalHelper: ModalHelper) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new TimeControl($(e), this.modalHelper));
    }
}

export default class TimeControl {
    constructor(targetInput: any, private modalHelper: ModalHelper) {
        const input = targetInput;
        input.attr("data-autofocus", "disabled");
        input.datetimepicker({
            format: Config.TIME_FORMAT, useCurrent: false,
            stepping: parseInt(input.attr("data-minute-steps") || Config.MINUTE_INTERVALS + ""),
            keepInvalid: true, locale: Config.DATE_LOCALE,
            icons: { up: "fas fa-chevron-up", down: "fas fa-chevron-down" },
        });
        const keys = input.data("DateTimePicker").keyBinds();
        keys.left = keys.right = null;
        input.on("dp.show", () => this.modalHelper.expandToFitPicker(input));
    }
}
