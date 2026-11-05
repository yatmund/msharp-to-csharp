export default class SubMenu {
    private submenuOptions = { showTimeout: 0, hideTimeout: 0 };

    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new SubMenu($(e)));
    }

    static createAccordion(selector: JQuery) { }

    constructor(private menu: any) {
        menu.addClass("sm");
        menu.smartmenus(this.submenuOptions);
    }
}
