import Config from "../config";
import { ModalHelper } from "../components/modal";

export class HtmlEditorFactory implements IService {
    constructor(private modalHelper: ModalHelper) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new HtmlEditor($(e), this.modalHelper).enable());
    }
}

export default class HtmlEditor {
    static editorConfigPath = "/scripts/ckeditor_config.js";

    constructor(private input: JQuery, private modalHelper: ModalHelper) { }

    enable() {
        if (window.isMobile) return;
        if (this.isCKEditor5()) this.enableCKEditor5();
        else this.enableCKEditor4();
    }

    protected isCKEditor5(): boolean { return Config.CK_EDITOR_VERSION === "5"; }

    protected enableCKEditor5() { this.loadCKEditor5ViaRequireJS(); }

    protected enableCKEditor4() {
        this.onDemandScript(Config.CK_EDITOR_BASE_PATH + "ckeditor.js", () => this.onCKEditor4ScriptReady());
    }

    protected loadCKEditor5ViaRequireJS() {
        requirejs(["ckeditor5"], () => this.onCKEditor5ScriptReady());
    }

    protected onCKEditor5ScriptReady() {
        const editors: any = { classic: "ClassicEditor", decoupled: "DecoupledEditor", inline: "InlineEditor", balloon: "BalloonEditor" };
        this.initializeCKEditor5(window[editors[Config.CK_EDITOR_5_BUNDLE]]);
    }

    protected initializeCKEditor5(EditorClass: any) {
        EditorClass.create(this.input[0], this.getCKEditor5Settings()).then((editor: any) => this.setupCKEditor5Events(editor));
    }

    protected onCKEditor4ScriptReady() {
        CKEDITOR.basePath = Config.CK_EDITOR_BASE_PATH;
        const editor = CKEDITOR.replace(this.input.attr("name"), this.getCKEditor4Settings());
        editor.on("change", () => { this.input.val(editor.getData()); });
        editor.on("instanceReady", () => this.modalHelper.adjustHeight());
    }

    protected setupCKEditor5Events(editor: any) {
        editor.model.document.on("change:data", () => this.input.val(editor.getData()));
        this.modalHelper.adjustHeight();
    }

    protected getCKEditor5Settings() {
        return { toolbar: this.getCKEditor5Toolbar(this.input.attr("data-toolbar") || Config.DEFAULT_HTML_EDITOR_MODE), placeholder: Config.CK_EDITOR_5_CONFIG.placeholder, licenseKey: "GPL" };
    }

    protected getCKEditor4Settings() {
        return { toolbar: this.input.attr("data-toolbar") || Config.DEFAULT_HTML_EDITOR_MODE, customConfig: HtmlEditor.editorConfigPath };
    }

    protected getCKEditor5Toolbar(toolbarMode: string): any {
        const compact = ["bold", "italic", "link"];
        return toolbarMode === "Compact" ? compact : compact.concat(["bulletedList", "numberedList", "blockQuote", "undo", "redo"]);
    }

    protected onDemandScript(url: string, callback: () => void) {
        requirejs([url], () => callback());
    }
}
