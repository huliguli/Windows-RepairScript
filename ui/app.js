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
  dashboard:'<rect x="3" y="3" width="8" height="8" rx="1"/><rect x="13" y="3" width="8" height="5" rx="1"/><rect x="13" y="11" width="8" height="10" rx="1"/><rect x="3" y="13" width="8" height="8" rx="1"/>',
  help:'<circle cx="12" cy="12" r="10"/><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3"/><path d="M12 17h.01"/>',
  rocket:'<path d="M4.5 16.5c-1.5 1.26-2 5-2 5s3.74-.5 5-2c.79-.78.8-2.07.09-2.91a2.18 2.18 0 0 0-3.09-.09Z"/><path d="m12 15-3-3a22 22 0 0 1 2-3.95A12.88 12.88 0 0 1 22 2c0 2.72-.78 7.5-6 11a22.35 22.35 0 0 1-3 2Z"/><path d="M9 12H4s.55-3.03 2-4c1.62-1.08 5 0 5 0"/><path d="M15 9v5s-3.03-.55-4-2c-1.08-1.62 0-5 0-5"/>',
  gear:'<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z"/>',
  min:'<path d="M5 12h14"/>',
  max:'<rect x="5" y="5" width="14" height="14" rx="2"/>',
  close:'<path d="M6 6l12 12"/><path d="M18 6 6 18"/>',
  check:'<path d="M22 11.1V12a10 10 0 1 1-5.9-9.1"/><path d="M22 4 12 14.1l-3-3"/>',
  xcirc:'<circle cx="12" cy="12" r="10"/><path d="M15 9l-6 6"/><path d="M9 9l6 6"/>',
  warn:'<circle cx="12" cy="12" r="10"/><path d="M12 8v4"/><path d="M12 16h.01"/>',
  clock:'<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/>',
  zap:'<path d="M13 2 4 14h7l-1 8 9-12h-7l1-6Z"/>',
  history:'<path d="M3 3v6h6"/><path d="M3.5 13A9 9 0 1 0 6 5.3L3 9"/><path d="M12 8v5l3.5 2"/>',
  calendar:'<rect x="3" y="4" width="18" height="17" rx="2"/><path d="M3 9h18"/><path d="M8 2v4"/><path d="M16 2v4"/>',
  save:'<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2Z"/><path d="M17 21v-8H7v8"/><path d="M7 3v5h8"/>',
  package:'<path d="M16.5 9.4 7.5 4.21"/><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"/><path d="m3.3 7 8.7 5 8.7-5"/><path d="M12 22V12"/>',
  tick:'<path d="M20 6 9 17l-5-5"/>',
  printer:'<path d="M6 9V3h12v6"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8" rx="1"/>',
  image:'<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/>',
};
function svg(name){return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">'+(ICONS[name]||'')+'</svg>';}

/* ---------- Kategorien + Aktionen (Spiegel zum C#-Katalog) ---------- */
const CATS = [
  {name:'Übersicht',    icon:'dashboard'},
  {name:'Reparieren',   icon:'wrench'},
  {name:'Netzwerk',     icon:'globe'},
  {name:'Aufräumen',    icon:'trash'},
  {name:'Diagnose',     icon:'activity'},
  {name:'Energie',      icon:'zap'},
  {name:'Wiederherstellung', icon:'rotate'},
  {name:'Geplant',      icon:'calendar'},
  {name:'Verlauf',      icon:'history'},
  {name:'Autostart',    icon:'rocket'},
  {name:'Bloatware',    icon:'package'},
  {name:'Einstellungen',icon:'gear'},
];
const DAYS = [['MON','Montag'],['TUE','Dienstag'],['WED','Mittwoch'],['THU','Donnerstag'],['FRI','Freitag'],['SAT','Samstag'],['SUN','Sonntag']];
const DAY_NAMES = {MON:'Montag',TUE:'Dienstag',WED:'Mittwoch',THU:'Donnerstag',FRI:'Freitag',SAT:'Samstag',SUN:'Sonntag'};
// Sonderaktionen mit eigener Eingabe (nicht id-/queue-basiert, eigener Handler).
const SPECIALS = [
  {cat:'Netzwerk', icon:'activity', title:'Netzwerk-Diagnose', special:'netdiag',
   desc:'Ping und Route (tracert) zu einem Ziel deiner Wahl.'},
  {cat:'Diagnose', icon:'save', title:'Treiber-Backup', special:'driverbackup',
   desc:'Alle installierten Treiber in einen wählbaren Ordner exportieren.'},
];
const INFO_SPECIAL = {
  netdiag:'Prüft, ob und wie gut dein PC ein bestimmtes Ziel im Internet erreicht. „Ping" misst die Antwortzeit, „tracert" zeigt den Weg dorthin Schritt für Schritt. Gut, um Verbindungsprobleme einzukreisen. Gib z. B. google.com oder 8.8.8.8 ein.',
  driverbackup:'Sichert alle zusätzlich installierten Gerätetreiber (z. B. Drucker, Grafik, WLAN) in einen Ordner deiner Wahl. Praktisch vor einer Windows-Neuinstallation – die Treiber lassen sich später daraus wiederherstellen.'
};
const ACTIONS = [
  {id:0,  cat:'Reparieren', icon:'wrench',      title:'Komplett-Reparatur', rec:true,  desc:'DISM ScanHealth + RestoreHealth, danach SFC. Der Rundum-Sorglos-Lauf.'},
  {id:1,  cat:'Reparieren', icon:'refresh',     title:'DISM RestoreHealth',           desc:'Repariert den Komponentenspeicher über Windows Update.'},
  {id:2,  cat:'Reparieren', icon:'shieldCheck', title:'SFC scannow',                  desc:'Prüft und repariert geschützte Systemdateien.'},
  {id:3,  cat:'Reparieren', icon:'search',      title:'SFC nur prüfen',               desc:'Sucht beschädigte Systemdateien, ohne etwas zu ändern.'},
  {id:4,  cat:'Reparieren', icon:'trash',       title:'WinSxS aufräumen',             desc:'Entfernt veraltete Komponenten – macht oft mehrere GB frei.'},
  {id:5,  cat:'Reparieren', icon:'activity',    title:'Komponentenspeicher analysieren', desc:'Zeigt, ob sich ein WinSxS-Cleanup lohnt.'},
  {id:6,  cat:'Reparieren', icon:'rotate',      title:'Windows-Update reparieren',    desc:'Setzt die Update-Komponenten zurück (SoftwareDistribution + catroot2).'},
  {id:7,  cat:'Reparieren', icon:'alert',       title:'CHKDSK planen', danger:true,    desc:'Plant eine Datenträgerprüfung beim nächsten Neustart.'},
  {id:20, cat:'Reparieren', icon:'printer',     title:'Drucker reparieren',           desc:'Leert hängende Druckaufträge und startet die Warteschlange neu.'},
  {id:21, cat:'Reparieren', icon:'clock',       title:'Uhrzeit synchronisieren',      desc:'Gleicht die Systemzeit mit dem Zeitserver ab.'},
  {id:22, cat:'Reparieren', icon:'search',      title:'Windows-Suche reparieren', danger:true, desc:'Setzt den Suchindex zurück – wird im Hintergrund neu aufgebaut.'},

  {id:8,  cat:'Netzwerk', icon:'globe',   title:'Netzwerk-Reset (komplett)', danger:true, desc:'DNS, Winsock und IP-Stack zurücksetzen. Neustart empfohlen.'},
  {id:9,  cat:'Netzwerk', icon:'globe',   title:'DNS-Cache leeren',              desc:'Löscht den DNS-Auflösungscache.'},
  {id:10, cat:'Netzwerk', icon:'refresh', title:'IP-Adresse erneuern',           desc:'Gibt die IP frei und fordert eine neue an.'},

  {id:11, cat:'Aufräumen', icon:'trash',    title:'Temp-Dateien löschen',        desc:'Leert Benutzer- und Windows-Temp-Ordner.'},
  {id:12, cat:'Aufräumen', icon:'download', title:'Update-Cache leeren',          desc:'Löscht heruntergeladene Update-Dateien.'},
  {id:13, cat:'Aufräumen', icon:'trash',    title:'Papierkorb leeren',            desc:'Leert den Papierkorb aller Laufwerke.'},
  {id:14, cat:'Aufräumen', icon:'server',   title:'Datenträgerbereinigung',       desc:'Öffnet das Windows-Tool cleanmgr.'},
  {id:26, cat:'Aufräumen', icon:'image',    title:'Miniaturansichten-Cache leeren', danger:true, desc:'Behebt falsche Vorschaubilder. Die Taskleiste startet kurz neu.'},
  {id:27, cat:'Aufräumen', icon:'package',  title:'Store-Cache leeren',           desc:'Setzt den Microsoft-Store-Cache zurück (wsreset).'},

  {id:15, cat:'Diagnose', icon:'cpu',     title:'System-Übersicht',             desc:'Modell, Windows-Version, RAM und Laufzeit auf einen Blick.'},
  {id:16, cat:'Diagnose', icon:'hdd',     title:'Festplatten-Gesundheit',       desc:'SMART-Status und Typ aller Datenträger.'},
  {id:17, cat:'Diagnose', icon:'battery', title:'Akkubericht erstellen',        desc:'Erzeugt einen powercfg-Akkubericht und öffnet ihn.'},
  {id:18, cat:'Diagnose', icon:'shield',  title:'Defender-Schnellscan',         desc:'Startet einen schnellen Microsoft-Defender-Scan.'},
  {id:19, cat:'Diagnose', icon:'cpu',     title:'RAM-Diagnose planen', danger:true, desc:'Öffnet die Windows-Speicherdiagnose (Neustart nötig).'},
  {id:23, cat:'Diagnose', icon:'alert',   title:'Absturz-Historie',             desc:'Zeigt unerwartete Neustarts und Bluescreens der letzten Zeit.'},
  {id:24, cat:'Diagnose', icon:'globe',   title:'Netzwerk-Übersicht',           desc:'IP-Adresse, Gateway und DNS aller aktiven Adapter.'},
  {id:25, cat:'Diagnose', icon:'rocket',  title:'Startzeit-Analyse',            desc:'Wie lange die letzten Windows-Starts gedauert haben.'},
];
const byId = id => ACTIONS.find(a => a.id === id);

// Erklärungen in einfacher Sprache (für Personen ohne Vorwissen)
const INFO = {
  0:'Lässt Windows seine eigenen Dateien überprüfen und beschädigte automatisch ersetzen. Das gute Erste-Hilfe-Programm, wenn der PC spinnt, abstürzt oder sich komisch verhält. Dauert ein paar Minuten.',
  1:'Repariert den „Bauplan" von Windows über das Internet. Hilft besonders, wenn Updates nicht installieren oder Windows beschädigt ist.',
  2:'Prüft die wichtigen Windows-Systemdateien und repariert beschädigte. Ein Klassiker bei Fehlern und Abstürzen.',
  3:'Schaut nur nach, ob Systemdateien beschädigt sind – ändert nichts. Gut, um erst mal zu sehen, ob alles in Ordnung ist.',
  4:'Löscht alte, nicht mehr benötigte Reste von Windows-Updates. Schafft oft mehrere Gigabyte Platz, ohne dass etwas kaputtgeht.',
  5:'Zeigt nur an, ob sich ein Aufräumen lohnt – verändert nichts.',
  6:'Setzt die Update-Funktion von Windows zurück. Hilft, wenn Updates hängen bleiben oder mit Fehlern abbrechen.',
  7:'Prüft die Festplatte auf Fehler – beim nächsten Neustart. Sinnvoll bei merkwürdigen Datei- oder Festplattenproblemen. Achtung: der nächste Start dauert dann deutlich länger.',
  8:'Setzt die komplette Internet- und Netzwerkeinstellung zurück. Der Notnagel, wenn gar nichts mehr ins Internet kommt. Danach ist ein Neustart nötig.',
  9:'Leert den Zwischenspeicher für Webadressen. Hilft, wenn einzelne Webseiten nicht laden, obwohl das Internet sonst geht.',
  10:'Holt sich eine frische Netzwerk-Adresse vom Router. Hilft bei Verbindungsproblemen im Heimnetz.',
  11:'Löscht temporäre Müll-Dateien, die Programme hinterlassen. Schafft Platz und schadet nichts.',
  12:'Löscht bereits heruntergeladene Update-Dateien. Hilft, wenn Updates klemmen, und schafft Platz.',
  13:'Leert den Papierkorb endgültig. Schafft Platz – die Dateien darin sind danach weg.',
  14:'Öffnet das Windows-Aufräum-Tool, in dem du selbst auswählen kannst, was gelöscht wird.',
  15:'Zeigt Infos zu deinem PC: Windows-Version, Arbeitsspeicher und wie lange er schon läuft.',
  16:'Zeigt, ob deine Festplatten/SSDs gesund sind. Gut für einen schnellen Sicherheits-Check.',
  17:'Erstellt einen Bericht über den Akku (bei Laptops) und öffnet ihn – zeigt z. B. den Verschleiß.',
  18:'Lässt den Windows-Virenschutz schnell die wichtigsten Stellen auf Schädlinge prüfen.',
  19:'Prüft den Arbeitsspeicher auf Fehler – beim nächsten Neustart. Sinnvoll bei häufigen Abstürzen oder Bluescreens.',
  20:'Wenn der Drucker nicht mehr druckt, hängt oft ein alter Druckauftrag fest. Diese Aktion wirft alle wartenden Aufträge raus und startet das Drucksystem neu – danach klappt Drucken meist wieder.',
  21:'Stellt die Uhr des PCs über das Internet richtig. Eine falsche Uhrzeit verursacht überraschend viele Probleme – z. B. Webseiten-Zertifikatsfehler oder fehlgeschlagene Anmeldungen.',
  22:'Baut den Index der Windows-Suche neu auf. Hilft, wenn die Suche im Startmenü oder Explorer nichts oder Falsches findet. Der Neuaufbau läuft im Hintergrund und kann eine Weile dauern.',
  23:'Zeigt, wann der PC zuletzt abgestürzt ist oder unerwartet ausging – und wann es ein normaler Neustart war. Gut, um Problemen auf die Spur zu kommen.',
  24:'Zeigt die wichtigsten Netzwerk-Daten deines PCs: seine Adresse im Netz (IP), den Weg ins Internet (Gateway) und wer Webadressen auflöst (DNS). Praktisch für die Fehlersuche oder am Telefon mit dem Support.',
  25:'Zeigt, wie lange die letzten Windows-Starts gedauert haben. Wird der Start immer langsamer, lohnt ein Blick in die Autostart-Verwaltung.',
  26:'Windows speichert kleine Vorschaubilder für Fotos und Dateien. Ist dieser Speicher beschädigt, zeigen Ordner falsche oder keine Bilder – das behebt diese Aktion. Die Taskleiste verschwindet dabei für einen Moment, das ist normal.',
  27:'Leert den Zwischenspeicher des Microsoft Store. Hilft, wenn der Store nicht öffnet, hängt oder Apps sich nicht installieren lassen. Es öffnet sich kurz ein schwarzes Fenster, danach der Store.'
};

/* ---------- Brücke zu C# (oder Mock im Browser) ---------- */
const HOST = (window.chrome && window.chrome.webview) ? window.chrome.webview : null;
function send(msg){ if(HOST){ HOST.postMessage(JSON.stringify(msg)); } else { mockHandle(msg); } }
if(HOST){ HOST.addEventListener('message', e => { try{ onHost(JSON.parse(e.data)); }catch(_){} }); }

function onHost(m){
  if(m.type==='log')   append(m.text, m.kind);
  else if(m.type==='state') setRunning(m.running);
  else if(m.type==='progress') setProgress(m.percent);
  else if(m.type==='done')  onDone(m.title, m.kind, m.message);
  else if(m.type==='shutdownScheduled') showShutdownBar(m.mode, m.delay);
  else if(m.type==='shutdownCancelled') hideShutdownBar();
  else if(m.type==='update'){ if(SET.autoUpdate) startAutoUpdate(m.version); else showUpdatePrompt(m.version); }
  else if(m.type==='updateProgress') setUpdateProgress(m.percent);
  else if(m.type==='updateStatus') setUpdatePhase(m.phase);
  else if(m.type==='updateError') setUpdateError(m.message);
  else if(m.type==='updated') toast('Aktualisiert','Erfolgreich auf '+m.version+' aktualisiert','good');
  else if(m.type==='stats') updateStats(m);
  else if(m.type==='autostart') renderAutostartList(m.items);
  else if(m.type==='selfstart') renderSelfStart(m);
  else if(m.type==='history') renderHistoryList(m.items);
  else if(m.type==='restorePoints') renderRestoreList(m.items);
  else if(m.type==='powerPlans') renderPowerList(m.items);
  else if(m.type==='bloatPackages') renderBloatList(m.items);
  else if(m.type==='schedule') renderScheduleStatus(m);
  else if(m.type==='zoom'){ SET.zoom=m.factor||1; markZoomActive(); }
  else if(m.type==='admin') setAdmin(m.on);
}
function setAdmin(on){
  const b=$('#admin-badge'), t=$('#admin-text');
  if(!b) return;
  b.classList.toggle('warn', !on);
  if(t) t.textContent = on ? 'Als Administrator' : 'Ohne Administratorrechte';
  b.title = on ? '' : 'Ohne Adminrechte funktionieren Reparaturen nicht. App als Administrator starten.';
}

/* ---------- DOM ---------- */
const $ = s => document.querySelector(s);
const nav = $('#nav'), cards = $('#cards'), body = $('#console-body');
const consoleEl = $('#console'), main = $('#main'), statusEl = $('#status'), statusText = $('#status-text');
let active = 'Reparieren', running = false;
let post = 'none', delay = 60;
let queue = [];

function esc(s){ return String(s==null?'':s).replace(/[&<>"]/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }

const ACCENTS = {
  teal:    ['#2dd4bf','#38bdf8'],
  blau:    ['#3b82f6','#22d3ee'],
  violett: ['#8b5cf6','#a78bfa'],
  gruen:   ['#22c55e','#4ade80'],
  orange:  ['#fb923c','#fbbf24'],
};
function applyAccent(name){
  const c = ACCENTS[name] || ACCENTS.teal;
  document.documentElement.style.setProperty('--accent', c[0]);
  document.documentElement.style.setProperty('--accent-2', c[1]);
}
const SET = {
  accent:      localStorage.getItem('accent') || 'teal',
  consoleOpen: localStorage.getItem('consoleOpen') !== 'false',
  confirmAll:  localStorage.getItem('confirmAll') === 'true',
  notify:      localStorage.getItem('notify') !== 'false',
  autoUpdate:  localStorage.getItem('autoUpdate') === 'true',
  zoom:        1,
};
applyAccent(SET.accent);

const ZOOMS = [['0.9','90 %'],['1','100 %'],['1.15','115 %'],['1.3','130 %'],['1.5','150 %'],['1.75','175 %']];
function markZoomActive(){
  document.querySelectorAll('.zoom-opt').forEach(x=>x.classList.toggle('active', Math.abs(parseFloat(x.dataset.z)-SET.zoom)<0.001));
}

$('#appmark').innerHTML = svg('wrench');
$('#btn-collapse').innerHTML = svg('chevron');
$('#queue-btn .qb-ico').innerHTML = svg('layers');
$('#q-close').innerHTML = svg('close');
$('#shutdown-bar .sb-ico').innerHTML = svg('power');
$('#update-bar .ub-ico').innerHTML = svg('download');
$('#ub-skip').innerHTML = svg('close');
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
  cards.classList.remove('dashboard','settings','autostart','history','power','restore','sched','bloat');
  const isDash = (name==='Übersicht');
  send({type:'dashboard', active:isDash});
  if(isDash){
    $('#cat-title').textContent='Übersicht';
    $('#cat-hint').textContent='Systemzustand auf einen Blick';
    renderDashboard();
    return;
  }
  if(name==='Autostart'){
    $('#cat-title').textContent='Autostart';
    $('#cat-hint').textContent='Programme, die beim Start mitlaufen';
    cards.classList.add('autostart');
    renderAutostart();
    send({type:'autostartList'});
    send({type:'selfStartGet'});
    return;
  }
  if(name==='Einstellungen'){
    $('#cat-title').textContent='Einstellungen';
    $('#cat-hint').textContent='Aussehen & Verhalten';
    cards.classList.add('settings');
    renderSettings();
    return;
  }
  if(name==='Verlauf'){
    $('#cat-title').textContent='Verlauf';
    $('#cat-hint').textContent='Die letzten Ausführungen';
    cards.classList.add('history');
    renderHistory();
    send({type:'historyList'});
    return;
  }
  if(name==='Wiederherstellung'){
    $('#cat-title').textContent='Wiederherstellung';
    $('#cat-hint').textContent='Systemzustand sichern & zurücksetzen';
    cards.classList.add('restore');
    renderRestore();
    send({type:'restoreList'});
    return;
  }
  if(name==='Energie'){
    $('#cat-title').textContent='Energie';
    $('#cat-hint').textContent='Energiesparplan wählen';
    cards.classList.add('power');
    renderPower();
    send({type:'powerList'});
    return;
  }
  if(name==='Geplant'){
    $('#cat-title').textContent='Geplante Wartung';
    $('#cat-hint').textContent='Automatisch im Hintergrund warten lassen';
    cards.classList.add('sched');
    renderSchedule();
    send({type:'scheduleStatus'});
    return;
  }
  if(name==='Bloatware'){
    $('#cat-title').textContent='Bloatware';
    $('#cat-hint').textContent='Vorinstallierte Apps entfernen';
    cards.classList.add('bloat');
    renderBloat();
    send({type:'bloatList'});
    return;
  }
  const list=ACTIONS.filter(a=>a.cat===name);
  const specials=SPECIALS.filter(a=>a.cat===name);
  $('#cat-title').textContent=name;
  $('#cat-hint').textContent=(list.length+specials.length)+' Aktionen · klicken zum Ausführen';
  cards.innerHTML='';
  list.forEach((a,i)=>{
    const el=document.createElement('div');
    el.className='card'+(a.danger?' danger':'');
    el.dataset.id=a.id;
    el.style.animationDelay=(i*30)+'ms';
    el.innerHTML=
      '<button class="card-help" title="Was macht das?">'+svg('help')+'</button>'+
      '<button class="card-add" title="Zur Warteschlange">'+svg('plus')+'</button>'+
      '<div class="card-ico">'+svg(a.icon)+'</div>'+
      '<div class="card-body">'+
        '<div class="card-title">'+a.title+(a.rec?'<span class="tag">empfohlen</span>':'')+'</div>'+
        '<div class="card-desc">'+a.desc+'</div>'+
      '</div>';
    el.onclick=()=>run(a);
    el.querySelector('.card-add').onclick=(e)=>{ e.stopPropagation(); addToQueue(a.id); };
    el.querySelector('.card-help').onclick=(e)=>{ e.stopPropagation(); infoModal(a); };
    cards.appendChild(el);
  });
  specials.forEach((a,i)=>{
    const el=document.createElement('div');
    el.className='card special';
    el.style.animationDelay=((list.length+i)*30)+'ms';
    el.innerHTML=
      '<button class="card-help" title="Was macht das?">'+svg('help')+'</button>'+
      '<div class="card-ico">'+svg(a.icon)+'</div>'+
      '<div class="card-body">'+
        '<div class="card-title">'+a.title+'<span class="tag alt">Eingabe</span></div>'+
        '<div class="card-desc">'+a.desc+'</div>'+
      '</div>';
    el.onclick=()=>runSpecial(a);
    el.querySelector('.card-help').onclick=(e)=>{ e.stopPropagation(); infoModalText(a.title, a.icon, INFO_SPECIAL[a.special]||a.desc); };
    cards.appendChild(el);
  });
  refreshAdded();
}

/* ---------- Dashboard ---------- */
const GC = 2 * Math.PI * 52;
function gaugeBlock(id, label){
  return '<div class="gauge"><svg viewBox="0 0 120 120">'+
    '<circle class="g-track" cx="60" cy="60" r="52"/>'+
    '<circle class="g-arc" id="g-'+id+'-arc" cx="60" cy="60" r="52"/></svg>'+
    '<div class="gauge-center"><span class="gauge-val" id="g-'+id+'-val">0%</span></div>'+
    '<div class="gauge-label">'+label+'</div></div>';
}
function renderDashboard(){
  cards.classList.add('dashboard');
  cards.innerHTML =
    '<div class="dash">'+
      '<div class="dash-gauges">'+ gaugeBlock('cpu','Prozessor') + gaugeBlock('ram','Arbeitsspeicher') + gaugeBlock('disk','Festplatte') +'</div>'+
      '<div class="dash-row">'+
        '<div class="dash-card">'+
          '<div class="health-top">'+
            '<div class="health-ring"><svg viewBox="0 0 120 120"><circle class="g-track" cx="60" cy="60" r="52"/><circle class="g-arc" id="g-hp-arc" cx="60" cy="60" r="52"/></svg>'+
            '<div class="health-num"><span id="health-score">--</span><small>/100</small></div></div>'+
            '<div><div class="health-title" id="health-title">Systemzustand</div><div class="health-sub" id="health-sub">wird ermittelt …</div></div>'+
          '</div>'+
          '<div class="recs" id="recs"></div>'+
        '</div>'+
        '<div class="dash-card">'+
          '<div class="info-title">System</div>'+
          '<div class="info-row"><span>Windows</span><b id="i-os">–</b></div>'+
          '<div class="info-row"><span>Gerät</span><b id="i-model">–</b></div>'+
          '<div class="info-row"><span>Arbeitsspeicher</span><b id="i-ram">–</b></div>'+
          '<div id="i-drives"></div>'+
          '<div class="info-row"><span>Eingeschaltet seit</span><b id="i-uptime">–</b></div>'+
        '</div>'+
      '</div>'+
    '</div>';
  // Ringe leer starten -> beim ersten Stats-Update füllen sie sich synchron zum Zahl-Count-up
  ['cpu','ram','disk','hp'].forEach(function(id){
    var a=$('#g-'+id+'-arc'); if(a){ a.style.strokeDasharray=GC; a.style.strokeDashoffset=GC; }
  });
}
function gaugeColor(p){ return p<60 ? 'var(--green)' : (p<85 ? 'var(--yellow)' : 'var(--red)'); }
function healthColor(s){ return s>=80 ? 'var(--green)' : (s>=50 ? 'var(--yellow)' : 'var(--red)'); }
function setArc(arc, p, color){ p=Math.max(0,Math.min(100,p)); arc.style.strokeDasharray=GC; arc.style.strokeDashoffset=GC*(1-p/100); arc.style.stroke=color; }

// OS-Einstellung „Bewegung reduzieren" (in WebView2/Chromium nativ verfügbar)
const REDUCE = window.matchMedia('(prefers-reduced-motion: reduce)');
// Dezenter Zahl-Count-up: interpoliert NUR textContent (kein Layout-Thrash), easeOutCubic.
// Respektiert prefers-reduced-motion (Endwert sofort) und löst laufende Animationen sauber ab.
// Bewusst ohne Animations-Library – ein paar Zeilen requestAnimationFrame genügen (offline, leichtgewichtig).
function countUp(el, to, suffix){
  if(!el) return;
  suffix = suffix || '';
  to = Math.round(to);
  if(REDUCE.matches || typeof requestAnimationFrame!=='function'){
    if(el._cuRaf){ cancelAnimationFrame(el._cuRaf); el._cuRaf=0; }
    el.textContent = to + suffix; return;
  }
  const cur = parseInt(el.textContent, 10);
  const from = isNaN(cur) ? 0 : cur;
  if(from === to){ el.textContent = to + suffix; return; }
  if(el._cuRaf) cancelAnimationFrame(el._cuRaf); // laufende Animation ablösen (kein Frame-Stau bei schnellen Updates)
  const t0 = performance.now();
  const ease = p => 1 - Math.pow(1-p, 3); // easeOutCubic – dezent, kein Overshoot
  const step = now => {
    const p = Math.min(1, (now - t0) / 600);
    el.textContent = Math.round(from + (to - from) * ease(p)) + suffix;
    el._cuRaf = p < 1 ? requestAnimationFrame(step) : 0;
  };
  el._cuRaf = requestAnimationFrame(step);
}
function setGauge(id, p){ const a=$('#g-'+id+'-arc'); if(a) setArc(a,p,gaugeColor(p)); const v=$('#g-'+id+'-val'); if(v) countUp(v, p, '%'); }
function dTxt(sel,v){ const e=$(sel); if(e) e.textContent=v; }
function updateStats(s){
  if(!cards.classList.contains('dashboard')) return;
  setGauge('cpu', s.cpu); setGauge('ram', s.ram); setGauge('disk', s.disk);
  const hr=$('#g-hp-arc'); if(hr) setArc(hr, s.score, healthColor(s.score));
  countUp($('#health-score'), s.score);
  dTxt('#health-title', s.score>=80?'Alles in Ordnung':(s.score>=50?'Kleinere Hinweise':'Aufmerksamkeit nötig'));
  dTxt('#health-sub', s.score>=80?'Dein PC ist gut in Schuss.':'Siehe Empfehlungen unten.');
  const rc=$('#recs');
  if(rc){
    rc.innerHTML='';
    (s.recs||[]).forEach(r=>{
      const click = r.action>=0;
      const el=document.createElement(click?'button':'div');
      el.className='rec'+(click?' clickable':'');
      el.innerHTML='<span class="rec-dot"></span><span>'+r.text+'</span>'+(click?'<span class="rec-go">'+svg('arrow')+'</span>':'');
      if(click){ const a=byId(r.action); if(a) el.onclick=()=>run(a); }
      rc.appendChild(el);
    });
  }
  dTxt('#i-os', s.os); dTxt('#i-model', s.model);
  dTxt('#i-ram', s.ramUsedGB+' / '+s.ramTotalGB+' GB ('+s.ram+'%)');
  const dv=$('#i-drives');
  if(dv){
    dv.innerHTML='';
    (s.drives||[]).forEach(d=>{
      const row=document.createElement('div'); row.className='info-row';
      const span=document.createElement('span'); span.textContent='Laufwerk '+d.name+(d.label?' · '+d.label:'');
      const b=document.createElement('b'); b.textContent=d.freeGB+' GB frei / '+d.totalGB+' GB';
      row.appendChild(span); row.appendChild(b); dv.appendChild(row);
    });
  }
  dTxt('#i-uptime', s.uptime);
}

/* ---------- Autostart ---------- */
function renderAutostart(){
  cards.innerHTML=
    '<div class="as-top">'+
      '<div class="set-card">'+
        '<div class="set-row" style="padding:0;border:0">'+
          '<div class="set-text"><div class="set-title">Windows-Wartung beim PC-Start starten</div>'+
          '<div class="set-desc">Startet die App automatisch nach der Anmeldung – über die Aufgabenplanung mit Administratorrechten, ganz ohne Nachfrage-Fenster.</div></div>'+
          '<span class="switch"><input type="checkbox" id="as-self" disabled/><i></i></span>'+
        '</div>'+
      '</div>'+
      '<div class="set-card">'+
        '<div class="set-title">Eigene Programme zum Autostart hinzufügen</div>'+
        '<div class="set-desc" style="margin:3px 0 12px">So geht es: Rechtsklick auf das Programm (im Startmenü oder auf dem Desktop) → <b>Verknüpfung erstellen</b> – und diese Verknüpfung in den Autostart-Ordner verschieben. Beim nächsten PC-Start läuft das Programm automatisch mit. Zum Entfernen die Verknüpfung dort einfach löschen.</div>'+
        '<div class="as-folder-btns">'+
          '<button id="as-open-user" class="mb">Autostart-Ordner öffnen</button>'+
          '<button id="as-open-common" class="mb">Ordner für alle Benutzer öffnen</button>'+
        '</div>'+
      '</div>'+
      '<div class="as-group" style="margin:4px 4px 0">Vorhandene Einträge</div>'+
    '</div>'+
    '<div id="as-list"><div class="as-loading">Autostart wird geladen …</div></div>';
  $('#as-self').onchange=e=>{
    e.target.disabled=true; // bis der Host den neuen Status bestätigt
    send({type:'selfStartSet', on:e.target.checked});
  };
  $('#as-open-user').onclick=()=>send({type:'openStartupFolder', scope:'user'});
  $('#as-open-common').onclick=()=>send({type:'openStartupFolder', scope:'common'});
}
function renderSelfStart(m){
  const i=$('#as-self'); if(!i) return;
  i.checked=!!m.on;
  i.disabled=false;
  if(m.changed===false) toast('Nicht geändert','Der Autostart-Eintrag konnte nicht angepasst werden.','bad');
  else if(m.changed===true) toast(m.on?'Autostart aktiv':'Autostart entfernt', m.on?'Windows-Wartung startet künftig mit dem PC.':'Windows-Wartung startet nicht mehr automatisch.','good');
}
function renderAutostartList(items){
  if(!cards.classList.contains('autostart')) return;
  const list=$('#as-list'); if(!list) return;
  if(!items || !items.length){ list.innerHTML='<div class="as-loading">Keine Autostart-Einträge gefunden.</div>'; return; }
  const groups={}; items.forEach(it=>{ (groups[it.locName]=groups[it.locName]||[]).push(it); });
  let html='<div class="as-wrap">';
  Object.keys(groups).forEach(g=>{
    html+='<div class="as-group">'+esc(g)+'</div>';
    groups[g].forEach(it=>{
      html+='<div class="as-item'+(it.enabled?'':' off')+'">'+
        '<div class="as-info"><div class="as-name">'+esc(it.name)+'</div><div class="as-cmd">'+esc(it.cmd||'')+'</div></div>'+
        '<span class="switch"><input type="checkbox" data-loc="'+esc(it.loc)+'" data-key="'+esc(it.key)+'"'+(it.enabled?' checked':'')+'/><i></i></span>'+
      '</div>';
    });
  });
  html+='</div>';
  list.innerHTML=html;
  list.querySelectorAll('.as-item input').forEach(inp=>{
    inp.onchange=()=>{
      inp.closest('.as-item').classList.toggle('off', !inp.checked);
      send({type:'autostartSet', loc:inp.dataset.loc, key:inp.dataset.key, enable:inp.checked});
    };
  });
}

/* ---------- Verlauf ---------- */
function renderHistory(){
  cards.innerHTML='<div class="as-loading">Verlauf wird geladen …</div>';
}
function histResultLabel(kind){
  if(kind==='good') return 'Erfolgreich';
  if(kind==='bad')  return 'Fehlgeschlagen';
  if(kind==='warn') return 'Mit Hinweisen';
  return 'Ausgeführt';
}
function renderHistoryList(items){
  if(!cards.classList.contains('history')) return;
  if(!items || !items.length){
    cards.innerHTML='<div class="hist-empty"><div class="hist-empty-ico">'+svg('clock')+'</div>'+
      '<div>Noch keine Ausführungen aufgezeichnet.</div>'+
      '<span>Sobald du eine Aktion startest, erscheint sie hier.</span></div>';
    return;
  }
  let html='<div class="hist-head"><span class="hist-count">'+items.length+(items.length===1?' Eintrag':' Einträge')+'</span>'+
    '<button id="hist-clear" class="link">Leeren</button></div><div class="hist-wrap">';
  items.forEach(it=>{
    const kind=(it.kind==='good'||it.kind==='bad'||it.kind==='warn')?it.kind:'norm';
    const secs=(it.seconds!=null && it.seconds!=='')?(it.seconds+' s'):'';
    html+='<div class="hist-item">'+
      '<span class="hist-dot '+kind+'"></span>'+
      '<div class="hist-info"><div class="hist-action">'+esc(it.action)+'</div>'+
      '<div class="hist-msg">'+esc(histResultLabel(kind))+(it.message?' · '+esc(it.message):'')+'</div></div>'+
      '<div class="hist-meta"><div class="hist-time">'+esc(it.time)+'</div>'+
      (secs?'<div class="hist-dur">'+esc(secs)+'</div>':'')+'</div>'+
    '</div>';
  });
  html+='</div>';
  cards.innerHTML=html;
  const cb=$('#hist-clear');
  if(cb) cb.onclick=()=>confirmModal('Verlauf leeren','Alle Verlaufseinträge endgültig löschen?',()=>send({type:'historyClear'}));
}

/* ---------- Wiederherstellung ---------- */
function renderRestore(){
  cards.innerHTML=
    '<div class="restore-wrap">'+
      '<div class="rp-note">'+svg('alert')+'<div><b>Wiederherstellungspunkte</b> sichern Systemzustand, Treiber, Programme und Einstellungen. '+
        'Eine Wiederherstellung startet den PC neu und setzt ihn auf einen früheren Stand zurück – <b>persönliche Dateien bleiben unberührt</b>.</div></div>'+
      '<div class="set-card">'+
        '<div class="set-title">Neuen Punkt anlegen</div>'+
        '<div class="set-desc" style="margin:4px 0 12px">Sichert den aktuellen Zustand – ideal vor riskanten Änderungen.</div>'+
        '<div class="rp-create">'+
          '<input id="rp-desc" type="text" maxlength="60" placeholder="Beschreibung (optional)" />'+
          '<button id="rp-create-btn" class="primary">Punkt anlegen</button>'+
        '</div>'+
      '</div>'+
      '<div class="rp-list-head"><span class="hist-count">Vorhandene Punkte</span><button id="rp-refresh" class="link">Aktualisieren</button></div>'+
      '<div id="rp-list"><div class="as-loading">Wiederherstellungspunkte werden geladen …</div></div>'+
    '</div>';
  $('#rp-create-btn').onclick=()=>{
    if(running){ if(consoleEl.classList.contains('open')) append('Es läuft bereits eine Aktion – bitte warten.','warn'); return; }
    send({type:'restoreCreate', desc:$('#rp-desc').value.trim()});
  };
  $('#rp-refresh').onclick=()=>{ const l=$('#rp-list'); if(l) l.innerHTML='<div class="as-loading">Wird geladen …</div>'; send({type:'restoreList'}); };
}
function rpTypeLabel(t){
  t=parseInt(t,10);
  if(t===0)  return 'Anwendung';
  if(t===10) return 'Systemänderung';
  if(t===12) return 'Einstellungen';
  if(t===13) return 'Deinstallation';
  return 'Punkt';
}
function renderRestoreList(items){
  if(!cards.classList.contains('restore')) return;
  const list=$('#rp-list'); if(!list) return;
  if(!items || !items.length){
    list.innerHTML='<div class="rp-empty">Keine Wiederherstellungspunkte vorhanden – der Systemschutz ist möglicherweise deaktiviert. Lege oben einen Punkt an, um ihn zu aktivieren.</div>';
    return;
  }
  let html='<div class="rp-items">';
  items.forEach(it=>{
    html+='<div class="rp-item">'+
      '<div class="rp-ico">'+svg('rotate')+'</div>'+
      '<div class="rp-info"><div class="rp-desc-t">'+esc(it.desc||'(ohne Beschreibung)')+'</div>'+
      '<div class="rp-meta">'+esc(it.time||'')+' · '+esc(rpTypeLabel(it.rtype))+' · Nr. '+esc(it.seq)+'</div></div>'+
      '<button class="rp-revert" data-seq="'+esc(it.seq)+'" data-desc="'+esc(it.desc||'')+'">Zurücksetzen</button>'+
    '</div>';
  });
  html+='</div>';
  list.innerHTML=html;
  list.querySelectorAll('.rp-revert').forEach(b=>{
    b.onclick=()=>confirmRevert(parseInt(b.dataset.seq,10), b.dataset.desc||'(ohne Beschreibung)');
  });
}
function confirmRevert(seq, desc){
  if(isNaN(seq)||seq<=0) return;
  // Wegen Tragweite: zweistufige Bestätigung
  confirmModal('Wirklich wiederherstellen?',
    'Windows wird auf „'+esc(desc)+'" zurückgesetzt und der PC startet sofort neu. Programme, Treiber und Einstellungen, die danach geändert wurden, gehen verloren. Persönliche Dateien bleiben erhalten.',
    ()=>confirmModal('Letzte Sicherheitsfrage',
      'Jetzt endgültig zurücksetzen und neu starten?',
      ()=>send({type:'restoreRevert', seq:seq})));
}

/* ---------- Energie ---------- */
function renderPower(){
  cards.innerHTML=
    '<div class="power-wrap">'+
      '<div class="rp-note">'+svg('zap')+'<div>Der Energiesparplan steuert das Verhältnis von <b>Leistung</b> und <b>Stromverbrauch</b>. '+
        '„Höchstleistung" ist schneller, braucht aber mehr Strom; „Energiesparmodus" schont den Akku.</div></div>'+
      '<div id="power-list"><div class="as-loading">Energiepläne werden geladen …</div></div>'+
    '</div>';
}
function renderPowerList(items){
  if(!cards.classList.contains('power')) return;
  const list=$('#power-list'); if(!list) return;
  if(!items || !items.length){ list.innerHTML='<div class="rp-empty">Keine Energiepläne gefunden.</div>'; return; }
  let html='<div class="power-items">';
  items.forEach(p=>{
    const on=!!p.active;
    html+='<button class="power-item'+(on?' on':'')+'" data-guid="'+esc(p.guid)+'">'+
      '<span class="power-radio"></span>'+
      '<span class="power-name">'+esc(p.name)+'</span>'+
      (on?'<span class="power-active">aktiv</span>':'')+
    '</button>';
  });
  html+='</div>';
  list.innerHTML=html;
  list.querySelectorAll('.power-item').forEach(b=>{
    b.onclick=()=>{
      if(b.classList.contains('on')) return;
      list.querySelectorAll('.power-item').forEach(x=>{ x.classList.remove('on'); const a=x.querySelector('.power-active'); if(a) a.remove(); });
      b.classList.add('on');
      send({type:'powerSet', guid:b.dataset.guid});
    };
  });
}

/* ---------- Bloatware ---------- */
let bloatSel = new Set();
function renderBloat(){
  bloatSel = new Set();
  cards.innerHTML=
    '<div class="bloat-wrap">'+
      '<div class="rp-note">'+svg('shield')+'<div>Entfernt <b>vorinstallierte Apps</b>, die viele nicht brauchen. Aus Sicherheit erscheinen <b>nur als unbedenklich bekannte</b> Apps – System, Store und Virenschutz sind geschützt. Entfernte Apps lassen sich jederzeit kostenlos über den <b>Microsoft Store</b> neu installieren.</div></div>'+
      '<div id="bloat-body"><div class="as-loading">Installierte Apps werden geladen …</div></div>'+
    '</div>';
}
function renderBloatList(items){
  if(!cards.classList.contains('bloat')) return;
  const body=$('#bloat-body'); if(!body) return;
  bloatSel=new Set();
  if(!items || !items.length){
    body.innerHTML='<div class="rp-empty">Keine bekannte Bloatware gefunden – auf diesem PC ist nichts aus der sicheren Liste vorinstalliert.</div>';
    return;
  }
  let html='<div class="bloat-bar">'+
      '<label class="bloat-rp"><span class="switch"><input type="checkbox" id="bloat-rp" checked/><i></i></span><span>Vorher Wiederherstellungspunkt anlegen</span></label>'+
      '<div class="bloat-bar-actions">'+
        '<button id="bloat-all" class="link">Alle</button>'+
        '<button id="bloat-none" class="link">Keine</button>'+
        '<button id="bloat-remove" class="primary" disabled>Entfernen</button>'+
      '</div>'+
    '</div>';
  const groups={}; items.forEach(it=>{ (groups[it.cat]=groups[it.cat]||[]).push(it); });
  html+='<div class="bloat-items">';
  Object.keys(groups).forEach(g=>{
    html+='<div class="as-group">'+esc(g)+'</div>';
    groups[g].forEach(it=>{
      html+='<button class="bloat-item" data-full="'+esc(it.full)+'">'+
        '<span class="bloat-box">'+svg('tick')+'</span>'+
        '<div class="bloat-ico">'+svg('package')+'</div>'+
        '<div class="bloat-info"><div class="bloat-name">'+esc(it.label)+'</div>'+
        '<div class="bloat-meta">'+esc(it.name)+(it.pub?' · '+esc(it.pub):'')+'</div></div>'+
      '</button>';
    });
  });
  html+='</div>';
  body.innerHTML=html;
  body.querySelectorAll('.bloat-item').forEach(b=>{
    b.onclick=()=>{
      const f=b.dataset.full;
      if(bloatSel.has(f)){ bloatSel.delete(f); b.classList.remove('sel'); }
      else { bloatSel.add(f); b.classList.add('sel'); }
      updateBloatRemove();
    };
  });
  $('#bloat-all').onclick=()=>{ body.querySelectorAll('.bloat-item').forEach(b=>{ bloatSel.add(b.dataset.full); b.classList.add('sel'); }); updateBloatRemove(); };
  $('#bloat-none').onclick=()=>{ bloatSel.clear(); body.querySelectorAll('.bloat-item').forEach(b=>b.classList.remove('sel')); updateBloatRemove(); };
  $('#bloat-remove').onclick=()=>bloatRemoveFlow();
  updateBloatRemove();
}
function updateBloatRemove(){
  const btn=$('#bloat-remove'); if(!btn) return;
  const n=bloatSel.size;
  btn.disabled = running || n===0;
  btn.textContent = n>0 ? ('Entfernen ('+n+')') : 'Entfernen';
}
function bloatRemoveFlow(){
  if(running){ if(consoleEl.classList.contains('open')) append('Es läuft bereits eine Aktion – bitte warten.','warn'); return; }
  const fulls=Array.from(bloatSel).filter(f=>/^[A-Za-z0-9._-]+$/.test(f));
  if(!fulls.length) return;
  const labels=[];
  cards.querySelectorAll('.bloat-item.sel .bloat-name').forEach(e=>labels.push(e.textContent));
  const rp = !!($('#bloat-rp') && $('#bloat-rp').checked);
  const shown=labels.slice(0,8).map(l=>'• '+esc(l)).join('<br>');
  const more=labels.length>8 ? '<br>… und '+(labels.length-8)+' weitere' : '';
  // Wegen Tragweite: zweistufige Bestätigung (wie bei der Wiederherstellung).
  confirmModal('Ausgewählte Apps entfernen?',
    '<b>'+fulls.length+'</b> App(en) werden entfernt:<br><br>'+shown+more+'<br><br>'+
    (rp?'Vorher wird ein Wiederherstellungspunkt angelegt. ':'')+
    'Die Apps lassen sich später über den Microsoft Store wieder installieren.',
    ()=>confirmModal('Letzte Sicherheitsfrage',
      'Jetzt '+fulls.length+' App(en) endgültig entfernen?',
      ()=>send({type:'bloatRemove', fulls:fulls, restore:rp})));
}

/* ---------- Geplante Wartung ---------- */
// Spiegel zu Catalog.AutoCatalog() in C# (Schlüssel + Standard-Satz müssen übereinstimmen).
const AUTO_TASKS = [
  {key:'dism',     std:true,  title:'Windows reparieren (DISM)',  desc:'Repariert den Komponentenspeicher über Windows Update.'},
  {key:'sfc',      std:true,  title:'Systemdateien prüfen (SFC)', desc:'Prüft und repariert geschützte Systemdateien.'},
  {key:'temp',     std:true,  title:'Temp-Dateien löschen',       desc:'Leert Benutzer- und Windows-Temp-Ordner.'},
  {key:'bin',      std:true,  title:'Papierkorb leeren',          desc:'Leert den Papierkorb aller Laufwerke.'},
  {key:'winsxs',   std:false, title:'WinSxS aufräumen',           desc:'Entfernt veraltete Update-Komponenten (dauert länger).'},
  {key:'updcache', std:false, title:'Update-Cache leeren',        desc:'Löscht heruntergeladene Update-Dateien.'},
  {key:'dns',      std:false, title:'DNS-Cache leeren',           desc:'Löscht den DNS-Auflösungscache.'},
  {key:'defender', std:false, title:'Defender-Schnellscan',       desc:'Kurzer Virenscan der wichtigsten Bereiche.'},
];
const AUTO_STD_KEYS = AUTO_TASKS.filter(t=>t.std).map(t=>t.key);
let schedMode='daily';

function schedUpdateCount(){
  const n=AUTO_TASKS.filter(t=>{ const i=$('#at-'+t.key); return i&&i.checked; }).length;
  const c=$('#sched-count'); if(c) c.textContent = n+(n===1?' Aufgabe':' Aufgaben')+' ausgewählt';
  const s=$('#sched-save'); if(s) s.disabled = n===0;
}
function renderSchedule(){
  schedMode='daily';
  let taskRows='';
  AUTO_TASKS.forEach(t=>{
    taskRows+='<div class="set-row"><div class="set-text">'+
      '<div class="set-title">'+t.title+(t.std?'<span class="std-tag">Standard</span>':'')+'</div>'+
      '<div class="set-desc">'+t.desc+'</div></div>'+
      toggleHTML('at-'+t.key, t.std)+'</div>';
  });
  let domOpts='';
  for(let i=1;i<=31;i++) domOpts+='<option value="'+i+'">'+i+'.</option>';
  cards.innerHTML=
    '<div class="sched-wrap">'+
      '<div class="rp-note">'+svg('calendar')+'<div>Die geplante Wartung läuft <b>automatisch im Hintergrund</b> (mit Administratorrechten) und meldet sich danach per Benachrichtigung. Unten lässt sich einstellen, <b>wann</b> sie läuft und <b>was</b> sie erledigt – ohne Änderung gilt der bewährte Standard. Ist die App gerade geöffnet, wird der Termin übersprungen.</div></div>'+
      '<div id="sched-status" class="sched-status"><div class="as-loading">Status wird geladen …</div></div>'+
      '<div class="set-card">'+
        '<div class="set-title" style="margin-bottom:14px">Wann? – Zeitplan</div>'+
        '<div class="sched-form">'+
          '<div class="sched-field"><label>Intervall</label>'+
            '<div class="seg sched-seg" id="sched-mode">'+
              '<button data-mode="daily" class="active">Täglich</button>'+
              '<button data-mode="weekly">Wöchentlich</button>'+
              '<button data-mode="monthly">Monatlich</button>'+
            '</div>'+
          '</div>'+
          '<div class="sched-field hidden" id="sched-days-field"><label>An diesen Tagen</label>'+
            '<div class="day-chips" id="sched-days">'+DAYS.map(d=>'<button type="button" class="day-chip'+(d[0]==='SUN'?' on':'')+'" data-day="'+d[0]+'" title="'+d[1]+'">'+d[1].slice(0,2)+'</button>').join('')+'</div>'+
          '</div>'+
          '<div class="sched-field hidden" id="sched-dom-field"><label>Tag des Monats</label>'+
            '<select id="sched-dom" class="sched-select">'+domOpts+'</select>'+
          '</div>'+
          '<div class="sched-field"><label>Uhrzeit</label>'+
            '<input id="sched-time" type="time" value="12:00" class="sched-time" />'+
          '</div>'+
        '</div>'+
        '<div class="sched-dom-hint hidden" id="sched-dom-hint">Hinweis: In Monaten ohne diesen Tag (z. B. Februar) wird der Lauf übersprungen.</div>'+
      '</div>'+
      '<div class="set-card">'+
        '<div class="set-title">Was? – Aufgaben des Wartungslaufs</div>'+
        '<div class="set-desc" style="margin:3px 0 4px">Frei wählbar. Ohne Änderung läuft der bewährte Standard-Satz (Reparatur + Aufräumen).</div>'+
        '<div id="sched-tasks">'+taskRows+'</div>'+
        '<div class="sched-tasks-foot"><span class="sched-count" id="sched-count"></span><button id="sched-reset" class="link">Auf Standard zurücksetzen</button></div>'+
      '</div>'+
      '<div class="sched-save-row"><button id="sched-save" class="primary">Zeitplan speichern</button></div>'+
    '</div>';
  document.querySelectorAll('#sched-mode button').forEach(b=>{
    b.onclick=()=>{
      document.querySelectorAll('#sched-mode button').forEach(x=>x.classList.remove('active'));
      b.classList.add('active'); schedMode=b.dataset.mode;
      $('#sched-days-field').classList.toggle('hidden', schedMode!=='weekly');
      $('#sched-dom-field').classList.toggle('hidden', schedMode!=='monthly');
      domHint();
    };
  });
  const domHint=()=>{
    const v=parseInt($('#sched-dom').value,10)||1;
    $('#sched-dom-hint').classList.toggle('hidden', !(schedMode==='monthly' && v>28));
  };
  $('#sched-dom').onchange=domHint;
  cards.querySelectorAll('.day-chip').forEach(b=>{ b.onclick=()=>b.classList.toggle('on'); });
  cards.querySelectorAll('#sched-tasks input').forEach(i=>{ i.onchange=schedUpdateCount; });
  $('#sched-reset').onclick=()=>{
    AUTO_TASKS.forEach(t=>{ const i=$('#at-'+t.key); if(i) i.checked=t.std; });
    schedUpdateCount();
  };
  schedUpdateCount();
  $('#sched-save').onclick=()=>{
    const time=$('#sched-time').value||'12:00';
    const parts=time.split(':');
    const hh=parseInt(parts[0],10), mm=parseInt(parts[1],10);
    if(isNaN(hh)||isNaN(mm)) return;
    const msg={type:'scheduleCreate', mode:schedMode, hh:hh, mm:mm};
    if(schedMode==='weekly'){
      const days=Array.from(cards.querySelectorAll('.day-chip.on')).map(b=>b.dataset.day);
      if(!days.length){ toast('Kein Wochentag','Bitte mindestens einen Wochentag auswählen.','warn'); return; }
      msg.days=days;
    }
    if(schedMode==='monthly') msg.dom=parseInt($('#sched-dom').value,10)||1;
    const sel=AUTO_TASKS.filter(t=>{ const i=$('#at-'+t.key); return i&&i.checked; }).map(t=>t.key);
    if(!sel.length){ toast('Keine Aufgabe','Bitte mindestens eine Aufgabe auswählen.','warn'); return; }
    // Entspricht die Auswahl exakt dem Standard, wird sie NICHT gespeichert ->
    // es gilt weiterhin der (ggf. künftig verbesserte) Standard-Satz.
    const isStd = sel.length===AUTO_STD_KEYS.length && AUTO_STD_KEYS.every(k=>sel.indexOf(k)>=0);
    if(!isStd) msg.actions=sel;
    send(msg);
  };
}
function schedWhenLabel(c){
  if(c.mode==='weekly'){
    let names=[];
    if(c.days && c.days.length) names=c.days.map(d=>DAY_NAMES[d]||d);
    else if(c.day) names=[DAY_NAMES[c.day]||c.day]; // Config aus Versionen bis 6.2
    return 'Jeden '+names.map(esc).join(', ')+' um '+esc(c.time)+' Uhr';
  }
  if(c.mode==='monthly') return 'Am '+(parseInt(c.dom,10)||1)+'. jedes Monats um '+esc(c.time)+' Uhr';
  return 'Täglich um '+esc(c.time)+' Uhr';
}
function schedTasksLabel(c){
  if(c.actions && c.actions.length){
    const names=c.actions.map(k=>{ const t=AUTO_TASKS.find(x=>x.key===k); return t?t.title:null; }).filter(Boolean);
    if(names.length) return names.join(' · ');
  }
  return 'Standard-Wartung: Reparatur (DISM + SFC) und Aufräumen (Temp + Papierkorb)';
}
function renderScheduleStatus(d){
  const st=$('#sched-status'); if(!st) return;
  if(d.justCreated===false) toast('Nicht gespeichert','Der Zeitplan konnte nicht angelegt werden.','bad');
  else if(d.justCreated===true) toast('Geplant','Die automatische Wartung ist eingerichtet.','good');
  if(d.exists && d.config){
    const c=d.config;
    st.innerHTML='<div class="sched-on"><span class="sched-badge">Aktiv</span>'+
      '<div class="sched-on-body"><div class="sched-on-txt">'+schedWhenLabel(c)+'</div>'+
      '<div class="sched-on-sub">'+esc(schedTasksLabel(c))+'</div></div>'+
      '<button id="sched-del" class="link danger">Entfernen</button></div>';
    $('#sched-del').onclick=()=>confirmModal('Geplante Wartung entfernen','Den automatischen Wartungstermin wirklich löschen?',()=>send({type:'scheduleDelete'}));
  } else {
    st.innerHTML='<div class="sched-off"><span class="sched-dot"></span>Aktuell ist keine automatische Wartung eingerichtet.</div>';
  }
}

/* ---------- Sonderaktionen (mit Eingabe) ---------- */
function runSpecial(a){
  if(running){ if(consoleEl.classList.contains('open')) append('Es läuft bereits eine Aktion – bitte warten oder stoppen.','warn'); return; }
  if(a.special==='netdiag'){
    promptModal('Netzwerk-Diagnose','Ziel eingeben – Webadresse oder IP (z. B. google.com oder 8.8.8.8):','google.com', val=>{
      const t=(val||'').trim();
      if(!t) return;
      if(!/^[a-zA-Z0-9][a-zA-Z0-9.\-:]*$/.test(t)){ toast('Ungültiges Ziel','Nur Buchstaben, Zahlen, Punkt und Bindestrich erlaubt.','bad'); return; }
      send({type:'netDiag', target:t});
    });
  } else if(a.special==='driverbackup'){
    confirmModal('Treiber-Backup','Im nächsten Schritt wählst du einen Zielordner. Anschließend werden alle installierten Treiber dorthin exportiert. Das kann ein paar Minuten dauern. Fortfahren?', ()=>send({type:'driverBackup'}));
  }
}
function promptModal(title, label, preset, onOk){
  const ov=document.createElement('div'); ov.className='modal-ov';
  ov.innerHTML=
    '<div class="modal">'+
      '<div class="modal-ico accent">'+svg('globe')+'</div>'+
      '<h3>'+esc(title)+'</h3><p>'+esc(label)+'</p>'+
      '<input class="modal-input" type="text" spellcheck="false" />'+
      '<div class="modal-btns"><button class="mb cancel">Abbrechen</button><button class="mb ok">Starten</button></div>'+
    '</div>';
  document.body.appendChild(ov);
  const inp=ov.querySelector('.modal-input');
  inp.value=preset||'';
  setTimeout(()=>{ try{ inp.focus(); inp.select(); }catch(_){} }, 60);
  const go=()=>{ const v=inp.value; ov.remove(); onOk(v); };
  ov.querySelector('.cancel').onclick=()=>ov.remove();
  ov.querySelector('.ok').onclick=go;
  inp.addEventListener('keydown', e=>{ if(e.key==='Enter'){ e.preventDefault(); go(); } });
  ov.onclick=e=>{ if(e.target===ov) ov.remove(); };
}
function infoModalText(title, icon, body){
  const ov=document.createElement('div'); ov.className='modal-ov';
  ov.innerHTML=
    '<div class="modal info-modal">'+
      '<div class="modal-ico accent">'+svg(icon)+'</div>'+
      '<h3>'+esc(title)+'</h3>'+
      '<div class="info-lead">In einfachen Worten</div>'+
      '<p class="info-body">'+esc(body)+'</p>'+
      '<div class="modal-btns"><button class="mb ok">Verstanden</button></div>'+
    '</div>';
  document.body.appendChild(ov);
  ov.querySelector('.ok').onclick=()=>ov.remove();
  ov.onclick=e=>{ if(e.target===ov) ov.remove(); };
}

/* ---------- Einstellungen ---------- */
function toggleHTML(id, on){ return '<span class="switch"><input type="checkbox" id="'+id+'"'+(on?' checked':'')+'/><i></i></span>'; }
function renderSettings(){
  let sw='';
  Object.keys(ACCENTS).forEach(k=>{
    const c=ACCENTS[k];
    sw+='<button class="swatch'+(SET.accent===k?' active':'')+'" data-acc="'+k+'" style="background:linear-gradient(135deg,'+c[0]+','+c[1]+')" title="'+k+'"></button>';
  });
  let zb='';
  ZOOMS.forEach(z=>{ zb+='<button class="zoom-opt'+(Math.abs(parseFloat(z[0])-SET.zoom)<0.001?' active':'')+'" data-z="'+z[0]+'">'+z[1]+'</button>'; });
  cards.innerHTML=
    '<div class="settings-wrap">'+
      '<div class="set-card">'+
        '<div class="set-title">Größe der Oberfläche</div>'+
        '<div class="set-desc" style="margin-bottom:14px">Alles größer anzeigen – angenehm bei Brille oder kleiner Schrift</div>'+
        '<div class="zoom-opts">'+zb+'</div>'+
      '</div>'+
      '<div class="set-card">'+
        '<div class="set-row"><div class="set-text"><div class="set-title">Windows-Benachrichtigungen</div><div class="set-desc">Mitteilung anzeigen, wenn eine Aktion fertig ist (während das Fenster im Hintergrund ist)</div></div>'+toggleHTML('s-notify', SET.notify)+'</div>'+
        '<div class="set-row"><div class="set-text"><div class="set-title">Konsole beim Start öffnen</div><div class="set-desc">Den Ausgabe-Bereich direkt sichtbar anzeigen</div></div>'+toggleHTML('s-console', SET.consoleOpen)+'</div>'+
        '<div class="set-row"><div class="set-text"><div class="set-title">Immer vor dem Ausführen fragen</div><div class="set-desc">Sicherheitsabfrage auch für harmlose Aktionen</div></div>'+toggleHTML('s-confirm', SET.confirmAll)+'</div>'+
        '<div class="set-row"><div class="set-text"><div class="set-title">Updates automatisch installieren</div><div class="set-desc">Wird beim Start eine neue Version gefunden, installiert sie sich ohne Nachfrage (Prüfsummen-Kontrolle bleibt aktiv) und das Programm startet neu</div></div>'+toggleHTML('s-autoupdate', SET.autoUpdate)+'</div>'+
      '</div>'+
      '<div class="set-card">'+
        '<div class="set-title">Akzentfarbe</div>'+
        '<div class="set-desc" style="margin-bottom:14px">Farbe der Oberfläche</div>'+
        '<div class="swatches">'+sw+'</div>'+
      '</div>'+
    '</div>';
  cards.querySelectorAll('.zoom-opt').forEach(b=>{
    b.onclick=()=>{ SET.zoom=parseFloat(b.dataset.z); send({type:'setZoom', factor:SET.zoom}); markZoomActive(); };
  });
  $('#s-notify').onchange=e=>{ SET.notify=e.target.checked; localStorage.setItem('notify', SET.notify); send({type:'setNotify', on:SET.notify}); };
  $('#s-console').onchange=e=>{ SET.consoleOpen=e.target.checked; localStorage.setItem('consoleOpen', SET.consoleOpen); };
  $('#s-confirm').onchange=e=>{ SET.confirmAll=e.target.checked; localStorage.setItem('confirmAll', SET.confirmAll); };
  $('#s-autoupdate').onchange=e=>{ SET.autoUpdate=e.target.checked; localStorage.setItem('autoUpdate', SET.autoUpdate); };
  cards.querySelectorAll('.swatch').forEach(b=>{
    b.onclick=()=>{ SET.accent=b.dataset.acc; localStorage.setItem('accent', SET.accent); applyAccent(SET.accent); cards.querySelectorAll('.swatch').forEach(x=>x.classList.toggle('active', x===b)); };
  });
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
  updateBloatRemove();
  if(r){ statusEl.classList.add('run'); statusText.textContent='läuft …'; }
  else { statusEl.classList.remove('run'); statusText.textContent='bereit'; setProgress(-1); }
}
// Live-Fortschritt von DISM/SFC (oder -1 zum Ausblenden)
function setProgress(pct){
  const w=$('#console-progress'), b=$('#console-progress-bar');
  if(pct==null || pct<0){ if(w) w.classList.remove('show'); if(b) b.style.width='0%'; if(running) statusText.textContent='läuft …'; return; }
  if(pct>100) pct=100;
  if(w) w.classList.add('show');
  if(b) b.style.width=pct+'%';
  if(running) statusText.textContent='läuft … '+pct+' %';
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
  else if(SET.confirmAll) confirmModal(a.title, 'Diese Aktion jetzt ausführen?', payload);
  else payload();
}
function onDone(title, kind, message){
  if(!consoleEl.classList.contains('open')) toast(title, message, kind);
  // Nach dem Anlegen eines Punktes die Liste auffrischen
  if(active==='Wiederherstellung') send({type:'restoreList'});
  // Nach dem Entfernen die Bloatware-Liste neu laden (zeigt verbleibende Apps)
  if(active==='Bloatware') send({type:'bloatList'});
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

/* ---------- Update (Banner + Dialog) ---------- */
$('#um-ico').innerHTML = svg('download');
let updateVersion='';

function showUpdate(version){ // kleines Banner oben (Fallback nach "Später")
  $('#ub-text').innerHTML = 'Neue Version <b>'+version+'</b> verfügbar';
  $('#update-bar').classList.add('show');
}
function umSet(title, html, opts){
  opts=opts||{};
  $('#um-title').textContent=title;
  $('#um-text').innerHTML=html;
  $('#um-progress').classList.toggle('hidden', !opts.progress);
  $('#um-btns').classList.toggle('hidden', !opts.buttons);
}
function showUpdatePrompt(version){
  updateVersion=version||'';
  $('#update-bar').classList.remove('show');
  umSet('Update verfügbar', 'Version <b>'+updateVersion+'</b> ist verfügbar.<br>Jetzt herunterladen?', {buttons:true});
  $('#um-later').textContent='Später';
  $('#um-go').textContent='Jetzt herunterladen';
  $('#um-later').onclick=()=>{ hideUpdateModal(); showUpdate(updateVersion); };
  $('#um-go').onclick=()=>startUpdateFlow();
  $('#update-modal').classList.remove('hidden');
}
function startUpdateFlow(){
  setUpdateProgress(0);
  umSet('Wird heruntergeladen …', 'Lade '+updateVersion+' …', {progress:true});
  $('#update-modal').classList.remove('hidden');
  send({type:'startUpdate'});
}
function startAutoUpdate(version){ // Einstellung „Updates automatisch installieren"
  updateVersion=version||'';
  $('#update-bar').classList.remove('show');
  setUpdateProgress(0);
  umSet('Update wird automatisch installiert', 'Version '+updateVersion+' wird geladen – das Programm startet gleich neu …', {progress:true});
  $('#update-modal').classList.remove('hidden');
  send({type:'startUpdate'});
}
function setUpdateProgress(p){ $('#um-bar').style.width=(p||0)+'%'; }
function setUpdatePhase(phase){
  if(phase==='download') umSet('Wird heruntergeladen …','Lade '+updateVersion+' …',{progress:true});
  else if(phase==='extract'){ setUpdateProgress(100); umSet('Wird entpackt …','Fast fertig …',{progress:true}); }
  else if(phase==='restart') umSet('Neustart …','Das Programm startet sich neu …',{progress:true});
}
function setUpdateError(msg){
  umSet('Update fehlgeschlagen', (msg||'Unbekannter Fehler'), {buttons:true});
  $('#um-later').textContent='Schließen';
  $('#um-go').textContent='Im Browser öffnen';
  $('#um-later').onclick=()=>hideUpdateModal();
  $('#um-go').onclick=()=>{ send({type:'openUpdate'}); hideUpdateModal(); };
}
function hideUpdateModal(){ $('#update-modal').classList.add('hidden'); }
$('#ub-get').onclick=()=>startUpdateFlow();
$('#ub-skip').onclick=()=>{ $('#update-bar').classList.remove('show'); }; // nur ausblenden, erscheint beim nächsten Start wieder

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

/* ---------- Erklär-Dialog (einfache Sprache) ---------- */
function infoModal(a){
  const ov=document.createElement('div'); ov.className='modal-ov';
  const warn = a.danger ? '<div class="info-warn">Achtung: Diese Aktion bewusst einsetzen – sie greift tiefer ins System ein.</div>' : '';
  ov.innerHTML=
    '<div class="modal info-modal">'+
      '<div class="modal-ico '+(a.danger?'warn':'accent')+'">'+svg(a.icon)+'</div>'+
      '<h3>'+a.title+'</h3>'+
      '<div class="info-lead">In einfachen Worten</div>'+
      '<p class="info-body">'+(INFO[a.id]||a.desc)+'</p>'+
      warn+
      '<div class="modal-btns"><button class="mb cancel">Schließen</button><button class="mb ok">Ausführen</button></div>'+
    '</div>';
  document.body.appendChild(ov);
  ov.querySelector('.cancel').onclick=()=>ov.remove();
  ov.querySelector('.ok').onclick=()=>{ ov.remove(); run(a); };
  ov.onclick=e=>{ if(e.target===ov) ov.remove(); };
}

/* ---------- Fenstersteuerung / Titelleiste ---------- */
document.querySelectorAll('.wc').forEach(b=>b.onclick=()=>send({type:'win', action:b.dataset.win}));
const tbDrag=document.querySelector('.tb-left');
tbDrag.addEventListener('mousedown', e=>{ if(e.button===0) send({type:'win', action:'drag'}); });
tbDrag.addEventListener('dblclick', ()=>send({type:'win', action:'max'}));

document.querySelectorAll('.rz').forEach(g=>{
  g.addEventListener('mousedown', e=>{ if(e.button===0){ e.preventDefault(); send({type:'resize', dir:g.dataset.rz}); } });
});

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
  else if(msg.type==='startUpdate'){
    let p=0; setUpdatePhase('download');
    const t=setInterval(()=>{
      p+=12;
      if(p>=100){ clearInterval(t); setUpdateProgress(100);
        setTimeout(()=>setUpdatePhase('extract'), 350);
        setTimeout(()=>setUpdatePhase('restart'), 950);
        setTimeout(()=>{ hideUpdateModal(); toast('Aktualisiert','Erfolgreich auf '+(updateVersion||'v4.3')+' aktualisiert','good'); }, 1800);
      } else setUpdateProgress(p);
    }, 170);
  }
}

function welcome(){
  append('Windows-Wartung  ·  bereit','head');
  append('Aktion links auswählen und auf eine Kachel klicken.','dim');
  append('','norm');
}

/* ---------- Start ---------- */
buildNav();
selectCat('Übersicht');
renderQueue();
welcome();
setConsole(SET.consoleOpen);
send({type:'setNotify', on:SET.notify});

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
if(location.hash==='#updateprompt'){ showUpdatePrompt('v4.3'); }
else if(location.hash==='#updating'){ updateVersion='v4.3'; umSet('Wird heruntergeladen …','Lade v4.3 …',{progress:true}); setUpdateProgress(62); $('#update-modal').classList.remove('hidden'); }
else if(location.hash==='#updated'){ toast('Aktualisiert','Erfolgreich auf v4.3 aktualisiert','good'); }
else if(location.hash==='#updatebar'){ showUpdate('v4.2'); }
else if(location.hash==='#info'){ selectCat('Reparieren'); infoModal(byId(7)); }
else if(location.hash==='#autostart'){ selectCat('Autostart'); }
else if(location.hash==='#settings'){ selectCat('Einstellungen'); }
else if(location.hash==='#rep'){ selectCat('Reparieren'); }
else if(location.hash==='#history'){ selectCat('Verlauf'); }
else if(location.hash==='#restore'){
  active='Wiederherstellung'; buildNav();
  cards.classList.remove('dashboard','settings','autostart','history','power','restore','sched');
  cards.classList.add('restore'); send({type:'dashboard', active:false});
  $('#cat-title').textContent='Wiederherstellung';
  $('#cat-hint').textContent='Systemzustand sichern & zurücksetzen';
  renderRestore();
  renderRestoreList([
    {seq:128,desc:'Manueller Punkt (Windows-Wartung)',rtype:12,time:'2026-06-09 14:05'},
    {seq:127,desc:'Geplante Wartung',rtype:12,time:'2026-06-08 03:00'},
    {seq:125,desc:'Windows Update',rtype:13,time:'2026-06-05 11:42'}
  ]);
}
else if(location.hash==='#power'){
  active='Energie'; buildNav();
  cards.classList.remove('dashboard','settings','autostart','history','power','restore','sched');
  cards.classList.add('power'); send({type:'dashboard', active:false});
  $('#cat-title').textContent='Energie';
  $('#cat-hint').textContent='Energiesparplan wählen';
  renderPower();
  renderPowerList([
    {guid:'381b4222-f694-41f0-9685-ff5bb260df2e', name:'Ausbalanciert', active:true},
    {guid:'8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c', name:'Höchstleistung', active:false},
    {guid:'a1841308-3541-4fab-bc81-f71556f20b4a', name:'Energiesparmodus', active:false}
  ]);
}
else if(location.hash==='#netdiag'){
  selectCat('Netzwerk');
  promptModal('Netzwerk-Diagnose','Ziel eingeben – Webadresse oder IP (z. B. google.com oder 8.8.8.8):','google.com', ()=>{});
}
else if(location.hash==='#sched'){
  active='Geplant'; buildNav();
  cards.classList.remove('dashboard','settings','autostart','history','power','restore','sched');
  cards.classList.add('sched'); send({type:'dashboard', active:false});
  $('#cat-title').textContent='Geplante Wartung';
  $('#cat-hint').textContent='Automatisch im Hintergrund warten lassen';
  renderSchedule();
  renderScheduleStatus({exists:true, config:{mode:'weekly', days:['SAT','SUN'], time:'12:00'}});
}
else if(location.hash==='#bloat'){
  active='Bloatware'; buildNav();
  cards.classList.remove('dashboard','settings','autostart','history','power','restore','sched','bloat');
  cards.classList.add('bloat'); send({type:'dashboard', active:false});
  $('#cat-title').textContent='Bloatware';
  $('#cat-hint').textContent='Vorinstallierte Apps entfernen';
  renderBloat();
  renderBloatList([
    {name:'Microsoft.MicrosoftSolitaireCollection', full:'Microsoft.MicrosoftSolitaireCollection_x', label:'Solitaire-Sammlung', cat:'Spiele & Xbox', pub:'Microsoft Corporation'},
    {name:'Microsoft.XboxGamingOverlay', full:'Microsoft.XboxGamingOverlay_x', label:'Xbox Game Bar', cat:'Spiele & Xbox', pub:'Microsoft Corporation'},
    {name:'Microsoft.GamingApp', full:'Microsoft.GamingApp_x', label:'Xbox-App', cat:'Spiele & Xbox', pub:'Microsoft Corporation'},
    {name:'Clipchamp.Clipchamp', full:'Clipchamp.Clipchamp_x', label:'Clipchamp (Videoeditor)', cat:'Medien', pub:'Microsoft Corporation'},
    {name:'Microsoft.ZuneVideo', full:'Microsoft.ZuneVideo_x', label:'Filme & TV', cat:'Medien', pub:'Microsoft Corporation'},
    {name:'Microsoft.BingNews', full:'Microsoft.BingNews_x', label:'Nachrichten (Bing)', cat:'Bing & Nachrichten', pub:'Microsoft Corporation'},
    {name:'Microsoft.BingWeather', full:'Microsoft.BingWeather_x', label:'Wetter (MSN)', cat:'Bing & Nachrichten', pub:'Microsoft Corporation'},
    {name:'MicrosoftTeams', full:'MicrosoftTeams_x', label:'Teams (privat / Chat)', cat:'Kommunikation & Office', pub:'Microsoft Corporation'},
    {name:'Microsoft.GetHelp', full:'Microsoft.GetHelp_x', label:'Hilfe anfordern', cat:'System-Extras', pub:'Microsoft Corporation'},
    {name:'Microsoft.Getstarted', full:'Microsoft.Getstarted_x', label:'Tipps', cat:'System-Extras', pub:'Microsoft Corporation'}
  ]);
  // Vorschau: zwei Apps vorausgewählt, damit der Button-/Auswahl-Zustand sichtbar ist
  ['Microsoft.MicrosoftSolitaireCollection_x','Microsoft.BingNews_x'].forEach(f=>{
    const b=cards.querySelector('.bloat-item[data-full="'+f+'"]');
    if(b){ b.classList.add('sel'); bloatSel.add(f); }
  });
  updateBloatRemove();
}
else if(location.hash==='#progress'){
  selectCat('Reparieren'); setConsole(true);
  append('▶  Komplett-Reparatur','head');
  append('›  DISM /Online /Cleanup-Image /RestoreHealth','dim');
  append('Tool für die Abbildverwaltung für die Bereitstellung','norm');
  append('Abbildversion: 10.0.22631.3737','norm');
  setRunning(true); setProgress(45);
}
else if(location.hash==='#dashseed'){
  // Übersicht ist bereits aktiv – realistische Werte einspeisen, damit Count-up, Ring-Füllung und Reveal sichtbar werden
  updateStats({
    cpu:23, ram:58, disk:71, score:88,
    ramUsedGB:9.3, ramTotalGB:16, diskFreeGB:148.6, diskTotalGB:512,
    uptime:'2 Tage 4 Std', os:'Windows 11 Pro (24H2)', model:'ASUS TUF Gaming F15',
    drives:[
      {name:'C:',label:'System',freeGB:148.6,totalGB:512,used:71},
      {name:'D:',label:'Daten',freeGB:402.1,totalGB:1024,used:61}
    ],
    recs:[{text:'Alles im grünen Bereich – aktuell ist nichts nötig.',action:-1}]
  });
}

/* UI ist vollständig geladen -> Host darf jetzt Marker prüfen + auf Updates checken */
send({type:'ready'});
if(location.hash.indexOf('toast')>=0){
  setConsole(false);
  toast('SFC scannow','Erfolgreich – keine Fehler','good');
  setTimeout(()=>toast('Netzwerk-Reset','Mit Hinweisen abgeschlossen','warn'),120);
}
