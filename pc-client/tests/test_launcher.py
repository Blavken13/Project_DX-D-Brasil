"""Exercise the real Framework authentication code against an isolated HTTP server."""
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import subprocess
import threading
from urllib.parse import parse_qs

ROOT = Path(__file__).resolve().parents[1]
TOKEN = 'A' * 43


class Gateway(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, status, payload):
        body = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        self.reply(200, {'ok': True, 'online': 3})

    def do_POST(self):
        fields = parse_qs(self.rfile.read(int(self.headers['Content-Length'])).decode())
        if self.path == '/accounts':
            if fields.get('token') != [TOKEN]:
                self.reply(401, {'error': 'unauthorized'})
            else:
                self.reply(200, {'players': [], 'id': 'test-account'})
            return
        username = fields.get('username', [''])[0]
        if self.path == '/auth/register':
            if username == 'existing':
                self.reply(409, {'ok': False, 'error': 'username_taken'})
            else:
                assert fields.get('password_confirm') == fields.get('password')
                self.reply(200, {'ok': True})
            return
        if fields.get('password') != ['test&password+123']:
            self.reply(401, {'ok': False, 'error': 'invalid_credentials'})
        else:
            self.reply(200, {'ok': True, 'account_id': 'test-account', 'username': username,
                             'auth_token': 'invalid' if username == 'invalidtoken' else TOKEN,
                             'expires_in': 691200 if username == 'invalidttl' else 3600})


def main():
    server = ThreadingHTTPServer(('127.0.0.1', 0), Gateway)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    folder = ROOT / 'work/auth-tests'
    folder.mkdir(parents=True, exist_ok=True)
    try:
        subprocess.run(['C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe',
                        '-NoProfile', '-File', str(ROOT / 'tests/test-auth.ps1'),
                        '-Gateway', f'http://127.0.0.1:{server.server_port}',
                        '-TestFolder', str(folder)], check=True)
    finally:
        server.shutdown()
        server.server_close()


if __name__ == '__main__':
    main()
