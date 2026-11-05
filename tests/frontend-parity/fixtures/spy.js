window.SEEN = {}; const note = (k, v) => { if (typeof v === 'string' && v.length < 400) (SEEN[k] = SEEN[k] || {})[v] = 1; };
window.SPY = $ => { const fn = $.fn, init = fn.init;
  fn.init = function (s, c, r) { if (typeof s === 'string' && s[0] !== '<') note('sel', s); return new init(s, c, r); }; fn.init.prototype = init.prototype;
  for (const m of ['find', 'filter', 'closest', 'is', 'not', 'parents', 'parent', 'children', 'has', 'next', 'prev', 'siblings']) { const o = fn[m]; fn[m] = function (a) { note('sel', a); return o.apply(this, arguments); }; }
  for (const m of ['attr', 'data', 'prop', 'removeAttr', 'hasClass', 'addClass', 'removeClass', 'toggleClass']) { const o = fn[m]; fn[m] = function (a) { note(m, a); return o.apply(this, arguments); }; }
  for (const m of ['on', 'off', 'one']) { const o = fn[m]; fn[m] = function (e, s) { note('event', e + (typeof s === 'string' ? ' @ ' + s : '')); return o.apply(this, arguments); }; }
  for (const m of ['ajax']) { const o = $[m]; $[m] = function (a) { note('ajax', JSON.stringify(a && a.url)); return o.apply(this, arguments); }; }
  for (const m of ['append', 'prepend', 'appendTo', 'prependTo', 'after', 'before', 'insertAfter', 'insertBefore', 'html', 'wrap']) { const o = fn[m]; fn[m] = function (a) { const t = this[0] && this[0].outerHTML ? this[0].outerHTML.slice(0, 120) : ''; note('dom', m + ' on ' + t + ' <- ' + (typeof a === 'string' ? a.slice(0, 120) : a && a.jquery && a[0] && a[0].outerHTML ? a[0].outerHTML.slice(0, 120) : '')); return o.apply(this, arguments); }; }
  const e = Element.prototype, ga = e.getAttribute; e.getAttribute = function (a) { note('attr', a); return ga.call(this, a); };
  const qs = Document.prototype.querySelectorAll; Document.prototype.querySelectorAll = function (a) { note('sel', a); return qs.call(this, a); };
};
