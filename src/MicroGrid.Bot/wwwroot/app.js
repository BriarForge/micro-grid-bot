const $ = (id) => document.getElementById(id);
let settings;
let credentials;

const money = (value, digits = 2) => value == null ? "—" : new Intl.NumberFormat("en-US", {style:"currency",currency:"USD",minimumFractionDigits:digits,maximumFractionDigits:digits}).format(value);
const number = (value, digits = 8) => value == null ? "—" : new Intl.NumberFormat("en-US", {maximumFractionDigits:digits}).format(value);

async function getJson(url, options) {
  const response = await fetch(url, options);
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.error || `Request failed (${response.status})`);
  return body;
}

function renderLadder(status) {
  const ladder = $("ladder");
  ladder.querySelectorAll(".level").forEach(node => node.remove());
  if (!settings || !status.lastPrice || !status.effectiveSpacing) return;
  const buys = settings.buyLevelsBelowMid, sells = settings.sellLevelsAboveMid;
  for (let i=1;i<=buys;i++) addLevel("buy", i, buys, status.lastPrice * Math.pow(1-status.effectiveSpacing,i));
  for (let i=1;i<=sells;i++) addLevel("sell", i, sells, status.lastPrice * Math.pow(1+status.effectiveSpacing,i));
  function addLevel(side,index,total,price){const el=document.createElement("div");el.className=`level ${side}`;el.style.width=`${30+index/total*40}%`;el.style[side==="buy"?"bottom":"top"]=`${50/index?50*(index/(total+1)):0}%`;if(side==="buy")el.style.bottom=`${50-index/(total+1)*46}%`;else el.style.top=`${50-index/(total+1)*46}%`;const label=document.createElement("label");label.textContent=money(price,0);el.append(label);ladder.append(el)}
}

function renderStatus(s) {
  const connection=$("connection"); connection.className=`pill ${s.connected?"online":"offline"}`; connection.querySelector("span").textContent=s.connected?"CONNECTED":"OFFLINE";
  $("environment").textContent=s.environment; $("safetyEnvironment").textContent=s.environment;
  $("lastPrice").textContent=money(s.lastPrice,2); $("equity").textContent=money(s.totalEquityUsd,6);
  $("spread").textContent=s.bidPrice&&s.askPrice?`Bid ${money(s.bidPrice)} · Ask ${money(s.askPrice)}`:"Waiting for OKX";
  $("fees").textContent=s.makerRate==null?"—":`${(s.makerRate*100).toFixed(3)}% / ${(s.takerRate*100).toFixed(3)}%`;
  $("feeTier").textContent=s.feeTier?`${s.feeTier} · live API values`:"Account fee tier";
  $("spacing").textContent=s.effectiveSpacing==null?"—":`${(s.effectiveSpacing*100).toFixed(3)}%`;
  $("updated").textContent=s.updatedAt?`Updated ${new Date(s.updatedAt).toLocaleTimeString()}`:"Not updated";
  const error=$("errorBanner"); error.textContent=s.lastError||""; error.classList.toggle("hidden",!s.lastError);
  $("balances").innerHTML=s.balances?.length?s.balances.map(b=>`<tr><td>${b.currency}</td><td>${number(b.available)}</td><td>${money(b.equityUsd,6)}</td></tr>`).join(""):`<tr><td colspan="3">No non-zero balances</td></tr>`;
  renderLadder(s);
}

function renderCredentials(c) {
  credentials = c;
  const chip = $("credentialsChip");
  if (c && c.configured) {
    chip.textContent = "CONFIGURED";
    chip.className = "tag online";
    $("keyHint").textContent = c.apiKeyHint || "…";
    const envLabel = c.demoMode === true ? "DEMO" : c.demoMode === false ? "LIVE" : "—";
    $("keyEnv").textContent = `${envLabel} · ${c.region || "GLOBAL"}`;
    $("keyUpdated").textContent = c.updatedAt ? new Date(c.updatedAt).toLocaleString() : "Just saved";
  } else {
    chip.textContent = "UNCONFIGURED";
    chip.className = "tag offline";
    $("keyHint").textContent = "—";
    $("keyEnv").textContent = "—";
    $("keyUpdated").textContent = "—";
  }
}

function populateForm(s) {
  settings=s; $("activePct").value=s.activePct*100; $("activeOut").value=s.activePct*100; $("reservePct").value=s.reservePct;
  $("levels").value=s.levels; $("minimumSpacing").value=s.minimumSpacing*100; $("buyLevels").value=s.buyLevelsBelowMid; $("sellLevels").value=s.sellLevelsAboveMid;
  $("maxExposure").value=s.maxBtcExposurePct*100; $("resumeExposure").value=s.resumeBtcExposurePct*100;
}

async function loadSettings() {
  try { settings = await getJson("/api/settings"); populateForm(settings); }
  catch(e) { $("errorBanner").textContent = e.message; $("errorBanner").classList.remove("hidden"); }
}

async function loadStatus() {
  try { renderStatus(await getJson("/api/status")); } catch {}
}

async function loadCredentials() {
  try { renderCredentials(await getJson("/api/credentials")); }
  catch(e) { renderCredentials(null); }
}

$("activePct").addEventListener("input",e=>$("activeOut").value=e.target.value);

$("settingsForm").addEventListener("submit", async e => {
  e.preventDefault();
  const note = $("saveResult");
  const body = {
    activePct: +$("activePct").value / 100,
    reservePct: +$("reservePct").value,
    maxBtcExposurePct: +$("maxExposure").value / 100,
    resumeBtcExposurePct: +$("resumeExposure").value / 100,
    levels: +$("levels").value,
    minimumSpacing: +$("minimumSpacing").value / 100,
    buyLevelsBelowMid: +$("buyLevels").value,
    sellLevelsAboveMid: +$("sellLevels").value,
    tradingEnabled: false
  };
  try {
    settings = await getJson("/api/settings", { method: "PUT", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
    note.textContent = "Settings saved locally.";
    note.className = "form-note success";
  } catch (err) {
    note.textContent = err.message;
    note.className = "form-note failure";
  }
});

$("credentialsForm").addEventListener("submit", async e => {
  e.preventDefault();
  const note = $("credentialsResult");
  const apiKey = $("apiKey").value.trim();
  const apiSecret = $("apiSecret").value;
  const passphrase = $("passphrase").value;
  if (!apiKey || !apiSecret || !passphrase) {
    note.textContent = "API key, secret, and passphrase are all required.";
    note.className = "form-note failure";
    return;
  }
  const body = {
    apiKey,
    apiSecret,
    passphrase,
    demoMode: $("demoMode").checked,
    region: $("region").value
  };
  try {
    renderCredentials(await getJson("/api/credentials", {
      method: "PUT",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(body)
    }));
    // Wipe sensitive fields. Never echo them back into inputs.
    $("apiKey").value = "";
    $("apiSecret").value = "";
    $("passphrase").value = "";
    note.textContent = "Credentials saved. Engine will reconnect within ~10 seconds.";
    note.className = "form-note success";
  } catch (err) {
    note.textContent = err.message;
    note.className = "form-note failure";
  }
});

$("credentialsClear").addEventListener("click", async () => {
  const note = $("credentialsResult");
  if (!confirm("Clear the locally-stored OKX credentials?")) return;
  try {
    renderCredentials(await getJson("/api/credentials", { method: "DELETE" }));
    note.textContent = "Stored credentials cleared.";
    note.className = "form-note";
  } catch (err) {
    note.textContent = err.message;
    note.className = "form-note failure";
  }
});

(async () => {
  await loadSettings();
  await loadCredentials();
  await loadStatus();
  setInterval(() => { loadStatus(); loadCredentials(); }, 5000);
})();