export default class UserHelp {
    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new UserHelp($(e)));
    }

    constructor(element: JQuery) {
        element.popover({ trigger: "focus", content: element.attr("data-user-help"), html: true });
    }
}
