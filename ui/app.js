"use strict";
/* =====================================================================
   Windows-Wartung - Oberflaeche

   Leitgedanke: EIN Hauptweg. Beim Oeffnen sieht der Nutzer, wie es seinem PC
   geht, und genau eine grosse Schaltflaeche. Die 28 Einzelwerkzeuge bleiben
   vollstaendig erhalten, liegen aber hinter "Alle Werkzeuge".

   Der Aktionskatalog kommt vom Host (C#) und wird NICHT hier gepflegt -
   frueher standen die Texte doppelt und waren auseinandergelaufen.

   Anrede durchgehend "Sie".
   ===================================================================== */

/* ---------- Bruecke zum Host ---------- */
const HOST = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
function send(msg){ if(HOST) HOST.postMessage(JSON.stringify(msg)); }
if(HOST) HOST.addEventListener('message', e => {
  try{ onHost(JSON.parse(e.data)); }catch(err){ console.error(err); }
});

/* ---------- Kleinkram ---------- */
const $  = s => document.querySelector(s);
const $$ = s => Array.prototype.slice.call(document.querySelectorAll(s));
function esc(s){
  return String(s==null?'':s).replace(/[&<>"']/g, c =>
    ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}
function el(tag, cls, html){
  const n = document.createElement(tag);
  if(cls) n.className = cls;
  if(html != null) n.innerHTML = html;
  return n;
}

/* ---------- Zustand ---------- */
const S = {
  catalog: null,        // vom Host
  checks: [],           // letzter Befund
  screen: 'start',
  cat: null,            // gewaehlte Werkzeug-Kategorie
  mode: null,           // 'check' | 'fix'
  steps: [],
  stepIndex: 0,
  stepTotal: 0,
  result: null,
  admin: true,
  version: '',
};

const SET = {
  theme:   localStorage.getItem('theme')   || 'system',   // system | light | dark
  notify:  localStorage.getItem('notify')  !== 'false',
  confirm: localStorage.getItem('confirm') === 'true',
  autoUpdate: localStorage.getItem('autoUpdate') === 'true',
  zoom: 1,
};

/* Adresszusatz nur fuer Belegaufnahmen (--view im Shot-Modus): erzwingt ein
   Farbschema bzw. oeffnet direkt eine Ansicht. Im normalen Betrieb leer. */
let HASH = [];
try { HASH = decodeURIComponent((location.hash || '').slice(1)).split(',').filter(Boolean); }
catch(_){ HASH = (location.hash || '').slice(1).split(',').filter(Boolean); }
if(HASH.indexOf('light') >= 0) SET.theme = 'light';
if(HASH.indexOf('dark')  >= 0) SET.theme = 'dark';

/* ---------- Hell und dunkel ---------- */
const darkQuery = window.matchMedia('(prefers-color-scheme: dark)');
function applyTheme(){
  const dark = SET.theme === 'dark' || (SET.theme === 'system' && darkQuery.matches);
  document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
}
darkQuery.addEventListener('change', () => { if(SET.theme === 'system') applyTheme(); });
applyTheme();

/* ---------- Feste Symbole ---------- */
$('#appmark').innerHTML = svg('wrench');
$('.wc[data-win="min"]').innerHTML = svg('min');
$('.wc[data-win="max"]').innerHTML = svg('max');
$('.wc[data-win="close"]').innerHTML = svg('close');
$('#sb-ico').innerHTML = svg('power');
$('#ub-ico').innerHTML = svg('download');
$$('details.disc > summary > span[aria-hidden]').forEach(s => s.innerHTML = svg('chevron'));

/* =====================================================================
   Bildschirm-Wechsel
   ===================================================================== */
const SCREENS = { start:'#s-start', run:'#s-run', result:'#s-result', tools:'#s-tools', sub:'#s-sub' };
function show(name){
  S.screen = name;
  Object.keys(SCREENS).forEach(k => $(SCREENS[k]).classList.toggle('on', k === name));
  const box = $(SCREENS[name]);
  const h = box.querySelector('h1');
  if(h){ h.setAttribute('tabindex','-1'); h.focus({preventScroll:true}); }
  // Nach dem Fokuswechsel wieder ganz nach oben. Das Setzen im selben Durchlauf
  // reicht nicht: der Browser rollt danach noch zum fokussierten Knoten.
  box.scrollTop = 0;
  requestAnimationFrame(() => { box.scrollTop = 0; });
}
$$('[data-go]').forEach(b => b.onclick = () => go(b.dataset.go));
function go(where){
  if(where === 'start'){ show('start'); renderStart(); return; }
  if(where === 'tools'){ show('tools'); renderTools(); return; }
  openSub(where);
}

/* =====================================================================
   Startbildschirm
   ===================================================================== */
const STATE_WORD = { ok:'in Ordnung', warn:'beachten', bad:'Problem', unknown:'keine Daten' };

function renderStart(){
  const ico = $('#start-ico');
  const worst = overallOf(S.checks);

  if(!S.checks.length){
    ico.className = 'vico'; ico.innerHTML = svg('shield');
    $('#start-title').textContent = 'Ihr PC wurde noch nicht geprüft';
    $('#start-lead').textContent =
      'Die Prüfung schaut nach, ob mit Windows etwas nicht stimmt: beschädigte Dateien, ' +
      'zu wenig freier Speicher, Probleme beim Start. Sie verändert dabei nichts an Ihren ' +
      'Fotos, Dokumenten oder Programmen.';
  } else {
    const n = S.checks.filter(c => c.state === 'warn' || c.state === 'bad').length;
    ico.className = 'vico ' + worst;
    ico.innerHTML = svg(worst === 'ok' ? 'checkCircle' : 'alert');
    if(worst === 'ok'){
      $('#start-title').textContent = 'Auf den ersten Blick sieht alles gut aus';
      $('#start-lead').textContent =
        'Wir haben schnell nachgesehen und nichts Auffälliges gefunden. Die vollständige ' +
        'Prüfung schaut zusätzlich die Dateien von Windows durch, das dauert ein paar Minuten.';
    } else {
      $('#start-title').textContent = n === 1
        ? 'Eine Sache sollten Sie sich ansehen'
        : n + ' Dinge sollten Sie sich ansehen';
      $('#start-lead').textContent =
        'Das ist ein schneller erster Blick. Die vollständige Prüfung sieht zusätzlich nach, ' +
        'ob Dateien von Windows beschädigt sind, und kann vieles davon selbst beheben.';
    }
  }

  const box = $('#start-areas');
  box.innerHTML = '';
  if(!S.checks.length){
    box.appendChild(el('p','hint','Der erste Blick auf Ihren PC wird geladen …'));
  } else {
    S.checks.forEach(c => box.appendChild(areaRow(c)));
  }

  $('#admin-line').innerHTML = S.admin
    ? svg('shield') + '<span>Läuft mit Administratorrechten. Vor Änderungen legen wir einen Sicherungspunkt an.</span>'
    : svg('alert') + '<span>Ohne Administratorrechte. Prüfen geht, Reparieren nicht. Bitte starten Sie das Programm über die rechte Maustaste als Administrator.</span>';
}

function areaRow(c){
  const b = el('button','area');
  b.type = 'button';
  b.innerHTML =
    '<span class="dot ' + c.state + '" aria-hidden="true"></span>' +
    '<span class="area-b"><span class="area-t">' + esc(c.title) + '</span>' +
    '<span class="area-s">' + esc(c.summary) + '</span></span>' +
    '<span class="pill ' + c.state + '">' + STATE_WORD[c.state] + '</span>';
  b.setAttribute('aria-label', c.title + ': ' + STATE_WORD[c.state] + '. ' + c.summary);
  b.onclick = () => detailModal(c);
  return b;
}

function overallOf(list){
  let bad = false, warn = false, any = false;
  list.forEach(c => {
    if(c.state === 'unknown') return;
    any = true;
    if(c.state === 'bad') bad = true; else if(c.state === 'warn') warn = true;
  });
  if(!any) return 'unknown';
  return bad ? 'bad' : (warn ? 'warn' : 'ok');
}

$('#btn-check').onclick = () => {
  if(!S.admin){
    infoModal('Administratorrechte fehlen',
      'Zum Prüfen und Reparieren braucht das Programm Administratorrechte.\n\n' +
      'Schließen Sie es bitte, klicken Sie das Symbol mit der rechten Maustaste an und ' +
      'wählen Sie „Als Administrator ausführen“.', 'warn');
    return;
  }
  startFlow('check');
};

/* =====================================================================
   Ablauf
   ===================================================================== */
function startFlow(mode){
  S.mode = mode;
  S.steps = [];
  S.stepIndex = 0;
  $('#console-body').innerHTML = '';
  $('#run-steps').innerHTML = '';
  $('#run-pct').textContent = '';
  setBar(-1);

  $('#run-ico').className = 'vico';
  $('#run-ico').innerHTML = svg(mode === 'fix' ? 'wrench' : 'shield');
  $('#run-title').textContent = mode === 'fix' ? 'Ihr PC wird repariert' : 'Ihr PC wird geprüft';
  $('#run-lead').textContent = mode === 'fix'
    ? 'Wir beheben jetzt, was sich beheben lässt. Bitte lassen Sie den PC dabei eingeschaltet. ' +
      'Ein Sicherungspunkt ist angelegt, damit sich alles rückgängig machen lässt.'
    : 'Das dauert einen Moment. Sie können das Fenster ruhig zur Seite legen und weiterarbeiten. ' +
      'Wir melden uns, wenn wir fertig sind.';
  $('#run-foot').innerHTML = svg('info') +
    '<span>' + (mode === 'fix'
      ? 'Sie können abbrechen. Was bereits repariert wurde, bleibt erhalten.'
      : 'Sie können jederzeit abbrechen. Die Prüfung verändert nichts an Ihrem PC.') + '</span>';

  show('run');
  send({ type: mode === 'fix' ? 'startFix' : 'startCheck' });
}

function setBar(pct){
  const bar = $('#run-bar'), fill = bar.querySelector('i');
  if(pct == null || pct < 0){
    bar.classList.add('indeterminate');
    bar.removeAttribute('aria-valuenow');
    fill.style.width = '35%';
    $('#run-pct').textContent = '';
  } else {
    bar.classList.remove('indeterminate');
    bar.setAttribute('aria-valuenow', pct);
    fill.style.width = Math.max(0, Math.min(100, pct)) + '%';
    $('#run-pct').textContent = pct + ' %';
  }
}

function renderSteps(){
  const box = $('#run-steps');
  box.innerHTML = '';
  S.steps.forEach((s, i) => {
    const n = i + 1;
    const cls = n < S.stepIndex ? 'done' : (n === S.stepIndex ? 'now' : 'todo');
    const row = el('div','step ' + cls);
    row.innerHTML =
      '<span class="snum">' + (cls === 'done' ? svg('check') : n) + '</span>' +
      '<span class="step-t">' + esc(s) + '</span>' +
      '<span class="step-m">' + (cls === 'done' ? 'erledigt' : (cls === 'now' ? 'läuft …' : 'wartet')) + '</span>';
    box.appendChild(row);
  });
}

$('#btn-cancel').onclick = () => {
  confirmModal('Vorgang abbrechen?',
    S.mode === 'fix'
      ? 'Was bereits repariert wurde, bleibt erhalten. Den Rest können Sie später erneut starten.'
      : 'Die Prüfung wird beendet. Ihrem PC passiert dabei nichts.',
    'Abbrechen', () => { send({type:'cancelFlow'}); });
};

/* =====================================================================
   Ergebnis
   ===================================================================== */
function renderResult(r){
  S.result = r;
  const ico = $('#res-ico');
  ico.className = 'vico ' + r.overall;
  ico.innerHTML = svg(r.overall === 'ok' ? 'checkCircle' : 'alert');

  const fixedNow = r.mode === 'fix';
  if(r.overall === 'ok'){
    $('#res-title').textContent = fixedNow ? 'Fertig, Ihr PC ist in Ordnung' : 'Alles in Ordnung';
    $('#res-lead').textContent = fixedNow
      ? 'Wir haben behoben, was zu beheben war. Es ist nichts weiter zu tun.'
      : 'Wir haben nachgesehen und nichts gefunden, das Sie stören müsste.';
  } else if(r.problems === 1){
    $('#res-title').textContent = 'Ein Punkt braucht Ihre Aufmerksamkeit';
    $('#res-lead').textContent = fixedNow
      ? 'Was wir selbst beheben konnten, haben wir behoben. Für einen Punkt brauchen wir kurz Ihre Hilfe.'
      : 'Die Prüfung ist fertig. Ein Punkt ist aufgefallen.';
  } else {
    $('#res-title').textContent = r.problems + ' Punkte brauchen Ihre Aufmerksamkeit';
    $('#res-lead').textContent = fixedNow
      ? 'Was wir selbst beheben konnten, haben wir behoben. Um den Rest kümmern Sie sich am besten selbst.'
      : 'Die Prüfung ist fertig. Diese Punkte sind aufgefallen.';
  }

  // Befunde ausführlich, Unauffälliges zusammengefasst. Sonst rutscht die
  // Handlungsempfehlung unter den sichtbaren Bereich und wird übersehen.
  const rank = { bad:0, warn:1 };
  const issues = r.checks.filter(c => c.state === 'bad' || c.state === 'warn')
                         .sort((a,b) => rank[a.state] - rank[b.state]);
  const good    = r.checks.filter(c => c.state === 'ok');
  const unknown = r.checks.filter(c => c.state === 'unknown');

  const box = $('#res-list');
  box.innerHTML = '';
  issues.forEach(c => box.appendChild(resultCard(c)));

  // Nicht Prüfbares wird getrennt ausgewiesen. Es unter "unauffällig" zu verbuchen
  // wäre eine Behauptung, die wir nicht belegen können.
  if(good.length)    box.appendChild(groupCard('ok', good,
    good.length === 1 ? 'Ein weiterer Bereich ist unauffällig'
                      : good.length + ' weitere Bereiche sind unauffällig'));
  if(unknown.length) box.appendChild(groupCard('unknown', unknown,
    unknown.length === 1 ? 'Ein Bereich ließ sich nicht prüfen'
                         : unknown.length + ' Bereiche ließen sich nicht prüfen'));

  // "Was Sie jetzt tun sollten" - genau EINE Empfehlung, mit hoechstens einer Hauptaktion.
  const next = $('#res-next');
  next.hidden = false;
  next.innerHTML = '';
  if(r.fixable && r.mode === 'check'){
    next.appendChild(el('span','next-b',
      '<div class="next-t">Was Sie jetzt tun sollten</div>' +
      '<div class="next-s">Vieles davon können wir selbst beheben. Vorher legen wir einen ' +
      'Sicherungspunkt an, damit sich alles rückgängig machen lässt.</div>'));
    const b = el('button','btn btn-primary btn-md','Gefundene Probleme beheben');
    b.onclick = () => startFlow('fix');
    next.appendChild(b);
  } else if(r.restart){
    next.appendChild(el('span','next-b',
      '<div class="next-t">Was Sie jetzt tun sollten</div>' +
      '<div class="next-s">Starten Sie den PC einmal neu. Erst dann sind die Änderungen ' +
      'vollständig abgeschlossen.</div>'));
    const b = el('button','btn btn-primary btn-md','Jetzt neu starten');
    b.onclick = () => confirmModal('PC neu starten?',
      'Bitte speichern Sie vorher Ihre Arbeit. Der Neustart beginnt in 60 Sekunden, ' +
      'Sie können ihn im Banner unten noch abbrechen.',
      'Neu starten', () => send({type:'restartNow'}));
    next.appendChild(b);
    const l = el('button','btn btn-ghost','Später');
    l.onclick = () => { next.hidden = true; };
    next.appendChild(l);
  } else if(r.overall === 'ok'){
    next.appendChild(el('span','next-b',
      '<div class="next-t">Sie müssen nichts weiter tun</div>' +
      '<div class="next-s">Eine Prüfung alle paar Monate genügt. Unter „Automatische Wartung“ ' +
      'können Sie das auch von allein laufen lassen.</div>'));
  } else {
    next.appendChild(el('span','next-b',
      '<div class="next-t">Was Sie jetzt tun sollten</div>' +
      '<div class="next-s">Die Hinweise oben sagen bei jedem Punkt, was zu tun ist. ' +
      'Das meiste davon eilt nicht.</div>'));
  }

  $('#res-foot').innerHTML = svg('clock') +
    '<span>Geprüft am ' + esc(r.when || '') + '</span>';
  $('#console-body-2').innerHTML = $('#console-body').innerHTML;

  show('result');
}

function groupCard(state, list, title){
  const card = el('div','rcard ' + state);
  card.innerHTML =
    '<span class="rico">' + svg(state === 'ok' ? 'check' : 'info') + '</span>' +
    '<span class="rbody"><span class="rt">' + esc(title) + '</span>' +
    '<div class="rs">' + esc(list.map(c => c.title).join(' · ')) + '</div></span>';
  const d = el('details','disc');
  d.style.marginTop = '0';
  d.innerHTML = '<summary>' + svg('chevron') + ' <b>Im Einzelnen ansehen</b></summary>' +
    '<div class="disc-body">' + list.map(c =>
      '<p><b>' + esc(c.title) + ':</b> ' + esc(c.summary) +
      (c.detail ? '<br><span style="color:var(--text-3);white-space:pre-line">' + esc(c.detail) + '</span>' : '') +
      '</p>').join('') + '</div>';
  card.querySelector('.rbody').appendChild(d);
  return card;
}

function resultCard(c){
  const card = el('div','rcard ' + c.state);
  card.innerHTML =
    '<span class="rico">' + svg(c.state === 'ok' ? 'check' : (c.state === 'unknown' ? 'info' : 'alert')) + '</span>' +
    '<span class="rbody">' +
      '<span class="rt">' + esc(c.title) + '</span>' +
      '<div class="rs">' + esc(c.summary) + '</div>' +
      (c.advice ? '<div class="radv">' + esc(c.advice) + '</div>' : '') +
    '</span>';
  if(c.detail){
    const d = el('details','disc');
    d.style.marginTop = '0';
    d.innerHTML = '<summary>' + svg('chevron') + ' <b>Einzelheiten</b></summary>' +
                  '<div class="disc-body"><p style="white-space:pre-line">' + esc(c.detail) + '</p></div>';
    card.querySelector('.rbody').appendChild(d);
  }
  return card;
}

$('#btn-save').onclick = () => send({type:'save'});

/* =====================================================================
   Werkzeugkasten
   ===================================================================== */
function renderTools(){
  if(!S.catalog) return;
  const tabs = $('#tool-tabs');
  tabs.innerHTML = '';
  const catIcons = { 'Reparieren':'wrench', 'Netzwerk':'globe', 'Aufräumen':'trash', 'Diagnose':'search' };
  if(!S.cat) S.cat = S.catalog.categories[0];

  S.catalog.categories.forEach(c => {
    const b = el('button','tab', svg(catIcons[c] || 'list') + esc(c));
    b.type = 'button';
    b.setAttribute('role','tab');
    b.setAttribute('aria-selected', String(c === S.cat));
    b.onclick = () => { S.cat = c; renderTools(); };
    tabs.appendChild(b);
  });

  const note = S.catalog.notes && S.catalog.notes[S.cat];
  $('#tool-note').innerHTML = note
    ? '<div class="catnote">' + svg('info') + '<div>' + esc(note) + '</div></div>' : '';

  const cards = $('#tool-cards');
  cards.innerHTML = '';
  S.catalog.actions.filter(a => a.cat === S.cat).forEach(a => cards.appendChild(toolCard(a)));

  $('#tools-foot').innerHTML = svg('info') +
    '<span>Weitere Bereiche:</span>';
}

function toolCard(a){
  const wrap = el('div','');
  wrap.style.position = 'relative';

  const b = el('button','card' + (a.danger ? ' danger' : ''));
  b.type = 'button';
  b.innerHTML =
    '<span class="card-ico">' + svg(a.icon || 'wrench') + '</span>' +
    '<span class="card-b">' +
      '<span class="card-t">' + esc(a.title) +
        (a.danger ? '<span class="tag warn">Nachfrage</span>' : '') + '</span>' +
      '<span class="card-d">' + esc(a.desc) + '</span>' +
      (a.tech ? '<span class="card-tech">' + esc(a.tech) + '</span>' : '') +
    '</span>';
  b.onclick = () => runAction(a);
  wrap.appendChild(b);

  const h = el('button','card-help', svg('help'));
  h.type = 'button';
  h.setAttribute('aria-label','Was macht „' + a.title + '“?');
  h.onclick = (e) => { e.stopPropagation(); infoModal(a.title, a.info || a.desc, 'info', a.tech); };
  wrap.appendChild(h);
  return wrap;
}

function runAction(a){
  if(!S.admin){
    infoModal('Administratorrechte fehlen',
      'Diese Aktion braucht Administratorrechte. Bitte schließen Sie das Programm und starten ' +
      'Sie es über die rechte Maustaste als Administrator.', 'warn');
    return;
  }
  const start = () => {
    $('#console-body').innerHTML = '';
    S.mode = 'action';
    S.steps = [a.title];
    S.stepIndex = 1;
    S.stepTotal = 1;
    $('#run-ico').className = 'vico';
    $('#run-ico').innerHTML = svg(a.icon || 'wrench');
    $('#run-title').textContent = a.title;
    $('#run-lead').textContent = a.desc;
    $('#run-foot').innerHTML = svg('info') + '<span>Sie können jederzeit abbrechen.</span>';
    renderSteps();
    setBar(-1);
    $('#run-status').textContent = 'Läuft …';
    show('run');
    send({ type:'run', id:a.id, restore:true, post:'none', delay:60 });
  };

  if(a.danger || SET.confirm){
    confirmModal(a.title,
      (a.info || a.desc) + (a.restore ? '\n\nVorher legen wir einen Sicherungspunkt an, damit sich das rückgängig machen lässt.' : ''),
      'Jetzt ausführen', start, a.danger);
  } else start();
}

/* =====================================================================
   Nebenansichten
   ===================================================================== */
const SUBS = {
  history:   { title:'Verlauf',              lead:'Was in der Vergangenheit ausgeführt wurde.' },
  restore:   { title:'Sicherungspunkte',     lead:'Ein Sicherungspunkt merkt sich den Zustand von Windows, Programmen und Einstellungen. Damit lässt sich der PC auf einen früheren Stand zurücksetzen. Ihre persönlichen Dateien bleiben dabei unberührt.' },
  schedule:  { title:'Automatische Wartung', lead:'Der PC kann sich regelmäßig selbst warten, ganz von allein im Hintergrund.' },
  autostart: { title:'Startprogramme',       lead:'Diese Programme starten automatisch mit, wenn Sie den PC einschalten. Je weniger es sind, desto schneller ist Ihr PC startklar. Ausschalten ist jederzeit umkehrbar.' },
  apps:      { title:'Vorinstallierte Apps', lead:'Apps, die ab Werk auf dem PC waren und die viele nie benutzen. Aus Sicherheit bieten wir nur Apps an, deren Entfernen bekanntermaßen unbedenklich ist.' },
  power:     { title:'Energie',              lead:'Der Energieplan steuert das Verhältnis von Leistung und Stromverbrauch. Mehr Leistung heißt mehr Strom und bei Laptops weniger Akkulaufzeit.' },
  settings:  { title:'Einstellungen',        lead:'Aussehen und Verhalten des Programms.' },
};
function openSub(key){
  const s = SUBS[key];
  if(!s) return;
  S.sub = key;
  $('#sub-title').textContent = s.title;
  $('#sub-lead').textContent = s.lead;
  $('#sub-body').innerHTML = '<div class="empty">Wird geladen …</div>';
  $('#sub-foot').innerHTML = '';
  show('sub');

  if(key === 'history')  send({type:'historyList'});
  if(key === 'restore')  send({type:'restoreList'});
  if(key === 'power')    send({type:'powerList'});
  if(key === 'apps')     send({type:'bloatList'});
  if(key === 'schedule') send({type:'scheduleStatus'});
  if(key === 'autostart'){ send({type:'autostartList'}); send({type:'selfStartGet'}); }
  if(key === 'settings') renderSettings();
}

function renderSettings(){
  const body = $('#sub-body');
  body.innerHTML = '';

  const themes = [['system','Wie Windows','monitor'],['light','Hell','sun'],['dark','Dunkel','moon']];
  const p1 = el('div','panel');
  p1.innerHTML = '<div class="panel-h"><div class="panel-t">Aussehen</div>' +
    '<div class="panel-d">Hell oder dunkel. „Wie Windows“ übernimmt Ihre Windows-Einstellung.</div></div>';
  const row = el('div','row');
  const seg = el('div','tabs');
  seg.style.margin = '0';
  themes.forEach(t => {
    const b = el('button','tab', svg(t[2]) + t[1]);
    b.type = 'button';
    b.setAttribute('aria-selected', String(SET.theme === t[0]));
    b.onclick = () => { SET.theme = t[0]; localStorage.setItem('theme', t[0]); applyTheme(); renderSettings(); };
    seg.appendChild(b);
  });
  row.appendChild(seg);
  p1.appendChild(row);
  body.appendChild(p1);

  const zooms = [[0.9,'90 %'],[1,'100 %'],[1.15,'115 %'],[1.3,'130 %'],[1.5,'150 %'],[1.75,'175 %']];
  const p2 = el('div','panel');
  p2.innerHTML = '<div class="panel-h"><div class="panel-t">Schriftgröße</div>' +
    '<div class="panel-d">Alles größer anzeigen. Angenehm, wenn die Schrift sonst zu klein ist.</div></div>';
  const row2 = el('div','row');
  const seg2 = el('div','tabs');
  seg2.style.margin = '0';
  zooms.forEach(z => {
    const b = el('button','tab', z[1]);
    b.type = 'button';
    b.setAttribute('aria-selected', String(Math.abs(z[0] - SET.zoom) < 0.001));
    b.onclick = () => { SET.zoom = z[0]; send({type:'setZoom', factor:z[0]}); renderSettings(); };
    seg2.appendChild(b);
  });
  row2.appendChild(seg2);
  p2.appendChild(row2);
  body.appendChild(p2);

  const p3 = el('div','panel');
  p3.innerHTML = '<div class="panel-h"><div class="panel-t">Verhalten</div></div>';
  p3.appendChild(switchRow('Mitteilung, wenn etwas fertig ist',
    'Windows zeigt eine kurze Meldung, wenn das Fenster gerade im Hintergrund ist.',
    SET.notify, v => { SET.notify = v; localStorage.setItem('notify', v); send({type:'setNotify', on:v}); }));
  p3.appendChild(switchRow('Immer vor dem Ausführen fragen',
    'Auch bei harmlosen Aktionen erst eine Sicherheitsfrage stellen.',
    SET.confirm, v => { SET.confirm = v; localStorage.setItem('confirm', v); }));
  p3.appendChild(switchRow('Neue Fassungen selbst installieren',
    'Findet das Programm beim Start eine neue Fassung, installiert es sie ohne Nachfrage und startet neu. Die Echtheit wird weiterhin geprüft.',
    SET.autoUpdate, v => { SET.autoUpdate = v; localStorage.setItem('autoUpdate', v); }));
  body.appendChild(p3);

  const p4 = el('div','panel');
  p4.innerHTML = '<div class="panel-h"><div class="panel-t">Über dieses Programm</div>' +
    '<div class="panel-d">Fassung ' + esc(S.version || '–') + '</div></div>';
  const r4 = el('div','row');
  r4.innerHTML = '<span class="row-b"><span class="row-t">Fehlerprotokoll</span>' +
    '<span class="row-s">Falls einmal etwas nicht klappt, steht hier, was passiert ist.</span></span>';
  const ob = el('button','btn btn-ghost','Protokoll öffnen');
  ob.onclick = () => send({type:'openLog'});
  r4.appendChild(ob);
  p4.appendChild(r4);
  body.appendChild(p4);
}

function switchRow(title, desc, on, onChange){
  const id = 'sw-' + Math.random().toString(36).slice(2,9);
  const row = el('label','row');
  row.setAttribute('for', id);
  row.innerHTML =
    '<span class="row-b"><span class="row-t">' + esc(title) + '</span>' +
    '<span class="row-s">' + esc(desc) + '</span></span>' +
    '<span class="switch"><input type="checkbox" id="' + id + '"' + (on ? ' checked' : '') + ' /><i></i></span>';
  row.querySelector('input').onchange = e => onChange(e.target.checked);
  return row;
}

/* =====================================================================
   Dialoge - mit Escape, Fokusfalle und Rollen
   Frueher fehlte all das ausgerechnet bei der Systemwiederherstellung.
   ===================================================================== */
let openDialog = null;

function buildModal(opts){
  const prev = document.activeElement;
  const ov = el('div','modal-ov');
  const box = el('div','modal');
  box.setAttribute('role','dialog');
  box.setAttribute('aria-modal','true');
  box.setAttribute('aria-labelledby','dlg-t');

  box.innerHTML =
    (opts.icon ? '<div class="modal-ico ' + (opts.tone || '') + '">' + svg(opts.icon) + '</div>' : '') +
    '<h2 id="dlg-t">' + esc(opts.title) + '</h2>' +
    (opts.lead ? '<div class="lead">' + esc(opts.lead) + '</div>' : '') +
    '<p>' + esc(opts.body) + '</p>' +
    (opts.tech ? '<p class="card-tech" style="margin-top:12px">Fachlich: ' + esc(opts.tech) + '</p>' : '');

  const btns = el('div','modal-btns');
  box.appendChild(btns);
  ov.appendChild(box);
  document.body.appendChild(ov);

  function close(){
    ov.remove();
    openDialog = null;
    document.removeEventListener('keydown', keys, true);
    try{ prev && prev.focus(); }catch(_){}
  }
  function keys(e){
    if(e.key === 'Escape'){ e.preventDefault(); close(); return; }
    if(e.key !== 'Tab') return;
    // Fokusfalle: der Tabulator darf den Dialog nicht verlassen.
    const f = box.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])');
    if(!f.length) return;
    const first = f[0], last = f[f.length - 1];
    if(e.shiftKey && document.activeElement === first){ e.preventDefault(); last.focus(); }
    else if(!e.shiftKey && document.activeElement === last){ e.preventDefault(); first.focus(); }
  }
  ov.onclick = e => { if(e.target === ov) close(); };
  document.addEventListener('keydown', keys, true);
  openDialog = { close: close };
  return { ov: ov, box: box, btns: btns, close: close };
}

function infoModal(title, body, tone, tech){
  const m = buildModal({ title:title, body:body, icon: tone === 'warn' ? 'alert' : 'help',
                         tone: tone === 'warn' ? 'warn' : '', lead:'In einfachen Worten', tech:tech });
  const ok = el('button','btn btn-primary btn-md','Verstanden');
  ok.onclick = m.close;
  m.btns.appendChild(ok);
  ok.focus();
}

function detailModal(c){
  const m = buildModal({
    title: c.title,
    body: c.summary + (c.advice ? '\n\nWas Sie tun können:\n' + c.advice : '') +
          (c.detail ? '\n\nEinzelheiten:\n' + c.detail : ''),
    icon: c.state === 'ok' ? 'checkCircle' : (c.state === 'unknown' ? 'info' : 'alert'),
    tone: c.state === 'bad' ? 'bad' : (c.state === 'warn' ? 'warn' : ''),
  });
  const ok = el('button','btn btn-primary btn-md','Schließen');
  ok.onclick = m.close;
  m.btns.appendChild(ok);
  ok.focus();
}

function confirmModal(title, body, okLabel, onOk, dangerous){
  const m = buildModal({ title:title, body:body, icon: dangerous ? 'alert' : 'help',
                         tone: dangerous ? 'warn' : '' });
  const cancel = el('button','btn btn-ghost','Abbrechen');
  cancel.onclick = m.close;
  const ok = el('button', dangerous ? 'btn btn-danger' : 'btn btn-primary btn-md', okLabel || 'Fortfahren');
  ok.onclick = () => { m.close(); onOk(); };
  m.btns.appendChild(cancel);
  m.btns.appendChild(ok);
  // Bei riskanten Aktionen liegt der Fokus auf dem sicheren Weg.
  (dangerous ? cancel : ok).focus();
}

/* =====================================================================
   Meldungen
   ===================================================================== */
function toast(title, msg, kind){
  const t = el('div','toast ' + (kind || 'good'));
  t.innerHTML =
    '<span class="toast-ico">' + svg(kind === 'bad' ? 'xcirc' : (kind === 'warn' ? 'alert' : 'check')) + '</span>' +
    '<span><span class="toast-t">' + esc(title) + '</span>' +
    '<span class="toast-m">' + esc(msg) + '</span></span>';
  t.onclick = () => t.remove();
  $('#toasts').appendChild(t);
  setTimeout(() => t.remove(), 6000);
}

function appendLine(text, kind){
  const box = $('#console-body');
  const ln = el('div','ln ' + (kind || 'norm'));
  ln.textContent = text;
  box.appendChild(ln);
  box.scrollTop = box.scrollHeight;
  while(box.childNodes.length > 2000) box.removeChild(box.firstChild);
}

/* =====================================================================
   Nachrichten vom Host
   ===================================================================== */
function onHost(m){
  switch(m.type){
    case 'catalog':
      S.catalog = m;
      S.version = m.version || '';
      // Belegaufnahme: gewuenschte Ansicht direkt oeffnen
      ['tools','history','restore','schedule','autostart','apps','power','settings']
        .forEach(k => { if(HASH.indexOf(k) >= 0) go(k); });
      // Nur fuer Belegaufnahmen: die (rein lesende) Pruefung von selbst starten.
      // Der Adresszusatz laesst sich ausschliesslich ueber --view von der Befehlszeile
      // setzen, im normalen Betrieb ist er leer.
      if(HASH.indexOf('check') >= 0) startFlow('check');
      break;

    case 'admin':
      S.admin = !!m.on;
      renderStart();
      break;

    case 'zoom':
      SET.zoom = m.factor || 1;
      break;

    case 'lastChecks':
      S.checks = m.checks || [];
      if(S.screen === 'start') renderStart();
      break;

    case 'flowStart':
      S.stepTotal = m.total || 0;
      S.steps = [];
      S.stepIndex = 0;
      renderSteps();
      break;

    case 'flowStep':
      S.stepIndex = m.index;
      if(S.steps.length < m.index) S.steps.push(m.label);
      else S.steps[m.index - 1] = m.label;
      renderSteps();
      setBar(-1);
      $('#run-status').textContent = 'Schritt ' + m.index + ' von ' + S.stepTotal + ' · ' + m.label;
      break;

    case 'flowDetail':
      if(S.screen === 'run') $('#run-status').textContent = m.text + ' …';
      break;

    case 'flowPercent':
      setBar(m.percent);
      break;

    case 'flowResult':
      renderResult(m);
      S.checks = (m.checks || []).filter(c => c.key !== 'files');
      break;

    case 'flowCancelled':
      toast('Abgebrochen', 'Der Vorgang wurde beendet. Ihrem PC ist nichts passiert.', 'warn');
      go('start');
      break;

    case 'flowError':
      infoModal('Das hat nicht geklappt', m.message, 'warn');
      go('start');
      break;

    // --- Einzelaktionen aus dem Werkzeugkasten ---
    case 'log':
      appendLine(m.text, m.kind);
      break;

    case 'progress':
      setBar(m.percent);
      break;

    case 'state':
      if(!m.running && S.mode === 'action'){ S.stepIndex = 2; renderSteps(); }
      break;

    case 'done':
      if(S.mode === 'action'){
        S.stepIndex = 2; renderSteps(); setBar(100);
        $('#run-status').textContent = m.message;
        $('#run-foot').innerHTML = svg(m.kind === 'good' ? 'check' : 'alert') + '<span>' + esc(m.message) + '</span>';
        $('#btn-cancel').textContent = 'Zurück';
        $('#btn-cancel').onclick = () => { $('#btn-cancel').textContent = 'Abbrechen'; go('tools'); };
      }
      if(SET.notify) toast(m.title, m.message, m.kind);
      break;

    // --- Nebenansichten ---
    case 'history':      renderHistory(m.items); break;
    case 'restorePoints':renderRestore(m.items); break;
    case 'powerPlans':   renderPower(m.items); break;
    case 'bloatPackages':renderApps(m.items); break;
    case 'autostart':    renderAutostart(m.items); break;
    case 'schedule':     renderSchedule(m); break;

    // --- Update ---
    case 'update':
      $('#ub-text').textContent = 'Es gibt eine neue Fassung (' + (m.version || '') + ').';
      $('#update-bar').classList.add('show');
      break;
    case 'updateProgress':
      $('#ub-text').textContent = m.percent >= 0
        ? 'Neue Fassung wird geladen … ' + m.percent + ' %'
        : 'Neue Fassung wird geladen …';
      break;
    case 'updateStatus':
      $('#ub-text').textContent = m.phase === 'extract' ? 'Wird ausgepackt …'
        : (m.phase === 'restart' ? 'Das Programm startet gleich neu …' : 'Wird geladen …');
      break;
    case 'updateError':
      $('#update-bar').classList.remove('show');
      infoModal('Die Aktualisierung hat nicht geklappt', m.message, 'warn');
      break;
    case 'updated':
      toast('Aktualisiert', 'Das Programm läuft jetzt in der Fassung ' + m.version + '.', 'good');
      break;

    case 'shutdownScheduled':
      $('#sb-text').textContent = 'Der PC wird in ' + m.delay + ' Sekunden ' +
        (m.mode === 'restart' ? 'neu gestartet' : 'heruntergefahren') + '.';
      $('#shutdown-bar').classList.add('show','alarm');
      break;
    case 'shutdownCancelled':
      $('#shutdown-bar').classList.remove('show','alarm');
      toast('Abgebrochen', 'Der PC bleibt an.', 'good');
      break;
  }
}

/* ---------- Nebenansichten rendern ---------- */
function renderHistory(items){
  const b = $('#sub-body');
  if(!items || !items.length){
    b.innerHTML = '<div class="empty">Hier ist noch nichts eingetragen.<br>' +
      'Sobald Sie eine Prüfung oder ein Werkzeug starten, erscheint es hier.</div>';
    return;
  }
  const words = { good:'Erfolgreich', bad:'Fehlgeschlagen', warn:'Mit Hinweisen' };
  let h = '<div class="panel"><div class="panel-b">';
  items.forEach(it => {
    const k = ['good','bad','warn'].indexOf(it.kind) >= 0 ? it.kind : 'unknown';
    h += '<div class="row"><span class="dot ' + (k === 'good' ? 'ok' : (k === 'bad' ? 'bad' : 'warn')) + '"></span>' +
      '<span class="row-b"><span class="row-t">' + esc(it.action) + '</span>' +
      '<span class="row-s">' + esc(words[k] || 'Ausgeführt') + (it.message ? ' · ' + esc(it.message) : '') + '</span></span>' +
      '<span class="row-m">' + esc(it.time || '') + '</span></div>';
  });
  h += '</div></div>';
  b.innerHTML = h;
}

function renderRestore(items){
  const b = $('#sub-body');
  let h = '<div class="panel"><div class="panel-h"><div class="panel-t">Neuen Sicherungspunkt anlegen</div>' +
    '<div class="panel-d">Sichert den aktuellen Zustand. Sinnvoll, bevor Sie etwas Größeres ändern.</div></div>' +
    '<div class="row"><span class="row-b"><input type="text" id="rp-desc" maxlength="60" ' +
    'placeholder="Beschreibung (freiwillig)" style="width:100%" /></span>' +
    '<button class="btn btn-primary btn-md" id="rp-new">Anlegen</button></div></div>';

  if(!items || !items.length){
    h += '<div class="empty">Es sind keine Sicherungspunkte vorhanden.<br>' +
         'Möglicherweise ist der Systemschutz von Windows ausgeschaltet. Legen Sie oben einen Punkt an, um ihn einzuschalten.</div>';
  } else {
    h += '<div class="panel"><div class="panel-b">';
    items.forEach(it => {
      h += '<div class="row"><span class="row-b"><span class="row-t">' +
        esc(it.desc || 'Ohne Beschreibung') + '</span>' +
        '<span class="row-s">' + esc(it.time || '') + ' · ' + rpType(it.rtype) + '</span></span>' +
        '<button class="btn btn-ghost" data-seq="' + esc(it.seq) + '">Hierhin zurück</button></div>';
    });
    h += '</div></div>';
  }
  b.innerHTML = h;
  $('#rp-new').onclick = () => send({type:'restoreCreate', desc:($('#rp-desc').value || '').trim()});
  b.querySelectorAll('[data-seq]').forEach(btn => {
    btn.onclick = () => {
      const seq = parseInt(btn.dataset.seq, 10);
      confirmModal('Windows zurücksetzen?',
        'Windows wird auf diesen früheren Stand zurückgesetzt und der PC startet dabei neu.\n\n' +
        'Ihre persönlichen Dateien bleiben erhalten. Programme, Treiber und Einstellungen, die Sie ' +
        'seitdem geändert haben, gehen verloren.',
        'Weiter', () => confirmModal('Wirklich? Letzte Nachfrage',
          'Bitte speichern Sie jetzt Ihre Arbeit. Der PC startet gleich neu.',
          'Jetzt zurücksetzen', () => send({type:'restoreRevert', seq:seq}), true), true);
    };
  });
}
function rpType(t){
  t = parseInt(t, 10);
  if(t === 0)  return 'vor einer Programm-Installation';
  if(t === 10) return 'vor einer Systemänderung';
  if(t === 12) return 'vor einer Einstellungsänderung';
  if(t === 13) return 'vor einer Deinstallation';
  return 'angelegt';
}

function renderPower(items){
  const b = $('#sub-body');
  if(!items || !items.length){ b.innerHTML = '<div class="empty">Es wurden keine Energiepläne gefunden.</div>'; return; }
  let h = '<div class="panel"><div class="panel-b">';
  items.forEach(p => {
    h += '<div class="row"><span class="row-b"><span class="row-t">' + esc(p.name) + '</span></span>' +
      (p.active ? '<span class="pill ok">aktiv</span>'
                : '<button class="btn btn-ghost" data-guid="' + esc(p.guid) + '">Auswählen</button>') + '</div>';
  });
  h += '</div></div>';
  b.innerHTML = h;
  b.querySelectorAll('[data-guid]').forEach(x => x.onclick = () => send({type:'powerSet', guid:x.dataset.guid}));
}

function renderAutostart(items){
  const b = $('#sub-body');
  if(!items || !items.length){ b.innerHTML = '<div class="empty">Es starten keine zusätzlichen Programme mit.</div>'; return; }
  const groups = {};
  items.forEach(it => { (groups[it.locName] = groups[it.locName] || []).push(it); });
  const box = el('div','panel');
  Object.keys(groups).forEach(g => {
    box.appendChild(el('div','group', esc(g)));
    groups[g].forEach(it => {
      const row = el('label','row');
      row.innerHTML =
        '<span class="row-b"><span class="row-t">' + esc(it.name) + '</span>' +
        '<span class="row-s">' + esc(it.cmd || '') + '</span></span>' +
        '<span class="switch"><input type="checkbox"' + (it.enabled ? ' checked' : '') +
        ' aria-label="' + esc(it.name) + ' beim Start mitladen" /><i></i></span>';
      row.querySelector('input').onchange = e =>
        send({type:'autostartSet', loc:it.loc, key:it.key, enable:e.target.checked});
      box.appendChild(row);
    });
  });
  b.innerHTML = '';
  b.appendChild(box);
}

function renderApps(items){
  const b = $('#sub-body');
  if(!items || !items.length){
    b.innerHTML = '<div class="empty">Es wurden keine entfernbaren vorinstallierten Apps gefunden.</div>';
    return;
  }
  const groups = {};
  items.forEach(it => { (groups[it.cat] = groups[it.cat] || []).push(it); });
  const box = el('div','panel');
  Object.keys(groups).forEach(g => {
    box.appendChild(el('div','group', esc(g)));
    groups[g].forEach(it => {
      const row = el('label','row');
      row.innerHTML =
        '<span class="row-b"><span class="row-t">' + esc(it.label) + '</span></span>' +
        '<span class="switch"><input type="checkbox" data-full="' + esc(it.full) + '"' +
        ' aria-label="' + esc(it.label) + ' zum Entfernen auswählen" /><i></i></span>';
      box.appendChild(row);
    });
  });
  b.innerHTML = '';
  b.appendChild(box);
  const bar = el('div','next');
  bar.innerHTML = '<span class="next-b"><span class="next-t">Ausgewählte Apps entfernen</span>' +
    '<span class="next-s">Vorher legen wir einen Sicherungspunkt an. Sie können die Apps später ' +
    'jederzeit kostenlos aus dem Microsoft Store neu installieren.</span></span>';
  const go2 = el('button','btn btn-danger','Entfernen');
  go2.onclick = () => {
    const sel = Array.prototype.slice.call(b.querySelectorAll('input[data-full]:checked'));
    if(!sel.length){ toast('Nichts ausgewählt','Wählen Sie zuerst mindestens eine App aus.','warn'); return; }
    const names = sel.map(x => x.closest('.row').querySelector('.row-t').textContent);
    confirmModal('Diese Apps entfernen?',
      names.join('\n') + '\n\nSie können sie später jederzeit kostenlos aus dem Microsoft Store zurückholen.',
      'Entfernen', () => send({type:'bloatRemove', restore:true, fulls:sel.map(x => x.dataset.full)}), true);
  };
  bar.appendChild(go2);
  b.appendChild(bar);
}

function renderSchedule(m){
  const b = $('#sub-body');
  const tasks = (S.catalog && S.catalog.autoTasks) || [];
  let h = '';
  if(m.exists && m.config){
    const c = m.config;
    const when = c.mode === 'weekly'
      ? 'Jeden ' + (c.days || []).map(d => DAY[d] || d).join(', ') + ' um ' + esc(c.time) + ' Uhr'
      : (c.mode === 'monthly' ? 'Am ' + (parseInt(c.dom,10) || 1) + '. jedes Monats um ' + esc(c.time) + ' Uhr'
                              : 'Jeden Tag um ' + esc(c.time) + ' Uhr');
    h += '<div class="panel"><div class="row"><span class="pill ok">eingerichtet</span>' +
      '<span class="row-b"><span class="row-t">' + esc(when) + '</span>' +
      '<span class="row-s">Läuft still im Hintergrund und meldet sich, wenn sie fertig ist.</span></span>' +
      '<button class="link danger" id="sc-del">Abschalten</button></div></div>';
  } else {
    h += '<div class="panel"><div class="row"><span class="pill unknown">nicht eingerichtet</span>' +
      '<span class="row-b"><span class="row-s">Aktuell wartet sich der PC nicht von allein.</span></span></div></div>';
  }

  h += '<div class="panel"><div class="panel-h"><div class="panel-t">Wann soll gewartet werden?</div></div>' +
    '<div class="row"><span class="row-b"><span class="row-t">Wie oft</span></span>' +
    '<select id="sc-mode"><option value="daily">Jeden Tag</option>' +
    '<option value="weekly">Einmal in der Woche</option>' +
    '<option value="monthly">Einmal im Monat</option></select></div>' +
    '<div class="row"><span class="row-b"><span class="row-t">Uhrzeit</span>' +
    '<span class="row-s">Am besten eine Zeit, zu der der PC an ist, aber nicht gebraucht wird.</span></span>' +
    '<input type="time" id="sc-time" value="12:00" /></div></div>';

  h += '<div class="panel"><div class="panel-h"><div class="panel-t">Was soll erledigt werden?</div>' +
    '<div class="panel-d">Ohne Änderung läuft der bewährte Standard.</div></div>';
  tasks.forEach(t => {
    h += '<label class="row"><span class="row-b"><span class="row-t">' + esc(t.title) + '</span>' +
      '<span class="row-s">' + esc(t.desc) + '</span></span>' +
      '<span class="switch"><input type="checkbox" data-key="' + esc(t.key) + '"' +
      (t.std ? ' checked' : '') + ' aria-label="' + esc(t.title) + '" /><i></i></span></label>';
  });
  h += '</div><div class="next"><span class="next-b"><span class="next-t">Automatische Wartung einrichten</span>' +
    '<span class="next-s">Sie können das jederzeit wieder abschalten.</span></span>' +
    '<button class="btn btn-primary btn-md" id="sc-save">Einrichten</button></div>';

  b.innerHTML = h;
  const del = $('#sc-del');
  if(del) del.onclick = () => confirmModal('Automatische Wartung abschalten?',
    'Der PC wartet sich dann nicht mehr von allein. Sie können sie jederzeit wieder einrichten.',
    'Abschalten', () => send({type:'scheduleDelete'}));
  $('#sc-save').onclick = () => {
    const mode = $('#sc-mode').value;
    const parts = ($('#sc-time').value || '12:00').split(':');
    const keys = Array.prototype.slice.call(b.querySelectorAll('input[data-key]:checked')).map(x => x.dataset.key);
    if(!keys.length){ toast('Nichts ausgewählt','Wählen Sie mindestens eine Aufgabe aus.','warn'); return; }
    const msg = {type:'scheduleCreate', mode:mode, hh:parseInt(parts[0],10), mm:parseInt(parts[1],10), actions:keys};
    if(mode === 'weekly') msg.days = ['SAT'];
    if(mode === 'monthly') msg.dom = 1;
    send(msg);
  };
}
const DAY = {MON:'Montag',TUE:'Dienstag',WED:'Mittwoch',THU:'Donnerstag',FRI:'Freitag',SAT:'Samstag',SUN:'Sonntag'};

/* ---------- Fenstersteuerung ---------- */
$$('.wc').forEach(b => b.onclick = () => send({type:'win', action:b.dataset.win}));
$$('.rz').forEach(z => z.onmousedown = e => { e.preventDefault(); send({type:'resize', dir:z.dataset.rz}); });
document.addEventListener('mousedown', e => {
  const drag = e.target.closest('[data-drag]');
  if(drag && e.button === 0 && !e.target.closest('button')) send({type:'win', action:'drag'});
});
document.addEventListener('contextmenu', e => e.preventDefault());

$('#sb-cancel').onclick = () => send({type:'cancelShutdown'});
$('#ub-get').onclick = () => send({type:'startUpdate'});
$('#ub-skip').onclick = () => $('#update-bar').classList.remove('show');

/* ---------- Los ---------- */
renderStart();
send({type:'ready'});
