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
const mailExample = {target:'player',recipient_id:'',subject:'Sua entrega chegou',message:'Obrigado! Resgate os anexos desta mensagem para recebê-los na mochila.',type:'purchase',items:[],vouchers:[]};
$('mail-json').value = sessionStorage.getItem('durango-mail-draft') || JSON.stringify(mailExample,null,2);
const titles = {overview:'Visão geral',players:'Jogadores',economy:'Economia',config:'Configurações',islands:'Ilhas',data:'Dados do jogo',actions:'Operações'};
function notice(message, error = false) { $('notice').hidden = false; $('notice').textContent = message; $('notice').className = error ? 'error' : ''; }
titles.reports = 'Relatórios de erros';
titles.premium = 'Premium';
let reportOffset = 0;
async function loadReports() {
 const filters = new URLSearchParams({offset:reportOffset,event_type:$('report-type').value,version_name:$('report-version').value.trim(),model:$('report-model').value.trim(),signature:$('report-signature').value.trim()});
 const result = await api('/admin/client-reports?' + filters);
 $('report-metrics').innerHTML = metric('Relatórios armazenados',fmt(result.total))+metric('Resultados filtrados',fmt(result.filtered_total))+metric('Instalações nos resultados',fmt(result.installations));
 $('report-table').innerHTML = table(['Recebido','Ocorrido','Tipo','APK','Modelo / Android','Última etapa','Assinatura','Detalhes'], result.reports.map(r=>[
  esc(new Date(r.received_at).toLocaleString('pt-BR')),esc(new Date(r.occurred_at).toLocaleString('pt-BR')),esc(r.event_type),esc(r.version_name),esc(`${r.manufacturer} ${r.model} / API ${r.android_api}`),esc(r.last_stage),esc(r.signature),`<button data-report-id="${esc(r.event_id)}">Abrir</button>`]));
 $('report-groups').innerHTML = table(['Assinatura','Tipo','Ocorrências','Instalações','Modelos','Versões'],result.groups.map(g=>[esc(g.signature),esc(g.event_type),fmt(g.count),fmt(g.installations),esc(g.models.join(', ')),esc(g.versions.join(', '))]));
 $('report-pagination').textContent = result.filtered_total ? `${fmt(reportOffset+1)}–${fmt(Math.min(reportOffset+100,result.filtered_total))} de ${fmt(result.filtered_total)}` : 'Nenhum relatório';
 $('report-prev').disabled = reportOffset===0; $('report-next').disabled = reportOffset+100>=result.filtered_total;
}
async function downloadReports(format) {
 const response = await fetch('/admin/client-reports/export?format='+format,{cache:'no-store',headers:{'X-Admin-Session':session}});
 if(!response.ok){if(response.status===403)endSession();throw new Error(`Exportação falhou: HTTP ${response.status}.`);}
 const url = URL.createObjectURL(await response.blob()); const link = document.createElement('a');link.href=url;link.download='relatorios-mobile-completos.'+format;link.click();setTimeout(()=>URL.revokeObjectURL(url),30000);
}
function reportAction(work) {Promise.resolve().then(work).catch(error=>notice(error.message,true));}
$('report-filter').addEventListener('click',()=>{reportOffset=0;reportAction(loadReports);});
$('report-prev').addEventListener('click',()=>{reportOffset=Math.max(0,reportOffset-100);reportAction(loadReports);});
$('report-next').addEventListener('click',()=>{reportOffset+=100;reportAction(loadReports);});
for(const format of ['csv','json'])$('report-'+format).addEventListener('click',async()=>{const button=$('report-'+format);button.disabled=true;try{await downloadReports(format);}catch(error){notice(error.message,true);}finally{button.disabled=false;}});
$('report-table').addEventListener('click',event=>{const button=event.target.closest('[data-report-id]');if(!button)return;reportAction(async()=>{const result=await api('/admin/client-reports/'+encodeURIComponent(button.dataset.reportId));$('report-detail').textContent=JSON.stringify(result,null,2);$('report-detail-card').hidden=false;$('report-detail-card').scrollIntoView({behavior:'smooth'});});});
function endSession() {
 session = ''; sessionStorage.removeItem('durango-admin-session'); clearInterval(timer); timer = null;
 $('app').hidden = true; $('login').hidden = false; $('password').value = ''; players = []; catalog = null;
}
async function api(path, body) {
 const controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),20000);
 try {
  const response = await fetch(path, {method:body === undefined ? 'GET':'POST',cache:'no-store',headers:{'X-Admin-Session':session,...(body === undefined ? {} : {'Content-Type':'application/x-www-form-urlencoded'})},body:body === undefined ? undefined:new URLSearchParams(body),signal:controller.signal});
  const text = await response.text(); let result; try { result = JSON.parse(text); } catch { result = {}; }
  if (!response.ok) {
   if (session && (response.status === 401 || response.status === 403)) {endSession();$('login-error').textContent = 'Sessão expirada. Entre novamente.';}
   throw new Error(result.error || `O servidor respondeu HTTP ${response.status}.`);
  }
  if (result.error) throw new Error(result.error);
  return result;
 } catch(error) {
  if(error && error.name==='AbortError')throw new Error('O servidor não respondeu em até 20 segundos. Tente novamente.');
  throw error;
 } finally {clearTimeout(timeout);}
}
function metric(label, value) { return `<div class="metric"><span>${esc(label)}</span><strong>${esc(value)}</strong></div>`; }
function table(headers, rows) { return rows.length ? `<table><thead><tr>${headers.map(h=>`<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows.map(row=>`<tr>${row.map(cell=>`<td>${cell}</td>`).join('')}</tr>`).join('')}</tbody></table>` : '<p class="muted">Nenhum registro encontrado.</p>'; }
function status(online) { return `<span class="tag ${online?'':'offline'}">${online?'Online':'Offline'}</span>`; }
async function startSession() {
 await api('/health'); $('login').hidden = true; $('app').hidden = false; $('password').value = '';
 clearInterval(timer); timer = setInterval(()=>{if(session && !document.hidden && ['overview','players','premium','economy','actions'].includes(page)) refresh();},5000);
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
  if(target==='premium')await loadPremium();
  if(target==='economy')await economy();
  if(target==='config')await config();
  if(target==='islands')await islands();
  if(target==='data')await loadCatalog();
  if(target==='reports')await loadReports();
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
function renderPlayers(){const search=$('player-search').value.toLowerCase();$('player-table').innerHTML=table(['Personagem','Nível','Estado','Premium','Região','T Stone','Warp Gem','Durango Coin','Ações'],players.filter(p=>`${p.name} ${p.entity_id}`.toLowerCase().includes(search)).map(p=>[`${esc(p.name||'Sem nome')}<br><small class="muted">${esc(p.entity_id)}</small>`,fmt(p.level),p.banned?'<span class="tag offline">Banido</span>':status(p.online),premiumLabel(p.premium),esc(p.region||'—'),fmt(p.t_stone),fmt(p.warp_gem),fmt(p.durango_coin),`<button data-action="wallet" data-id="${esc(p.entity_id)}">Saldo</button>${p.online?`<button data-action="kick" data-id="${esc(p.entity_id)}">Desconectar</button>`:''}<button class="danger" data-action="${p.banned?'unban':'ban'}" data-id="${esc(p.entity_id)}">${p.banned?'Desbanir':'Banir'}</button>`]));}
$('player-search').addEventListener('input',renderPlayers);
const premiumDate = seconds => seconds > 0 ? new Date(seconds*1000).toLocaleString('pt-BR') : '—';
function premiumLabel(p){return p?.active ? '<span class="tag">Ativo</span><br><small>'+esc(premiumDate(p.expires_at))+'</small>' : p?.expires_at>0 ? '<span class="tag offline">Expirado</span>' : '—';}
let premiumPackages=[], premiumRows=[];
let premiumOperation=sessionStorage.getItem('durango-premium-operation')||'';
function premiumDraftChanged(){premiumOperation='';sessionStorage.removeItem('durango-premium-operation');premiumPreview();}
function premiumPreview(){
 const pkg=premiumPackages.find(p=>p.Id===$('premium-package').value), row=premiumRows.find(p=>p.entity_id===$('premium-recipient').value);
 if(!pkg){$('premium-benefits').textContent='';$('premium-preview').textContent='';return;}
 $('premium-benefits').textContent='+'+pkg.InventoryBonus+' de capacidade · '+pkg.DailyGems+' Warp Gems/dia · '+pkg.DailyItems.map(i=>i.Count+' × '+(i.Name||'consumível')).join(', ')+' · '+pkg.ImmediateGems+' Warp Gems na ativação.';
 const days=Number($('premium-days').value), current=row?.premium?.packages?.find(p=>p.package_id===pkg.Id);
 $('premium-preview').textContent=Number.isInteger(days)&&days>0&&days<=3650 ? 'Vencimento previsto: '+premiumDate(Math.max(Date.now()/1000,current?.expires_at||0)+days*86400)+' (hora local).' : 'Informe de 1 a 3650 dias inteiros.';
}
async function loadPremium(){
 const [state,rows]=await Promise.all([api('/admin/premium'),api('/admin/players')]);premiumPackages=state.packages;premiumRows=rows;
 const recipient=$('premium-recipient').value,pkg=$('premium-package').value;
 $('premium-recipient').innerHTML=rows.filter(p=>p.mail_eligible).map(p=>'<option value="'+esc(p.entity_id)+'">'+esc(p.name||p.entity_id)+' · '+esc(p.entity_id)+'</option>').join('');
 $('premium-package').innerHTML=premiumPackages.map(p=>'<option value="'+esc(p.Id)+'">'+esc(p.Name)+'</option>').join('');
 if(rows.some(p=>p.entity_id===recipient))$('premium-recipient').value=recipient;
 if(premiumPackages.some(p=>p.Id===pkg))$('premium-package').value=pkg;
 if(!pkg&&premiumPackages.length)$('premium-days').value=premiumPackages[0].Days;
 $('premium-grant').disabled=!$('premium-recipient').value;
 $('premium-metrics').innerHTML=metric('Personagens premium',fmt(rows.filter(p=>p.premium?.active).length))+metric('Pacotes ativos',fmt(rows.reduce((n,p)=>n+(p.premium?.packages?.filter(s=>s.active).length||0),0)))+metric('XP adicional','50%')+metric('Chance de item extra','25%');
 renderPremium();premiumPreview();
 $('premium-history').innerHTML=table(['Data','Personagem','Pacote','Ação','Dias','Vencimento','Gems'],state.history.map(o=>[esc(premiumDate(o.At)),esc(rows.find(p=>p.entity_id===o.EntityId)?.name||o.EntityId),esc(premiumPackages.find(p=>p.Id===o.PackageId)?.Name||o.PackageId),o.Action==='grant'?'Concessão / renovação':'Revogação',fmt(o.Days),esc(premiumDate(o.Until)),fmt(o.Gems)]));
}
function renderPremium(){
 const search=$('premium-search').value.toLowerCase(),filter=$('premium-filter').value;
 const rows=premiumRows.filter(p=>(p.name+' '+p.entity_id).toLowerCase().includes(search)).filter(p=>filter==='all'||filter==='active'&&p.premium?.active||filter==='expired'&&!p.premium?.active&&p.premium?.expires_at>0||filter==='none'&&!p.premium?.expires_at);
 $('premium-table').innerHTML=table(['Personagem','Conexão','Premium','Pacotes / vencimento','Capacidade','Gems diárias','Ações'],rows.map(p=>[
  esc(p.name||p.entity_id)+'<br><small>'+esc(p.entity_id)+'</small>',status(p.online),premiumLabel(p.premium),
  (p.premium?.packages||[]).map(s=>esc(s.name)+' · '+esc(premiumDate(s.expires_at))+(s.active?' · '+fmt(Math.ceil(s.remaining_seconds/3600))+' h restantes':' · encerrado')).join('<br>')||'—',
  fmt(p.inventory_capacity),fmt(p.premium?.daily_gems),'<button data-premium-select="'+esc(p.entity_id)+'">Selecionar</button> '+(p.premium?.packages||[]).filter(s=>s.active).map(s=>'<button class="danger" data-premium-revoke="'+esc(p.entity_id)+'" data-package="'+esc(s.package_id)+'">Revogar '+esc(s.name)+'</button>').join(' ')]));
}
$('premium-search').addEventListener('input',renderPremium);$('premium-filter').addEventListener('change',renderPremium);
for(const id of ['premium-recipient','premium-days'])$(id).addEventListener('change',premiumDraftChanged);
$('premium-package').addEventListener('change',()=>{const pkg=premiumPackages.find(p=>p.Id===$('premium-package').value);if(pkg)$('premium-days').value=pkg.Days;premiumDraftChanged();});
$('premium-days').addEventListener('input',premiumDraftChanged);
$('premium-form').addEventListener('submit',async event=>{
 event.preventDefault();const days=Number($('premium-days').value),entity_id=$('premium-recipient').value,package_id=$('premium-package').value;
 if(!entity_id||!Number.isInteger(days)||days<1||days>3650){notice('Escolha um personagem e uma duração válida.',true);return;}
 const pkg=premiumPackages.find(p=>p.Id===package_id),row=premiumRows.find(p=>p.entity_id===entity_id);
 if(!confirm('Liberar / renovar '+pkg.Name+' para '+(row?.name||entity_id)+' por '+days+' dias? '+pkg.ImmediateGems+' Warp Gems serão creditadas na ativação.'))return;
 const fingerprint=JSON.stringify({entity_id,package_id,days});
 try{const pending=premiumOperation?JSON.parse(premiumOperation):null;if(pending?.fingerprint!==fingerprint)premiumOperation=JSON.stringify({fingerprint,id:newMailId()});}catch{premiumOperation=JSON.stringify({fingerprint,id:newMailId()});}
 sessionStorage.setItem('durango-premium-operation',premiumOperation);
 $('premium-grant').disabled=true;
 try{const result=await api('/admin/premium/grant',{entity_id,package_id,days,request_id:JSON.parse(premiumOperation).id});premiumOperation='';sessionStorage.removeItem('durango-premium-operation');$('premium-result').textContent=(result.duplicate?'Operação já confirmada. ':'Premium aplicado. ')+'Vencimento: '+premiumDate(result.operation.Until);await loadPremium();}
 catch(error){notice(error.message,true);}finally{$('premium-grant').disabled=false;}
});
$('premium-table').addEventListener('click',async event=>{
 const select=event.target.closest('[data-premium-select]');if(select){$('premium-recipient').value=select.dataset.premiumSelect;premiumDraftChanged();$('premium-form').scrollIntoView({behavior:'smooth'});return;}
 const revoke=event.target.closest('[data-premium-revoke]');if(!revoke)return;
 const row=premiumRows.find(p=>p.entity_id===revoke.dataset.premiumRevoke),pkg=premiumPackages.find(p=>p.Id===revoke.dataset.package);
 if(!confirm('Revogar '+pkg.Name+' de '+(row?.name||revoke.dataset.premiumRevoke)+'? Outros pacotes continuarão ativos. Itens e gems já entregues serão preservados.'))return;
 revoke.disabled=true;try{await api('/admin/premium/revoke',{entity_id:revoke.dataset.premiumRevoke,package_id:pkg.Id,request_id:newMailId()});await loadPremium();notice('Pacote revogado.');}catch(error){notice(error.message,true);revoke.disabled=false;}
});

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
async function execute(button,work){button.disabled=true;button.classList.add('is-busy');button.setAttribute('aria-busy','true');try{notice(await work());}catch(error){notice(error.message,true);}finally{button.classList.remove('is-busy');button.removeAttribute('aria-busy');button.disabled=false;}}
function on(id,work){$(id).addEventListener('click',()=>execute($(id),work));}
on('save-config',()=>saveJson('/admin/config','config-editor'));
on('save-islands',()=>saveJson('/admin/islands','islands-editor'));
on('load-island',async()=>{const id=$('island-select').value;if(!id)throw new Error('Selecione uma ilha.');const data=await api('/admin/island/config?id='+encodeURIComponent(id));islandId=id;$('island-editor').value=JSON.stringify(data,null,2);$('island-editor').disabled=false;$('save-island').disabled=false;return 'Configuração de '+id+' carregada.';});
on('save-island',()=>{if(!islandId)throw new Error('Carregue a ilha antes de salvar.');return saveJson('/admin/island/config','island-editor',{id:islandId});});
on('load-data',async()=>{const path=$('data-select').value;if(!path)throw new Error('Selecione um arquivo.');const data=await api('/admin/data?path='+encodeURIComponent(path));const text=JSON.stringify(data,null,2);dataFile={path,text};$('data-viewer').textContent=text.slice(0,300000);$('data-detail').textContent=`${path} · ${fmt(text.length)} caracteres${text.length>300000?' · Prévia limitada. Baixe o JSON para consultar o arquivo completo.':''}`;$('download-data').disabled=false;return 'Arquivo carregado.';});
$('download-data').addEventListener('click',()=>{if(!dataFile)return;const url=URL.createObjectURL(new Blob([dataFile.text],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download=dataFile.path.replace(/\//g,'_');a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);});
const reload=async()=>{await api('/admin/reload',{});return 'Configurações recarregadas no servidor.';};on('reload',reload);on('reload-config',reload);
function invalidateMailPreview() {
 mailPreview=null;$('mail-preview-result').hidden=true;
 $('mail-result').textContent='O email será validado automaticamente antes do envio.';
}
function mailChanged() {
 invalidateMailPreview();
 mailRequestId=newMailId();sessionStorage.setItem('durango-mail-request-id',mailRequestId);
 sessionStorage.setItem('durango-mail-draft',$('mail-json').value);
}
function syncMailDraftFromControls() {
 const data=JSON.parse($('mail-json').value);
 data.target=$('mail-target').value;
 if(data.target==='all')delete data.recipient_id;
 else{
  const recipientId=$('mail-recipient').value;
  if(!recipientId)throw new Error('Selecione um jogador.');
  data.recipient_id=recipientId;
 }
 const json=JSON.stringify(data,null,2);
 if(json!==$('mail-json').value){$('mail-json').value=json;mailChanged();}
 return json;
}
function syncMailControlsOrNotice(){try{syncMailDraftFromControls();}catch(error){invalidateMailPreview();notice(error.message,true);}}
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
function filterMailItems(){
 if(!mailItems)return;
 const query=$('mail-item-search').value.toLowerCase(),scope=$('mail-item-scope').value;
 const filtered=mailItems.filter(i=>(scope==='all'||(scope==='shop'&&i.shop)||(scope==='skins'&&i.skin))&&`${i.name} ${i.prototype_id} ${i.category||''} ${(i.shop_categories||[]).join(' ')}`.toLowerCase().includes(query));
 $('mail-item').replaceChildren(...filtered.map(i=>new Option(`${i.name} · ${i.prototype_id}${i.shop?' · Loja':''}`,i.prototype_id)));
 $('mail-item-count').textContent=`${fmt(filtered.length)} item(ns) nesta lista · catálogo total ${fmt(mailItems.length)}.`;
}
$('mail-item-search').addEventListener('input',filterMailItems);
$('mail-item-scope').addEventListener('change',filterMailItems);
$('mail-target').addEventListener('change',()=>{$('mail-recipient').disabled=$('mail-target').value==='all';syncMailControlsOrNotice();});
$('mail-recipient').addEventListener('change',syncMailControlsOrNotice);
on('mail-template',async()=>{syncMailDraftFromControls();return 'Destinatário atualizado no JSON.';});
on('mail-add-item',async()=>{const data=JSON.parse($('mail-json').value),id=$('mail-item').value;if(!id)throw new Error('Selecione um item.');if(!Array.isArray(data.items))data.items=[];data.items.push({prototype_id:id,quantity:1,level:1});$('mail-json').value=JSON.stringify(data,null,2);mailChanged();return 'Anexo adicionado. Ajuste quantity e level no JSON, se necessário.';});
on('mail-add-portal-stones',async()=>{const quantity=Number($('mail-portal-stones').value);if(!Number.isInteger(quantity)||quantity<1||quantity>240)throw new Error('Pedras de Portal: informe uma quantidade inteira entre 1 e 240.');const data=JSON.parse($('mail-json').value);if(!Array.isArray(data.vouchers))data.vouchers=[];data.vouchers=data.vouchers.filter(v=>v?.voucher_id!=='voucher_resource_induced_stone');data.vouchers.push({voucher_id:'voucher_resource_induced_stone',quantity});$('mail-json').value=JSON.stringify(data,null,2);mailChanged();return `${fmt(quantity)} Pedra(s) de Portal adicionada(s) ao email.`;});
async function validateMailPreview() {
 const json=syncMailDraftFromControls();
 const data=await api('/admin/mail/preview',{json});
 if(json!==$('mail-json').value)throw new Error('O JSON mudou durante a validação. Valide novamente.');
 mailPreview={json,data};
 $('mail-preview-result').textContent=JSON.stringify(data,null,2);
 $('mail-preview-result').hidden=false;
 $('mail-result').textContent=`Pronto para enviar a ${fmt(data.recipients)} personagem(ns), com ${fmt(data.attachments_per_player)} item(ns) e ${fmt(data.portal_stones_per_player)} Pedra(s) de Portal por personagem.`;
 return mailPreview;
}
on('mail-preview',async()=>{await validateMailPreview();return 'JSON válido. Confira a prévia antes de enviar.';});
on('mail-send',async()=>{let preview=mailPreview;if(!preview||preview.json!==$('mail-json').value)preview=await validateMailPreview();if(!confirm(`Enviar “${preview.data.subject}” para ${fmt(preview.data.recipients)} personagem(ns), com ${fmt(preview.data.attachments_per_player)} item(ns) e ${fmt(preview.data.portal_stones_per_player)} Pedra(s) de Portal para cada um?`))return 'Envio cancelado.';sessionStorage.setItem('durango-mail-request-id',mailRequestId);sessionStorage.setItem('durango-mail-draft',preview.json);const result=await api('/admin/mail/send',{json:preview.json,request_id:mailRequestId});$('mail-result').textContent=result.duplicate?'Este envio já foi registrado. Nenhuma mensagem duplicada foi criada.':`Email entregue ao correio de ${fmt(result.sent)} personagem(ns).`;await loadMailTools();return $('mail-result').textContent;});
on('mail-new',async()=>{if(!confirm('Preparar outro envio? Enviar novamente o mesmo conteúdo criará novas mensagens.'))return 'Envio atual mantido.';mailChanged();return 'Novo envio preparado. Valide o JSON.';});
on('announce',async()=>{const text=$('announcement').value.trim();if(!text)throw new Error('Escreva a mensagem.');const result=await api('/admin/announce',{text});$('announcement').value='';return `Anúncio enviado para ${fmt(result.sent)} jogadores.`;});
for(const [id,onValue]of [['maintenance-on','1'],['maintenance-off','0']])on(id,async()=>{const result=await api('/admin/maintenance',{on:onValue});maintenance(result.maintenance);return result.maintenance?'Manutenção ativada.':'Servidor aberto.';});
if(session)startSession().catch(error=>{endSession();$('login-error').textContent=error.message;});
})();
