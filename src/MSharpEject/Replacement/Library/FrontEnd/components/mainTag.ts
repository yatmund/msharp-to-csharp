import Url from "./url";
import LiteEvent from "./liteEvent";
import AjaxRedirect from "../mvc/ajaxRedirect";
import ResponseProcessor from "../mvc/responseProcessor";

export interface IMainTagUrlChangedEventArgs {
    mainTagName: string;
    url: string;
    encodedUrl: string;
    addedToUrl: boolean;
}

export class MainTagHelper implements IService {
    private state?: { [name: string]: string };
    onUrlChanged = new LiteEvent<IMainTagUrlChangedEventArgs>();

    constructor(private url: Url, private ajaxRedirect: AjaxRedirect, private responseProcessor: ResponseProcessor) { }

    enableLink(selector: JQuery) {
        selector.off("click.open-main-tag").on("click.open-main-tag", (e: JQueryEventObject) => this.render(e));
    }

    initialize() {
        this.responseProcessor.viewChanged.handle(() => this.validateState());
    }

    resetState() { this.state = undefined; }

    tryOpenFromUrl() {
        $("main[name^='$']").each((_: number, e: any) => this.tryOpenFromUrlInternal($(e)));
    }

    private tryOpenFromUrlInternal(element: JQuery) {
        const name = element.attr("name");
        const encoded = this.url.getQuery(this.queryKey(name));
        if (encoded) this.openWithUrl(name, this.url.decodeGzipUrl(encoded));
        else this.tryOpenDefaultUrl(element);
    }

    private tryOpenDefaultUrl(element: JQuery) {
        const url = element.attr("data-default-url");
        if (url) this.openWithUrl(element.attr("name"), url);
    }

    private queryKey(mainTagName: string): string { return mainTagName.replace("$", "_"); }

    removeFromUrl(mainTagName: string) {
        history.replaceState({}, document.title, this.url.removeQuery(window.location.pathname + window.location.search, this.queryKey(mainTagName)));
        this.onUrlChanged.raise({ mainTagName, url: null, encodedUrl: null, addedToUrl: false });
    }

    changeUrl(url: string, mainTagName: string, title?: string) {
        const encodedUrl = this.url.encodeGzipUrl(url);
        const target = this.url.updateQuery(window.location.pathname + window.location.search, this.queryKey(mainTagName), encodedUrl);
        history.pushState({}, title || document.title, target);
        this.onUrlChanged.raise({ mainTagName, url, encodedUrl, addedToUrl: true });
    }

    invalidateChildren(mainTagElement: JQuery) {
        mainTagElement.find("main[name]").each((_: number, e: any) => this.removeFromUrl($(e).attr("name")));
    }

    render(event?: JQueryEventObject, url?: string): boolean {
        const trigger = $(event.currentTarget);
        const name = trigger.attr("target");
        const element = $(`main[name='${name}']`);
        const target = url || trigger.attr("href");
        if (!element.length) return true;
        new MainTag(this.url, this.ajaxRedirect, this, target, element, name, trigger).render();
        return false;
    }

    openWithUrl(mainTagName: string, url?: string): boolean {
        const element = $(`main[name='${mainTagName}']`);
        if (!element.length) return false;
        new MainTag(this.url, this.ajaxRedirect, this, url, element, mainTagName, element).render(false);
        return true;
    }

    reload(mainTagName: string): boolean {
        const element = $(`main[name='${mainTagName}']`);
        return this.openWithUrl(mainTagName, element.attr("data-current-url"));
    }

    private validateState() {
        if (this.state) Object.keys(this.state).filter(n => !$(`main[name='${n}']`).length).forEach(n => delete this.state[n]);
    }
}

export default class MainTag {
    private url: string;

    constructor(private urlService: Url, private ajaxRedirect: AjaxRedirect, private helper: MainTagHelper, baseUrl: string, private element: JQuery, private mainTagName: string, private trigger: JQuery) {
        this.url = baseUrl;
    }

    render(changeUrl: boolean = true) {
        if (!this.isValidUrl(this.url)) return;
        this.helper.invalidateChildren(this.element);
        this.ajaxRedirect.go(this.url, this.element, false, true, false, success => {
            if (!success) return;
            const main = $(`main[name='${this.mainTagName}']`);
            main.attr("data-current-url", this.url);
            if (changeUrl) this.helper.changeUrl(this.url, this.mainTagName);
        });
    }

    protected isValidUrl(mainTagUrl: string): boolean { return !!mainTagUrl; }
}
