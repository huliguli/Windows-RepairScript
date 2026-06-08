"use strict";

/* ---------- SVG-Icons (Feather-Stil) ---------- */
const ICONS = {
  wrench:'<path d="M14.6 6.3a3.8 3.8 0 0 1-4.9 4.9L4.2 16.7V20h3.3l5.5-5.5a3.8 3.8 0 0 1 4.9-4.9l-2.7 2.7-2.3-.6-.6-2.3 2.9-3.1Z"/>',
  refresh:'<path d="M21 3v6h-6"/><path d="M3 12a9 9 0 0 1 15-6.7L21 9"/><path d="M3 21v-6h6"/><path d="M21 12a9 9 0 0 1-15 6.7L3 15"/>',
  rotate:'<path d="M3 3v6h6"/><path d="M3.5 13A9 9 0 1 0 6 5.3L3 9"/>',
  shieldCheck:'<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/><path d="M9 12l2 2 4-4"/>',
  shield:'<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/>',
  search:'<circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/>',
  trash:'<path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/>',
  activity:'<path d="M22 12h-4l-3 9-6-18-3 9H2"/>',
  alert:'<path d="M10.3 3.6 1.8 18a2 2 0 0 0 1.7 3h16.9a2 2 0 0 0 1.7-3L13.7 3.6a2 2 0 0 0-3.4 0Z"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
  globe:'<circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15 15 0 0 1 0 20 15 15 0 0 1 0-20Z"/>',
  download:'<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="M7 10l5 5 5-5"/><path d="M12 15V3"/>',
  server:'<rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01"/><path d="M7 16.5h.01"/>',
  cpu:'<rect x="6" y="6" width="12" height="12" rx="2"/><rect x="9" y="9" width="6" height="6" rx="1"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/>',
  hdd:'<path d="M22 12H2"/><path d="M5.5 6h13l3.5 6v6a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1v-6Z"/><path d="M6.5 16h.01"/><path d="M10.5 16h.01"/>',
  battery:'<rect x="2" y="7" width="16" height="10" rx="2"/><path d="M22 10v4"/><path d="M6 10.5v3"/>',
  arrow:'<path d="M5 12h14"/><path d="M13 6l6 6-6 6"/>',
  chevron:'<path d="M6 9l6 6 6-6"/>',
  up:'<path d="M18 15l-6-6-6 6"/>',
  down:'<path d="M6 9l6 6 6-6"/>',
  plus:'<path d="M12 5v14"/><path d="M5 12h14"/>',
  layers:'<path d="M12 2 2 7l10 5 10-5-10-5Z"/><path d="m2 17 10 5 10-5"/><path d="m2 12 10 5 10-5"/>',
  power:'<path d="M12 3v9"/><path d="M6.4 6.4a8 8 0 1 0 11.2 0"/>',
  min:'<path d="M5 12h14"/>',
  max:'<rect x="5" y="5" width="14" height="14" rx="2"/>',
  close:'<path d="M6 6l12 12"/><path d="M18 6 6 18"/>',
  check:'<path d="M22 11.1V12a10 10 0 1 1-5.9-9.1"/><path d="M22 4 12 14.1l-3-3"/>',
  xcirc:'<circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6"/><path d="M9 9l6 6"/>',
  warn:'<circle cx="12" cy="12" r="10"/><path d="M12 8v4"/><path d="M12 16h.01"/>',
};
function svg(name){return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">'+(ICONS[name]||'')+'</svg>';}

/* ---------- Kategorien + Aktionen (Spiegel zum C#-Katalog) ---------- */
const CATS = [
  {name:'Reparieren', icon:'wrench'},
  {name:'Netzwerk',   icon:'globe'},
  {name:'Aufräumen',  icon:'trash'},
  {name:'Diagnose',   icon:'activity'},
];
const ACTIONS = [
  {id:0,  cat:'Reparieren', icon:'wrench',      title:'Komplett-Reparatur', rec:true,  desc:'DISM ScanHealth + RestoreHealth, danach SFC. Der Rundum-Sorglos-Lauf.'},
  {id:1,  cat:'Reparieren', icon:'refresh',     title:'DISM RestoreHealth',           desc:'Repariert den Komponentenspeicher über Windows Update.'},
  {id:2,  cat:'Reparieren', icon:'shieldCheck', title:'SFC scannow',                  desc:'Prüft und repariert geschützte Systemdateien.'},
  {id:3,  cat:'Reparieren', icon:'search',      title:'SFC nur prüfen',               desc:'Sucht beschädigte Systemdateien, ohne etwas zu ändern.'},
  {id:4,  cat:'Reparieren', icon:'trash',       title:'WinSxS aufräumen',             desc:'Entfernt veraltete Komponenten – macht oft mehrere GB frei.'},
  {id:5,  cat:'Reparieren', icon:'activity',    title:'Komponentenspeicher analysieren', desc:'Zeigt, ob sich ein WinSxS-Cleanup lohnt.'},
  {id:6,  cat:'Reparieren', icon:'rotate',      title:'Windows-Update reparieren',    desc:'Setzt die Update-Komponenten zurück (SoftwareDistribution + catroot2).'},
  {id:7,  cat:'Reparieren', icon:'alert',       title:'CHKDSK planen', danger:true,    desc:'Plant eine Datenträgerprüfung beim nächsten Neustart.'},

  {id:8,  cat:'Netzwerk', icon:'globe',   title:'Netzwerk-Reset (komplett)', danger:true, desc:'DNS, Winsock und IP-Stack zurücksetzen. Neustart empfohlen.'},
  {id:9,  cat:'Netzwerk', icon:'globe',   title:'DNS-Cache leeren',              desc:'Löscht den DNS-Auflösungscache.'},
  {id:10, cat:'Netzwerk', icon:'refresh', title:'IP-Adresse erneuern',           desc:'Gibt die IP frei und fordert eine neue an.'},

  {id:11, cat:'Aufräumen', icon:'trash',    title:'Temp-Dateien löschen',        desc:'Leert Benutzer- und Windows-Temp-Ordner.'},
  {id:12, cat:'Aufräumen', icon:'download', title:'Update-Cache leeren',          desc:'Löscht heruntergeladene Update-Dateien.'},
  {id:13, cat:'Aufräumen', icon:'trash',    title:'Papierkorb leeren',            desc:'Leert den Papierkorb aller Laufwerke.'},
  {id:14, cat:'Aufräumen', icon:'server',   title:'Datenträgerbereinigung',       desc:'Öffnet das Windows-Tool cleanmgr.'},

  {id:15, cat:'Diagnose', icon:'cpu',     title:'System-Übersicht',             desc:'Modell, Windows-Version, RAM und Laufzeit auf einen Blick.'},
  {id:16, cat:'Diagnose', icon:'hdd',     title:'Festplatten-Gesundheit',       desc:'SMART-Status und Typ aller Datenträger.'},
  {id:17, cat:'Diagnose', icon:'battery', title:'Akkubericht erstellen',        desc:'Erzeugt einen powercfg-Akkubericht und öffnet ihn.'},
  {id:18, cat:'Diagnose', icon:'shield',  title:'Defender-Schnellscan',         desc:'Startet einen schnellen Microsoft-Defender-Scan.'},
  {id:19, cat:'Diagnose', icon:'cpu',     title:'RAM-Diagnose planen', danger:true, desc:'Öffnet die Windows-Speicherdiagnose (Neustart nötig).'},
];
const byId = id => ACTIONS.find(a => a.id === id);

/* ---------- Brücke zu C# (oder Mock im Browser) ---------- */
const HOST = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
function send(msg){ if(HOST){ HOST.postMessage(JSON.stringify(msg)); } else { mockHandle(msg); } }
if(HOST){ HOST.addEventListener('message', e => { try{ onHost(JSON.parse(e.data)); }catch(_){} }); }

function onHost(m){
  if(m.type==='log')   append(m.text, m.kind);
  else if(m.type==='state') setRunning(m.running);
  else if(m.type==='done')  onDone(m.title, m.kind, m.message);
  else if(m.type==='shutdownScheduled') showShutdownBar(m.mode, m.delay);
  else if(m.type==='shutdownCancelled') hideShutdownBar();
}

/* ---------- DOM ---------- */
const $ = s => document.querySelector(s);
const nav = $('#nav'), cards = $('#cards'), body = $('#console-body');
const consoleEl = $('#console'), main = $('#main'), statusEl = $('#status'), statusText = $('#status-text');
let active = 'Reparieren', running = false;
let post = 'none', delay = 60;
let queue = [];

$('#appmark').innerHTML = svg('wrench');
$('#btn-collapse').innerHTML = svg('chevron');
$('#queue-btn .qb-ico').innerHTML = svg('layers');
$('#q-close').innerHTML = svg('close');
$('#shutdown-bar .sb-ico').innerHTML = svg('power');
document.querySelector('.wc[data-win="min"]').innerHTML = svg('min');
document.querySelector('.wc[data-win="max"]').innerHTML = svg('max');
document.querySelector('.wc[data-win="close"]').innerHTML = svg('close');

/* ---------- Navigation + Karten ---------- */
function buildNav(){
  nav.innerHTML='';
  CATS.forEach(c=>{
    const el=document.createElement('div');
    el.className='nav-item'+(c.name===active?' active':'');
    el.innerHTML=svg(c.icon)+'<span class="nav-label">'+c.name+'</span>';
    el.onclick=()=>selectCat(c.name);
    nav.appendChild(el);
  });
}
function selectCat(name){
  active=name; buildNav();
  const list=ACTIONS.filter(a=>a.cat===name);
  $('#cat-title').textContent=name;
  $('#cat-hint').textContent=list.length+' Aktionen · klicken zum Ausführen';
  cards.innerHTML='';
  list.forEach((a,i)=>{
    const el=document.createElement('div');
    el.className='card'+(a.danger?' danger':'');
    el.dataset.id=a.id;
    el.style.animationDelay=(i*30)+'ms';
    el.innerHTML=
      '<button class="card-add" title="Zur Warteschlange">'+svg('plus')+'</button>'+
      '<div class="card-ico">'+svg(a.icon)+'</div>'+
      '<div class="card-body">'+
        '<div class="card-title">'+a.title+(a.rec?'<span class="tag">empfohlen</span>':'')+'</div>'+
        '<div class="card-desc">'+a.desc+'</div>'+
      '</div>';
    el.onclick=()=>run(a);
    el.querySelector('.card-add').onclick=(e)=>{ e.stopPropagation(); addToQueue(a.id); };
    cards.appendChild(el);
  });
  refreshAdded();
}

/* ---------- Konsole ---------- */
function append(text, kind){
  const ln=document.createElement('div');
  ln.className='ln '+(kind||'norm');
  ln.textContent=text;
  body.appendChild(ln);
  body.scrollTop=body.scrollHeight;
}
function setRunning(r){
  running=r;
  $('#btn-stop').disabled=!r;
  $('#q-run').disabled=r || queue.length===0;
  if(r){ statusEl.classList.add('run'); statusText.textContent='läuft …'; }
  else { statusEl.classList.remove('run'); statusText.textContent='bereit'; }
}
function setConsole(open){
  consoleEl.classList.toggle('open', open);
  main.classList.toggle('collapsed', !open);
}
$('#btn-collapse').onclick=()=>setConsole(false);
$('#console-reopen').onclick=()=>setConsole(true);
$('#btn-clear').onclick=()=>{ body.innerHTML=''; welcome(); };
$('#btn-save').onclick=()=>send({type:'save'});
$('#btn-stop').onclick=()=>send({type:'cancel'});

/* ---------- Post-Aktion ---------- */
document.querySelectorAll('#post-seg button').forEach(b=>{
  b.onclick=()=>{
    document.querySelectorAll('#post-seg button').forEach(x=>x.classList.remove('active'));
    b.classList.add('active');
    post=b.dataset.post;
    $('#post-delay-row').classList.toggle('hidden', post==='none');
    updateQueueAfter();
  };
});
$('#post-delay-row').classList.add('hidden');
$('#post-delay').onchange=()=>{ delay=clampDelay($('#post-delay').value); $('#post-delay').value=delay; updateQueueAfter(); };
function clampDelay(v){ v=parseInt(v,10); if(isNaN(v)) return 60; return Math.max(5, Math.min(86400, v)); }
function postLabel(){
  if(post==='none') return 'Danach: <b>nichts</b>';
  return 'Danach: <b>'+(post==='shutdown'?'Herunterfahren':'Neustart')+'</b> in '+delay+' Sek.';
}

/* ---------- Aktion direkt ausführen ---------- */
function run(a){
  if(running){ if(consoleEl.classList.contains('open')) append('Es läuft bereits eine Aktion – bitte warten oder stoppen.','warn'); return; }
  const restore = $('#restore').checked;
  const payload = ()=>send({type:'run', id:a.id, restore:restore, post:post, delay:delay});
  if(a.danger) confirmModal(a.title, a.desc, payload);
  else payload();
}
function onDone(title, kind, message){
  if(!consoleEl.classList.contains('open')) toast(title, message, kind);
}

/* ---------- Warteschlange ---------- */
function addToQueue(id){
  queue.push(id);
  renderQueue();
  pulseBadge();
}
function removeFromQueue(i){ queue.splice(i,1); renderQueue(); }
function moveQueue(i, dir){
  const j=i+dir;
  if(j<0||j>=queue.length) return;
  const t=queue[i]; queue[i]=queue[j]; queue[j]=t;
  renderQueue();
}
function renderQueue(){
  const count=queue.length;
  const badge=$('#queue-count');
  badge.textContent=count;
  badge.classList.toggle('has', count>0);
  $('#q-run').disabled = running || count===0;
  $('#q-list').classList.toggle('hidden', count===0);
  $('#q-empty').classList.toggle('hidden', count>0);

  const list=$('#q-list'); list.innerHTML='';
  queue.forEach((id,i)=>{
    const a=byId(id);
    const el=document.createElement('div');
    el.className='q-item'+(a.danger?' danger':'');
    el.innerHTML=
      '<span class="q-num">'+(i+1)+'</span>'+
      '<div class="q-ico">'+svg(a.icon)+'</div>'+
      '<span class="q-title">'+a.title+'</span>'+
      '<span class="q-ctrl">'+
        '<button data-a="up" title="Hoch">'+svg('up')+'</button>'+
        '<button data-a="down" title="Runter">'+svg('down')+'</button>'+
        '<button data-a="del" class="del" title="Entfernen">'+svg('close')+'</button>'+
      '</span>';
    el.querySelector('[data-a="up"]').onclick=()=>moveQueue(i,-1);
    el.querySelector('[data-a="down"]').onclick=()=>moveQueue(i,1);
    el.querySelector('[data-a="del"]').onclick=()=>removeFromQueue(i);
    list.appendChild(el);
  });
  updateQueueAfter();
  refreshAdded();
}
function updateQueueAfter(){ $('#q-after').innerHTML = postLabel(); }
function refreshAdded(){
  const set=new Set(queue);
  document.querySelectorAll('.card').forEach(c=>{
    const btn=c.querySelector('.card-add');
    if(btn) btn.classList.toggle('added', set.has(parseInt(c.dataset.id,10)));
  });
}
function pulseBadge(){
  const b=$('#queue-count'); b.animate?b.animate([{transform:'scale(1.4)'},{transform:'scale(1)'}],{duration:260,easing:'ease-out'}):0;
}
function openQueue(open){
  $('#queue').classList.toggle('open', open);
  $('#q-overlay').classList.toggle('show', open);
}
$('#queue-btn').onclick=()=>openQueue(true);
$('#q-close').onclick=()=>openQueue(false);
$('#q-overlay').onclick=()=>openQueue(false);
$('#q-clear').onclick=()=>{ queue=[]; renderQueue(); };
$('#q-run').onclick=()=>{
  if(running || queue.length===0) return;
  const restore=$('#restore').checked;
  const ids=queue.slice();
  const start=()=>{ send({type:'runQueue', ids:ids, restore:restore, post:post, delay:delay}); openQueue(false); };
  const dangerCount=ids.filter(id=>byId(id).danger).length;
  if(dangerCount>0) confirmModal('Warteschlange starten', dangerCount+' Aktion(en) erfordern eine Bestätigung. Alle '+ids.length+' der Reihe nach ausführen?', start);
  else start();
};

/* ---------- Shutdown-Banner ---------- */
let sbTimer=null;
function showShutdownBar(mode, secs){
  hideShutdownBar();
  const bar=$('#shutdown-bar'); bar.classList.add('show');
  let left=secs;
  const word = mode==='restart' ? 'neu gestartet' : 'heruntergefahren';
  const paint=()=>{ $('#sb-text').innerHTML='Der PC wird in <b>'+left+' Sek.</b> '+word+' …'; };
  paint();
  sbTimer=setInterval(()=>{ left--; if(left<=0){ clearInterval(sbTimer); sbTimer=null; } else paint(); }, 1000);
}
function hideShutdownBar(){ if(sbTimer){clearInterval(sbTimer);sbTimer=null;} $('#shutdown-bar').classList.remove('show'); }
$('#sb-cancel').onclick=()=>{ send({type:'cancelShutdown'}); hideShutdownBar(); };

/* ---------- Toasts ---------- */
function toast(title, msg, kind){
  const ico = kind==='good'?'check':(kind==='warn'?'warn':'xcirc');
  const el=document.createElement('div');
  el.className='toast '+(kind||'good');
  el.innerHTML=
    '<div class="toast-ico">'+svg(ico)+'</div>'+
    '<div class="toast-body"><div class="toast-title">'+title+'</div>'+
    '<div class="toast-msg">'+msg+'</div><div class="toast-more">Klicken für Details →</div></div>';
  el.onclick=()=>{ setConsole(true); dismiss(el); };
  $('#toasts').appendChild(el);
  el._timer=setTimeout(()=>dismiss(el), 4200);
}
function dismiss(el){ clearTimeout(el._timer); el.classList.add('out'); setTimeout(()=>el.remove(), 360); }

/* ---------- Bestätigungs-Dialog ---------- */
function confirmModal(title, desc, onYes){
  const ov=document.createElement('div'); ov.className='modal-ov';
  ov.innerHTML=
    '<div class="modal">'+
      '<div class="modal-ico">'+svg('alert')+'</div>'+
      '<h3>'+title+'</h3><p>'+desc+'</p>'+
      '<div class="modal-btns"><button class="mb cancel">Abbrechen</button><button class="mb ok">Ausführen</button></div>'+
    '</div>';
  document.body.appendChild(ov);
  ov.querySelector('.cancel').onclick=()=>ov.remove();
  ov.querySelector('.ok').onclick=()=>{ ov.remove(); onYes(); };
  ov.onclick=e=>{ if(e.target===ov) ov.remove(); };
}

/* ---------- Fenstersteuerung / Titelleiste ---------- */
document.querySelectorAll('.wc').forEach(b=>b.onclick=()=>send({type:'win', action:b.dataset.win}));
const tbDrag=document.querySelector('.tb-left');
tbDrag.addEventListener('mousedown', e=>{ if(e.button===0) send({type:'win', action:'drag'}); });
tbDrag.addEventListener('dblclick', ()=>send({type:'win', action:'max'}));

/* ---------- Mock (Browser-Vorschau ohne C#) ---------- */
function mockRun(jobs, mPost, mDelay){
  setRunning(true);
  let qi=0;
  const nextJob=()=>{
    if(qi>=jobs.length){
      append('✔  Erfolgreich abgeschlossen','good'); append('','norm'); setRunning(false);
      onDone(jobs.length>1?'Warteschlange':byId(jobs[0]).title,'good','Erfolgreich abgeschlossen');
      if(mPost!=='none') showShutdownBar(mPost, mDelay);
      return;
    }
    const a=byId(jobs[qi]); append('▶  '+a.title,'head');
    let s=0; const steps=['Wird ausgeführt …','Abschluss …'];
    const t=setInterval(()=>{
      if(s<steps.length){ append('›  '+steps[s],'dim'); s++; }
      else { clearInterval(t); qi++; nextJob(); }
    }, 260);
  };
  nextJob();
}
function mockHandle(msg){
  if(msg.type==='run') mockRun([msg.id], msg.post, msg.delay);
  else if(msg.type==='runQueue') mockRun(msg.ids, msg.post, msg.delay);
  else if(msg.type==='cancelShutdown') hideShutdownBar();
}

function welcome(){
  append('Windows-Wartung  ·  bereit','head');
  append('Aktion links auswählen und auf eine Kachel klicken.','dim');
  append('','norm');
}

/* ---------- Start ---------- */
buildNav();
selectCat('Reparieren');
renderQueue();
welcome();

/* Vorschau-Hilfen für Screenshots */
if(location.search.indexOf('seed')>=0){
  append('▶  Komplett-Reparatur','head');
  append('›  DISM /Online /Cleanup-Image /ScanHealth','dim');
  append('Der Vorgang wurde erfolgreich abgeschlossen.','norm');
  append('›  sfc /scannow','dim');
  append('Der Windows-Ressourcenschutz hat keine Integritätsverletzungen gefunden.','norm');
  append('✔  Erfolgreich in 142.3s','good');
}
if(location.hash.indexOf('queue')>=0){
  queue=[0,11]; post='shutdown';
  document.querySelectorAll('#post-seg button').forEach(x=>x.classList.toggle('active', x.dataset.post==='shutdown'));
  $('#post-delay-row').classList.remove('hidden');
  renderQueue(); openQueue(true);
}
if(location.hash.indexOf('shutdown')>=0){ showShutdownBar('shutdown', 58); }
if(location.hash.indexOf('toast')>=0){
  setConsole(false);
  toast('SFC scannow','Erfolgreich – keine Fehler','good');
  setTimeout(()=>toast('Netzwerk-Reset','Mit Hinweisen abgeschlossen','warn'),120);
}
