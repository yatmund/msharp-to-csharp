import Url from "./url";
import AjaxRedirect from "../mvc/ajaxRedirect";

export class GroupingFactory implements IService {
    constructor(private url: Url, private ajaxRedirect: AjaxRedirect) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new Grouping($(e), this.url, this.ajaxRedirect));
    }
}

export default class Grouping {
    constructor(private dropdown: JQuery, private url: Url, private ajaxRedirect: AjaxRedirect) {
        dropdown.on("change", () => {
            const target = this.url.updateQuery(this.url.current(), "GroupBy", dropdown.val());
            this.ajaxRedirect.go(this.url.removeEmptyQueries(target), dropdown, false, false, false);
        });
    }
}
