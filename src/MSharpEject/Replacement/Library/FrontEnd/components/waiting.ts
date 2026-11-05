import Url from "./url";

export default class Waiting implements IService {
    private tokens: string[] = [];
    private tokenSeed = 0;

    constructor(private url: Url) { }

    show(blockScreen: boolean = false, validate: boolean = true): string {
        const token = "wait-" + (++this.tokenSeed);
        this.tokens.push(token);
        if (blockScreen && $(".wait-screen").length === 0) this.addCover();
        return token;
    }

    hide(token?: string) {
        this.tokens = token ? this.tokens.filter(t => t !== token) : [];
        if (this.tokens.length === 0) $(".wait-screen").remove();
    }

    private addCover() {
        const image = this.url.ofContent("/img/loading.gif");
        $("body").append($(`<div class="wait-screen"><div class="wait-container"><div class="wait-box"><img src="${image}"></div></div></div>`));
    }
}
