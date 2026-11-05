import LiteEvent from "../components/liteEvent";

export default class ResponseProcessor implements IService {
    private dynamicallyLoadedScriptFiles: string[] = [];
    private seed = 0;

    subformChanged = new LiteEvent<IResponseProcessorEventArgs>();
    viewChanged = new LiteEvent<IViewUpdatedEventArgs>();
    processCompleted = new LiteEvent<IEventArgs>();
    nothingFoundToProcess = new LiteEvent<IResponseProcessorEventArgs>();

    processAjaxResponse(response: any, containerModule: JQuery, trigger: JQuery, args: any, ajaxTarget?: string, ajaxhref?: string) {
        if (typeof response !== "string") { this.onNothingFoundToProcess(response, trigger); return; }
        const parsed = $($.parseHTML(response, document, true));
        const main = parsed.filter("main").add(parsed.find("main")).first();
        if (ajaxTarget) {
            this.navigatebyAjaxTarget(main.length ? main : parsed, ajaxTarget);
            this.onProcessCompleted();
            return;
        }
        if (main.length) { this.navigate(main, trigger, args); return; }
        if (parsed.is("[data-module]") && containerModule && containerModule.length) {
            containerModule.replaceWith(parsed);
            this.onViewChanged(parsed, trigger);
            this.onProcessCompleted();
            return;
        }
        if (trigger && trigger.is && trigger.is("[data-add-subform]")) {
            const name = trigger.attr("data-add-subform");
            $(`[data-subform=${name}] .subform-item:last, [data-subform=${name}] tbody:last`).last().parent().append(parsed);
            this.onSubformChanged(response, trigger);
            this.onViewChanged(parsed, trigger);
            this.onProcessCompleted();
            return;
        }
        this.onNothingFoundToProcess(response, trigger);
    }

    protected onNothingFoundToProcess(response: any, trigger: JQuery) {
        this.nothingFoundToProcess.raise({ response, trigger });
    }

    protected onSubformChanged(response: any, trigger: JQuery) { this.subformChanged.raise({ response, trigger }); }

    protected onViewChanged(container: JQuery, trigger: JQuery, isNewPage: boolean = false) {
        this.viewChanged.raise({ container, trigger, isNewPage });
    }

    protected navigate(element: JQuery, trigger: JQuery, args: any) {
        this.processWithTheContent(trigger, element, args, element.find("script[src]"));
    }

    protected navigatebyAjaxTarget(element: JQuery, ajaxTarget: string) {
        const target = $(`main[name='${ajaxTarget}']`);
        const content = element.is("main") ? element.contents() : element;
        target.empty().append(content);
        this.onViewChanged(target, target, false);
    }

    private getNewCss(page: JQuery): string[] {
        const existing = this.getCss($("head"));
        return this.getCss(page).filter(href => existing.indexOf(href) < 0);
    }

    private getCss(container: JQuery): string[] {
        return container.find("link[rel=stylesheet]").map((_: number, l: any) => $(l).attr("href")).get();
    }

    protected processWithTheContent(trigger: JQuery, newMain: JQuery, args: any, referencedScripts: JQuery) {
        const css = this.getNewCss(newMain);
        css.forEach(href => $("head").append($('<link rel="stylesheet">').attr("href", href)));
        this.processWithTheContentInternal(trigger, newMain, args, referencedScripts);
    }

    private processWithTheContentInternal(trigger: JQuery, newMain: JQuery, args: any, referencedScripts: JQuery) {
        const inModal = trigger && trigger.closest && trigger.closest(".modal").length > 0;
        const target = inModal ? trigger.closest(".modal-body").find("main").first() : this.mainOf(trigger);
        this.replaceContent(target.length ? target : $("main").first(), newMain);
        this.updateUrl(referencedScripts, newMain, trigger);
        this.onViewChanged(newMain, trigger, args === true || args === "newPage");
        this.onProcessCompleted();
    }

    private mainOf(trigger: JQuery): JQuery {
        const named = trigger && trigger.closest ? trigger.closest("main[name]") : $();
        return named.length ? named : $("main").first();
    }

    private replaceContent(target: JQuery, newMain: JQuery) {
        const name = target.attr("name");
        if (name) newMain.attr("name", name);
        target.replaceWith(newMain);
    }

    protected updateUrl(referencedScripts: JQuery, element: JQuery, trigger: JQuery) {
        referencedScripts.each((_: number, s: any) => {
            const src = $(s).attr("src");
            if (this.dynamicallyLoadedScriptFiles.indexOf(src) >= 0) return;
            this.dynamicallyLoadedScriptFiles.push(src);
            $.getScript(src);
        });
    }

    protected setWindowTitle(title: string) { if (title) document.title = title; }

    protected onProcessCompleted() { this.processCompleted.raise({}); }
}
