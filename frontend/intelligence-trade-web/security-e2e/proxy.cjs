// Disposable HTTPS terminator and cookie-observation endpoint for this test only.
// Cookie values never leave the request and are never logged or returned.
const fs = require('node:fs');
const http = require('node:http');
const https = require('node:https');

const tls = {
  key: fs.readFileSync(`${process.env.SECURITY_CERT_DIR}/key.pem`),
  cert: fs.readFileSync(`${process.env.SECURITY_CERT_DIR}/cert.pem`),
};
const observations = new Map();
let sessionAttributes;
const upstreamHost = process.env.SECURITY_UPSTREAM_HOST || '127.0.0.1';

function proxy(req, res, port, secure) {
  const upstream = (secure ? https : http).request({
    hostname: upstreamHost, port, method: req.method, path: req.url,
    headers: req.headers,
    // Only the fixture's loopback Identity endpoint has a self-signed cert.
    ...(secure ? { rejectUnauthorized: false } : {}),
  }, (response) => {
    const cookies = response.headers['set-cookie'] || [];
    for (const cookie of cookies) {
      if (cookie.startsWith('TradeSystem.Bff.Session=')) {
        sessionAttributes = {
          secure: /(?:^|;)\s*secure(?:;|$)/i.test(cookie),
          httpOnly: /(?:^|;)\s*httponly(?:;|$)/i.test(cookie),
          sameSiteLax: /(?:^|;)\s*samesite=lax(?:;|$)/i.test(cookie),
        };
      }
    }
    // Identity currently constructs its redirect Location using HTTP even when
    // Kestrel is reached over HTTPS; keep this correction inside the test proxy.
    if (secure && response.headers.location) {
      response.headers.location = response.headers.location.replace(
        /^http:\/\/localtest\.me:18995(?=\/|$)/,
        'https://localtest.me:18995',
      );
    }
    res.writeHead(response.statusCode, response.headers);
    response.pipe(res);
  });
  upstream.on('error', () => { res.writeHead(502); res.end(); });
  req.pipe(upstream);
}

https.createServer(tls, (req, res) => {
  const url = new URL(req.url, 'https://localtest.me:18994');
  if (url.pathname === '/__security/control') {
    res.setHeader('Set-Cookie', 'Issue225Control=1; Path=/; SameSite=None; Secure; HttpOnly');
    return res.end('ok');
  }
  if (url.pathname === '/__security/observe') {
    const names = new Set((req.headers.cookie || '').split(';').map((part) => part.trim().split('=')[0]));
    observations.set(url.searchParams.get('id'), {
      session: names.has('TradeSystem.Bff.Session'),
      control: names.has('Issue225Control'),
    });
    return res.end('ok');
  }
  if (url.pathname === '/__security/results') {
    res.setHeader('Content-Type', 'application/json');
    return res.end(JSON.stringify({ observation: observations.get(url.searchParams.get('id')), sessionAttributes }));
  }
  if (url.pathname === '/__security/attacker') {
    res.setHeader('Content-Type', 'text/html');
    return res.end('<!doctype html><title>Cross-site test</title>');
  }
  proxy(req, res, 18082, false);
}).listen(18994, '127.0.0.1');

https.createServer(tls, (req, res) => proxy(req, res, 18443, true))
  .listen(18995, '127.0.0.1');
