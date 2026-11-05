import Url from "../components/url";
import Waiting from "../components/waiting";
import LiteEvent from "../components/liteEvent";
import ResponseProcessor from "./responseProcessor";
import Config from "../config";

export default class AjaxRedirect implements IService {
    private requestCounter = 0;
    private inFlight: { [key: string]: { id: number; xhr: JQueryXHR; trigger: JQuery } } = {};

    ajaxChangedUrl = 0;
    isAjaxRedirecting = false;
    beforeRedirect = new LiteEvent<IEventArgs>();

    constructor(protected url: Url, private responseProcessor: ResponseProcessor, private waiting: Waiting) { }

    enableRedirect(selector: JQuery) {
        selector.off("click.ajax-redirect").on("click.ajax-redirect", (e: JQueryEventObject) => this.redirect(e));
    }

    protected onRedirected(trigger: JQuery, title: string, url: string) {
        if (window.location.href !== url) history.pushState({}, title, url);
        this.onTitleChanged(title, url);
    }

    protected onTitleChanged(title: string, url: string) {
        if (title) document.title = title;
    }

    protected onMainTagRedirected(trigger: JQuery, title: string, url: string): boolean { return false; }

    protected isInternalMainTag(mainTag: JQuery): boolean { return !!mainTag && mainTag.is("main[name]"); }

    protected finalTargetAsMainTag(trigger: JQuery): JQuery | undefined {
        if (!trigger || !trigger.closest) return undefined;
        const main = trigger.closest("main[name]");
        return main.length ? main : undefined;
    }

    private targetKeyFor(trigger: JQuery): string {
        const main = this.finalTargetAsMainTag(trigger);
        return main ? main.attr("name") : "page";
    }

    cancelPending(trigger?: JQuery) {
        const keys = trigger ? [this.targetKeyFor(trigger)] : Object.keys(this.inFlight);
        keys.forEach(k => {
            const pending = this.inFlight[k];
            if (pending) { pending.xhr.abort(); delete this.inFlight[k]; }
        });
    }

    protected onRedirectionFailed(trigger: JQuery, url: string, response: JQueryXHR) {
        if (response.status === 401) { this.url.onAuthenticationFailed(); return; }
        console.error("Redirection failed", url, response.status);
    }

    private redirect(event: JQueryEventObject) {
        if (event.ctrlKey || event.button === 1) return true;
        const link = $(event.currentTarget);
        let url = link.attr("href");
        const ajaxTarget = link.attr("ajax-target");
        const ajaxhref = link.attr("href");
        this.go(url, link, false, !!link.attr("data-keep-scroll"), true, undefined, ajaxTarget, ajaxhref);
        return false;
    }

    go(inputUrl: string, trigger: JQuery = null, isBack: boolean = false, keepScroll: boolean = false, addToHistory: boolean = true,
        onComplete?: (successful: boolean) => void, ajaxTarget?: string, ajaxhref?: string): boolean {
        if (!trigger) trigger = $(window);
        const url = this.url.effectiveUrlProvider(inputUrl, trigger);
        this.beforeRedirect.raise({});
        this.cancelPending(trigger);
        const key = this.targetKeyFor(trigger);
        const id = ++this.requestCounter;
        const scroll = $(window).scrollTop();
        const token = this.waiting.show(false, false);
        const main = this.finalTargetAsMainTag(trigger) || $("main").first();
        main.addClass("w3-semi-fade-out");
        this.isAjaxRedirecting = true;
        const finish = () => {
            if (this.inFlight[key] && this.inFlight[key].id === id) delete this.inFlight[key];
            this.isAjaxRedirecting = Object.keys(this.inFlight).length > 0;
            main.removeClass("w3-semi-fade-out");
            this.waiting.hide(token);
        };
        const xhr = $.ajax({
            url, type: "GET", xhrFields: { withCredentials: true },
            success: (response: any) => {
                if (this.inFlight[key] && this.inFlight[key].id !== id) return;
                const title = $($.parseHTML(response || "")).filter("#page_meta_title").val() as string;
                const tag = this.finalTargetAsMainTag(trigger);
                const inModal = trigger.closest && trigger.closest(".modal").length > 0;
                if (addToHistory && !isBack && !ajaxTarget && !tag && !inModal) this.onRedirected(trigger, title, this.url.makeAbsolute(window.location.origin, "/" + this.url.makeRelative(url).replace(/^\//, "")));
                this.responseProcessor.processAjaxResponse(response, null, trigger, isBack ? "back" : null, ajaxTarget, ajaxhref);
                if (addToHistory && tag) {
                    const name = tag.attr("name");
                    $(`main[name='${name}']`).attr("data-current-url", url);
                    window.page.mainTag.changeUrl(url, name);
                }
                if (keepScroll) $(window).scrollTop(scroll);
                else if (Config.REDIRECT_SCROLLS_UP) $(window).scrollTop(0);
                if (onComplete) onComplete(true);
            },
            error: (response: JQueryXHR) => {
                if (response.statusText === "abort") return;
                if (onComplete) onComplete(false);
                this.onRedirectionFailed(trigger, url, response);
            },
            complete: finish,
        });
        this.inFlight[key] = { id, xhr, trigger };
        return false;
    }
}
