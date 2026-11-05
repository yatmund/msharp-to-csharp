import "alertify";
import "bootstrap";
import Services from "./di/services";
import { ServiceContainer } from "./di/serviceContainer";
import { ServiceDescription } from "./di/serviceDescription";
import SystemExtensions from "./extensions/systemExtensions";
import { enableValidateForCheckboxList, enableValidateForTimePicker } from "./extensions/jQueryExtensions";
import Url from "./components/url";
import Alert from "./components/alert";
import Waiting from "./components/waiting";
import Form from "./components/form";
import Validate from "./components/validate";
import Grid from "./components/grid";
import { GroupingFactory } from "./components/grouping";
import MasterDetail from "./components/masterDetail";
import Paging from "./components/paging";
import Sorting from "./components/sorting";
import { ModalHelper } from "./components/modal";
import { MainTagHelper } from "./components/mainTag";
import AjaxRedirect from "./mvc/ajaxRedirect";
import ResponseProcessor from "./mvc/responseProcessor";
import ServerInvoker from "./mvc/serverInvoker";
import StandardAction from "./mvc/standardAction";
import WindowEx from "./mvc/windowEx";
import ConfirmBoxFactory from "./plugins/confirmBox";
import CustomCheckbox from "./plugins/customCheckbox";
import CustomRadio from "./plugins/customRadio";
import Select from "./plugins/select";
import InstantSearch from "./plugins/instantSearch";
import UserHelp from "./plugins/userHelp";
import SubMenu from "./plugins/subMenu";
import NumbericUpDown from "./plugins/numericUpDown";
import PasswordStength from "./plugins/passwordStength";
import DateDropdown from "./plugins/dateDropdown";
import MergeColumn from "./plugins/mergeColumn";
import SanityAdapter from "./plugins/sanityAdapter";
import TestingContext from "./plugins/testingContext";
import { DatePickerFactory } from "./plugins/datePicker";
import { DateTimePickerFactory } from "./plugins/dateTimePicker";
import { TimeControlFactory } from "./plugins/timeControl";
import { SliderFactory } from "./plugins/slider";
import { AutoCompleteFactory } from "./plugins/autoComplete";
import { MultiSelectFactory } from "./plugins/multiSelect";
import { FileUploadFactory } from "./plugins/fileUpload";
import { HtmlEditorFactory } from "./plugins/htmlEditor";
import { CKEditorFileManagerFactory } from "./plugins/ckEditorFileManager";
import { GlobalSearchFactory } from "./plugins/globalSearch";

export default class PlatformPage implements IServiceLocator {
    services = new ServiceContainer();
    modal: ModalHelper;
    waiting: Waiting;
    mainTag: MainTagHelper;

    constructor() {
        this.configureServices(this.services);
        SystemExtensions.initialize();
        this.modal = this.getService<ModalHelper>(Services.ModalHelper);
        this.waiting = this.getService<Waiting>(Services.Waiting);
        this.mainTag = this.getService<MainTagHelper>(Services.MainTagHelper);
        window.require && window.alertify === undefined && this.fixAlertIssues();
        this.initializeServices();

        $(() => {
            this.getService<Alert>(Services.Alert).enableAlert();
            this.getService<Validate>(Services.Validate).configure();
            enableValidateForCheckboxList();
            enableValidateForTimePicker();
            const standard = this.getService<StandardAction>(Services.StandardAction);
            standard.runStartup($("body"), null, "PreInit");
            try { this.initialize(); }
            catch (error) { alert("initialization failed: " + error); }
            standard.runStartup($("body"), null, "Init");
            this.getService<TestingContext>(Services.TestingContext).onPageInitialized();
            window.IsPlatformLoaded = true;
        });
    }

    protected initializeServices() {
        this.getService<ModalHelper>(Services.ModalHelper).initialize();
        this.getService<StandardAction>(Services.StandardAction).initialize();
        this.getService<Validate>(Services.Validate).initialize();
        this.getService<MasterDetail>(Services.MasterDetail).initialize();
        this.getService<MainTagHelper>(Services.MainTagHelper).initialize();
        this.getService<ResponseProcessor>(Services.ResponseProcessor).viewChanged.handle(e => this.onViewChanged(e.container, e.trigger, e.isNewPage));
    }

    private fixAlertIssues() { window.alertify = window.require("alertify"); }

    protected initializeActions: any[] = [];
    protected onInit(action: any) { this.initializeActions.push(action); }

    protected preInitializeActions: any[] = [];
    protected onPreInit(action: any) { this.preInitializeActions.push(action); }

    protected onViewChanged(container: JQuery = null, trigger: any = null, newPage: boolean = false, firstTime: boolean = false) {
        const standard = this.getService<StandardAction>(Services.StandardAction);
        standard.runStartup(container, trigger, "PreInit");
        this.initialize();
        standard.runStartup(container, trigger, "Init");
    }

    initialize() {
        this.preInitializeActions.forEach(a => a());
        const s = <T extends IService>(key: string) => this.getService<T>(key);
        const form = s<Form>(Services.Form);
        const grid = s<Grid>(Services.Grid);
        const paging = s<Paging>(Services.Paging);
        const sorting = s<Sorting>(Services.Sorting);
        const invoker = s<ServerInvoker>(Services.ServerInvoker);
        const redirect = s<AjaxRedirect>(Services.AjaxRedirect);
        const master = s<MasterDetail>(Services.MasterDetail);

        this.enableCustomCheckbox();
        this.enableCustomRadio();
        form.enablesubmitCleanGet($("form[method=get]"));
        s<ConfirmBoxFactory>(Services.ConfirmBoxFactory).enable($("[data-confirm-question]"));
        redirect.enableRedirect($("a[data-redirect=ajax]"));
        this.modal.enableLink($("[target='$modal'][href]"));
        this.mainTag.enableLink($("[target^='$']:not([target = '$modal'])[href]"));
        this.modal.enableEnsureHeight($("[data-toggle=tab]"));
        paging.enableWithAjax($("a[data-pagination]"));
        sorting.enableAjaxSorting($("a[data-sort]"));
        invoker.enableinvokeWithPost($("[formaction][formmethod=post]"));
        invoker.enableInvokeWithAjax($("[formaction]").not("[formmethod=post]"), "click.formaction", "formaction");
        invoker.enableInvokeWithAjax($("[data-change-action]:not([autocomplete-source]):not([data-control=collapsible-checkboxes])"), "change.data-action", "data-change-action");
        invoker.enableInvokeWithAjax($("[data-change-action][data-control=collapsible-checkboxes]"), "hidden.bs.select", "data-change-action");
        invoker.enableInvokeWithAjax($("[data-change-action][data-control=date-picker],[data-change-action][data-control=calendar],[data-change-action][data-control=time-picker]"), "dp.change.data-action", "data-change-action");
        form.enableDefaultButtonKeyPress($("form input, form select"));
        this.enablecleanUpNumberField(form);
        grid.enableColumn($(".select-cols .apply"));
        grid.enableSelectCol($(".select-grid-cols .group-control").parent());
        grid.enableToggle($("th.select-all > input:checkbox"));
        s<GroupingFactory>(Services.GroupingFactory).enable($(".form-group #GroupBy"));
        master.enable($("[data-delete-subform]"));
        master.updateSubFormStates();
        paging.enableOnSizeChanged($("form .pagination-size").find("select[name=p],select[name$='.p']"));
        sorting.enablesetSortHeaderClass($("th[data-sort]"));
        sorting.enableDragSort($("[data-sort-item]").parents("tbody,.r-grid-body"));
        InstantSearch.enable($("[name=InstantSearch]"));
        UserHelp.enable($("[data-user-help]"));
        s<Select>(Services.Select).enableEnhance($("select:not([data-control='collapsible-checkboxes'])"));
        s<MultiSelectFactory>(Services.MultiSelectFactory).enable($("[data-control=collapsible-checkboxes]"));
        s<DatePickerFactory>(Services.DatePickerFactory).enable($("[data-control=date-picker],[data-control=calendar]"));
        s<DateTimePickerFactory>(Services.DateTimePickerFactory).enable($("[data-control='date-picker|time-picker']"));
        s<TimeControlFactory>(Services.TimeControlFactory).enable($("[data-control=time-picker]"));
        DateDropdown.enable($("[data-control=date-drop-downs]"));
        NumbericUpDown.enable($("[data-control=numeric-up-down]"));
        s<SliderFactory>(Services.SliderFactory).enable($("[data-control=range-slider],[data-control=slider]"));
        s<HtmlEditorFactory>(Services.HtmlEditorFactory).enable($("[data-control=html-editor]"));
        s<FileUploadFactory>(Services.FileUploadFactory).enable($(".file-upload input:file"));
        s<AutoCompleteFactory>(Services.AutoCompleteFactory).enable($("input[autocomplete-source]"));
        s<GlobalSearchFactory>(Services.GlobalSearchFactory).enable($("input[data-search-source]"));
        s<CKEditorFileManagerFactory>(Services.CKEditorFileManagerFactory).enable($(".ckeditor-file-uri"));
        PasswordStength.enable($(".password-strength"));
        SubMenu.enable($(".with-submenu"));
        SubMenu.createAccordion($("ul.accordion"));
        MergeColumn.mergeActionButtons();
        s<SanityAdapter>(Services.SanityAdapter).enable();
        $("[autofocus]:not([data-autofocus=disabled]):first").focus();
        s<WindowEx>(Services.WindowEx).enableBack($(window));
        this.initializeActions.forEach(a => a());
        this.modal.adjustHeight();
    }

    protected enableCustomCheckbox() { CustomCheckbox.enable($("input[type=checkbox]")); }

    protected enablecleanUpNumberField(form: Form) { form.enablecleanUpNumberField($("[data-val-number]")); }

    protected enableCustomRadio() { CustomRadio.enable($("input[type=radio]")); }

    protected goBack(target: any): boolean {
        this.getService<Url>(Services.Url).goBack(target);
        return false;
    }

    protected customizeValidationTooltip() { }

    protected refresh(keepScroll: boolean = false): boolean {
        window.location.reload();
        return false;
    }

    protected configureServices(services: ServiceContainer) {
        const out: IOutParam<ServiceDescription> = {};
        const add = (key: string, factory: (...args: any[]) => IService, ...deps: string[]) => {
            if (services.tryAddSingleton(key, factory, out)) out.value.withDependencies(...deps);
        };
        const S = Services;
        add(S.ServiceLocator, () => this);
        add(S.Url, () => new Url());
        add(S.Alert, () => new Alert());
        add(S.Waiting, (url: Url) => new Waiting(url), S.Url);
        add(S.ResponseProcessor, () => new ResponseProcessor());
        add(S.AjaxRedirect, (u: Url, r: ResponseProcessor, w: Waiting) => new AjaxRedirect(u, r, w), S.Url, S.ResponseProcessor, S.Waiting);
        add(S.Validate, (a: Alert, r: ResponseProcessor) => new Validate(a, r), S.Alert, S.ResponseProcessor);
        add(S.Form, (u: Url, v: Validate, w: Waiting, a: AjaxRedirect) => new Form(u, v, w, a), S.Url, S.Validate, S.Waiting, S.AjaxRedirect);
        add(S.ServerInvoker, (u: Url, v: Validate, w: Waiting, f: Form, r: ResponseProcessor) => new ServerInvoker(u, v, w, f, r), S.Url, S.Validate, S.Waiting, S.Form, S.ResponseProcessor);
        add(S.ModalHelper, (u: Url, a: AjaxRedirect, r: ResponseProcessor) => new ModalHelper(u, a, r), S.Url, S.AjaxRedirect, S.ResponseProcessor);
        add(S.MainTagHelper, (u: Url, a: AjaxRedirect, r: ResponseProcessor) => new MainTagHelper(u, a, r), S.Url, S.AjaxRedirect, S.ResponseProcessor);
        add(S.Select, () => new Select());
        add(S.StandardAction, (a: Alert, f: Form, w: Waiting, j: AjaxRedirect, r: ResponseProcessor, s: Select, m: ModalHelper, t: MainTagHelper, l: IServiceLocator) => new StandardAction(a, f, w, j, r, s, m, t, l),
            S.Alert, S.Form, S.Waiting, S.AjaxRedirect, S.ResponseProcessor, S.Select, S.ModalHelper, S.MainTagHelper, S.ServiceLocator);
        add(S.WindowEx, (m: ModalHelper, t: MainTagHelper, a: AjaxRedirect) => new WindowEx(m, t, a), S.ModalHelper, S.MainTagHelper, S.AjaxRedirect);
        add(S.Grid, () => new Grid());
        add(S.GroupingFactory, (u: Url, a: AjaxRedirect) => new GroupingFactory(u, a), S.Url, S.AjaxRedirect);
        add(S.MasterDetail, (v: Validate, r: ResponseProcessor) => new MasterDetail(v, r), S.Validate, S.ResponseProcessor);
        add(S.Paging, (u: Url, i: ServerInvoker) => new Paging(u, i), S.Url, S.ServerInvoker);
        add(S.Sorting, (u: Url, i: ServerInvoker) => new Sorting(u, i), S.Url, S.ServerInvoker);
        add(S.ConfirmBoxFactory, () => new ConfirmBoxFactory());
        add(S.SanityAdapter, () => new SanityAdapter());
        add(S.TestingContext, (a: AjaxRedirect, m: ModalHelper, i: ServerInvoker) => new TestingContext(a, m, i), S.AjaxRedirect, S.ModalHelper, S.ServerInvoker);
        add(S.DatePickerFactory, (m: ModalHelper) => new DatePickerFactory(m), S.ModalHelper);
        add(S.DateTimePickerFactory, (m: ModalHelper) => new DateTimePickerFactory(m), S.ModalHelper);
        add(S.TimeControlFactory, (m: ModalHelper) => new TimeControlFactory(m), S.ModalHelper);
        add(S.SliderFactory, (f: Form) => new SliderFactory(f), S.Form);
        add(S.AutoCompleteFactory, (u: Url, f: Form, i: ServerInvoker) => new AutoCompleteFactory(u, f, i), S.Url, S.Form, S.ServerInvoker);
        add(S.MultiSelectFactory, (m: ModalHelper) => new MultiSelectFactory(m), S.ModalHelper);
        add(S.FileUploadFactory, (u: Url, i: ServerInvoker) => new FileUploadFactory(u, i), S.Url, S.ServerInvoker);
        add(S.HtmlEditorFactory, (m: ModalHelper) => new HtmlEditorFactory(m), S.ModalHelper);
        add(S.CKEditorFileManagerFactory, (u: Url) => new CKEditorFileManagerFactory(u), S.Url);
        add(S.GlobalSearchFactory, (w: Waiting, m: ModalHelper) => new GlobalSearchFactory(w, m), S.Waiting, S.ModalHelper);
    }

    getService<T extends IService>(key: string): T { return this.services.getService<T>(key); }
}
