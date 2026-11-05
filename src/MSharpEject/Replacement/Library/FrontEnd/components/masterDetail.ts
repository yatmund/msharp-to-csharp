import Validate from "./validate";
import ResponseProcessor from "../mvc/responseProcessor";

export default class MasterDetail implements IService {
    constructor(private validate: Validate, private responseProcessor: ResponseProcessor) { }

    initialize() {
        this.responseProcessor.subformChanged.handle(() => this.updateSubFormStates());
    }

    enable(selector: JQuery) {
        selector.off("click.delete-subform").on("click.delete-subform", (e: JQueryEventObject) => this.deleteSubForm(e));
    }

    updateSubFormStates() {
        $("input[name$=MustBeDeleted][value=False]").val("false");
        $("input[name$=MustBeDeleted][value=True]").val("true");
        $("[data-subform-max]").each((_: number, e: any) => {
            const container = $(e);
            const count = container.find(".subform-item:visible").length;
            $(`[data-add-subform=${container.attr("data-subform")}]`).toggle(count < parseInt(container.attr("data-subform-max")));
        });
        $("[data-subform-min]").each((_: number, e: any) => {
            const container = $(e);
            const count = container.find(".subform-item:visible").length;
            $(`[data-delete-subform=${container.attr("data-subform")}]`).css("visibility", count > parseInt(container.attr("data-subform-min")) ? "visible" : "hidden");
        });
        $(".horizontal-subform thead").each((_: number, e: any) => {
            const count = $(e).closest(".horizontal-subform").find(".subform-item:visible").length;
            $(e).css("visibility", count ? "visible" : "hidden");
        });
    }

    private deleteSubForm(event: JQueryEventObject) {
        event.preventDefault();
        const item = $(event.currentTarget).closest(".subform-item");
        item.find("input[name$=MustBeDeleted]").val("true");
        this.validate.removeTooltipsRelatedTo(item);
        item.hide();
        this.updateSubFormStates();
        return false;
    }
}
