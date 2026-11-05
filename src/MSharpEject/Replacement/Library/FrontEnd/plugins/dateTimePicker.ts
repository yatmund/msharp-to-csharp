import Config from "../config";
import { ModalHelper } from "../components/modal";
import dateTimePickerBase from "./dateTimePickerBase";

export class DateTimePickerFactory implements IService {
    constructor(private modalHelper: ModalHelper) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new DateTimePicker($(e), this.modalHelper).show());
    }
}

export default class DateTimePicker extends dateTimePickerBase {
    protected controlType = "date-picker|time-picker";
    protected format = Config.DATE_TIME_FORMAT;

    constructor(targetInput: JQuery, modalHelper: ModalHelper) { super(targetInput, modalHelper); }

    protected modifyOptions(options: any) {
        options.stepping = 1;
        options.minDate = "01/01/1900";
        options.maxDate = "01/01/2090";
        options.sideBySide = true;
        options.showClear = true;
    }
}
