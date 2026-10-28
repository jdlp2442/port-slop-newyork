const http=require('http'),fs=require('fs'),path=require('path'),{spawn}=require('child_process');
const root='D:/escena/slop-newyork-main';
const MIME={'.html':'text/html','.js':'application/javascript','.mjs':'application/javascript','.css':'text/css','.json':'application/json'};
const server=http.createServer((req,res)=>{const u=req.url.split('?')[0];const safe=path.normalize(path.join(root,u==='/'?'index.html':u));fs.readFile(safe,(e,d)=>{if(e){res.statusCode=404;return res.end()}res.setHeader('Content-Type',MIME[path.extname(safe)]||'text/plain');res.end(d)})});
server.listen(8095,async()=>{
  const chrome=spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',['--headless=new','--no-sandbox','--disable-gpu-sandbox','--enable-unsafe-webgpu','--remote-debugging-port=9224','--user-data-dir=D:/escena/_insp-profile','about:blank'],{stdio:'ignore'});
  await new Promise(r=>setTimeout(r,3000));
  const list=await (await fetch('http://localhost:9224/json/list')).json();
  const page=list.find(t=>t.type==='page');
  const ws=new WebSocket(page.webSocketDebuggerUrl);
  await new Promise(r=>ws.addEventListener('open',r,{once:true}));
  let id=1;
  const call=(m,p={})=>new Promise((res,rej)=>{const h=e=>{const d=JSON.parse(e.data);if(d.id!==id)return;ws.removeEventListener('message',h);d.error?rej(new Error(d.error.message)):res(d.result)};ws.addEventListener('message',h);ws.send(JSON.stringify({id:id++,method:m,params:p}))});
  await call('Page.enable');await call('Runtime.enable');
  await call('Page.navigate',{url:'http://localhost:8095/index.html'});
  for(let i=0;i<60;i++){await new Promise(r=>setTimeout(r,2000));const r=await call('Runtime.evaluate',{expression:'!!(window.__NYC_APP&&window.__NYC_APP.ready===true)',returnByValue:true});if(r.result&&r.result.value)break;}
  const code=fs.readFileSync('D:/escena/slop-newyork-main/tools/_inspect-trees.cs.js','utf8');
  const r=await call('Runtime.evaluate',{expression:code,returnByValue:true});
  console.log(r.result?r.result.value:JSON.stringify(r.exceptionDetails));
  ws.close();chrome.kill();server.close();process.exit(0);
});
