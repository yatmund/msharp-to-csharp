import AjaxRedirect from "../mvc/ajaxRedirect";
import { ModalHelper } from "../components/modal";
import ServerInvoker from "../mvc/serverInvoker";

export default class TestingContext implements IService {
    private isLoaded = false;

    constructor(private ajaxRedirect: AjaxRedirect, private modalHelper: ModalHelper, private serverInvoker: ServerInvoker) { }

    isAjaxRedirecting(): boolean { return this.ajaxRedirect.isAjaxRedirecting; }

    isOpeningModal(): boolean { return !!this.modalHelper.currentModal && this.modalHelper.currentModal.isOpening; }

    isClosingModal(): boolean { return this.modalHelper.isClosingModal; }

    isAwaitingAjaxResponse(): boolean { return this.serverInvoker.isAwaitingAjaxResponse; }

    isPlatformLoaded(): boolean { return this.isLoaded; }

    onPageInitialized() {
        this.isLoaded = true;
        window.testingContext = this;
    }
}
