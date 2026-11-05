(function () {
// Recording stand-ins for third-party UI plugins, so calls can be compared without shipping them.
window.CALLS = [];
const rec = (n, a) => CALLS.push(n + '(' + a.map(x => { try { return JSON.stringify(x, (k, v) => typeof v === 'function' ? 'fn' : v instanceof Element ? '<' + v.tagName + '>' : (v && v.jquery) ? '$' : v); } catch (e) { return '?'; } }).join(',') + ')');
define('alertify', [], () => { const a = function () { return a; };
  for (const m of ['log', 'success', 'error', 'warning', 'message', 'notify']) a[m] = (...x) => { rec('alertify.' + m, x); return a; };
  a.alert = (t, cb) => { rec('alertify.alert', [t]); if (typeof cb === 'function') cb(); return a; };
  a.confirm = (t, ok, c) => { rec('alertify.confirm', [t]); const f = window.CONFIRM_NO ? c : ok; if (f) f(!window.CONFIRM_NO); return a; };
  a.set = (...x) => { rec('alertify.set', x); return a; }; a.defaults = { glossary: {}, notifier: {}, theme: {} }; a.delay = 0; a.labels = {}; return a; });
window.PLUGINS = $ => { for (const n of ['datetimepicker', 'typeahead', 'fileupload', 'filestyle', 'bootstrapSlider', 'slider', 'selectpicker', 'combodate', 'spinedit', 'pwstrength', 'smartmenus', 'sortable', 'tooltip', 'popover'])
  $.fn[n] = function (...a) { (window.LAST = window.LAST || {})[n] = a; rec('$.' + n + '[' + this.length + ']', a); if (n === 'datetimepicker') this.data('DateTimePicker', new Proxy({}, { get: (t, k) => (...x) => { rec('dtp.' + String(k), x); return {}; } })); return this; };
  $.fn.selectpicker.Constructor = {}; $.expr.pseudos.focusable = e => /input|select|textarea|button|a/i.test(e.nodeName); window.moment = window.moment || (v => ({ isValid: () => true, v })); };
define('pako/dist/pako', [], () => ({ gzip: s => new TextEncoder().encode(s), ungzip: (b, o) => o && o.to === 'string' ? new TextDecoder().decode(b) : b }));
window.CKEDITOR = { replace: (...x) => { rec('CKEDITOR.replace', x); return { on: () => {}, getData: () => '' }; }, config: {} };

})();
