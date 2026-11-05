export type CrossDomainEventCommands = "refresh-page" | "set-iframe-height" | "close-modal" | "file-uploaded" | string;

export default class CrossDomainEvent {
    static handle(command: CrossDomainEventCommands, handler: ((arg: any) => void)) {
        window.addEventListener("message", (e: MessageEvent) => {
            try {
                const data = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
                if (data && data.command === command) handler(data.arg);
            } catch (ignored) { }
        });
    }

    static raise(target: Window, command: CrossDomainEventCommands, arg?: any) {
        target.postMessage(JSON.stringify({ command, arg }), "*");
    }
}
