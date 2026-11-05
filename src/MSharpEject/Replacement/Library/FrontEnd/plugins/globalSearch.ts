import Waiting from "../components/waiting";
import { ModalHelper } from "../components/modal";

export class GlobalSearchFactory implements IService {
    constructor(private waiting: Waiting, private modalHelper: ModalHelper) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new GlobalSearch($(e), this.waiting, this.modalHelper).enable());
    }
}

export default class GlobalSearch implements IService {
    private urlList: string[] = [];
    private panel: JQuery;
    private helpPanel: JQuery;
    private groupsPanel: JQuery;
    private resultsPanel: JQuery;
    private isTyping = false;
    private searchedText = "";
    private currentAjaxRequests: JQueryXHR[] = [];

    protected boldSearch(str: string, searchText: string): string {
        if (!searchText) return str;
        const escaped = searchText.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
        return str.replace(new RegExp(escaped, "gi"), m => "<b>" + m + "</b>");
    }

    protected boldSearchAll(str: string, searchText: string): string {
        return searchText.split(" ").filter(s => s).reduce((text, word) => this.boldSearch(text, word), str);
    }

    constructor(private input: JQuery, private waiting: Waiting, private modalHelper: ModalHelper) { }

    enable() {
        if (this.input.is("[data-globalsearch-enabled=true]")) return;
        this.input.attr("data-globalsearch-enabled", true);
        this.createSearchComponent(this.input.attr("data-search-source").split(";"));
    }

    protected createSearchComponent(urls: string[]) {
        this.urlList = urls;
        const modal = $("#global-search-modal");
        this.panel = modal.find(".global-search-panel");
        this.helpPanel = modal.find(".global-search-help");
        this.groupsPanel = modal.find(".global-search-groups");
        this.resultsPanel = modal.find(".global-search-results");
        let timer: any;
        this.input.on("keyup", () => {
            clearTimeout(timer);
            timer = setTimeout(() => this.search(), 300);
        });
    }

    private search() {
        const text = (this.input.val() || "").trim();
        if (text === this.searchedText) return;
        this.searchedText = text;
        this.currentAjaxRequests.forEach(r => r.abort());
        this.currentAjaxRequests = [];
        this.groupsPanel.empty();
        if (!text) {
            this.resultsPanel.empty();
            this.helpPanel.show();
            return;
        }
        this.helpPanel.hide();
        this.resultsPanel.html("<div class='global-search-loading'>Searching...</div>");
        const context: ISearchContext = { ajaxList: [], groupsPanel: this.groupsPanel, resultsPanel: this.resultsPanel, resultCount: 0, beginSearchStarted: true, searchedText: text };
        this.urlList.forEach(entry => {
            const [url, icon] = entry.split("#").map(p => p.trim());
            const sender: IAjaxObject = { url, icon, state: AjaxState.pending };
            context.ajaxList.push(sender);
            sender.ajx = $.ajax({
                url, dataType: "json", data: { searcher: text }, xhrFields: { withCredentials: true },
                success: (result: IResultItemDto[]) => this.onSuccess(sender, context, result),
                complete: (jqXHR: JQueryXHR) => this.onComplete(context, jqXHR),
                error: (jqXHR: JQueryXHR) => this.onError(sender, jqXHR),
            });
            this.currentAjaxRequests.push(sender.ajx);
        });
    }

    protected onSuccess(sender: IAjaxObject, context: ISearchContext, result: IResultItemDto[]) {
        sender.state = AjaxState.success;
        sender.result = (result || []).filter(i => this.isValidResult(i, context));
        if (!sender.result.length) return;
        if (!context.resultCount) context.resultsPanel.find(".global-search-loading").remove();
        const groups = this.groupBy(sender.result, "GroupTitle");
        Object.keys(groups).forEach(title => {
            this.createSearchItems(sender, context, context.groupsPanel.children().length, title, groups[title]);
        });
        this.modalHelper.enableLink(context.resultsPanel.find("[target='$modal'][href]"));
    }

    protected isValidResult(item: IResultItemDto, context: ISearchContext): boolean { return !!item.Title; }

    protected createSearchItems(sender: IAjaxObject, context: ISearchContext, groupIndex: number, groupTitle: string, items: IResultItemDto[]) {
        const id = this.safeId(groupTitle) + "-" + groupIndex;
        const active = groupIndex === 0 ? "active" : "";
        context.groupsPanel.append($(`<li class="nav-item"><a class="nav-link ${active}" href="#${id}" role="tab" data-toggle="tab"><i class="${sender.icon}"></i> ${groupTitle} <span class="badge badge-secondary">${items.length}</span></a></li>`));
        const row = $('<div class="row"></div>');
        items.forEach(i => { row.append(this.createItem(i, context)); context.resultCount++; });
        const pane = $(`<div role="tabpanel" class="tab-pane ${active}"></div>`).attr("id", id).append(row);
        context.resultsPanel.append(pane);
    }

    safeId(title: string): string { return (title || "").replace(/[^a-zA-Z0-9]/g, "-"); }

    protected createItem(item: IResultItemDto, context: ISearchContext): JQuery {
        const target = item.Action === ActionEnum.Popup ? ' target="$modal"' : item.Action === ActionEnum.NewWindow ? ' target="_blank"' : "";
        const link = (content: string) => `<a href="${item.Url}"${target}>${content}</a>`;
        return $(`<div><div class="search-item"><div class="icon"><a name="Photo" class="profile-photo" href="${item.Url}">${this.showIcon(item)}</a></div>`
            + `<div class="result-item-content"><div class="type">${link(item.GroupTitle)}</div>`
            + `<div class="title">${link(this.boldSearchAll(item.Title, context.searchedText))}</div>`
            + `<div class="body">${link(this.boldSearchAll(item.Description || "", context.searchedText))}</div></div></div></div>`);
    }

    protected onComplete(context: ISearchContext, jqXHR: JQueryXHR) {
        if (context.ajaxList.some(a => a.state === AjaxState.pending)) return;
        if (context.resultCount) return;
        context.resultsPanel.html(`<div class="global-search-no-results"><p>No results found for "<strong>${this.boldSearchAll(context.searchedText, context.searchedText)}</strong>"</p></div>`);
    }

    protected onError(sender: IAjaxObject, jqXHR: JQueryXHR) {
        sender.state = AjaxState.failed;
        sender.displayMessage = jqXHR.statusText;
    }

    protected showIcon(item: any): string {
        if (!item.IconUrl) return '<div class="icon"></div>';
        if (item.IconUrl.indexOf("fa-") >= 0) return `<span class="icon-background" style="background-color: ${item.Colour}"><span class="${item.IconUrl}"></span></span>`;
        return `<img src="${item.IconUrl}">`;
    }

    protected groupBy(array: IResultItemDto[], key: string): IResultGroupDto {
        const result: IResultGroupDto = {};
        array.forEach(i => { const k = (i as any)[key] || ""; (result[k] = result[k] || []).push(i); });
        return result;
    }
}

export enum AjaxState { pending = 0, success = 1, failed = 2 }

export interface ISearchContext { ajaxList: IAjaxObject[]; groupsPanel: JQuery; resultsPanel: JQuery; resultCount: number; beginSearchStarted: boolean; searchedText: string; }

export interface IResultGroupDto { [key: string]: IResultItemDto[]; }

export interface IResultItemDto { Title: string; Description: string; IconUrl: string; Url: string; Colour: string; GroupTitle: string; Action: ActionEnum; }

export interface IAjaxObject { url: string; icon: string; state: AjaxState; ajx?: JQueryXHR; displayMessage?: string; result?: IResultItemDto[]; }

export enum ActionEnum { Redirect = 0, Popup = 1, NewWindow = 2 }
