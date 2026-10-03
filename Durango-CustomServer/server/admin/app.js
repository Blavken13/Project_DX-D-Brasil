(() => {
'use strict';
const $ = id => document.getElementById(id);
const fmt = n => Number(n ?? 0).toLocaleString('pt-BR');
const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let session = sessionStorage.getItem('durango-admin-session') || '';
let page = 'overview', players = [], catalog = null, dataFile = null, islandId = null, timer = null, refreshing = false;
let mailItems = null, mailPreview = null;
const newMailId = () => Array.from(crypto.getRandomValues(new Uint8Array(16)),n=>n.toString(16).padStart(2,'0')).join('');
let mailRequestId = sessionStorage.getItem('durango-mail-request-id') || newMailId();
const mailExample = {target:'player',recipient_id:'',subject:'Sua entrega chegou',message:'Obrigado! Resgate os anexos desta mensagem para recebê-los na mochila.',type:'purchase',items:[]};
$('mail-json').value = sessionStorage.getItem('durango-mail-draft') || JSON.stringify(mailExample,null,2);
const titles = {overview:'Visão geral',players:'Jogadores',economy:'Economia',config:'Configurações',islands:'Ilhas',data:'Dados do jogo',actions:'Operações'};
function notice(message, error = false) { $('notice').hidden = false; $('notice').textContent = message; $('notice').className = error ? 'error' : ''; }
function endSession() {
 session = ''; sessionStorage.removeItem('durango-admin-session'); clearInterval(timer); timer = null;
 $('app').hidden = true; $('login').hidden = false; $('password').value = ''; players = []; catalog = null;
}
async function api(path, body) {
 const response = await fetch(path, {method:body === undefined ? 'GET':'POST',cache:'no-store',headers:{'X-Admin-Session':session,...(body === undefined ? {} : {'Content-Type':'application/x-www-form-urlencoded'})},body:body === undefined ? undefined:new URLSearchParams(body)});
 const text = await response.text(); let result; try { result = JSON.parse(text); } catch { result = {}; }
 if (!response.ok) {
  if (session && (response.status === 401 || response.status === 403)) {endSession();$('login-error').textContent = 'Sessão expirada. Entre novamente.';}
  throw new Error(result.error || `O servidor respondeu HTTP ${response.status}.`);
 }
 if (result.error) throw new Error(result.error);
 return result;
}
function metric(label, value) { return `<div class="metric"><span>${esc(label)}</span><strong>${esc(value)}</strong></div>`; }
function table(headers, rows) { return rows.length ? `<table><thead><tr>${headers.map(h=>`<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows.map(row=>`<tr>${row.map(cell=>`<td>${cell}</td>`).join('')}</tr>`).join('')}</tbody></table>` : '<p class="muted">Nenhum registro encontrado.</p>'; }
function status(online) { return `<span class="tag ${online?'':'offline'}">${online?'Online':'Offline'}</span>`; }
async function startSession() {
 await api('/health'); $('login').hidden = true; $('app').hidden = false; $('password').value = '';
 clearInterval(timer); timer = setInterval(()=>{if(session && !document.hidden && ['overview','players','economy','actions'].includes(page)) refresh();},5000);
 await refresh();
}
$('login-form').addEventListener('submit', async event => {
 event.preventDefault(); $('login-error').textContent = ''; $('login-button').disabled = true;
 try {const result = await api('/admin/login',{username:$('username').value.trim(),password:$('password').value}); session = result.session;sessionStorage.setItem('durango-admin-session',session);await startSession();}
 catch(error) {$('login-error').textContent = error.message;} finally {$('login-button').disabled = false;}
});
$('logout').addEventListener('click',async()=>{try{await api('/admin/logout',{});}catch{}finally{endSession();}});
document.querySelectorAll('nav button').forEach(button=>button.addEventListener('click',()=>{
 page = button.dataset.page; document.querySelectorAll('.page').forEach(el=>el.hidden=el.id!==page);
 document.querySelectorAll('nav button').forEach(el=>el.classList.toggle('active',el===button));$('page-title').textContent=titles[page];$('notice').hidden=true;refresh();
}));
$('refresh').addEventListener('click',()=>{if(['config','islands'].includes(page)&&!confirm('Atualizar substituirá os campos por dados salvos no servidor. Continuar?'))return;refresh();});
async function refresh() {
 if (!session || refreshing) return; refreshing = true; $('refresh').disabled = true;
 try {
  const target = page;
  if(target==='overview')await overview();
  if(target==='players')await loadPlayers();
  if(target==='economy')await economy();
  if(target==='config')await config();
  if(target==='islands')await islands();
  if(target==='data')await loadCatalog();
  if(target==='actions'){catalog=await api('/admin/catalog');maintenance(catalog.maintenance);await loadMailTools();}
  $('connection').textContent='● Servidor conectado';$('updated').textContent='Atualizado em '+new Date().toLocaleString('pt-BR');
 }catch(error){$('connection').textContent='● Falha na conexão';notice(error.message,true);}
 finally{refreshing=false;$('refresh').disabled=false;}
}
async function overview() {
 const [health,who,inventory]=await Promise.all([api('/health'),api('/admin/who'),api('/admin/catalog')]);
 $('metrics').innerHTML=metric('Jogadores online',`${fmt(health.players_online)} / ${health.max_players>0?fmt(health.max_players):'sem limite'}`)+metric('Tempo ativo',`${Math.floor(health.uptime_sec/3600)}h ${Math.floor(health.uptime_sec/60)%60}min`)+metric('Mundos carregados',fmt(health.worlds_loaded))+metric('Tabelas e configurações',fmt(inventory.files.length));
 const states=[['Tempo por tick (p50)',fmt(health.tick_ms?.p50)+' ms'],['Erros no loop',fmt(health.loop_errors)],['Falhas de salvamento',fmt(health.save_failures)],['Último salvamento',health.last_save_ago_sec>=0?fmt(Math.round(health.last_save_ago_sec))+' s atrás':'Aguardando primeiro salvamento'],['Manutenção',inventory.maintenance?'Ativa':'Desativada'],['Terrenos disponíveis',fmt(inventory.terrains.length)],['Catálogo Android',inventory.android_catalog_available?'Disponível (pode ser parcial)':'Ausente']];
 $('server-state').innerHTML=states.map(([key,value])=>`<div><dt>${esc(key)}</dt><dd>${esc(value)}</dd></div>`).join('');
 $('packets').innerHTML=table(['Tipo','Quantidade'],Object.entries(health.unhandled_packet_types||{}).map(([key,value])=>[esc(key),fmt(value)]));
 $('online').innerHTML=table(['Nome','ID','Região'],who.map(p=>[esc(p.name),esc(p.entity_id),esc(p.region)]));maintenance(inventory.maintenance);
}
async function loadPlayers(){const [rows,bans]=await Promise.all([api('/admin/players'),api('/admin/bans')]);players=rows;renderPlayers();$('bans').innerHTML=table(['Conta (resumo)','Motivo','Data'],bans.map(b=>[esc(b.key),esc(b.reason),esc(new Date(b.at*1000).toLocaleString('pt-BR'))]));}
function renderPlayers(){const search=$('player-search').value.toLowerCase();$('player-table').innerHTML=table(['Personagem','Nível','Estado','Região','T Stone','Warp Gem','Durango Coin','Ações'],players.filter(p=>`${p.name} ${p.entity_id}`.toLowerCase().includes(search)).map(p=>[`${esc(p.name||'Sem nome')}<br><small class="muted">${esc(p.entity_id)}</small>`,fmt(p.level),p.banned?'<span class="tag offline">Banido</span>':status(p.online),esc(p.region||'—'),fmt(p.t_stone),fmt(p.warp_gem),fmt(p.durango_coin),`<button data-action="wallet" data-id="${esc(p.entity_id)}">Saldo</button>${p.online?`<button data-action="kick" data-id="${esc(p.entity_id)}">Desconectar</button>`:''}<button class="danger" data-action="${p.banned?'unban':'ban'}" data-id="${esc(p.entity_id)}">${p.banned?'Desbanir':'Banir'}</button>`]));}
$('player-search').addEventListener('input',renderPlayers);
$('player-table').addEventListener('click',async event=>{
 const button=event.target.closest('button[data-action]');if(!button)return;
 const entity_id=button.dataset.id,action=button.dataset.action;let body={entity_id},path='/admin/'+action;
 if(action==='wallet'){
  const currency=prompt('Moeda: tstone, warpgem ou durangocoin','tstone');if(currency===null)return;
  if(!['tstone','warpgem','durangocoin'].includes(currency)){notice('Moeda inválida.',true);return;}
  const amount=prompt('Novo saldo inteiro (0 a 99999999)');if(amount===null)return;
  if(!/^\d+$/.test(amount)||Number(amount)>99999999){notice('Saldo inválido.',true);return;}
  if(!confirm(`Definir ${currency} do personagem ${entity_id} para ${fmt(amount)}?`))return;
  body={entity_id,currency,amount};path='/admin/economy/set';
 }else{if(!confirm(`${action==='ban'?'Banir a conta inteira de':action==='unban'?'Remover o banimento de':'Desconectar'} ${entity_id}?`))return;if(action!=='unban'){const reason=prompt('Motivo da ação','Ação administrativa.');if(reason===null)return;body.reason=reason;}}
 await execute(button,async()=>{const result=await api(path,body);if(result.kicked===false||result.unbanned===false)throw new Error('A ação não foi aplicada. Atualize os dados.');await loadPlayers();return 'Alteração aplicada ao personagem.';});
});
async function economy(){const data=await api('/admin/economy?top=100');$('economy-metrics').innerHTML=metric('T Stone total',fmt(data.total_tstone))+metric('Warp Gem total',fmt(data.total_warp_gem))+metric('Durango Coin total',fmt(data.total_durango_coin))+metric('Personagens',fmt(data.character_count));$('economy-table').innerHTML=table(['Nome','T Stone','Warp Gem','Durango Coin','Estado'],(data.top||[]).map(p=>[esc(p.name||p.entity_id),fmt(p.t_stone),fmt(p.warp_gem),fmt(p.durango_coin),status(p.online)]));}
async function config(){const data=await api('/admin/config');$('config-editor').value=JSON.stringify(data,null,2);$('features').innerHTML='';for(const [key,value]of Object.entries(data.Features||{})){if(typeof value!=='boolean')continue;const label=document.createElement('label'),input=document.createElement('input');input.type='checkbox';input.checked=value;input.addEventListener('change',()=>{try{const next=JSON.parse($('config-editor').value);next.Features[key]=input.checked;$('config-editor').value=JSON.stringify(next,null,2);}catch(error){input.checked=!input.checked;notice('Corrija o JSON antes de alterar as opções.',true);}});label.append(input,document.createTextNode(key));$('features').append(label);}}
async function saveJson(path,id,extra={}){const json=$(id).value;const parsed=JSON.parse(json);if(!parsed||Array.isArray(parsed)||typeof parsed!=='object')throw new Error('O JSON precisa conter um objeto.');await api(path,{...extra,json});return 'Arquivo salvo. Consulte as instruções da tela para aplicar a alteração.';}
async function islands(){const data=await api('/admin/islands');$('islands-editor').value=JSON.stringify(data,null,2);const rows=data.Islands||[];$('island-table').innerHTML=table(['ID','Nome','Terreno','Nível','Gateway','Jogo'],rows.map(p=>[esc(p.Id),esc(p.Name),esc(p.Terrain),`${fmt(p.MinLevel)}–${fmt(p.MaxLevel)}`,esc(p.Address||`${p.Host}:${p.GatewayPort}`),esc(p.GamePort)]));$('island-select').replaceChildren(...rows.map(p=>new Option(`${p.Id} · ${p.Terrain}`,p.Id)));islandId=null;$('island-editor').value='';$('island-editor').disabled=true;$('save-island').disabled=true;}
$('island-select').addEventListener('change',()=>{islandId=null;$('save-island').disabled=true;$('island-editor').disabled=true;$('island-editor').value='';});
async function loadCatalog(){catalog=await api('/admin/catalog');$('catalog-summary').textContent=`${fmt(catalog.files.length)} arquivos JSON · ${fmt(catalog.terrains.length)} terrenos. Consulta dos arquivos efetivamente disponíveis no servidor.`;filterCatalog();}
function filterCatalog(){if(!catalog)return;const previous=$('data-select').value,query=$('data-search').value.toLowerCase();$('data-select').replaceChildren(...catalog.files.filter(f=>f.path.toLowerCase().includes(query)).map(f=>new Option(`${f.path} (${fmt(Math.round(f.bytes/1024))} KB)`,f.path)));if([...$('data-select').options].some(o=>o.value===previous))$('data-select').value=previous;}
$('data-search').addEventListener('input',filterCatalog);
function maintenance(on){$('maintenance-state').textContent=on?'Manutenção ativa. Novas entradas bloqueadas.':'Servidor aberto para novas entradas.';}
async function execute(button,work){button.disabled=true;try{notice(await work());}catch(error){notice(error.message,true);}finally{button.disabled=false;}}
function on(id,work){$(id).addEventListener('click',()=>execute($(id),work));}
on('save-config',()=>saveJson('/admin/config','config-editor'));
on('save-islands',()=>saveJson('/admin/islands','islands-editor'));
on('load-island',async()=>{const id=$('island-select').value;if(!id)throw new Error('Selecione uma ilha.');const data=await api('/admin/island/config?id='+encodeURIComponent(id));islandId=id;$('island-editor').value=JSON.stringify(data,null,2);$('island-editor').disabled=false;$('save-island').disabled=false;return 'Configuração de '+id+' carregada.';});
on('save-island',()=>{if(!islandId)throw new Error('Carregue a ilha antes de salvar.');return saveJson('/admin/island/config','island-editor',{id:islandId});});
on('load-data',async()=>{const path=$('data-select').value;if(!path)throw new Error('Selecione um arquivo.');const data=await api('/admin/data?path='+encodeURIComponent(path));const text=JSON.stringify(data,null,2);dataFile={path,text};$('data-viewer').textContent=text.slice(0,300000);$('data-detail').textContent=`${path} · ${fmt(text.length)} caracteres${text.length>300000?' · Prévia limitada. Baixe o JSON para consultar o arquivo completo.':''}`;$('download-data').disabled=false;return 'Arquivo carregado.';});
$('download-data').addEventListener('click',()=>{if(!dataFile)return;const url=URL.createObjectURL(new Blob([dataFile.text],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download=dataFile.path.replace(/\//g,'_');a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
const reload=async()=>{await api('/admin/reload',{});return 'Configurações recarregadas no servidor.';};on('reload',reload);on('reload-config',reload);
function mailChanged() {
 mailPreview=null; $('mail-send').disabled=true; $('mail-preview-result').hidden=true;
 mailRequestId=newMailId();sessionStorage.setItem('durango-mail-request-id',mailRequestId);
 sessionStorage.setItem('durango-mail-draft',$('mail-json').value);$('mail-result').textContent='Valide o JSON antes de enviar.';
}
$('mail-json').addEventListener('input',mailChanged);
async function loadMailTools() {
 const state=await api('/admin/mail/status');$('mail-status').textContent=`Correio ativo · ${fmt(state.messages)} mensagens · ${fmt(state.pending)} aguardando resgate.`;
 const [items,recipients]=await Promise.all([mailItems?Promise.resolve(mailItems):api('/admin/mail/items'),api('/admin/players')]);mailItems=items;
 const previous=$('mail-recipient').value;
 $('mail-recipient').replaceChildren(...recipients.filter(p=>p.mail_eligible).map(p=>new Option(`${p.name} · ${p.entity_id}`,p.entity_id)));
 if([...$('mail-recipient').options].some(o=>o.value===previous))$('mail-recipient').value=previous;
 if(!$('mail-json').dataset.initialized){let draft={};try{draft=JSON.parse($('mail-json').value);}catch{}$('mail-target').value=draft.target==='all'?'all':'player';$('mail-recipient').disabled=draft.target==='all';if(draft.recipient_id&&[...$('mail-recipient').options].some(o=>o.value===draft.recipient_id))$('mail-recipient').value=draft.recipient_id;$('mail-json').dataset.initialized='1';}
 filterMailItems();
}
function filterMailItems(){if(!mailItems)return;const query=$('mail-item-search').value.toLowerCase();$('mail-item').replaceChildren(...mailItems.filter(i=>`${i.name} ${i.prototype_id}`.toLowerCase().includes(query)).slice(0,200).map(i=>new Option(`${i.name} · ${i.prototype_id}`,i.prototype_id)));}
$('mail-item-search').addEventListener('input',filterMailItems);
$('mail-target').addEventListener('change',()=>{$('mail-recipient').disabled=$('mail-target').value==='all';mailPreview=null;$('mail-send').disabled=true;});
$('mail-recipient').addEventListener('change',()=>{mailPreview=null;$('mail-send').disabled=true;});
on('mail-template',async()=>{const data=JSON.parse($('mail-json').value);data.target=$('mail-target').value;if(data.target==='all')delete data.recipient_id;else{if(!$('mail-recipient').value)throw new Error('Selecione um jogador.');data.recipient_id=$('mail-recipient').value;}$('mail-json').value=JSON.stringify(data,null,2);mailChanged();return 'Destinatário atualizado no JSON.';});
on('mail-add-item',async()=>{const data=JSON.parse($('mail-json').value),id=$('mail-item').value;if(!id)throw new Error('Selecione um item.');if(!Array.isArray(data.items))data.items=[];data.items.push({prototype_id:id,quantity:1,level:1});$('mail-json').value=JSON.stringify(data,null,2);mailChanged();return 'Anexo adicionado. Ajuste quantity e level no JSON, se necessário.';});
on('mail-preview',async()=>{const json=$('mail-json').value;const data=await api('/admin/mail/preview',{json});if(json!==$('mail-json').value)throw new Error('O JSON mudou durante a validação. Valide novamente.');mailPreview={json,data};$('mail-preview-result').textContent=JSON.stringify(data,null,2);$('mail-preview-result').hidden=false;$('mail-send').disabled=false;$('mail-result').textContent=`Pronto para enviar a ${fmt(data.recipients)} personagem(ns), com ${fmt(data.attachments_per_player)} anexo(s) por personagem.`;return 'JSON válido. Confira a prévia antes de enviar.';});
on('mail-send',async()=>{if(!mailPreview||mailPreview.json!==$('mail-json').value)throw new Error('Valide o JSON novamente.');const preview=mailPreview;if(!confirm(`Enviar “${preview.data.subject}” para ${fmt(preview.data.recipients)} personagem(ns), com ${fmt(preview.data.attachments_per_player)} anexo(s) para cada um?`))return 'Envio cancelado.';sessionStorage.setItem('durango-mail-request-id',mailRequestId);sessionStorage.setItem('durango-mail-draft',preview.json);const result=await api('/admin/mail/send',{json:preview.json,request_id:mailRequestId});$('mail-result').textContent=result.duplicate?'Este envio já foi registrado. Nenhuma mensagem duplicada foi criada.':`Email entregue ao correio de ${fmt(result.sent)} personagem(ns).`;await loadMailTools();return $('mail-result').textContent;});
on('mail-new',async()=>{if(!confirm('Preparar outro envio? Enviar novamente o mesmo conteúdo criará novas mensagens.'))return 'Envio atual mantido.';mailChanged();return 'Novo envio preparado. Valide o JSON.';});
on('announce',async()=>{const text=$('announcement').value.trim();if(!text)throw new Error('Escreva a mensagem.');const result=await api('/admin/announce',{text});$('announcement').value='';return `Anúncio enviado para ${fmt(result.sent)} jogadores.`;});
for(const [id,onValue]of [['maintenance-on','1'],['maintenance-off','0']])on(id,async()=>{const result=await api('/admin/maintenance',{on:onValue});maintenance(result.maintenance);return result.maintenance?'Manutenção ativada.':'Servidor aberto.';});
if(session)startSession().catch(error=>{endSession();$('login-error').textContent=error.message;});
})();
