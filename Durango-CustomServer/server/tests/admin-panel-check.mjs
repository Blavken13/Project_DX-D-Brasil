import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import crypto from 'node:crypto';
import net from 'node:net';
import dgram from 'node:dgram';
import { spawn, execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { mobileReportChecks, mobileReportRestartChecks } from './mobile-reports-check.mjs';
import { premiumAdminChecks, premiumRestartChecks } from './premium-admin-check.mjs';

// Real HTTP integration, with an isolated data directory and disposable saves.
const server = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dll = path.join(server, 'bin', process.env.DURANGO_ADMIN_TEST_CONFIGURATION || 'Debug', 'net9.0', 'DurangoServer.dll');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'durango-admin-check-'));
const data = path.join(root, 'data');
fs.cpSync(path.join(server, 'data'), data, { recursive: true });
const salt = crypto.randomBytes(24);
fs.writeFileSync(path.join(data, 'admin-auth.json'), JSON.stringify({
    username: 'admin-fixture', iterations: 100000, salt: salt.toString('base64'),
    password_hash: crypto.pbkdf2Sync('integration-test-password', salt, 100000, 32, 'sha256').toString('base64')
}));
const stateDir = path.join(root, 'AppData-nx', 'offline', 'admin-check');
fs.mkdirSync(stateDir, { recursive: true });
for (const [index,name] of ['mail-alice','mail-bob'].entries()) {
    fs.writeFileSync(path.join(stateDir, `${index+1}.player`), JSON.stringify({
        player_slot:index+1, owner_key:`mail-test-${name}`, inventory_items:[],
        player_info:{player_entity_id:name,player_name:name,player_level:10}
    }));
}
async function freePort() {
    // Windows/Hyper-V can reserve UDP ranges even when TCP is free.
    for (let attempt = 0; attempt < 100; attempt++) {
        const port = crypto.randomInt(20000, 48000);
        const listener = net.createServer();
        const udp = dgram.createSocket('udp4');
        let udpBound = false;
        try {
            await new Promise((resolve, reject) => {
                listener.once('error', reject);
                listener.listen(port, '127.0.0.1', resolve);
            });
            await new Promise((resolve, reject) => {
                udp.once('error', reject);
                udp.bind(port + 1, '0.0.0.0', () => { udpBound = true; resolve(); });
            });
            return port;
        } catch {}
        finally {
            if (listener.listening) await new Promise(resolve => listener.close(resolve));
            if (udpBound) udp.close();
        }
    }
    throw new Error('Não foi possível selecionar portas TCP/UDP livres para os testes');
}
const gatewayPort = await freePort(), gamePort = await freePort();
const origin = `http://127.0.0.1:${gatewayPort}`;
const distro = process.env.DURANGO_ADMIN_TEST_WSL;
const linuxPath = value => '/mnt/' + value[0].toLowerCase() + value.slice(2).replaceAll('\\', '/');
const start = (ports = { gatewayPort, gamePort }, runtimeDistro = distro) => {
    const args = ['--data', runtimeDistro ? linuxPath(data) : data, '--terrains', runtimeDistro ? linuxPath(path.join(data, 'terrains')) : path.join(data, 'terrains'),
        '--storage-key', 'admin-check', '--gateway-port', String(ports.gatewayPort), '--game-port', String(ports.gamePort)];
    return runtimeDistro
        ? spawn('wsl.exe', ['--distribution', runtimeDistro, '--exec', 'sh', '-c', 'echo ADMIN_TEST_PID=$$; exec "$@"', 'sh',
            'env', 'DURANGO_ADMIN_CONFIG_DIR=' + linuxPath(path.join(root, 'persistent-config')),
            linuxPath(path.join(server, 'bin', 'test-runtime-linux', 'dotnet')), linuxPath(dll), ...args])
        : spawn('dotnet', [dll, ...args], { env: { ...process.env, DURANGO_ADMIN_CONFIG_DIR: path.join(root, 'persistent-config') } });
};
let child = start();
let udpFixture;
let logs = '', session = '', checks = 0;
child.stdout.on('data', chunk => { logs = (logs + chunk).slice(-10000); });
child.stderr.on('data', chunk => { logs = (logs + chunk).slice(-10000); });
async function stop() {
    if (child.exitCode !== null) return;
    const exited = new Promise(resolve => child.once('exit', resolve));
    if (distro) {
        const pid = logs.match(/ADMIN_TEST_PID=(\d+)/)?.[1];
        assert.ok(pid, 'PID Linux da instância isolada disponível');
        execFileSync('wsl.exe', ['--distribution', distro, '--exec', 'kill', '-INT', pid]);
    } else child.kill();
    await exited;
}
const request = (route, body, headers = {}) => fetch(origin + route, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/x-www-form-urlencoded' }),
        ...(session ? { 'X-Admin-Session': session } : {}), ...headers },
    body: body === undefined ? undefined : new URLSearchParams(body), signal: AbortSignal.timeout(10000)
});
function check(condition, message) { assert.ok(condition, message); checks++; console.log('OK', message); }
async function json(route, body) { const response = await request(route, body); assert.equal(response.status, 200, route); return response.json(); }
try {
    let ready = false;
    for (let i = 0; i < 100; i++) {
        if (child.exitCode !== null) throw new Error('Servidor encerrou: ' + logs);
        try { if ((await request('/status')).ok) { ready = true; break; } } catch {}
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    check(ready, 'servidor inicializa com dados e saves isolados');
    // Let the first startup autosave finish before comparing the existing files.
    await new Promise(resolve => setTimeout(resolve, 500));
    const saveHashes = () => Object.fromEntries(fs.readdirSync(stateDir).sort().filter(name => name !== '.server.lock')
        .map(name => [name, crypto.createHash('sha256').update(fs.readFileSync(path.join(stateDir, name))).digest('hex')]));
    const startupCases = [
        [{gatewayPort, gamePort:await freePort()}, `TCP ${gatewayPort}`],
        [{gatewayPort:await freePort(), gamePort}, `TCP ${gamePort}`],
        [{gatewayPort:await freePort(), gamePort:await freePort()}, 'acesso exclusivo aos saves']
    ];
    if (!distro) {
        const udpPort = (await freePort()) + 1;
        udpFixture = dgram.createSocket('udp4');
        await new Promise((resolve, reject) => {
            udpFixture.once('error', reject);
            udpFixture.bind(udpPort, '0.0.0.0', resolve);
        });
        startupCases.push([{gatewayPort:udpPort-1, gamePort:await freePort()}, `UDP ${udpPort}`]);
    }
    if (process.env.DURANGO_ADMIN_CROSS_WSL)
        startupCases.push([{gatewayPort:await freePort(), gamePort:await freePort()}, 'acesso exclusivo aos saves',
            process.env.DURANGO_ADMIN_CROSS_WSL === 'Windows' ? null : process.env.DURANGO_ADMIN_CROSS_WSL]);
    for (const [ports, diagnostic, runtimeDistro = distro] of startupCases) {
        const before = saveHashes();
        const duplicate = start(ports, runtimeDistro);
        let output = '';
        duplicate.stdout.on('data', chunk => { output += chunk; });
        duplicate.stderr.on('data', chunk => { output += chunk; });
        const code = await new Promise((resolve, reject) => {
            const timer = setTimeout(() => {
                if (runtimeDistro) {
                    const pid = output.match(/ADMIN_TEST_PID=(\d+)/)?.[1];
                    if (pid) execFileSync('wsl.exe', ['--distribution', runtimeDistro, '--exec', 'kill', '-INT', pid]);
                } else duplicate.kill();
                reject(new Error('Instância duplicada não encerrou: ' + output));
            }, 10000);
            duplicate.once('error', error => { clearTimeout(timer); reject(error); });
            duplicate.once('close', exitCode => { clearTimeout(timer); resolve(exitCode); });
        });
        const rejectedSafely = code === 1 && output.includes(diagnostic) && output.includes('Nenhum save foi carregado ou gravado');
        if (!rejectedSafely) console.error(`STARTUP_FAILURE code=${code} runtime=${runtimeDistro || 'Windows'}\n${output}`);
        check(rejectedSafely,
            'inicialização duplicada recusada antes de carregar saves: ' + diagnostic);
        assert.deepEqual(saveHashes(), before);
        check(!output.includes('[host] storage'), 'tentativa recusada preserva os arquivos de progresso: ' + diagnostic);
        check((await request('/status')).ok, 'instância original continua respondendo após recusa: ' + diagnostic);
    }
    for (const route of ['/admin', '/admin/', '/admin/app.js', '/admin/style.css'])
        check((await request(route)).ok, 'interface disponível: ' + route);
    for (const route of ['/health', '/admin/players', '/admin/config', '/admin/catalog', '/admin/mail/status', '/admin/mail/items', '/admin/premium'])
        check((await request(route)).status === 403, 'acesso anônimo bloqueado: ' + route);
    check((await request('/admin/login', { username: 'admin-fixture', password: 'wrong' })).status === 401, 'senha incorreta rejeitada');
    check((await request('/admin/login', { username: 'admin-fixture', password: 'integration-test-password' }, { Origin: 'https://external.example' })).status === 403, 'login de outra origem bloqueado');
    const login = await json('/admin/login', { username: 'admin-fixture', password: 'integration-test-password' });
    session = login.session;
    check(session?.length === 64 && login.expires_in === 28800, 'login emite sessão com expiração');
    check((await json('/health')).players_online === 0, 'saúde autenticada');
    const reportState = await mobileReportChecks({origin,request,json,check,root});
    const players = await json('/admin/players');
    check(players.length > 0 && players.every(p => !p.online), 'saves offline visíveis');
    const premiumState = await premiumAdminChecks({request,json,check});
    const mailItems = await json('/admin/mail/items');
    check(mailItems.length >= 2400 && mailItems.some(i => i.prototype_id === 'fatigue_drug_store'), 'catálogo completo de anexos disponível');
    const shopItems = mailItems.filter(i => i.shop), skinItems = mailItems.filter(i => i.skin);
    check(shopItems.length === 357, 'catálogo identifica os 357 protótipos referenciados pela loja');
    check(skinItems.length === 166 && skinItems.some(i => i.prototype_id === 'clothes_brachio_costume'), 'catálogo identifica as 166 skins/roupas/acessórios da loja');
    check(shopItems.some(i => i.prototype_id === 'metal_set') && shopItems.some(i => i.prototype_id === 'rope')
        && !skinItems.some(i => i.prototype_id === 'metal_set') && !skinItems.some(i => i.prototype_id === 'rope'),
        'materiais de craft não são classificados como skins');
    const email = {target:'player',recipient_id:'mail-alice',subject:'Entrega de compra',message:'Seus itens estão disponíveis.',type:'purchase',items:[{name:'fatigue_drug_store',quantity:2,level:1}],vouchers:[]};
    const preview = await json('/admin/mail/preview',{json:JSON.stringify(email)});
    check(preview.recipients === 1 && preview.attachments_per_player === 2 && preview.portal_stones_per_player === 0, 'prévia valida destinatário e nomes dos anexos');
    check((await json('/admin/mail/inbox?entity_id=mail-alice')).length === 0, 'prévia não envia mensagens');
    check((await request('/admin/mail/send',{json:JSON.stringify(email),request_id:'test-email'},{Origin:'https://external.example'})).status === 403, 'envio de email de outra origem bloqueado');
    check((await request('/admin/mail/preview',{json:JSON.stringify({...email,items:[{name:'unknown-mail-item'}]})})).status === 400, 'nome de item inexistente recusado');
    check((await request('/admin/mail/preview',{json:JSON.stringify({...email,items:[],vouchers:[{voucher_id:'unknown-voucher',quantity:1}]})})).status === 400, 'voucher desconhecido recusado');
    const sent = await json('/admin/mail/send',{json:JSON.stringify(email),request_id:'test-email'});
    check(sent.sent === 1 && !sent.duplicate, 'email individual persistido para personagem offline');
    check((await json('/admin/mail/send',{json:JSON.stringify(email),request_id:'test-email'})).duplicate, 'repetição HTTP não duplica email');
    check((await request('/admin/mail/send',{json:JSON.stringify({...email,subject:'Outra compra'}),request_id:'test-email'})).status === 409, 'identificador repetido com outro conteúdo recusado');
    const voucherEmail = {...email,subject:'Pedras de Portal',message:'Presente para teste de crateras.',items:[],vouchers:[{voucher_id:'voucher_resource_induced_stone',quantity:7}]};
    const voucherPreview = await json('/admin/mail/preview',{json:JSON.stringify(voucherEmail)});
    check(voucherPreview.recipients === 1 && voucherPreview.portal_stones_per_player === 7 && voucherPreview.vouchers?.[0]?.voucher_id === 'voucher_resource_induced_stone', 'prévia aceita Pedras de Portal como voucher nativo');
    check((await json('/admin/mail/send',{json:JSON.stringify(voucherEmail),request_id:'test-portal-stones'})).sent === 1, 'email com Pedras de Portal persistido');
    const aliceWithVoucher = await json('/admin/mail/inbox?entity_id=mail-alice');
    check(aliceWithVoucher.some(m => m.AttachedVouchers?.some(v => v.VoucherId === 'voucher_resource_induced_stone' && v.Count === 7)), 'caixa administrativa expõe o voucher anexado');
    const collective = {...email,target:'all',items:[],vouchers:[]};delete collective.recipient_id;
    check((await json('/admin/mail/send',{json:JSON.stringify(collective),request_id:'test-all-email'})).sent === 2, 'envio coletivo cobre personagens elegíveis existentes');
    check((await json('/admin/mail/inbox?entity_id=mail-bob')).length === 1, 'mensagem coletiva entregue ao segundo personagem');
    const inventory = await json('/admin/catalog');
    check(inventory.files.length >= 78 && inventory.terrains.length > 0, 'inventário real de tabelas e terrenos');
    check(!inventory.files.some(f => f.path.includes('admin-auth')), 'hash de senha excluído do inventário');
    check((await request('/admin/data?path=admin-auth.json')).status === 404, 'credenciais não podem ser consultadas');
    check((await request('/admin/data?path=../admin-auth.json')).status === 404, 'travessia de diretório recusada');
    check((await json('/admin/data?path=assets%2Fconstants.json')) != null, 'consulta de tabela real');
    check((await request('/admin/config', { json: '{invalid' })).status === 400, 'configuração malformada recusada');
    const config = await json('/admin/config');
    const damageBefore = config.Animals.DamageBase;
    config.Animals.DamageBase = damageBefore + 1;
    check((await json('/admin/config', { json: JSON.stringify(config) })).saved, 'configuração salva em ambiente isolado');
    check(JSON.parse(fs.readFileSync(path.join(root, 'persistent-config', 'config.json'))).Animals.DamageBase === damageBefore + 1, 'alterações copiadas para armazenamento persistente');
    const islands = await json('/admin/islands');
    check((await json('/admin/islands', { json: JSON.stringify(islands) })).saved, 'catálogo de ilhas salvo');
    const island = await json('/admin/island/config?id=isle01');
    check((await json('/admin/island/config', { id: 'isle01', json: JSON.stringify(island) })).saved, 'configuração por ilha salva');
    check((await request('/admin/island/config?id=C%3A')).status === 400, 'ID de ilha inválido recusado');
    const result = await json('/admin/economy/set', { entity_id: players[0].entity_id, currency: 'warpgem', amount: '123' });
    check(result.after === 123 && (await json('/admin/economy')).total_warp_gem >= 123, 'saldo atualizado e consultado');
    check((await request('/admin/maintenance', { on: '1' }, { Origin: 'https://external.example' })).status === 403, 'operações de outra origem recusadas');
    check((await json('/admin/maintenance', { on: '1' })).maintenance, 'manutenção ativada');
    check(!(await json('/admin/maintenance', { on: '0' })).maintenance, 'servidor reaberto');
    check((await json('/admin/announce', { text: 'Teste administrativo' })).sent === 0, 'anúncio executado');
    check((await json('/admin/reload', {})).reloaded, 'recarga de configuração executada');
    premiumState.warp_gem = (await json('/admin/players')).find(p => p.entity_id === premiumState.entity_id).warp_gem;
    check((await json('/admin/logout', {})).logged_out, 'logout executado');
    check((await request('/health')).status === 403, 'sessão revogada após logout');
    session = '';
    for (let i = 0; i < 3; i++) await request('/admin/login', { username: 'admin-fixture', password: 'wrong' });
    check((await request('/admin/login', { username: 'admin-fixture', password: 'wrong' })).status === 429, 'tentativas de login limitadas');
    await stop(); logs = '';
    config.Animals.DamageBase = damageBefore;
    fs.writeFileSync(path.join(data, 'config.json'), JSON.stringify(config));
    child = start();
    child.stdout.on('data', chunk => { logs = (logs + chunk).slice(-10000); });
    child.stderr.on('data', chunk => { logs = (logs + chunk).slice(-10000); });
    let restored = false;
    for (let i = 0; i < 100; i++) {
        if (child.exitCode !== null) throw new Error('Reinício falhou: ' + logs);
        try { if ((await request('/status')).ok) { restored = true; break; } } catch {}
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    check(restored, 'servidor reinicia com configuração persistente');
    session = (await json('/admin/login', { username: 'admin-fixture', password: 'integration-test-password' })).session;
    check((await json('/admin/config')).Animals.DamageBase === damageBefore + 1, 'alteração administrativa sobrevive ao reinício com dados substituídos');
    await mobileReportRestartChecks({request,json,check,state:reportState});
    await premiumRestartChecks({json,check,state:premiumState});
    const mailbox = await json('/admin/mail/inbox?entity_id=mail-alice');
    check(mailbox.length === 3 && mailbox.some(m => m.AttachedItems?.length === 2)
        && mailbox.some(m => m.AttachedVouchers?.some(v => v.VoucherId === 'voucher_resource_induced_stone' && v.Count === 7)),
        'emails, itens e Pedras de Portal preservados após reinício');
    console.log(`PASS ${checks} verificações HTTP`);
} catch (error) { console.error(logs); throw error; }
finally {
    if (udpFixture) udpFixture.close();
    await stop();
    const resolved = fs.realpathSync(root);
    assert.ok(resolved.startsWith(fs.realpathSync(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('durango-admin-check-'));
    fs.rmSync(resolved, { recursive: true });
}
