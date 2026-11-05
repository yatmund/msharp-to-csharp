import Url from "../components/url";

export class CKEditorFileManagerFactory implements IService {
    constructor(private url: Url) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new CKEditorFileManager($(e), this.url).enable());
    }
}

export default class CKEditorFileManager {
    constructor(private item: JQuery, private url: Url) { }

    enable() {
        this.item.off("click.ckeditor-file").on("click.ckeditor-file", () => { this.handleFileSelection(this.item.attr("data-download-uri") || this.item.text()); return false; });
    }

    protected handleFileSelection(uri: string) {
        if (this.isCKEditor5()) this.handleCKEditor5FileSelection(uri);
        else this.handleCKEditor4FileSelection(uri);
    }

    protected isCKEditor5(): boolean { return !window.opener.CKEDITOR; }

    protected handleCKEditor5FileSelection(uri: string) {
        window.opener.postMessage({ type: "ckeditor-file-selected", url: uri }, "*");
        window.close();
    }

    protected handleCKEditor4FileSelection(uri: string) {
        window.opener.CKEDITOR.tools.callFunction(this.url.getQuery("CKEditorFuncNum"), uri);
        window.close();
    }
}
