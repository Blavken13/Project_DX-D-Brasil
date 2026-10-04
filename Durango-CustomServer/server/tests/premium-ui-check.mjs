// Exercise the shipped UI handlers with a small DOM fixture; no browser required.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import crypto from 'node:crypto';
const root=new URL('../',import.meta.url);
const html=fs.readFileSync(new URL('admin/index.html',root),'utf8');
const source=fs.readFileSync(new URL('admin/app.js',root),'utf8');
const elements=new Map();
class Element {
    constructor(id){this.id=id;this.value='';this.listeners=new Map();this.dataset={};this.textContent='';this.disabled=false;this.hidden=false;this.classList={add(){},remove(){},toggle(){}};}
    addEventListener(type,handler){const list=this.listeners.get(type)||[];list.push(handler);this.listeners.set(type,list);}
    async fire(type,event={}){for(const handler of this.listeners.get(type)||[])await handler({preventDefault(){},...event});}
    set innerHTML(value){this.content=value;const options=[...value.matchAll(/<option value="([^"]*)"/g)];this.options=options.map(m=>({value:m[1]}));if(options.length)this.value=options[0][1];}
    get innerHTML(){return this.content||'';}
    scrollIntoView(){}
}
for(const id of html.matchAll(/\bid="([^"]+)"/g)){assert(!elements.has(id[1]),'unique DOM ID '+id[1]);elements.set(id[1],new Element(id[1]));}
const el=id=>{assert(elements.has(id),'shipped HTML contains '+id);return elements.get(id);};
el('premium-days').value='30';el('premium-filter').value='active';
const storage=new Map();const requests=[];
const pkg={Id:'monthly_package_1',Name:'Premium 30 dias',Days:30,InventoryBonus:40,DailyGems:20,ImmediateGems:300,DailyItems:[{PrototypeId:'fried_hen',Name:'Frango frito',Count:3}]};
const rows=[{entity_id:'alice',name:'Alice <script>',mail_eligible:true,online:false,inventory_capacity:240,premium:{active:true,expires_at:Date.now()/1000+86400,inventory_bonus:40,daily_gems:20,packages:[{package_id:pkg.Id,name:pkg.Name,active:true,expires_at:Date.now()/1000+86400,remaining_seconds:86400}]}},
    {entity_id:'bob',name:'Bob',mail_eligible:true,online:false,inventory_capacity:200,premium:{active:false,expires_at:0,packages:[]}}];
let failNext=false;
const sandbox={testUi:null,console,URLSearchParams,AbortController,setTimeout,clearTimeout,setInterval(){},clearInterval(){},crypto:crypto.webcrypto,
    sessionStorage:{getItem:key=>storage.get(key)||null,setItem:(key,value)=>storage.set(key,value),removeItem:key=>storage.delete(key)},
    document:{getElementById:el,querySelectorAll:()=>[],hidden:false},confirm:()=>true,
    fetch:async(path,options)=>{
        requests.push({path,body:options.body?Object.fromEntries(options.body):null});
        if(failNext){failNext=false;throw new Error('network timeout');}
        let data=path==='/admin/premium'?{packages:[pkg],history:[]}:path==='/admin/players'?rows:
            {duplicate:false,operation:{Until:Date.now()/1000+86400},premium:rows[0].premium};
        return {ok:true,status:200,text:async()=>JSON.stringify(data)};
    }};
vm.createContext(sandbox);
vm.runInContext(source.replace(/\}\)\(\);\s*$/, "testUi = {loadPremium,renderPremium,premiumPreview,renderPlayers,setSession(value){session=value},setPlayers(value){players=value}};})();"),sandbox);
sandbox.testUi.setSession('test-session');
await sandbox.testUi.loadPremium();
assert(el('premium-table').innerHTML.includes('Alice &lt;script&gt;'),'player names escaped');
assert(!el('premium-table').innerHTML.includes('>Bob<'),'active filter excludes nonpremium players');
assert(el('premium-benefits').textContent.includes('300 Warp Gems'),'activation bonus displayed');
assert(el('premium-benefits').textContent.includes('Frango frito')&&!el('premium-benefits').textContent.includes('fried_hen'),'consumables use display names');
assert(el('premium-preview').textContent.includes('Vencimento previsto:'),'renewal preview displayed');
el('premium-filter').value='none';await el('premium-filter').fire('change');
assert(el('premium-table').innerHTML.includes('Bob')&&!el('premium-table').innerHTML.includes('Alice'),'no-premium filter');
el('premium-filter').value='all';await el('premium-filter').fire('change');
el('premium-days').value='0';await el('premium-form').fire('submit');
assert(!requests.some(r=>r.path==='/admin/premium/grant'),'invalid days never submit');
el('premium-days').value='7';await el('premium-days').fire('input');
failNext=true;await el('premium-form').fire('submit');
const pending=requests.filter(r=>r.path==='/admin/premium/grant').at(-1);
assert(pending.body.entity_id==='alice'&&pending.body.days==='7','form uses chosen character and days');
assert(storage.has('durango-premium-operation'),'failed response preserves idempotency key');
await el('premium-form').fire('submit');
const retry=requests.filter(r=>r.path==='/admin/premium/grant').at(-1);
assert(retry.body.request_id===pending.body.request_id,'retry reuses same request ID');
assert(!storage.has('durango-premium-operation'),'confirmed operation clears pending request');
assert(el('premium-result').textContent.includes('Premium aplicado.'),'successful grant feedback');
sandbox.testUi.setPlayers(rows);el('player-search').value='';sandbox.testUi.renderPlayers();
assert(el('player-table').innerHTML.includes('<th>Premium</th>'),'players table shows premium');
console.log('PASS Premium UI: filters, safe rendering, renewal preview, validation, grant and retry.');
