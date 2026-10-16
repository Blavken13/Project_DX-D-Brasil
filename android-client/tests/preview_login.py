"""Local preview of APK assets, with a fake Java bridge only on /test-login.html."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import urllib.parse

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'android-client/work/original-nexon/client/assets'
BRIDGE = r"""
<script>
(() => {
  const count = (key) => { document.body.dataset[key] = String(+(document.body.dataset[key] || 0) + 1); };
  const authenticate = (username, register) => {
    count(register ? 'registerCalls' : 'loginCalls');
    document.body.dataset.username = username;
    PRIMAL.busy(true);
    setTimeout(() => {
      PRIMAL.busy(false);
      if (username === 'invalid_user') PRIMAL.notice('Usuário ou senha incorretos.', false);
      else { PRIMAL.account(username); PRIMAL.notice('', true); }
    }, 200);
  };
  window.PrimalAndroid = {
    ready() { PRIMAL.account(null); PRIMAL.status(0); PRIMAL.version('Durango Brasil · Teste local'); },
    signIn(username) { authenticate(username, false); },
    signUp(username) { authenticate(username, true); },
    signOut() { count('logoutCalls'); PRIMAL.account(null); },
    play() { count('playCalls'); PRIMAL.notice('Abrindo seleção de personagem…', true); },
    update() {}
  };
})();
</script>
"""


class Handler(SimpleHTTPRequestHandler):
    def do_GET(self):
        if urllib.parse.urlsplit(self.path).path == '/durango-br/launcher/web/test-login.html':
            html = (ASSETS / 'durango-br/launcher/web/index.html').read_text('utf-8')
            payload = html.replace('<script src="mobile.js"></script>', BRIDGE + '<script src="mobile.js"></script>').encode('utf-8')
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.send_header('Content-Length', str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)
        else:
            super().do_GET()


if __name__ == '__main__':
    server = ThreadingHTTPServer(('127.0.0.1', 19880), partial(Handler, directory=str(ASSETS)))
    print('Prévia: http://127.0.0.1:19880/durango-br/launcher/web/index.html', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.server_close()
