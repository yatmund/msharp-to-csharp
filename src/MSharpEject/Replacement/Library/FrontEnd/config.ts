export default class Config {
    static TIME_FORMAT = "HH:mm";
    static DATE_FORMAT = "DD/MM/YYYY";
    static DATE_TIME_FORMAT = "DD/MM/YYYY HH:mm";
    static MINUTE_INTERVALS = 5;
    static DATE_LOCALE = "en-gb";
    static DISABLE_BUTTONS_DURING_AJAX = false;
    static REDIRECT_SCROLLS_UP = true;
    static AUTOCOMPLETE_INPUT_DELAY = 500;
    static DEFAULT_MODAL_BACKDROP = "static";
    static DEFAULT_HTML_EDITOR_MODE = "Medium";
    static CK_EDITOR_BASE_PATH = "/lib/ckeditor/";
    static CK_EDITOR_VERSION: "auto" | "4" | "5" = "auto";
    static CK_EDITOR_5_BUNDLE = "classic";
    static CK_EDITOR_5_USE_CDN = false;
    static CK_EDITOR_5_CONFIG = { language: "en", placeholder: "" };
}
