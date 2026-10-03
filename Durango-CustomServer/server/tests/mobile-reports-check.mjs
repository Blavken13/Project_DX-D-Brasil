import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

// Called by the admin fixture: real server, isolated disk and authenticated session.
export async function mobileReportChecks({origin,request,json,check,root}) {
 const make = overrides=>({event_id:crypto.randomUUID(),installation_id:crypto.randomUUID(),session_id:crypto.randomUUID(),platform:'Android',occurred_at:Date.now(),event_type:'native_crash',
  version_name:'test-reports',version_code:50210,manufacturer:'Fixture',model:'Phone A',android_api:35,page_size:16384,last_stage:'UNITY_STARTING',exit_reason:5,exit_status:11,rss_kib:12000,
  metadata_available:true,graphics:['get_graphicsDeviceName: Adreno fixture'],native_frames:['#0 pc 12ab libunity.so render+4 build=0123456789abcdef0123456789abcdef0123456789abcdef'],breadcrumbs:['1700000000 UNITY_STARTING'],...overrides});
 const post=(report,extra={})=>fetch(origin+'/client-reports/mobile',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({schema_version:1,report}),signal:AbortSignal.timeout(10000),...extra});
 for(const route of ['/admin/client-reports','/admin/client-reports/export?format=csv','/admin/client-reports/export?format=json','/admin/client-reports/'+crypto.randomUUID()])
  check((await fetch(origin+route)).status===403,'relatórios protegidos: '+route);
 check((await request('/admin/client-reports',undefined,{Origin:'https://external.example'})).status===403,'origem externa não consulta relatórios');
 const report=make({password:'never-store-this',access_token:'never-store-this',message:'never-store-this',java_frames:['some.Class.call(File.java:10)','credential=never-store-this']});
 let response=await post(report);check(response.status===200&&(await response.json()).accepted[0]===report.event_id,'ingestão anônima Android confirma UUID');
 check((await post(report)).status===200,'reenvio idempotente confirmado');
 check((await post({...report,model:'Changed'})).status===409,'UUID com conteúdo diferente é recusado');
 check((await post(make({platform:'Windows'}))).status===400,'cliente PC não entra no coletor mobile');
 check((await post(make({installation_id:'../escape'}))).status===400,'ID inválido recusado sem travessia de diretório');
 check((await post(make({model:'x'.repeat(161)}))).status===400,'campos grandes recusados');
 check((await post(make({graphics:Array(9).fill('x')}))).status===400,'listas limitadas');
 check((await post(make(),{body:'{invalid'})).status===400,'JSON malformado recusado');
 check((await post(make(),{body:'{"schema_version":1,"schema_version":1}'})).status===400,'propriedades JSON duplicadas recusadas');
 check((await post(make(),{headers:{'Content-Type':'text/plain'}})).status===415,'formato não JSON recusado');
 check((await post(make(),{body:'x'.repeat(65537)})).status===413,'corpo acima de 64 KiB recusado');
 let list=await json('/admin/client-reports');check(list.total===1&&list.reports.length===1,'duplicata não aparece duas vezes');
 let detail=await json('/admin/client-reports/'+report.event_id);
 check(!JSON.stringify(detail).includes('never-store-this')&&detail.java_frames.length===1,'campos fora da lista e frames inválidos não são armazenados');
 check(detail.native_frames[0].includes('build=0123456789abcdef0123456789abcdef0123456789abcdef'),'BuildID longo preservado para correlação');
 const same=make({model:'Phone B'});check((await post(same)).ok,'segundo aparelho aceito');
 list=await json('/admin/client-reports');check(list.groups.length===1&&list.groups[0].count===2&&list.groups[0].installations===2,'mesma pilha agrupa modelos diferentes');
 const formula=make({event_type:'visual',native_frames:[],model:'=HYPERLINK("example")',visual_category:'black_character'});
 check((await post(formula)).ok,'relato visual aceito');
 // Slow bodies occupy only collector slots, while normal game/status routes stay responsive.
 const slow=[];
 try {
  for(let i=0;i<2;i++){
   const socket=http.request(origin+'/client-reports/mobile',{method:'POST',headers:{'Content-Type':'application/json','Content-Length':'64000'}});socket.on('error',()=>{});socket.on('response',r=>r.resume());socket.write('{');slow.push(socket);
  }
  await new Promise(resolve=>setTimeout(resolve,150));
  const started=performance.now();check((await request('/status')).ok&&performance.now()-started<1500,'POSTs lentos não bloqueiam o status do jogo');
  check((await request('/admin/client-reports')).ok,'POSTs lentos não bloqueiam a consulta administrativa');
  check((await post(make())).status===503,'concorrência da ingestão limitada');
 }finally{for(const socket of slow)socket.destroy();}
 await new Promise(resolve=>setTimeout(resolve,200));
 // Seed extra VALID persisted records directly to test pagination/export, without defeating rate limits.
 const directory=path.join(root,'AppData-nx','mobile-reports');
 const stored=JSON.parse(fs.readFileSync(path.join(directory,report.event_id+'.json')));
 for(let i=0;i<105;i++){const value={...stored,event_id:crypto.randomUUID(),received_at:new Date().toISOString()};fs.writeFileSync(path.join(directory,value.event_id+'.json'),JSON.stringify(value));}
 const exportCsv=await request('/admin/client-reports/export?format=csv');const csv=await exportCsv.text();
 check(exportCsv.headers.get('content-disposition')?.includes('attachment')&&csv.includes('native_frames;breadcrumbs;visual_category'),'CSV contém todas as colunas e é anexado');
 check(csv.includes("'=HYPERLINK"),'CSV neutraliza fórmulas de planilha');
 const exportJson=await request('/admin/client-reports/export?format=json&model=missing');
 check((await exportJson.json()).length===3,'exportação JSON ignora filtros da página');
 // Disk failure must not acknowledge an event and must not stop normal gateway routes.
 const renamed=directory+'-fixture';fs.renameSync(directory,renamed);fs.writeFileSync(directory,'not-a-directory');
 try{check((await post(make())).status===503,'falha de disco não confirma recebimento');check((await request('/status')).ok,'falha de disco não derruba gateway');}
 finally{fs.unlinkSync(directory);fs.renameSync(renamed,directory);}
 const limited=crypto.randomUUID();let firstLimited;
 for(let i=0;i<10;i++){const item=make({installation_id:limited,model:'Rate fixture'});if(i===0)firstLimited=item;assert.equal((await post(item)).status,200);}
 response=await post(make({installation_id:limited}));check(response.status===429&&response.headers.get('retry-after')==='60','limite por instalação retorna 429 com espera');
 check((await post(firstLimited)).status===200,'evento já confirmado pode ser repetido mesmo no limite');
 return {count:118,report};
}

export async function mobileReportRestartChecks({request,json,check,state}) {
 const list=await json('/admin/client-reports');check(list.total===state.count&&list.reports.length===100,'relatórios persistem e primeira página é limitada');
 check((await json('/admin/client-reports?offset=100')).reports.length===18,'segunda página contém todos os restantes');
 check((await (await request('/admin/client-reports/export?format=json&model=missing')).json()).length===state.count,'download completo cobre páginas e ignora filtros');
 const csv=await (await request('/admin/client-reports/export?format=csv')).text();check(csv.split(/\r?\n/).filter(Boolean).length===state.count+1,'CSV exporta uma linha por erro, além do cabeçalho');
}
