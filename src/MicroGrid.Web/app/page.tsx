import { hasSupabaseEnv } from "@/lib/env";
import { createClient } from "@/lib/supabase/server";
import { requestCommand, signIn, signOut } from "./actions";

const mockOrders = [
  ["BUY", "118,744.20", "0.00084", "OPEN"], ["BUY", "118,601.70", "0.00084", "OPEN"],
  ["SELL", "119,172.80", "0.00083", "OPEN"], ["SELL", "119,315.80", "0.00083", "OPEN"],
];

function Setup() {
  return <main className="shell setup"><div className="mark">MG</div><p className="eyebrow">Configuration required</p><h1>Connect your control plane.</h1><p className="muted">The dashboard is built, but Supabase is not configured. From src/MicroGrid.Web, copy the environment template and add your project URL and publishable key.</p><pre>Copy-Item .env.example .env.local</pre><p className="warning">Keep OKX_API_KEY, OKX_API_SECRET, and OKX_PASSPHRASE on the .NET engine host only. Never put them in the Vercel dashboard project.</p></main>;
}

export default async function Home() {
  if (!hasSupabaseEnv()) return <Setup />;
  const supabase = await createClient();
  const { data: claims } = await supabase.auth.getClaims();
  const userId = claims?.claims?.sub;
  if (!userId) return <main className="shell setup"><div className="mark">MG</div><p className="eyebrow">Operator access</p><h1>Sign in to the grid.</h1><p className="muted">A one-time link will be sent to an authorized operator email.</p><form className="auth" action={signIn}><input name="email" type="email" autoComplete="email" placeholder="operator@example.com" required/><button className="btn primary">Send secure link</button></form></main>;

  const { data: bots } = await supabase.from("bot_instances").select("id,name,mode,status,last_heartbeat_at").limit(1);
  const bot = bots?.[0];
  if (!bot) return <main className="shell setup"><div className="mark">MG</div><p className="eyebrow">No authorized bot</p><h1>Your control plane is empty.</h1><p className="muted">Apply the migration, create a bot instance, and add this user to bot_memberships.</p><form action={signOut}><button className="btn">Sign out</button></form></main>;

  return <main className="shell">
    <header className="topbar"><div className="brand"><span className="mark">MG</span><span>Micro Grid <span className="muted">/ Control</span></span></div><div className="status"><span className="dot"/>{bot.mode.toUpperCase()} · {bot.status}</div></header>
    <section className="hero"><div><p className="eyebrow">BTC–USDT · OKX SPOT</p><h1>Grid, under control.</h1></div><p>Live operational state from the engine. Commands remain pending until the worker acknowledges them.</p></section>
    <section className="grid">
      <article className="card metric"><span className="label">Mid price</span><strong>$118,958.42</strong><small>+0.84% today</small></article>
      <article className="card metric"><span className="label">Total equity</span><strong>$1,024.81</strong><small>+$24.81 realized</small></article>
      <article className="card metric"><span className="label">BTC exposure</span><strong>41.8%</strong><small>23.2% headroom</small></article>
      <article className="card metric"><span className="label">Active orders</span><strong>24 / 25</strong><small>Post-only</small></article>
      <article className="card chart"><div className="cardhead"><h2>Grid distribution</h2><span className="badge">0.12% spacing</span></div><div className="chartgrid"><div className="levels">{[42,55,47,68,78,92,76,66,58,49,40,34].map((h,i)=><i key={i} style={{height:`${h}%`}}/>)}</div><svg className="chartline" viewBox="0 0 100 40" preserveAspectRatio="none"><path d="M0 30 C12 28 14 34 25 23 S39 19 48 22 S62 8 72 13 S85 10 100 4" fill="none" stroke="#62e6d2" strokeWidth=".7" vectorEffect="non-scaling-stroke"/></svg><span className="price">$118,958</span></div></article>
      <article className="card controls"><div className="cardhead"><h2>Engine controls</h2><span className="badge">ACK REQUIRED</span></div><p className="muted">Commands expire after five minutes and are safe to retry.</p><div className="controlrow">{["pause","resume","recenter","rescale"].map(type=><form action={requestCommand} key={type}><input type="hidden" name="botId" value={bot.id}/><input type="hidden" name="type" value={type}/><button className="btn" style={{width:"100%"}}>{type[0].toUpperCase()+type.slice(1)}</button></form>)}</div><div className="warning">Remote commands cannot stop the engine during a control-plane outage. Local engine risk limits remain authoritative.</div><form action={requestCommand}><input type="hidden" name="botId" value={bot.id}/><input type="hidden" name="type" value="emergency_stop"/><button className="btn danger" style={{width:"100%"}}>Emergency stop request</button></form></article>
      <article className="card orders"><div className="cardhead"><h2>Open orders</h2><span className="label">SANITIZED PROJECTION</span></div><table><thead><tr><th>Side</th><th>Price</th><th>BTC</th><th>Status</th></tr></thead><tbody>{mockOrders.map((o,i)=><tr key={i}><td className={o[0]==="BUY"?"buy":"sell"}>{o[0]}</td><td>${o[1]}</td><td>{o[2]}</td><td>{o[3]}</td></tr>)}</tbody></table></article>
      <article className="card activity"><div className="cardhead"><h2>Engine activity</h2><span className="label">UTC</span></div><ul className="feed"><li><time>04:21:18</time><span>Private stream reconciled</span></li><li><time>04:20:44</time><span>Buy fill matched · sell re-armed</span></li><li><time>04:18:02</time><span>Heartbeat · exposure within limit</span></li><li><time>04:15:30</time><span>Grid snapshot v184 persisted</span></li></ul></article>
    </section>
  </main>;
}
