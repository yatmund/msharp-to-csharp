import Alert from "../components/alert";
import Select from "../plugins/select";
import Waiting from "../components/waiting";
import { ModalHelper } from "../components/modal";
import { MainTagHelper } from "../components/mainTag";
import AjaxRedirect from "./ajaxRedirect";
import Form from "../components/form";
import ResponseProcessor from "./responseProcessor";

export default class StandardAction implements IService {
    constructor(private alert: Alert, private form: Form, private waiting: Waiting, private ajaxRedirect: AjaxRedirect, private responseProcessor: ResponseProcessor,
        private select: Select, private modalHelper: ModalHelper, private mainTagHelper: MainTagHelper, private serviceLocator: IServiceLocator) { }

    initialize() {
        this.responseProcessor.nothingFoundToProcess.handle(data => this.runAll(data.response, data.trigger));
    }

    runStartup(container: JQuery = $(document), trigger: any = null, stage: string = "Init") {
        container.find("input[name='Startup.Actions']").each((_: number, e: any) => {
            this.runAll(JSON.parse($(e).val() || "[]").filter((a: any) => (a.Stage || "Init") === stage), trigger);
        });
    }

    runAll(actions: any, trigger: any = null) {
        if (!Array.isArray(actions)) return;
        for (const action of actions) if (this.run(action, trigger) === false) return;
    }

    private run(action: any, trigger: any): boolean {
        if (action.Notify !== undefined) this.notify(action, trigger);
        else if (action.Script) $.globalEval(action.Script);
        else if (action.BrowserAction === "Back") window.history.back();
        else if (action.BrowserAction === "CloseModal") return this.modalHelper.close() !== false;
        else if (action.BrowserAction === "CloseModalRebindParent" || action.BrowserAction === "CloseModalRefreshParent") this.closeAndRefreshParent();
        else if (action.BrowserAction === "Close") window.close();
        else if (action.BrowserAction === "Print") window.print();
        else if (action.BrowserAction === "Refresh") window.page.refresh();
        else if (action.BrowserAction === "ShowPleaseWait") this.waiting.show(true);
        else if (action.ReplaceSource !== undefined) this.select.replaceSource(action.ReplaceSource, action.Items);
        else if (action.Download) window.download(action.Download);
        else if (action.Redirect) { this.redirect(action, trigger); return false; }
        else if (action.ReplaceView !== undefined) this.replaceView(action.ReplaceView, trigger);
        else if (action.ServiceKey) this.loadService(action);
        else if (action.SubForm) this.addSubForm(action, trigger);
        else if (action.MainTagUrl) this.renderMainTag(action);
        else if (action.ServiceConfigurationUrl) this.loadServiceAfterConfiguration(action);
        else alert("Don't know how to handle: " + JSON.stringify(action).htmlEncode());
        return true;
    }

    private closeAndRefreshParent() {
        this.modalHelper.close();
        window.page.refresh();
    }

    private replaceView(html: string, trigger: any) {
        $("main").first().empty().append($("<div/>").append(html));
    }

    private addSubForm(action: any, trigger: any) {
        const container = $(`[data-subform=${action.SubForm}]`);
        container.append(action.Html);
        this.responseProcessor["onSubformChanged"](action, trigger);
    }

    private notify(action: any, trigger: any) {
        if (action.Obstruct === false) this.alert.alertUnobtrusively(action.Notify, action.Style);
        else this.alert.alert(action.Notify, action.Style);
    }

    protected redirect(action: any, trigger: any) {
        if (action.Target) window.open(action.Redirect, action.Target);
        else if (action.WithAjax === false) window.location.replace(action.Redirect);
        else this.ajaxRedirect.go(action.Redirect, trigger, false, false, true);
    }

    private openModal(event: any, url: string, options: any) { this.modalHelper.open(event, url, options); }

    private openModalHtmlContent(event: any, title: string, html: string, options: any) { this.modalHelper.openHtmlContent(event, title, html, options); }

    private renderMainTag(action: any) { this.mainTagHelper.openWithUrl(action.MainTagName, action.MainTagUrl); }

    private loadServiceAfterConfiguration(action: any) {
        requirejs([action.ServiceConfigurationUrl], () => this.loadService(action));
    }

    private loadService(action: any) {
        const service: any = this.serviceLocator.getService(action.ServiceKey);
        service[action.Function].apply(service, action.Arguments || []);
    }
}
