const page = (title, main) => `<!doctype html><html><head><title>${title}</title></head><body><main>${main}</main></body></html>`;
const J = (res, o) => { res.writeHead(200, { 'content-type': 'application/json' }); res.end(JSON.stringify(o)); return true; };
const H = (res, h, code) => { res.writeHead(code || 200, { 'content-type': 'text/html' }); res.end(h); return true; };
const actions = {
  notify: [{ Notify: 'Saved!', Obstruct: false, Style: 'success' }],
  'notify-ob': [{ Notify: 'Blocked', Obstruct: true, Style: 'error' }],
  redirect: [{ Redirect: '/r/page2', WithAjax: true }],
  'redirect-full': [{ Redirect: '/fx/other.html', WithAjax: false }],
  'redirect-modal': [{ Redirect: '/r/modal', Target: 'modal' }],
  replace: [{ ReplaceView: '<main><b id=rv>replaced</b></main>' }],
  script: [{ Script: 'window.RAN=(window.RAN||0)+1', Stage: 'Init' }],
  source: [{ ReplaceSource: 'Pick', Items: [{ Value: '1', Text: 'A', Selected: false }, { Value: '2', Text: 'B', Selected: true }] }],
  close: [{ BrowserAction: 'CloseModal' }], refresh: [{ BrowserAction: 'Refresh' }], back: [{ BrowserAction: 'Back' }], wait: [{ BrowserAction: 'ShowPleaseWait' }],
  many: [{ Notify: 'one', Style: 'info' }, { Script: 'window.RAN=7', Stage: 'Init' }, { Redirect: '/r/page2', WithAjax: true }],
  empty: [], unknown: [{ Foo: 1 }],
  'close-refresh': [{ BrowserAction: 'CloseModalRefreshParent' }], 'close-rebind': [{ BrowserAction: 'CloseModalRebindParent' }], print: [{ BrowserAction: 'Print' }],
  service: [{ ServiceKey: 'probe', Function: 'hello', Arguments: [1, 'a'] }], download: [{ Download: '/r/frag' }], preinit: [{ Script: 'window.RAN=11', Stage: 'PreInit' }],
  subform: [{ SubForm: 'Items', Html: '<tr class=subform-item><td>new</td></tr>' }], 'target-self': [{ Redirect: '/r/page2', WithAjax: false, Target: '_self' }], 'ajax-target': [{ Redirect: '/r/page2', WithAjax: true, Target: '$side' }],
};
module.exports = (req, res, u, body, log) => {
  const p = u.pathname; if (!/^\/(r|a|x)(\/|$)/.test(p)) return false;
  log.push(req.method + ' ' + req.url + (body ? ' body=' + body : '') + ' xhr=' + (req.headers['x-requested-with'] || '') + ' accept=' + (req.headers.accept || '').slice(0, 30));
  if (p === '/r/page2') return H(res, page('Page two', '<h1 id=p2>two</h1><a id=l3 data-redirect=ajax href="/r/page3">3</a>'));
  if (p === '/r/page3') return H(res, page('Page three', '<h1 id=p3>three</h1>'));
  if (p === '/r/frag') return H(res, '<div id=frag>fragment</div>');
  if (p === '/r/modal2') return H(res, page('Dlg', '<form data-module=D method=post action="/x"><input name=Inner value=i /><button id=mclose formaction="/a/close">c</button><button id=mrefresh formaction="/a/close-refresh">r</button><a id=mlink data-redirect=ajax href="/r/page3">l</a></form>'));
  if (p === '/r/modal') return H(res, page('Dialog', '<div id=dlg>in modal</div>'));
  if (p === '/r/rich') return H(res, page('R', '<form data-module=M2><input type=checkbox name=C /><select name=S><option>1</option></select><input name=D data-control=date-picker /><a data-redirect=ajax href="/r/page3">n</a></form>'));
  if (p === '/r/startup') return H(res, page('S', '<input type=hidden name="Startup.Actions" value=\'[{&quot;Notify&quot;:&quot;hey&quot;,&quot;Obstruct&quot;:false,&quot;Style&quot;:&quot;info&quot;}]\' />'));
  if (p === '/r/500') return H(res, 'boom', 500);
  if (p === '/r/401') return H(res, 'no', 401);
  if (p === '/a/module') return H(res, '<div data-module="M2" id=m2>new module</div>');
  if (p === '/a/row') return H(res, '<tr class=subform-item><td><input type=hidden name="Items[2].MustBeDeleted" value=False /><a data-delete-subform="Items" href="#">x</a></td></tr>');
  if (p === '/a/text') return H(res, 'plain text');
  if (p === '/x/gs') return J(res, [{ Title: 'Apple pie', Description: 'A sweet pie', IconUrl: '/i.png', Url: '/r/page2', GroupTitle: 'Food', Action: 0 }, { Title: 'Pear', Description: '', IconUrl: 'fas fa-pear', Url: '/r/page3', GroupTitle: 'Food', Action: 1 }, { Title: 'Pen', Description: 'Blue pen', Url: '/r/frag', GroupTitle: 'Other', Action: 2 }]);
  if (p === '/x/gs2') return J(res, []);
  if (p === '/a/html') return H(res, '<main><i id=fromhtml>html response</i></main>');
  const name = p.slice(3); if (p.startsWith('/a/') && actions[name]) return J(res, actions[name]);
  return H(res, 'unrouted');
};
