import Form from "../components/form";

export class SliderFactory implements IService {
    constructor(private form: Form) { }

    enable(selector: JQuery) {
        selector.each((_: number, e: any) => new Slider($(e), this.form).enable());
    }
}

export default class Slider {
    private input: JQuery;
    private options: any = { min: 0, max: 100, value: null, range: false, formatter: null, tooltip: "always", upper: null, tooltip_split: false };

    constructor(targetInput: JQuery, private form: Form) { this.input = targetInput; }

    private enableRange() {
        const id = this.input.attr("id");
        let upper = $(`[name="${id}.${this.options.upper}"]`);
        if (!upper.length) upper = $(`[name="${id}Max"]`);
        if (!upper.length) throw new Error("Upper input was not found for the range slider.");
        this.options.range = true;
        this.options.value = [parseFloat(this.input.val()) || this.options.min, parseFloat(upper.val()) || this.options.max];
        this.options.formatter = (v: any) => Array.isArray(v) ? v[0] + " - " + v[1] : v;
        const parent = this.input.parent();
        parent.children().hide();
        const slider = $("<input type='text' class='range-slider'/>").attr("id", id + "_slider").appendTo(parent);
        slider.slider(this.options).on("change", (e: any) => {
            this.input.val(e.value.newValue[0]);
            upper.val(e.value.newValue[1]);
        });
    }

    enable() {
        const data = this.input.attr("data-options");
        if (data) $.extend(true, this.options, JSON.parse(this.form.cleanJson(data)));
        if (this.input.attr("data-control") === "range-slider") this.enableRange();
        else {
            this.options.value = parseFloat(this.input.val()) || this.options.min;
            this.input.slider(this.options);
        }
    }
}
