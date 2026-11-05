import Url from "./url";
import CrossDomainEvent from "./crossDomainEvent";
import AjaxRedirect from "../mvc/ajaxRedirect";
import ResponseProcessor from "../mvc/responseProcessor";
import Config from "../config";

export class ModalHelper implements IService {
    current: any = null;
    currentModal: Modal = null;
    isAjaxModal = false;
    isClosingModal = false;

    constructor(private url: Url, private ajaxRedirect: AjaxRedirect, private responseProcessor: ResponseProcessor) { }

    enableLink(selector: JQuery) {
        selector.off("click.open-modal").on("click.open-modal", (e: JQueryEventObject) => {
            e.preventDefault();
            const target = $(e.currentTarget);
            if (target.attr("data-mode") === "iframe") this.openiFrame(e);
            else this.open(e);
            return false;
        });
    }

    initialize() {
        CrossDomainEvent.handle("set-iframe-height", (height: number) => this.setIFrameHeight(height));
        CrossDomainEvent.handle("close-modal", () => this.close());
        this.responseProcessor.processCompleted.handle(() => { if (this.currentModal) this.adjustHeight(); });
    }

    private closeMe() {
        if (this.current) this.current.modal("hide");
    }

    close(): boolean {
        if (!this.isOrGoingToBeModal()) {
            if (window.parent !== window) { CrossDomainEvent.raise(window.parent, "close-modal"); return false; }
            return true;
        }
        this.isClosingModal = true;
        this.closeMe();
        this.isClosingModal = false;
        return true;
    }

    private setIFrameHeight(height: number) {
        if (this.current) this.current.find("iframe").height(height);
    }

    enableEnsureHeight(selector: JQuery) {
        selector.off("click.tab-toggle").on("click.tab-toggle", () => this.ensureHeight());
    }

    private ensureHeight() { setTimeout(() => this.adjustHeight(), 1); }

    adjustHeight(overflow: number = 0) {
        if (window.parent !== window && this.isIFramed()) {
            CrossDomainEvent.raise(window.parent, "set-iframe-height", $("body").prop("scrollHeight") + overflow);
        }
    }

    private isIFramed(): boolean { return window.self !== window.top; }

    expandToFitPicker(target: any) {
        const picker = $(target.currentTarget || target).siblings(".bootstrap-datetimepicker-widget");
        if (picker.length) this.adjustHeight(picker.height());
    }

    private ensureNonModal() {
        if (this.url.getQuery("_modal")) this.changeUrl(null);
    }

    tryOpenFromUrl() {
        const url = this.url.getQuery("_modal");
        if (url) this.open(undefined, url);
    }

    changeUrl(url: string, iframe: boolean = false) {
        let current = this.url.removeQuery(window.location.pathname + window.location.search, "_modal");
        current = this.url.removeQuery(current, "_iframe");
        if (url) {
            current = this.url.addQuery(current, "_modal", encodeURIComponent(url));
            if (iframe) current = this.url.addQuery(current, "_iframe", "true");
        }
        history.pushState({}, document.title, current);
    }

    isOrGoingToBeModal(): boolean { return !!this.current || !!this.url.getQuery("_modal"); }

    open(event?: JQueryEventObject, url?: string, options?: any) {
        new Modal(this.url, this.ajaxRedirect, this, event, url, options).open();
    }

    openiFrame(event?: JQueryEventObject, url?: string, options?: any) {
        new Modal(this.url, this.ajaxRedirect, this, event, url, options).openiFrame();
    }

    openHtmlContent(event?: JQueryEventObject, modalTitle?: string, htmlContent?: string, options?: any) {
        new Modal(this.url, this.ajaxRedirect, this, event, undefined, options).openHtmlContent(modalTitle, htmlContent);
    }

    protected openWithUrl() { this.tryOpenFromUrl(); }
}

export default class Modal {
    isOpening = false;
    opener: JQuery;
    private url: string;
    private rawUrl: string;
    private modalOptions: any = {};
    scrollPosition = 0;

    constructor(private urlService: Url, private ajaxRedirect: AjaxRedirect, private helper: ModalHelper, event?: JQueryEventObject, targeturl?: string, opt?: any) {
        const target = event ? $(event.currentTarget) : null;
        this.opener = target;
        this.rawUrl = targeturl || (target ? target.attr("href") : undefined);
        this.url = this.rawUrl ? this.urlService.effectiveUrlProvider(this.rawUrl, target) : this.rawUrl;
        const options = opt || (target ? target.attr("data-modal-options") : undefined);
        if (options) this.modalOptions = typeof options === "string" ? JSON.parse(options) : options;
    }

    onComplete(success: Boolean) {
        this.isOpening = false;
        this.helper.isAjaxModal = true;
        if (!success) this.helper.close();
    }

    onClose() {
        this.helper.current = null;
        this.helper.currentModal = null;
        $(window).scrollTop(this.scrollPosition);
    }

    private show(html: string, changeUrl: boolean, iframe: boolean) {
        if (this.helper.current) this.helper.close();
        this.isOpening = true;
        this.scrollPosition = $(window).scrollTop();
        this.helper.current = $(html);
        this.helper.currentModal = this;
        this.helper.current.appendTo("body");
        const { width, height } = this.modalOptions;
        this.helper.current.find(".modal-dialog").attr("style", (width ? `width:${width}; max-width: none;` : "") + (height ? `height:${height};` : ""));
        this.helper.current.modal({ backdrop: this.modalOptions.backdrop || Config.DEFAULT_MODAL_BACKDROP, keyboard: !!this.modalOptions.keyboard });
        this.helper.current.on("hidden.bs.modal", () => {
            this.helper.current && this.helper.current.remove();
            if (changeUrl) this.helper.changeUrl(null);
            this.onClose();
        });
        if (changeUrl && this.rawUrl) this.helper.changeUrl(this.rawUrl, iframe);
    }

    open(changeUrl: boolean = true): boolean {
        this.show(this.getModalTemplateForAjax(this.modalOptions), changeUrl, false);
        this.ajaxRedirect.go(this.url, this.helper.current.find(".modal-body main"), false, true, false, s => this.onComplete(s));
        return true;
    }

    openiFrame(changeUrl: boolean = true): boolean {
        this.show(this.getModalTemplateForiFrame(this.modalOptions), changeUrl, true);
        this.helper.current.find("iframe").attr("src", this.url);
        this.isOpening = false;
        return true;
    }

    openHtmlContent(modalTitle: string, htmlContent: string): boolean {
        this.show(this.getModalTemplateForHtmlContent(this.modalOptions), false, false);
        this.helper.current.find(".modal-title").text(modalTitle || "");
        this.helper.current.find(".modal-body").html(htmlContent);
        this.isOpening = false;
        return true;
    }

    shouldKeepScroll(): boolean { return true; }

    private template(body: string, title: string = ""): string {
        return `<div class="modal" id="myModal" tabindex="-1" role="dialog" aria-labelledby="myModalLabel" aria-hidden="true"> <div class="modal-dialog"> <div class="modal-content"> <div class="modal-header">${title} <button type="button" class="close" data-dismiss="modal" aria-label="Close"> <i class="fa fa-times-circle"></i> </button> </div> <div class="modal-body">${body} </div> </div></div></div>`;
    }

    protected getModalTemplateForAjax(options: any): string { return this.template(" <main></main>"); }

    protected getModalTemplateForiFrame(options: any): string {
        return this.template(' <iframe style="width:100%; border:0;"></iframe>').replace('class="modal"', 'class="modal fade"');
    }

    protected getModalTemplateForHtmlContent(options: any): string { return this.template(" ", '<h5 class="modal-title"></h5>'); }
}
