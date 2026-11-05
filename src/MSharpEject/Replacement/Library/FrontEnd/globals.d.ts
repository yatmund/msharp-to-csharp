declare const $: any;
declare const jQuery: any;
declare const requirejs: any;
declare const moment: any;
declare const CKEDITOR: any;
declare const alertify: any;
type JQuery = any;
type JQueryXHR = any;
type JQueryEventObject = any;
type JQuerySerializeArrayElement = { name: string; value: string };
type Validator = any;
type TooltipOption = any;
interface IService { }
interface IServiceLocator extends IService { getService<T extends IService>(key: string): T; }
interface IOutParam<T> { value?: T; }
interface IEventArgs { }
interface IResponseProcessorEventArgs { response: any; trigger: JQuery; }
interface IViewUpdatedEventArgs { container: JQuery; trigger: JQuery; isNewPage: boolean; }
interface IInvocationContext { url: string; trigger: JQuery; containerModule: JQuery; tag?: any; }
interface String { contains(text: string): boolean; htmlEncode(): string; htmlDecode(): string; withPrefix(prefix: string): string; }
interface Window { [key: string]: any; }
