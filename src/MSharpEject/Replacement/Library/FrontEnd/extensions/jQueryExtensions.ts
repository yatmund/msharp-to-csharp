import "jquery";

const enableValidateForTimePicker = () => {
    $.validator.addMethod("time", function (value: string, element: any) {
        return this.optional(element) || /^([01]?\d|2[0-3]):[0-5]\d$/.test(value);
    });
};

const enableValidateForCheckboxList = () => {
    $.validator.unobtrusive.adapters.add("selection-required", [], (options: any) => {
        options.rules["required"] = true;
        if (options.message) options.messages["required"] = options.message;
    });
};

const raiseEvent = (event: string, owner: any, data?: any): boolean => {
    const handler = owner["on" + event];
    return !handler || handler.call(owner, data) !== false;
};

function screenOffset(this: any) {
    const offset = this.offset();
    return { top: offset.top - $(window).scrollTop(), left: offset.left - $(window).scrollLeft() };
}

function bindFirst(this: any, name: any, fn: any) {
    this.on(name, fn);
    return this.each(function () {
        const handlers = $._data(this, "events")[name.split(".")[0]];
        handlers.splice(0, 0, handlers.pop());
    });
}

function getUniqueSelector(this: any) {
    const path: string[] = [];
    let node = this[0];
    while (node && node.nodeType === 1) {
        if (node.id) { path.unshift("#" + node.id); break; }
        const parent = node.parentNode;
        const index = parent ? Array.prototype.indexOf.call(parent.children, node) + 1 : 1;
        path.unshift(`${node.tagName.toLowerCase()}:nth-child(${index})`);
        node = parent;
    }
    return path.join(" > ");
}

$.fn.extend({ screenOffset, bindFirst, getUniqueSelector, raiseEvent(event: string, data?: any) { return raiseEvent(event, this, data); } });

export { enableValidateForTimePicker, enableValidateForCheckboxList, raiseEvent, screenOffset, bindFirst, getUniqueSelector };
