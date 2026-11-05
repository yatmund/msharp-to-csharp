import { ModalHelper } from "../components/modal";

export class MultiSelectFactory implements IService {
    constructor(private modalHelper: ModalHelper) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new MultiSelect($(e), this.modalHelper).show());
    }
}

export default class MultiSelect implements IService {
    constructor(protected selectControl: JQuery, private modalHelper: ModalHelper) { }

    show() {
        $.fn.selectpicker.Constructor.BootstrapVersion = "4";
        this.selectControl.selectpicker({
            maxOptions: false, actionsBox: true, container: false, deselectAllText: "Deselect All", dropdownAlignRight: false, dropupAuto: true, header: false, hideDisabled: false,
            liveSearch: true, liveSearchNormalize: false, liveSearchPlaceholder: null, liveSearchStyle: "contains", maxOptionsText: "Cannot select more items", mobile: false,
            multipleSeparator: ", ", noneSelectedText: "Nothing selected", noneResultsText: "No results matched", selectAllText: "Select All", selectedTextFormat: "count > 1",
            selectOnTab: false, showContent: true, showIcon: true, showSubtext: false, size: "auto", styleBase: "btn", title: null, virtualScroll: false, width: false, windowPadding: 0, sanitize: true,
        });
        this.MoveActionButtons();
        this.selectControl.on("shown.bs.select", () => this.modalHelper.adjustHeight());
    }

    private MoveActionButtons() {
        const menu = this.selectControl.parent().find(".dropdown-menu");
        menu.prepend(menu.find(".bs-actionsbox"));
    }
}
