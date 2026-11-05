import Url from "../components/url";
import ServerInvoker from "../mvc/serverInvoker";

export class FileUploadFactory implements IService {
    constructor(protected url: Url, protected serverInvoker: ServerInvoker) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new FileUpload($(e), this.url, this.serverInvoker).enable());
    }
}

export default class FileUpload {
    protected container: JQuery;
    protected deleteButton: JQuery;
    protected progressBar: JQuery;
    protected currentFileLink: JQuery;
    protected existingFileNameInput: JQuery;
    protected actionInput: JQuery;
    protected tempFileIdInput: JQuery;
    protected filenameInput: JQuery;
    protected validationInput: JQuery;

    constructor(protected input: JQuery, protected url: Url, protected serverInvoker: ServerInvoker) {
        this.container = input.closest(".file-upload");
        this.deleteButton = this.container.find(".delete-file");
        this.progressBar = this.container.find(".progress-bar");
        this.currentFileLink = this.container.find(".current-file a");
        this.existingFileNameInput = this.container.find(".Filename");
        this.actionInput = this.container.find(".Action");
        this.tempFileIdInput = this.container.find(".TempFileId");
        this.filenameInput = this.container.find(".Filename");
        this.validationInput = this.container.find(".validation");
    }

    enable() {
        this.input.attr("data-url", this.getDataUrlAttribute());
        this.input.filestyle(this.getFilestyleOptions());
        this.fixMasterDetailsInputName();
        this.input.fileupload(this.getFileuploadOptions());
        this.deleteButton.off("click.delete-file").on("click.delete-file", () => this.onDeleteButtonClicked());
    }

    protected getDataUrlAttribute(): string { return this.url.effectiveUrlProvider("/upload", this.input); }

    protected getFilestyleOptions(): any {
        return { input: true, buttonBefore: true, disabled: false, badge: false, dragdrop: true };
    }

    protected getFileuploadOptions(): any {
        return {
            dataType: "json", dropZone: this.container, replaceFileInput: false,
            drop: (e: any, data: any) => this.onDragDropped(e, data),
            change: (e: any, data: any) => this.onChange(e, data),
            progressall: (e: any, data: any) => this.onProgressAll(e, data),
            error: (response: any) => this.onUploadError(response),
            success: (response: any) => this.onUploadSuccess(response),
            xhrFields: { withCredentials: true },
            complete: (response: any) => this.onUploadCompleted(response),
        };
    }

    private fixMasterDetailsInputName() {
        const name = this.input.attr("name");
        if (name) this.input.attr("name", name.replace(/\.\d+\./, m => m));
    }

    private removeExistingFile() {
        this.deleteButton.hide();
    }

    private onDeleteButtonClicked() {
        this.actionInput.val("Removed");
        this.progressBar.width(0);
        this.tempFileIdInput.val("");
        this.setValidationValue("");
        this.input.filestyle("clear");
        this.removeExistingFile();
    }

    private onDragDropped(e: any, data: any) {
        if (data.files.length) this.filenameInput.val(data.files[0].name);
    }

    protected onProgressAll(e: any, data: any) {
        this.progressBar.width(parseInt((data.loaded / data.total * 100) + "", 10) + "%");
    }

    private onUploadError(response: any) {
        alertify.alert(response.responseText || "Upload failed.");
    }

    private onUploadSuccess(response: any) {
        if (response.Error) { alertify.alert(response.Error); return; }
        this.UploadCompleted({ id: response.Result.ID, filename: response.Result.Name, url: response.Result.Url });
    }

    protected onUploadCompleted(response: any) { }

    protected UploadCompleted(args: IFileUploadedEventArgs) {
        this.tempFileIdInput.val(args.id);
        this.filenameInput.val(args.filename);
        this.setValidationValue("value");
        this.deleteButton.show();
    }

    private onChange(e: any, data: any) { this.progressBar.width(0); }

    protected setValidationValue(value: string) {
        this.validationInput.val(value);
        this.validationInput.valid();
    }
}

export class FileUploadS3 extends FileUpload {
    constructor(input: JQuery, url: Url, serverInvoker: ServerInvoker, protected bucketUrl: string) { super(input, url, serverInvoker); }

    protected getDataUrlAttribute(): string { return this.bucketUrl; }

    protected getFileuploadOptions(): any {
        const options = super.getFileuploadOptions();
        options.xhrFields = {};
        options.add = (e: any, data: any) => this.add(e, data);
        return options;
    }

    private add(e: any, data: any) {
        data.formData = { key: this.uuidv4() + "/" + data.files[0].name };
        data.submit();
    }

    protected onUploadCompleted({ id, filename }: { id: any; filename: any }) {
        this.UploadCompleted({ id, filename, url: this.bucketUrl + "/" + id + "/" + filename });
    }

    private uuidv4(): string {
        return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, c => {
            const r = Math.random() * 16 | 0;
            return (c === "x" ? r : (r & 0x3 | 0x8)).toString(16);
        });
    }
}

export interface IFileUploadedEventArgs { id: string; filename: string; url: string; }
