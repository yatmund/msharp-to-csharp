// Static + fixture server. /olive = real dist, /platform = replacement, /lib = third party, anything missing in /lib is an empty AMD stub.
const http = require('http'), fs = require('fs'), path = require('path'), url = require('url');
const roots = { '/olive/': process.env.OLIVE_DIST + '/', '/platform/': __dirname + '/build/', '/lib/': process.env.LIBS + '/', '/fx/': __dirname + '/fixtures/' };
const types = { '.js': 'text/javascript', '.html': 'text/html', '.css': 'text/css', '.json': 'application/json' };
const log = [];
const routes = require('./routes.js');
http.createServer((req, res) => {
  const u = url.parse(req.url, true); let body = [];
  req.on('data', d => body.push(d)); req.on('end', () => {
    body = Buffer.concat(body).toString();
    if (u.pathname === '/__log') { res.end(JSON.stringify(log.splice(0))); return; }
    if (routes(req, res, u, body, log)) return;
    for (const [p, dir] of Object.entries(roots)) if (u.pathname.startsWith(p)) {
      const f = path.join(dir, decodeURIComponent(u.pathname.slice(p.length)));
      if (fs.existsSync(f) && fs.statSync(f).isFile()) { res.writeHead(200, { 'content-type': types[path.extname(f)] || 'application/octet-stream' }); res.end(fs.readFileSync(f)); return; }
      if (p === '/lib/') { res.writeHead(200, { 'content-type': 'text/javascript' }); res.end('console.log("STUB ' + u.pathname + '");define({});'); return; }
    }
    res.writeHead(404); res.end('nope');
  });
}).listen(+process.env.PORT || 8123);
