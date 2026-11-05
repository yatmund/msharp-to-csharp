// side comes from the page: ?side=olive|platform
window.SIDE = new URLSearchParams(location.search).get('side') || 'olive';
requirejs.config({
  baseUrl: '/lib', paths: {
    jquery: 'jquery/dist/jquery', 'jquery-validate': 'jquery-validation/dist/jquery.validate',
    'jquery-validate-unobtrusive': 'jquery-validation-unobtrusive/jquery.validate.unobtrusive',
    popper: 'popper.js/dist/umd/popper', bootstrap: 'bootstrap/dist/js/bootstrap',
    moment: 'moment/min/moment.min', 'moment-locale': 'moment/locale/en-gb', olive: '/' + SIDE,
  },
  map: { '*': { 'popper.js': 'popper', '../moment': 'moment' } },
  shim: { bootstrap: ['jquery', 'popper'], 'jquery-validate': ['jquery'], 'jquery-validate-unobtrusive': ['jquery-validate'], 'moment-locale': ['moment'],
    'olive/extensions/jQueryExtensions': { deps: ['jquery', 'jquery-validate-unobtrusive'], exports: '_' } },
});
requirejs.onError = e => console.log('REQERR ' + e.requireType + ' ' + e.requireModules + ' ' + e.message + ' ' + (e.stack || '').split(String.fromCharCode(10)).slice(1, 5).join(' | '));
requirejs(['jquery'], $ => { window.$ = window.jQuery = $; if (window.SPY) SPY($); PLUGINS($);
  requirejs(['bootstrap', 'jquery-validate', 'jquery-validate-unobtrusive', 'alertify', 'pako/dist/pako', 'moment'], (b, v, u, alertify, pk, mo) => { window.alertify = alertify; window.moment = mo; PLUGINS($);
    requirejs([SIDE === 'olive' ? 'olive/olivePage' : 'olive/platformPage'], m => { window.page = new m.default(); window.PAGE_READY = true; }); }); });
