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
  sub: null,            // geoeffnete Nebenansicht
  storage: null,        // letztes Ergebnis "wo steckt der Platz?"
  registry: null,       // letztes Ergebnis der Registrierungs-Pruefung
  cat: null,            // gewaehlte Werkzeug-Kategorie
  mode: null,           // 'check' | 'fix'
  steps: [],
  stepIndex: 0,
  stepTotal: 0,
  result: null,
  queue: [],            // vorgemerkte Aktionen (Werkzeugkasten)
  post: 'none',         // was nach dem letzten Lauf passieren soll
  delay: 60,            // Sekunden zum Abbrechen, bevor es passiert
  selfStart: false,     // startet die App mit dem PC?
  admin: true,
  fremdesKonto: false,  // laeuft unter einem anderen Konto als dem angemeldeten
  laeuftAls: '',
  angemeldet: '',
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

  kontoHinweis(box);
}

/* Läuft das Programm unter einem anderen Konto als der angemeldete Mensch?
   Dann zeigen mehrere Ansichten dessen Profil statt seines eigenen. Das ist nicht
   gefährlich, aber verwirrend, und Verwirrung ist bei dieser Zielgruppe teuer.
   Deshalb steht es da, wo es auffällt, und nicht im Kleingedruckten. */
function kontoHinweis(box){
  if(!S.fremdesKonto || !box) return;
  const k = el('div','catnote');
  k.style.marginTop = '11px';
  k.innerHTML = svg('alert') +
    '<div><b>Sie arbeiten gerade unter dem Konto „' + esc(S.laeuftAls) + '“.</b><br>' +
    'Angemeldet ist aber „' + esc(S.angemeldet) + '“. Weil das Programm Administratorrechte ' +
    'braucht, läuft es unter dem Konto, dessen Kennwort eingegeben wurde. Alles, was zu ' +
    '„Ihren“ Dateien und Programmen gehört, zeigen wir deshalb für „' + esc(S.laeuftAls) + '“: ' +
    'Startprogramme, Verlauf, der belegte Speicherplatz und die Einträge der Registrierung.<br>' +
    'Am PC selbst wird trotzdem alles richtig repariert.</div>';
  box.appendChild(k);
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

  renderPostChoice();
  show('run');
  send({ type: mode === 'fix' ? 'startFix' : 'startCheck' });
  // Der Hauptweg schickt seine Wahl getrennt: startCheck/startFix tragen sie nicht mit,
  // und der Nutzer darf sie waehrend des Laufs noch aendern.
  send({ type:'setPost', post:S.post, delay:S.delay });
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
    'Abbrechen', () => { send({type:'cancel'}); });
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
  // Beim Speicherplatz bleibt der Rat sonst abstrakt. Von hier aus geht es dorthin,
  // wo steht, WAS den Platz belegt.
  if(c.key === 'space' && (c.state === 'warn' || c.state === 'bad')){
    const wo = el('button','btn btn-ghost','Wo steckt der Platz?');
    wo.style.marginTop = '10px';
    wo.onclick = () => go('storage');
    card.querySelector('.rbody').appendChild(wo);
  }
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

  renderQueueBar();
  renderPostChoice();
  $('#tools-foot').innerHTML = svg('info') + '<span>Weitere Bereiche:</span>';
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
        (a.danger ? '<span class="tag warn">Nachfrage</span>' : '') +
        (a.special ? '<span class="tag rec">Eingabe</span>' : '') + '</span>' +
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

  // Sonderaktionen brauchen eine Eingabe und lassen sich deshalb nicht sammeln.
  if(!a.special){
    const p = el('button','card-add', svg('plus'));
    p.type = 'button';
    p.setAttribute('aria-label','„' + a.title + '“ vormerken');
    p.onclick = (e) => { e.stopPropagation(); addToQueue(a.id); };
    wrap.appendChild(p);
  }
  return wrap;
}

/* ---------- Vormerken (Warteschlange) ---------- */
function addToQueue(id){
  if(S.queue.indexOf(id) >= 0){ toast('Schon vorgemerkt','Diese Aktion steht bereits auf der Liste.','warn'); return; }
  S.queue.push(id);
  renderQueueBar();
}
function removeFromQueue(i){ S.queue.splice(i,1); renderQueueBar(); }

function renderQueueBar(){
  const bar = $('#queue-bar');
  if(!S.queue.length){ bar.hidden = true; bar.innerHTML = ''; return; }
  bar.hidden = false;
  bar.innerHTML = '';

  const b = el('span','next-b');
  b.innerHTML = '<span class="next-t">' +
    (S.queue.length === 1 ? 'Eine Aktion vorgemerkt' : S.queue.length + ' Aktionen vorgemerkt') +
    '</span><span class="next-s">Sie laufen nacheinander in dieser Reihenfolge.</span>';
  const chips = el('div','chips');
  S.queue.forEach((id,i) => {
    const a = S.catalog.actions.find(x => x.id === id);
    if(!a) return;
    const c = el('span','chip', '<span>' + (i+1) + '. ' + esc(a.title) + '</span>');
    const x = el('button','chip-x', svg('close'));
    x.type = 'button';
    x.setAttribute('aria-label','„' + a.title + '“ von der Liste nehmen');
    x.onclick = () => removeFromQueue(i);
    c.appendChild(x);
    chips.appendChild(c);
  });
  b.appendChild(chips);
  bar.appendChild(b);

  const clear = el('button','btn btn-ghost','Liste leeren');
  clear.onclick = () => { S.queue = []; renderQueueBar(); };
  bar.appendChild(clear);

  const go = el('button','btn btn-primary btn-md','Der Reihe nach ausführen');
  go.onclick = () => startQueue();
  bar.appendChild(go);
}

/* „Wenn alles fertig ist“: gilt für den Hauptweg, für Einzelaktionen und für die Liste.

   Sie steht bewusst AUCH auf dem Ablauf-Bildschirm. Dort trifft man die Entscheidung
   nämlich wirklich: Der Lauf hat begonnen, dauert eine Viertelstunde, und jetzt will man
   weggehen. Vorher, auf dem Startbildschirm, hätte sie nur die eine große Schaltfläche
   zugestellt.

   Die Wartezeit ist keine Schikane: Sie ist das Zeitfenster, in dem sich das Herunterfahren
   über das Banner noch abbrechen lässt. Deshalb heißt sie auch so. */
const POST_ZEITEN = [[60,'1 Minute'],[300,'5 Minuten'],[900,'15 Minuten'],[1800,'30 Minuten']];

function renderPostChoice(){
  ['#post-choice','#run-post'].forEach(sel => zeichnePostWahl($(sel)));
}

function zeichnePostWahl(box){
  if(!box) return;
  const wahl = [['none','Nichts weiter','check'],['shutdown','PC herunterfahren','power'],
                ['restart','PC neu starten','refresh']];
  box.innerHTML = '<span class="post-label">Wenn alles fertig ist:</span>';
  const seg = el('div','tabs');
  seg.style.margin = '0';
  wahl.forEach(w => {
    const b = el('button','tab', svg(w[2]) + w[1]);
    b.type = 'button';
    b.setAttribute('aria-selected', String(S.post === w[0]));
    b.onclick = () => { S.post = w[0]; postGeaendert(); };
    seg.appendChild(b);
  });
  box.appendChild(seg);

  if(S.post === 'none') return;

  const zeile = el('span','post-label');
  zeile.textContent = 'Vorher noch Zeit zum Abbrechen:';
  box.appendChild(zeile);

  const seg2 = el('div','tabs');
  seg2.style.margin = '0';
  POST_ZEITEN.forEach(z => {
    const b = el('button','tab', z[1]);
    b.type = 'button';
    b.setAttribute('aria-selected', String(S.delay === z[0]));
    b.onclick = () => { S.delay = z[0]; postGeaendert(); };
    seg2.appendChild(b);
  });
  box.appendChild(seg2);
}

/* Läuft gerade etwas, muss der Host die neue Wahl sofort erfahren: Bei einem Lauf, der
   schon gestartet ist, kommt sie sonst nirgends mehr an. */
function postGeaendert(){
  renderPostChoice();
  if(S.screen === 'run') send({type:'setPost', post:S.post, delay:S.delay});
}

function startQueue(){
  if(!S.admin){ needAdmin(); return; }
  const ids = S.queue.slice();
  const gefaehrlich = ids.filter(id => (S.catalog.actions.find(a => a.id === id) || {}).danger).length;
  const namen = ids.map(id => (S.catalog.actions.find(a => a.id === id) || {}).title).join('\n');
  const los = () => {
    $('#console-body').innerHTML = '';
    S.mode = 'action';
    S.steps = ids.map(id => (S.catalog.actions.find(a => a.id === id) || {}).title);
    S.stepIndex = 1;
    S.stepTotal = ids.length;
    $('#run-ico').className = 'vico';
    $('#run-ico').innerHTML = svg('list');
    $('#run-title').textContent = 'Ihre Liste wird abgearbeitet';
    $('#run-lead').textContent = 'Die vorgemerkten Aktionen laufen jetzt nacheinander. ' +
      'Vor der ersten Reparatur legen wir einen Sicherungspunkt an.';
    $('#run-foot').innerHTML = svg('info') + '<span>Sie können jederzeit abbrechen.</span>';
    renderSteps();
    setBar(-1);
    show('run');
    send({ type:'runQueue', ids:ids, restore:true, post:S.post, delay:S.delay });
    S.queue = [];
    renderQueueBar();
  };
  confirmModal('Liste starten?',
    namen + (S.post !== 'none'
      ? '\n\nDanach wird der PC ' + (S.post === 'restart' ? 'neu gestartet' : 'heruntergefahren') + '.'
      : ''),
    'Los geht es', los, gefaehrlich > 0);
}

function needAdmin(){
  infoModal('Administratorrechte fehlen',
    'Dafür braucht das Programm Administratorrechte. Bitte schließen Sie es, klicken Sie das ' +
    'Symbol mit der rechten Maustaste an und wählen Sie „Als Administrator ausführen“.', 'warn');
}

function runAction(a){
  if(!S.admin){ needAdmin(); return; }

  // Sonderaktionen: erst die Eingabe, dann ein eigener Befehl.
  if(a.special === 'netdiag'){
    promptModal(a.title,
      'Welche Adresse sollen wir testen? Zum Beispiel google.com oder die Adresse einer Seite, ' +
      'die bei Ihnen nicht lädt.', 'google.com', wert => {
        const ziel = (wert || '').trim();
        if(!ziel) return;
        if(!/^[a-zA-Z0-9][a-zA-Z0-9.\-:]*$/.test(ziel)){
          toast('Das geht so nicht', 'Bitte nur Buchstaben, Zahlen, Punkt und Bindestrich eingeben.', 'warn');
          return;
        }
        prepareSingleRun(a);
        send({ type:'netDiag', target:ziel });
      });
    return;
  }
  if(a.special === 'driverbackup'){
    confirmModal(a.title,
      a.info + '\n\nIm nächsten Schritt öffnet sich ein Fenster, in dem Sie den Zielordner aussuchen.',
      'Ordner aussuchen', () => { prepareSingleRun(a); send({ type:'driverBackup' }); });
    return;
  }

  const start = () => {
    prepareSingleRun(a);
    send({ type:'run', id:a.id, restore:true, post:S.post, delay:S.delay });
  };

  if(a.danger || SET.confirm){
    confirmModal(a.title,
      (a.info || a.desc) +
      (a.restore ? '\n\nVorher legen wir einen Sicherungspunkt an, damit sich das rückgängig machen lässt.' : '') +
      (S.post !== 'none' ? '\n\nDanach wird der PC ' +
        (S.post === 'restart' ? 'neu gestartet' : 'heruntergefahren') + '.' : ''),
      'Jetzt ausführen', start, a.danger);
  } else start();
}

/* Bereitet den Ablauf-Bildschirm für eine einzelne Aktion vor. */
function prepareSingleRun(a){
  renderPostChoice();
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
}

/* ---------- Eingabe-Dialog ---------- */
function promptModal(title, body, vorgabe, onOk){
  const m = buildModal({ title:title, body:body, icon:'globe' });
  const inp = el('input','modal-input');
  inp.type = 'text';
  inp.spellcheck = false;
  inp.value = vorgabe || '';
  inp.setAttribute('aria-label', title);
  m.box.insertBefore(inp, m.btns);
  const cancel = el('button','btn btn-ghost','Abbrechen');
  cancel.onclick = m.close;
  const ok = el('button','btn btn-primary btn-md','Starten');
  ok.onclick = () => { const v = inp.value; m.close(); onOk(v); };
  m.btns.appendChild(cancel);
  m.btns.appendChild(ok);
  inp.addEventListener('keydown', e => { if(e.key === 'Enter'){ e.preventDefault(); ok.click(); } });
  setTimeout(() => { try{ inp.focus(); inp.select(); }catch(_){} }, 50);
}

/* =====================================================================
   Nebenansichten
   ===================================================================== */
const SUBS = {
  storage:   { title:'Wo steckt der Platz?', lead:'Wir sehen nach, was Ihren Speicher belegt: was sich gefahrlos wegräumen lässt und was Ihnen selbst gehört. Zuerst wird nur geschaut. Gelöscht wird nichts, was Sie nicht ausdrücklich auswählen.' },
  registry:  { title:'Einträge, die ins Leere zeigen', lead:'Windows führt eine Liste, in der Programme hinterlegen, wo ihre Dateien liegen. Wird ein Programm entfernt, bleibt der Eintrag manchmal stehen und zeigt auf nichts mehr. Hier sehen Sie diese Einträge.' },
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

  if(key === 'storage'){  S.storage = null;  scanLaeuft('Wird durchgesehen'); send({type:'storageScan'}); }
  if(key === 'registry'){ S.registry = null; scanLaeuft('Wird durchgesehen'); send({type:'registryScan'}); }
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
  // „Machen Sie Platz frei“ war bisher ein Rat ohne Weg. Von hier aus geht es
  // direkt dorthin, wo steht, WO der Platz steckt.
  if(c.key === 'space'){
    const wo = el('button','btn btn-ghost','Wo steckt der Platz?');
    wo.onclick = () => { m.close(); go('storage'); };
    m.btns.appendChild(wo);
  }
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
      ['tools','history','restore','schedule','autostart','apps','power','settings','storage','registry']
        .forEach(k => { if(HASH.indexOf(k) >= 0) go(k); });
      // Nur fuer Belegaufnahmen: die (rein lesende) Pruefung von selbst starten.
      // Der Adresszusatz laesst sich ausschliesslich ueber --view von der Befehlszeile
      // setzen, im normalen Betrieb ist er leer.
      if(HASH.indexOf('check') >= 0) startFlow('check');
      break;

    case 'admin':
      S.admin = !!m.on;
      S.fremdesKonto = !!m.fremdesKonto;
      S.laeuftAls = m.laeuftAls || '';
      S.angemeldet = m.angemeldet || '';
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

    // --- Speicher- und Registrierungs-Suche ---
    case 'scanProgress': {
      const st = $('#scan-status');
      if(st) st.textContent = m.text + ' …';
      break;
    }

    case 'storageResult':
      if(S.sub === 'storage') renderStorage(m);
      break;

    case 'storageCleaned': {
      const zeilen = (m.posten || []).map(p => p.titel + ': ' + p.groesse).join('\n');
      // Die Zusicherung „Ihre Dokumente wurden nicht angerührt“ darf NICHT fallen, wenn
      // der Papierkorb dabei war: dort liegen genau diese Dateien. Wer sich darauf
      // verlässt, sucht später gar nicht erst im Papierkorb nach.
      const korbDabei = (m.posten || []).some(p => p.schluessel === 'papierkorb');
      infoModal('Aufgeräumt',
        (m.gesamtBytes > 0
          ? 'Es sind ' + m.gesamt + ' frei geworden.\n\n' + zeilen
          : 'Es ist nichts frei geworden. Vermutlich waren die Dateien gerade in Benutzung; ' +
            'nach einem Neustart klappt es meist.\n\n' + zeilen) +
        (korbDabei
          ? '\n\nDer Papierkorb wurde geleert. Was darin lag, lässt sich nicht mehr ' +
            'zurückholen. Ihre übrigen Dokumente, Fotos und Programme wurden nicht angerührt.'
          : '\n\nIhre Dokumente, Fotos und Programme wurden nicht angerührt.'));
      break;
    }

    case 'registryResult':
      if(S.sub === 'registry') renderRegistry(m);
      break;

    case 'registryCleaned': {
      const dlg = buildModal({
        title: m.entfernt === 0 ? 'Es wurde nichts entfernt'
             : m.entfernt === 1 ? 'Ein Eintrag wurde entfernt'
                                : m.entfernt + ' Einträge wurden entfernt',
        body: (m.fehlgeschlagen > 0
                ? m.fehlgeschlagen + ' Eintrag/Einträge ließen sich nicht entfernen und wurden ' +
                  'unverändert gelassen.\n\n' : '') +
              (m.sicherungspunkt
                ? 'Vorher wurde ein Sicherungspunkt von Windows angelegt.\n\n'
                : 'Ein Sicherungspunkt ließ sich nicht anlegen (vermutlich ist der Systemschutz ' +
                  'ausgeschaltet). Die Sicherungsdatei unten wurde trotzdem geschrieben.\n\n') +
              (m.sicherung
                ? 'Die Sicherung liegt hier:\n' + m.sicherung + '\n\nEin Doppelklick auf diese ' +
                  'Datei holt die Einträge zurück.'
                : 'Es wurde nichts verändert.'),
        icon: 'save' });
      const zu = el('button','btn btn-ghost','Schließen');
      zu.onclick = dlg.close;
      dlg.btns.appendChild(zu);
      if(m.sicherung){
        const auf = el('button','btn btn-primary btn-md','Sicherung anzeigen');
        auf.onclick = () => { dlg.close(); send({type:'openRegBackup'}); };
        dlg.btns.appendChild(auf);
        auf.focus();
      } else zu.focus();
      break;
    }

    case 'scanError':
      infoModal('Das hat nicht geklappt', m.message, 'warn');
      if(S.screen === 'sub' && $('#scan-status'))
        $('#sub-body').innerHTML = '<div class="empty">Es liegt kein Ergebnis vor.</div>';
      break;

    // --- Nebenansichten ---
    case 'history':      renderHistory(m.items); break;
    case 'restorePoints':renderRestore(m.items); break;
    case 'powerPlans':   renderPower(m.items); break;
    case 'bloatPackages':renderApps(m.items); break;
    case 'autostart':
      renderAutostart(m.items);
      if(m.fehlgeschlagen) toast('Ließ sich nicht umstellen',
        'Windows hat die Änderung nicht angenommen. Der Schalter steht wieder so, wie es ' +
        'wirklich ist.', 'bad');
      break;
    case 'selfstart':
      S.selfStart = !!m.on;
      { const i = $('#as-self'); if(i) i.checked = S.selfStart; }
      if(m.changed === true)  toast(S.selfStart ? 'Eingerichtet' : 'Entfernt',
        S.selfStart ? 'Windows-Wartung startet künftig mit dem PC.' : 'Windows-Wartung startet nicht mehr automatisch.', 'good');
      if(m.changed === false) toast('Nicht geändert','Der Eintrag ließ sich nicht anpassen.','bad');
      break;
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
    case 'updateError': {
      $('#update-bar').classList.remove('show');
      const dlg = buildModal({
        title: 'Die Aktualisierung hat nicht geklappt',
        body: m.message + '\n\nSie können die neue Fassung stattdessen im Browser ' +
              'herunterladen und von Hand installieren.',
        icon: 'alert', tone: 'warn' });
      const zu = el('button','btn btn-ghost','Schließen');
      zu.onclick = dlg.close;
      const br = el('button','btn btn-primary btn-md','Im Browser öffnen');
      br.onclick = () => { dlg.close(); send({type:'openUpdate'}); };
      dlg.btns.appendChild(zu);
      dlg.btns.appendChild(br);
      br.focus();
      break;
    }
    case 'updated':
      toast('Aktualisiert', 'Das Programm läuft jetzt in der Fassung ' + m.version + '.', 'good');
      break;

    case 'updateFailedSilently':
      // Ohne diese Meldung wäre für den Nutzer nach dem Klick auf „Jetzt aktualisieren“
      // einfach nichts passiert, und er hätte keine Ahnung, warum.
      infoModal('Die Aktualisierung wurde rückgängig gemacht',
        'Beim Austauschen der Programmdateien ist etwas schiefgegangen, deshalb haben wir ' +
        'die bisherige Fassung wiederhergestellt. Ihr PC hat davon keinen Schaden genommen ' +
        'und das Programm läuft normal weiter.\n\n' +
        'Sie können es später noch einmal versuchen. Klappt es wieder nicht, laden Sie die ' +
        'Fassung ' + m.version + ' am besten von Hand herunter.', 'warn');
      break;

    case 'shutdownScheduled':
      $('#sb-text').textContent = 'Der PC wird in ' + dauer(m.delay) + ' ' +
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
  let h = '<div class="panel"><div class="panel-h" style="display:flex;align-items:center;' +
    'justify-content:space-between;gap:12px"><span><span class="panel-t">' + items.length +
    (items.length === 1 ? ' Eintrag' : ' Einträge') + '</span></span>' +
    '<button class="link danger" id="hist-clear">Verlauf leeren</button></div><div class="panel-b">';
  items.forEach(it => {
    const k = ['good','bad','warn'].indexOf(it.kind) >= 0 ? it.kind : 'unknown';
    h += '<div class="row"><span class="dot ' + (k === 'good' ? 'ok' : (k === 'bad' ? 'bad' : 'warn')) + '"></span>' +
      '<span class="row-b"><span class="row-t">' + esc(it.action) + '</span>' +
      '<span class="row-s">' + esc(words[k] || 'Ausgeführt') + (it.message ? ' · ' + esc(it.message) : '') + '</span></span>' +
      '<span class="row-m">' + esc(it.time || '') + '</span></div>';
  });
  h += '</div></div>';
  b.innerHTML = h;
  $('#hist-clear').onclick = () => confirmModal('Verlauf leeren?',
    'Alle Einträge werden endgültig gelöscht. Auf Ihren PC hat das keine Auswirkung.',
    'Leeren', () => send({type:'historyClear'}));
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
  b.innerHTML = '';

  // Selbststart der App und der Weg zum Autostart-Ordner. Beides stand in v6.6
  // ueber der Liste und war beim Umbau verlorengegangen.
  const kopf = el('div','panel');
  kopf.innerHTML = '<div class="panel-h"><div class="panel-t">Windows-Wartung selbst</div>' +
    '<div class="panel-d">Das Programm kann sich beim Anmelden von allein starten. ' +
    'Es fragt dann nicht jedes Mal nach Administratorrechten.</div></div>';
  const selbst = switchRow('Mit dem PC starten',
    'Wird über die Aufgabenplanung von Windows eingerichtet.',
    !!S.selfStart, v => send({type:'selfStartSet', on:v}));
  selbst.querySelector('input').id = 'as-self';
  kopf.appendChild(selbst);
  b.appendChild(kopf);

  const ordner = el('div','panel');
  ordner.innerHTML = '<div class="panel-h"><div class="panel-t">Eigene Programme mitstarten lassen</div>' +
    '<div class="panel-d">Legen Sie eine Verknüpfung in den Autostart-Ordner, dann startet ' +
    'das Programm künftig mit. „Nur für mich“ gilt für Ihr Benutzerkonto, „Für alle“ für jeden ' +
    'an diesem PC.</div></div>';
  const zeile = el('div','row');
  zeile.innerHTML = '<span class="row-b"><span class="row-s">Ordner im Explorer öffnen</span></span>';
  const b1 = el('button','btn btn-ghost', svg('folder') + 'Nur für mich');
  b1.onclick = () => send({type:'openStartupFolder', scope:'user'});
  const b2 = el('button','btn btn-ghost', svg('folder') + 'Für alle');
  b2.onclick = () => send({type:'openStartupFolder', scope:'common'});
  zeile.appendChild(b1); zeile.appendChild(b2);
  ordner.appendChild(zeile);
  b.appendChild(ordner);

  if(!items || !items.length){
    b.appendChild(el('div','empty','Es starten keine zusätzlichen Programme mit.'));
    return;
  }
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

/* Wartezeiten in Alltagssprache. „in 1800 Sekunden“ rechnet niemand im Kopf um. */
function dauer(sekunden){
  const s = Number(sekunden) || 0;
  if(s < 90) return s + ' Sekunden';
  const m = Math.round(s / 60);
  return m === 1 ? 'einer Minute' : m + ' Minuten';
}

/* Größen in Alltagsschreibweise. Die Einzelposten bringt der Host schon fertig mit;
   für Summen, die erst hier entstehen, wird dieselbe Staffelung nachgebildet. */
function groesse(bytes){
  const b = Number(bytes) || 0;
  if(b >= 1073741824) return (b / 1073741824).toFixed(1).replace('.', ',') + ' GB';
  if(b >= 1048576)    return Math.round(b / 1048576) + ' MB';
  if(b >= 1024)       return Math.round(b / 1024) + ' KB';
  return b + ' Byte';
}

/* Wartezustand der beiden Suchläufe. Ein Suchlauf ohne Ausweg wäre eine Sackgasse:
   auf einem vollen Rechner kann das Durchsehen der eigenen Ordner dauern.

   Beim Entfernen aus der Registrierung gibt es bewusst KEIN Abbrechen: dort laufen
   Sicherungspunkt und Sicherungsdatei, und ein Knopf, der mittendrin nichts mehr
   ausrichtet, wäre ein Versprechen, das die App nicht halten kann. */
function scanLaeuft(text, unterzeile, abbrechbar){
  const b = $('#sub-body');
  b.innerHTML = '';
  const p = el('div','panel');
  p.innerHTML = '<div class="panel-h"><div class="panel-t">' + esc(text) + ' …</div>' +
    '<div class="panel-d">' + esc(unterzeile || 'Dabei wird nichts verändert. Wir schauen nur nach.') + '</div></div>';
  const row = el('div','row');
  row.innerHTML = '<span class="row-b"><span class="row-s" id="scan-status">Einen Moment bitte …</span></span>';
  if(abbrechbar !== false){
    const ab = el('button','btn btn-ghost','Abbrechen');
    ab.onclick = () => { send({type:'cancel'}); go('tools'); };
    row.appendChild(ab);
  }
  p.appendChild(row);
  b.appendChild(p);
  $('#sub-foot').innerHTML = '';
}

/* =====================================================================
   Wo steckt der Platz?

   Zwei Listen mit klar verschiedener Bedeutung, und das muss man sehen:
   oben, was die App selbst gefahrlos wegräumen kann, unten, was IHNEN
   gehört und was wir deshalb nicht anfassen.
   ===================================================================== */
function renderStorage(m){
  S.storage = m;
  const b = $('#sub-body');
  b.innerHTML = '';

  const belegt = el('div','panel');
  belegt.innerHTML = '<div class="panel-h"><div class="panel-t">Ihr Windows-Laufwerk</div>' +
    '<div class="panel-d">' + esc(m.frei) + ' von ' + esc(m.gesamt) + ' sind noch frei.</div></div>';
  b.appendChild(belegt);

  /* ---- Teil 1: aufräumbar ---- */
  const posten = (m.aufraeumbar || []).filter(p => p.aufraeumbar);

  // Der Papierkorb wird getrennt ausgewiesen. Er steckte vorher in derselben Summe und
  // unter demselben Satz „legt Windows neu an“ - und genau dort liegen die gelöschten
  // Dokumente und Fotos, die derselbe Satz für nicht betroffen erklärte.
  const nachwachsend = posten.filter(p => !p.unwiederbringlich);
  const korb = posten.filter(p => p.unwiederbringlich);
  const summe = nachwachsend.reduce((s,p) => s + (p.bytes || 0), 0);

  const weg = el('div','panel');
  weg.innerHTML = '<div class="panel-h"><div class="panel-t">Das können wir für Sie wegräumen</div>' +
    '<div class="panel-d">Zusammen ' + esc(groesse(summe)) + '. Das alles legt Windows bei Bedarf ' +
    'neu an. Ihre Dokumente, Fotos und Programme sind nicht dabei.' +
    (korb.length
      ? '<br><br>Der Papierkorb steht gesondert darunter und zählt hier nicht mit: Er enthält ' +
        'gelöschte Dateien, die noch zurückzuholen wären. Was Sie dort wegräumen, ist danach weg.'
      : '') +
    '</div></div>';
  if(!posten.length){
    weg.appendChild(el('div','empty','Es liegt nichts herum, das wir wegräumen könnten.'));
  } else {
    nachwachsend.concat(korb).forEach(p => {
      const row = el('label','row');
      row.innerHTML =
        '<span class="row-b"><span class="row-t">' + esc(p.titel) +
          (p.unwiederbringlich ? '<span class="tag warn">endgültig</span>' : '') + '</span>' +
        '<span class="row-s">' + esc(p.erklaerung) + '</span></span>' +
        '<span class="row-m">' + esc(p.groesse) + '</span>' +
        '<span class="switch"><input type="checkbox" data-key="' + esc(p.schluessel) + '"' +
        // Der Papierkorb ist NICHT vorangehakt: was darin liegt, ist danach wirklich weg.
        // Alles andere legt Windows von allein neu an.
        (p.unwiederbringlich ? '' : ' checked') +
        ' aria-label="' + esc(p.titel) + ' aufräumen" /><i></i></span>';
      weg.appendChild(row);
    });
  }
  b.appendChild(weg);

  if(posten.length){
    const bar = el('div','next');
    bar.innerHTML = '<span class="next-b"><span class="next-t">Ausgewähltes aufräumen</span>' +
      '<span class="next-s">Dateien, die gerade in Benutzung sind, bleiben liegen. Wir sagen Ihnen ' +
      'hinterher, wie viel wirklich frei geworden ist.</span></span>';
    const go2 = el('button','btn btn-primary btn-md','Jetzt aufräumen');
    go2.onclick = () => starteAufraeumen(b, posten);
    bar.appendChild(go2);
    b.appendChild(bar);
  }

  /* ---- Teil 2: die großen Brocken, die uns nicht gehören ---- */
  const brocken = m.brocken || [];
  if(brocken.length){
    const gross = el('div','panel');
    gross.innerHTML = '<div class="panel-h"><div class="panel-t">Die größten Brocken in Ihren Ordnern</div>' +
      '<div class="panel-d">Das hier gehört Ihnen, deshalb fassen wir es nicht an. Wenn Sie Platz ' +
      'brauchen, schauen Sie am besten selbst nach, was davon Sie noch benötigen.</div></div>';
    brocken.forEach((p, i) => {
      const row = el('div','row');
      row.innerHTML =
        '<span class="row-b"><span class="row-t">' + esc(p.titel) + '</span>' +
        '<span class="row-s">' + esc(p.erklaerung) + '</span></span>' +
        '<span class="row-m">' + esc(p.groesse) + '</span>';
      const zeig = el('button','btn btn-ghost', svg('folder') + 'Anzeigen');
      zeig.setAttribute('aria-label','„' + p.titel + '“ im Explorer anzeigen');
      zeig.onclick = () => send({type:'openBrocken', index:i});
      row.appendChild(zeig);
      gross.appendChild(row);
    });
    b.appendChild(gross);
  }

  $('#sub-foot').innerHTML = svg('info') +
    '<span>Der Ordner „Downloads“ steht bewusst nicht in der oberen Liste: dort legen viele ' +
    'Menschen Dateien ab, die sie behalten möchten.</span>';
}

function starteAufraeumen(b, posten){
  if(!S.admin){ needAdmin(); return; }
  const sel = Array.prototype.slice.call(b.querySelectorAll('input[data-key]:checked'));
  if(!sel.length){ toast('Nichts ausgewählt','Wählen Sie zuerst aus, was wir wegräumen sollen.','warn'); return; }
  const keys = sel.map(x => x.dataset.key);
  const zeilen = keys.map(k => {
    const p = posten.find(x => x.schluessel === k) || {};
    return '· ' + p.titel + ' (' + p.groesse + ')';
  }).join('\n');
  const endgueltig = keys.some(k => (posten.find(x => x.schluessel === k) || {}).unwiederbringlich);

  confirmModal('Das hier wegräumen?',
    zeilen + (endgueltig
      ? '\n\nDer Papierkorb ist dabei. Was darin liegt, ist danach endgültig weg und lässt sich ' +
        'nicht mehr zurückholen. Schauen Sie vorher kurz nach, ob noch etwas Wichtiges darin ist.'
      : '\n\nAll das legt Windows bei Bedarf von allein neu an.'),
    'Jetzt aufräumen', () => {
      scanLaeuft('Wird aufgeräumt', 'Wir räumen die ausgewählten Punkte weg und messen danach neu.');
      send({type:'storageClean', keys:keys});
    }, endgueltig);
}

/* =====================================================================
   Einträge, die ins Leere zeigen

   Haltung: Diese Ansicht verspricht NICHTS. Kein „macht Ihren PC schneller“,
   keine Punktzahl, keine roten Zahlen. Sie zeigt, was gefunden wurde, warum
   es als tot gilt und wo die Sicherung liegt - und überlässt die Entscheidung
   dem Nutzer.
   ===================================================================== */
function renderRegistry(m){
  S.registry = m;
  const funde = m.funde || [];
  const b = $('#sub-body');
  b.innerHTML = '';

  const kopf = el('div','panel');
  kopf.innerHTML = '<div class="panel-h"><div class="panel-t">' +
    (funde.length === 0 ? 'Es wurde nichts gefunden'
      : funde.length === 1 ? 'Ein Eintrag zeigt ins Leere'
      : funde.length + ' Einträge zeigen ins Leere') + '</div>' +
    '<div class="panel-d">Gemeldet wird nur, was sich nachweisen lässt: Der Eintrag nennt eine Datei, ' +
    'und die gibt es nicht mehr. Vermutungen bleiben außen vor.<br><br>' +
    'Ehrlich gesagt: Ihr PC wird davon nicht schneller. Diese Einträge kosten so gut wie keinen ' +
    'Platz und bremsen nichts. Was das Aufräumen bringt, ist Ordnung.</div></div>';
  b.appendChild(kopf);

  if(!funde.length){
    b.appendChild(el('div','empty','Alles in Ordnung. Es gibt hier nichts zu tun.'));
    $('#sub-foot').innerHTML = '';
    return;
  }

  // Nach Kategorie gruppieren, darin nach der fehlenden Datei: ein Programm hinterlässt
  // oft mehrere Einträge zur selben Datei. Als getrennte Zeilen sähe das nach mehr aus,
  // als es ist - genau die Zahlenschinderei, die wir nicht wollen.
  const kats = {};
  funde.forEach(f => {
    const k = kats[f.kategorie] || (kats[f.kategorie] = {});
    const g = k[f.ziel] || (k[f.ziel] = { titel:f.titel, grund:f.grund, ziel:f.ziel, ids:[], titel2:[] });
    g.ids.push(f.id);
    // Jeder Name wird mitgeführt. Sonst verschwindet der zweite Eintrag einer Gruppe
    // still: der Nutzer entfernt ihn mit, ohne ihn je gelesen zu haben.
    if(g.titel2.indexOf(f.titel) < 0) g.titel2.push(f.titel);
  });

  Object.keys(kats).forEach(kat => {
    const gruppen = Object.keys(kats[kat]).map(z => kats[kat][z]);
    const p = el('div','panel');
    p.innerHTML = '<div class="panel-h"><div class="panel-t">' + esc(kat) +
      ' <span class="pill unknown">' + gruppen.length + '</span></div>' +
      '<div class="panel-d">' + esc((m.hinweise && m.hinweise[kat]) || '') + '</div></div>';

    // Lange Gruppen werden zugeklappt. Die Merkzettel machen allein 98 Prozent aller
    // Funde aus; ausgeklappt steht der Nutzer vor mehreren hundert Zeilen und findet den
    // Knopf am Ende nie. Wichtig dabei: Der Knopf „Alle auswählen“ liegt MIT drin. Wer
    // hunderte Einträge auf einmal auswählt, soll sie vorher wenigstens aufgeklappt haben.
    const ziel = gruppen.length > 12 ? zugeklappt(p, gruppen.length) : p;

    const alle = el('div','row');
    alle.innerHTML = '<span class="row-b"><span class="row-s">Alle in dieser Gruppe auswählen</span></span>';
    const knopf = el('button','btn btn-ghost','Alle auswählen');
    // Der Knopf richtet sich nach seiner eigenen Beschriftung, nicht nach dem Zustand
    // der Kästchen. Sonst kehrte er sich um, sobald der Nutzer eine einzelne Zeile von
    // Hand abwählte: „Auswahl aufheben“ hakte dann alles an, auch die eben abgewählte
    // Zeile - und das auf dem Weg, der Einträge entfernt.
    let angehakt = false;
    knopf.onclick = () => {
      angehakt = !angehakt;
      Array.prototype.slice.call(p.querySelectorAll('input[data-ids]'))
        .forEach(x => { x.checked = angehakt; });
      knopf.textContent = angehakt ? 'Auswahl aufheben' : 'Alle auswählen';
    };
    alle.appendChild(knopf);
    ziel.appendChild(alle);

    gruppen.forEach(g => {
      const row = el('label','row');
      row.innerHTML =
        '<span class="row-b"><span class="row-t">' + esc(g.titel2.join(', ')) +
          (g.ids.length > 1 ? '<span class="tag rec">' + g.ids.length + ' Einträge</span>' : '') + '</span>' +
        '<span class="row-s">' + esc(g.grund) + '</span>' +
        '<span class="row-s">Zeigt auf: ' + esc(g.ziel) + '</span></span>' +
        '<span class="switch"><input type="checkbox" data-ids="' + esc(g.ids.join(',')) + '"' +
        // Nichts ist vorangehakt. Wer in der Registrierung etwas entfernt, soll das
        // bewusst tun - nicht, weil ein Programm es ihm vorausgewählt hat.
        ' aria-label="' + esc(g.titel) + ' entfernen" /><i></i></span>';
      ziel.appendChild(row);
    });
    b.appendChild(p);
  });

  const bar = el('div','next');
  bar.innerHTML = '<span class="next-b"><span class="next-t">Ausgewählte Einträge entfernen</span>' +
    '<span class="next-s">Vorher schreiben wir eine Sicherungsdatei. Damit lässt sich jeder ' +
    'einzelne Eintrag mit einem Doppelklick zurückholen. Zusätzlich versuchen wir, einen ' +
    'Sicherungspunkt von Windows anzulegen.</span></span>';
  const go2 = el('button','btn btn-danger','Entfernen');
  go2.onclick = () => starteRegistryClean(b);
  bar.appendChild(go2);
  b.appendChild(bar);

  $('#sub-foot').innerHTML = svg('info') +
    '<span>Es wird nichts entfernt, was nur gerade nicht erreichbar ist: Einträge auf USB-Sticks, ' +
    'Speicherkarten und Netzlaufwerken bleiben unangetastet.</span>';
}

/* Hängt einen zugeklappten Bereich an ein Panel und gibt den Platz darin zurück.
   Dasselbe Muster wie „Im Einzelnen ansehen“ im Ergebnis-Bildschirm. */
function zugeklappt(panel, anzahl){
  const d = el('details','disc');
  d.style.margin = '0';
  d.style.border = '0';
  d.innerHTML = '<summary>' + svg('chevron') +
    ' <b>Alle ' + anzahl + ' Einträge ansehen</b>' +
    '<span style="color:var(--text-3)">Zugeklappt, weil es sehr viele sind</span></summary>';
  const platz = el('div','');
  d.appendChild(platz);
  panel.appendChild(d);
  return platz;
}

function starteRegistryClean(b){
  if(!S.admin){ needAdmin(); return; }
  const sel = Array.prototype.slice.call(b.querySelectorAll('input[data-ids]:checked'));
  if(!sel.length){ toast('Nichts ausgewählt','Wählen Sie zuerst aus, was entfernt werden soll.','warn'); return; }

  const ids = [];
  sel.forEach(x => x.dataset.ids.split(',').filter(Boolean).forEach(i => ids.push(i)));
  if(!ids.length){ toast('Nichts ausgewählt','Zu dieser Auswahl liegt kein Eintrag vor.','warn'); return; }

  const namen = sel.slice(0, 12)
    .map(x => '· ' + x.closest('.row').querySelector('.row-t').textContent.replace(/\d+ Einträge$/,'').trim())
    .join('\n');
  const rest = sel.length > 12 ? '\n· … und ' + (sel.length - 12) + ' weitere' : '';
  // Eine Zeile kann für mehrere Einträge stehen. Dann muss die Sicherheitsfrage die
  // echte Zahl nennen, sonst bestätigt der Nutzer mehr, als vor ihm steht.
  const anzahl = ids.length !== sel.length
    ? '\n\nDas sind zusammen ' + ids.length + ' Einträge.' : '';

  confirmModal('Diese Einträge entfernen?',
    namen + rest + anzahl +
    '\n\nVorher schreiben wir eine Sicherungsdatei mit genau diesen Einträgen. Lässt sie ' +
    'sich nicht vollständig schreiben, wird nichts verändert. Zusätzlich versuchen wir, ' +
    'einen Sicherungspunkt von Windows anzulegen; ob das geklappt hat, sagen wir Ihnen ' +
    'hinterher.',
    'Entfernen', () => {
      scanLaeuft('Einträge werden entfernt',
        'Erst der Sicherungspunkt, dann die Sicherungsdatei, dann die Einträge. ' +
        'Das dauert einen Moment und lässt sich nicht mehr anhalten.', false);
      send({type:'registryClean', ids:ids});
    }, true);
}

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
