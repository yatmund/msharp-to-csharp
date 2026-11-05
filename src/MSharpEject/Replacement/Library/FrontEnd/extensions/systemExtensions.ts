export default class SystemExtensions {
    static initialize() {
        SystemExtensions.extendString();
        window.download = SystemExtensions.download;
        (JSON as any).safeParse = SystemExtensions.safeParse;
        SystemExtensions.extend(Array, "groupBy", SystemExtensions.groupBy);
    }

    private static extend(type: any, name: string, implementation: Function) {
        if (type.prototype[name]) return;
        Object.defineProperty(type.prototype, name, { value: implementation, enumerable: false, writable: true, configurable: true });
    }

    private static extendString() {
        const extend = (name: string, f: Function) => SystemExtensions.extend(String, name, f);
        extend("contains", function (text: string) { return this.indexOf(text) >= 0; });
        extend("htmlEncode", function () { return $("<div/>").text(this + "").html(); });
        extend("htmlDecode", function () { return $("<div/>").html(this + "").text(); });
        extend("withPrefix", function (prefix: string) { return this.indexOf(prefix) === 0 ? this + "" : prefix + this; });
    }

    private static safeParse(data: string) {
        try { return JSON.parse(data); } catch (e) { return null; }
    }

    private static download(url: string) {
        $("<iframe style='position:absolute; width:0; height:0; border:0; visibility:hidden;'></iframe>").attr("src", url).appendTo("body");
    }

    private static groupBy(this: any, selector: (item: any) => string) {
        const result: any = {};
        this.forEach((item: any) => { const key = selector(item); (result[key] = result[key] || []).push(item); });
        return result;
    }
}
