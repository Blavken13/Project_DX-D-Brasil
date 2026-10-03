import http from 'node:http';
import net from 'node:net';

// Development helper: WSL NAT exposes localhost but may not expose the Wi-Fi IP.
// Binds only the requested private LAN address; forwards only diagnostics and status.
const args=process.argv.slice(2), options={};
for(let i=0;i<args.length;i+=2)options[args[i]]=args[i+1];
const address=options['--listen'], port=Number(options['--port']||8190);
if(net.isIP(address)!==4||!(/^(10\.|192\.168\.|172\.(1[6-9]|2\d|3[01])\.)/.test(address)))throw new Error('Informe um IP privado IPv4 em --listen.');
const upstream=new URL(options['--upstream']||'http://127.0.0.1:8190');
if(upstream.protocol!=='http:'||upstream.hostname!=='127.0.0.1'||upstream.username||upstream.password)throw new Error('Upstream deve ser HTTP em 127.0.0.1.');
let active=0;
const server=http.createServer((request,response)=>{
 const path=new URL(request.url,'http://localhost').pathname;
 if(!((path==='/client-reports/mobile'&&request.method==='POST')||(path==='/status'&&request.method==='GET'))){response.writeHead(404);response.end();return;}
 if(active>=16){response.writeHead(503);response.end();return;}
 if(Number(request.headers['content-length']||0)>65536){response.writeHead(413);response.end();return;}
 active++;let finished=false;
 const done=()=>{if(!finished){finished=true;active--;}};
 const forwarded=http.request(new URL(path,upstream),{method:request.method,headers:{...(request.headers['content-type']?{'Content-Type':request.headers['content-type']}:{}),...(request.headers['content-length']?{'Content-Length':request.headers['content-length']}:{}),...(request.headers['content-encoding']?{'Content-Encoding':request.headers['content-encoding']}:{}),Connection:'close'}},remote=>{
  response.writeHead(remote.statusCode,remote.headers);remote.pipe(response);
 });
 forwarded.on('error',()=>{if(!response.headersSent)response.writeHead(503);response.end();done();});
 request.on('aborted',()=>{forwarded.destroy();done();});
 response.on('close',()=>{forwarded.destroy();done();});
 request.setTimeout(6000,()=>{forwarded.destroy();request.destroy();done();});
 forwarded.setTimeout(6000,()=>forwarded.destroy());
 let bytes=0;request.on('data',chunk=>{bytes+=chunk.length;if(bytes>65536){forwarded.destroy();request.destroy();done();}});
 request.pipe(forwarded);
});
server.headersTimeout=7000;server.requestTimeout=7000;server.maxConnections=32;
server.listen(port,address,()=>console.log(`Relatórios locais: http://${address}:${port}/client-reports/mobile → ${upstream.origin}`));
