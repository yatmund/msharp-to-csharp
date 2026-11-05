export default class Alert implements IService {
    enableAlert() {
        window.alert = (text: any) => this.alert(text);
    }

    alert(text: string, style?: string, callback?: Function) {
        alertify.alert(text, callback);
    }

    confirm(text: string, style?: string, callback?: Function) {
        alertify.confirm(text, (ok: boolean) => { if (callback) callback(ok); });
    }

    alertUnobtrusively(message: string, style?: string) {
        alertify.log(message, style);
    }
}
