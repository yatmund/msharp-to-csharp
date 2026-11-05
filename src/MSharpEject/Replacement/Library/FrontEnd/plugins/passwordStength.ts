export default class PasswordStength {
    static enable(selector: JQuery) {
        selector.each((_: number, e: any) => new PasswordStength($(e)));
    }

    constructor(private container: JQuery) { this.enable(); }

    private enable() { }
}
