export default class LiteEvent<T> {
    private handlers: { (data?: T): void; }[] = [];

    handle(handler: { (data?: T): void }) { this.handlers.push(handler); }

    remove(handler: { (data?: T): void }) { this.handlers = this.handlers.filter(h => h !== handler); }

    raise(data?: T) { this.handlers.slice(0).forEach(h => h(data)); }
}
