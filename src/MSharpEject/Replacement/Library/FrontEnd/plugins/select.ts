export default class Select implements IService {
    enableEnhance(selector: JQuery) {
        selector.each((_: number, e: any) => this.enhance($(e)));
    }

    private enhance(selectControl: JQuery) {
        selectControl.selectpicker();
    }

    replaceSource(controlId: string, items: any) {
        const control = $("#" + controlId);
        control.empty();
        (items || []).forEach((i: any) => control.append($("<option></option>").attr("value", i.Value).text(i.Text)));
    }
}
