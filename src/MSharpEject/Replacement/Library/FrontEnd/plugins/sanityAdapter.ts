export default class SanityAdapter implements IService {
    enable() {
        if (window.isSanityTest) this.skipNewWindows();
    }

    private skipNewWindows() {
        $("a[target=_blank]").removeAttr("target");
    }
}
