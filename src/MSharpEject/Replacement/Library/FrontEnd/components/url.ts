import "pako/dist/pako";

export default class Url implements IService {
    effectiveUrlProvider: ((url: string, trigger: JQuery) => string) = (url) => url;
    onAuthenticationFailed: (() => void) = () => this.goToLoginPage();
    baseContentUrl = window.BaseThemeUrl || "";

    private pako(): any { return window.require("pako/dist/pako"); }

    decodeGzipUrl(inputUrl: string): string {
        if (!inputUrl || inputUrl.indexOf("...") !== 0) return inputUrl;
        const base64 = inputUrl.slice(3).replace(/~/g, "+").replace(/_/g, "/").replace(/-/g, "=");
        return this.pako().ungzip(Uint8Array.from(atob(base64), c => c.charCodeAt(0)), { to: "string" });
    }

    encodeGzipUrl(inputValue: string): string {
        const bytes: Uint8Array = this.pako().gzip(inputValue);
        return "..." + btoa(String.fromCharCode(...Array.from(bytes))).replace(/\+/g, "~").replace(/\//g, "_").replace(/=/g, "-");
    }

    makeAbsolute(baseUrl: string, relativeUrl: string): string {
        baseUrl = baseUrl || window.location.origin;
        relativeUrl = relativeUrl || "";
        if (relativeUrl.indexOf("/") !== 0) relativeUrl = "/" + relativeUrl;
        return baseUrl.replace(/\/$/, "") + relativeUrl;
    }

    makeRelative(url: string): string {
        return this.isAbsolute(url) ? url.split("/").splice(3).join("/") : url;
    }

    isAbsolute(url: string): Boolean {
        return /^https?:\/\//i.test(url || "");
    }

    current(): string { return window.location.href; }

    goBack(target: any) {
        const returnUrl = this.getQuery("ReturnUrl");
        if (returnUrl) window.location.href = returnUrl;
        else history.back();
    }

    updateQuery(uri: any, key: any, value: any) {
        const pattern = new RegExp("([?&])" + key + "=[^&]*", "i");
        if (pattern.test(uri)) return uri.replace(pattern, "$1" + key + "=" + value);
        return this.addQuery(uri, key, value);
    }

    removeQuery(url: string, parameter: string): string {
        const [path, query] = this.split(url);
        if (query == null) return url;
        const kept = query.split("&").filter(p => p.split("=")[0].toLowerCase() !== (parameter + "").toLowerCase());
        return path + "?" + kept.join("&");
    }

    getQuery(name: string, url?: string): string {
        const query = this.split(url || this.current())[1];
        if (!query) return "";
        for (const pair of query.split("#")[0].split("&")) {
            const index = pair.indexOf("=");
            const key = index < 0 ? pair : pair.slice(0, index);
            if (key.toLowerCase() === name.toLowerCase()) return decodeURIComponent(index < 0 ? "" : pair.slice(index + 1));
        }
        return "";
    }

    getModalQuery(name: string): string {
        const modal = this.getQuery("_modal");
        return modal ? this.getQuery(name, modal) : "";
    }

    goToUrlAfterLogin(url: string) {
        window.location.href = "/login?returnUrl=" + this.encodeGzipUrl(url);
    }

    private goToLoginPage() {
        this.goToUrlAfterLogin(this.makeRelative(this.current()));
    }

    private split(url: string): [string, string] {
        const index = (url || "").indexOf("?");
        return index < 0 ? [url, null] : [url.slice(0, index), url.slice(index + 1)];
    }

    addQuery(url: string, key: string, value: any): string {
        return url + (url.indexOf("?") >= 0 ? "&" : "?") + key + "=" + value;
    }

    removeEmptyQueries(url: string): string {
        const [path, query] = this.split(url);
        if (query == null) return url;
        const kept = query.split("&").filter(p => p.indexOf("=") >= 0 && p.slice(p.indexOf("=") + 1) !== "");
        return kept.length ? path + "?" + kept.join("&") : path;
    }

    getBaseThemeUrl(): string { return this.baseContentUrl; }

    ofContent(relativeUrl: string): string {
        const base = this.getBaseThemeUrl().replace(/\/$/, "");
        return base + (relativeUrl.indexOf("/") === 0 ? "" : "/") + relativeUrl;
    }
}
