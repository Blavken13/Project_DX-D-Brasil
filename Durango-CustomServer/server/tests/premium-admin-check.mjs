export async function premiumAdminChecks({request,json,check}) {
    for(const route of ['/admin/premium/grant','/admin/premium/revoke'])
        check((await request(route,{entity_id:'mail-alice',package_id:'monthly_package_1',days:'30',request_id:'unauthorized'},{'X-Admin-Session':''})).status===403,'premium exige sessão: '+route);
    const catalog=await json('/admin/premium');
    check(catalog.packages.length===3&&catalog.xp_multiplier===1.5&&catalog.gather_extra_chance===0.25,'catálogo premium e extras');
    const rows=await json('/admin/players'), alice=rows.find(p=>p.entity_id==='mail-alice');
    const grant={entity_id:alice.entity_id,package_id:'monthly_package_1',days:'30',request_id:'premium-http-grant'};
    check((await request('/admin/premium/grant',grant,{Origin:'https://foreign.example'})).status===403,'concessão de outra origem bloqueada');
    check((await request('/admin/premium/grant',{...grant,days:'0',request_id:'bad-days'})).status===400,'prazo zero recusado');
    check((await request('/admin/premium/grant',{...grant,entity_id:'missing'})).status===404,'personagem inexistente recusado');
    const first=await json('/admin/premium/grant',grant);
    check(first.premium.active&&first.premium.inventory_bonus===40,'concessão para personagem offline');
    const retry=await json('/admin/premium/grant',grant);
    check(retry.duplicate&&retry.operation.Until===first.operation.Until,'retry preserva vencimento');
    check((await request('/admin/premium/grant',{...grant,days:'7'})).status===400,'request_id conflitante recusado');
    const renewal=await json('/admin/premium/grant',{...grant,days:'7',request_id:'premium-http-renew'});
    check(renewal.operation.Until===first.operation.Until+7*86400,'HTTP renova somando duração');
    await json('/admin/premium/grant',{...grant,package_id:'day_package_2',days:'15',request_id:'premium-http-other'});
    const updated=(await json('/admin/players')).find(p=>p.entity_id===alice.entity_id);
    check(updated.inventory_capacity===alice.inventory_capacity+80&&updated.warp_gem===alice.warp_gem+750,'pacotes acumulam capacidade e gems de ativação');
    check((await json('/admin/mail/inbox?entity_id=mail-alice')).length===0,'personagem offline não recebe diárias até acessar o jogo');
    await json('/admin/premium/revoke',{entity_id:alice.entity_id,package_id:'day_package_2',request_id:'premium-http-revoke'});
    const revoked=(await json('/admin/players')).find(p=>p.entity_id===alice.entity_id);
    check(revoked.premium.active&&revoked.premium.inventory_bonus===40,'revogação de um pacote mantém o outro');
    const history=(await json('/admin/premium')).history;
    check(history.length===4&&history.some(o=>o.Action==='revoke'),'histórico persistente das operações');
    return {entity_id:alice.entity_id,until:renewal.operation.Until,warp_gem:alice.warp_gem+750};
}
export async function premiumRestartChecks({json,check,state}) {
    const row=(await json('/admin/players')).find(p=>p.entity_id===state.entity_id);
    check(row.premium.active&&row.premium.expires_at===state.until,'prazo premium preservado no restart');
    check(row.warp_gem===state.warp_gem,'restart não duplica gems de ativação');
    check((await json('/admin/premium')).history.length===4,'histórico premium preservado');
}
