import { ModalHelper } from "../components/modal";
import { MainTagHelper } from "../components/mainTag";
import AjaxRedirect from "./ajaxRedirect";

export default class WindowEx implements IService {
    constructor(private modalHelper: ModalHelper, private mainTagHelper: MainTagHelper, private ajaxRedirect: AjaxRedirect) { }

    enableBack(selector: JQuery) {
        selector.off("popstate.ajax-redirect").on("popstate.ajax-redirect", (e: JQueryEventObject) => this.back(e));
    }

    private back(event: JQueryEventObject) {
        if (this.modalHelper.isOrGoingToBeModal()) { this.modalHelper.close(); return; }
        this.mainTagHelper.resetState();
        if (window.location.href.indexOf("#") >= 0 && !(event.originalEvent && event.originalEvent.state)) return;
        this.ajaxRedirect.go(window.location.pathname + window.location.search, null, true, false, false);
    }
}
