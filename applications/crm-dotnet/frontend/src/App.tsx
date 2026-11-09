
import { useEffect, useState, type FormEvent } from "react";
import type Keycloak from "keycloak-js";

type Company = { id: string; name: string; industry?: string; website?: string };
type Contact = { id: string; firstName: string; lastName: string; email?: string };
type Opportunity = { id: string; name: string; amount: number; stage: string; companyId: string };
type Props = { keycloak: Keycloak };
type ChatMessage = { role: "user" | "assistant"; content: string };

export default function App({ keycloak }: Props) {
  const [tab, setTab] = useState("Overview");
  const [companies, setCompanies] = useState<Company[]>([]);
  const [contacts, setContacts] = useState<Contact[]>([]);
  const [opportunities, setOpportunities] = useState<Opportunity[]>([]);
  const [error, setError] = useState("");
  const [chatOpen, setChatOpen] = useState(false);
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([{ role: "assistant", content: "Hi! I can answer read-only questions about your CRM, such as your largest opportunities or companies in your pipeline." }]);
  const [chatInput, setChatInput] = useState("");
  const [chatBusy, setChatBusy] = useState(false);
  const [chatError, setChatError] = useState("");

  async function api<T>(path: string, init?: RequestInit): Promise<T> {
    await keycloak.updateToken(30);
    const response = await fetch(`${import.meta.env.VITE_API_BASE || ""}${path}`, {
      ...init,
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${keycloak.token}`, ...(init?.headers || {}) }
    });
    if (!response.ok) throw new Error(`${response.status}: ${await response.text()}`);
    return response.json();
  }

  async function load() {
    try {
      setError("");
      const [c, p, o] = await Promise.all([
        api<Company[]>("/api/companies"), api<Contact[]>("/api/contacts"), api<Opportunity[]>("/api/opportunities")
      ]);
      setCompanies(c); setContacts(p); setOpportunities(o);
    } catch (e) { setError(String(e)); }
  }

  useEffect(() => { void load(); }, []);
  const revenue = opportunities.reduce((sum, item) => sum + item.amount, 0);

  async function sendChat(event: FormEvent) {
    event.preventDefault();
    const content = chatInput.trim();
    if (!content || chatBusy) return;
    const next = [...chatMessages, { role: "user" as const, content }];
    setChatMessages(next); setChatInput(""); setChatBusy(true); setChatError("");
    try {
      const result = await api<{ answer: string }>("/api/assistant/chat", {
        method: "POST", body: JSON.stringify({ messages: next.slice(-10) })
      });
      setChatMessages(current => [...current, { role: "assistant" as const, content: result.answer }]);
    } catch (e) { setChatError(String(e)); }
    finally { setChatBusy(false); }
  }


  async function addCompany() {
    const name = prompt("Company name");
    if (!name?.trim()) return;
    try { await api("/api/companies", { method: "POST", body: JSON.stringify({ name, industry: "Technology", website: "" }) }); await load(); }
    catch (e) { setError(String(e)); }
  }

  return <div className="shell">
    <aside className="sidebar">
      <div className="brand"><span className="brand-mark">F</span><span>Foundgine <small>CRM</small></span></div>
      <div className="nav-label">WORKSPACE</div>
      {["Overview", "Companies", "Contacts", "Opportunities"].map(x =>
        <button className={`nav ${tab === x ? "active" : ""}`} onClick={() => setTab(x)} key={x}>{x}</button>)}
      <div className="sidebar-bottom">
        <div className="avatar">{(keycloak.tokenParsed?.preferred_username || "U").slice(0,1).toUpperCase()}</div>
        <div className="user"><b>{keycloak.tokenParsed?.preferred_username}</b><small>Authenticated by Keycloak</small></div>
        <button className="logout" onClick={() => keycloak.logout({ redirectUri: location.origin })}>↗</button>
      </div>
    </aside>
    <main className="main">
      <header><div><div className="eyebrow">CUSTOMER RELATIONSHIP MANAGEMENT</div><h1>{tab}</h1></div><button className="primary" onClick={addCompany}>＋ Add company</button></header>
      {error && <div className="error">{error}<button onClick={() => setError("")}>×</button></div>}
      {tab === "Overview" && <>
        <section className="welcome"><div><div className="eyebrow light">YOUR SALES WORKSPACE</div><h2>Good to see you, {keycloak.tokenParsed?.given_name || keycloak.tokenParsed?.preferred_username}.</h2><p>Here’s what is happening across your customer relationships.</p></div><div className="welcome-icon">✳</div></section>
        <section className="stats">
          <Stat label="Companies" value={companies.length.toString()} note="In your tenant" />
          <Stat label="Contacts" value={contacts.length.toString()} note="People in CRM" />
          <Stat label="Open opportunities" value={opportunities.length.toString()} note="Across all stages" />
          <Stat label="Pipeline value" value={revenue.toLocaleString("en-NZ",{style:"currency",currency:"NZD",maximumFractionDigits:0})} note="Opportunity amount" />
        </section>
        <section className="panel"><div className="panel-head"><div><h3>Recent opportunities</h3><p>Your active sales pipeline</p></div><button className="text-button" onClick={() => setTab("Opportunities")}>View all →</button></div><OpportunityTable items={opportunities.slice(0,6)} /></section>
      </>}
      {tab === "Companies" && <section className="panel"><div className="panel-head"><div><h3>Companies</h3><p>Accounts belonging to your tenant</p></div><button className="primary" onClick={addCompany}>＋ Add company</button></div><table><thead><tr><th>COMPANY</th><th>INDUSTRY</th><th>WEBSITE</th></tr></thead><tbody>{companies.map(c=><tr key={c.id}><td><b>{c.name}</b></td><td>{c.industry || "—"}</td><td>{c.website || "—"}</td></tr>)}</tbody></table></section>}
      {tab === "Contacts" && <section className="panel"><div className="panel-head"><div><h3>Contacts</h3><p>People connected to your accounts</p></div></div><table><thead><tr><th>NAME</th><th>EMAIL</th></tr></thead><tbody>{contacts.map(c=><tr key={c.id}><td><b>{c.firstName} {c.lastName}</b></td><td>{c.email || "—"}</td></tr>)}</tbody></table></section>}
      {tab === "Opportunities" && <section className="panel"><div className="panel-head"><div><h3>Opportunities</h3><p>Sales pipeline and expected revenue</p></div></div><OpportunityTable items={opportunities} /></section>}
      <footer>Foundgine CRM <span>·</span> Identity managed by Keycloak <span>·</span> Demo starter</footer>
    </main>
    <button className="assistant-launcher" onClick={() => setChatOpen(value => !value)} aria-label={chatOpen ? "Close AI assistant" : "Open AI assistant"}>✦ <span>Ask AI</span></button>
    {chatOpen && <section className="assistant-panel" aria-label="CRM AI assistant">
      <div className="assistant-header"><div><b>Foundgine Assistant</b><small>Read-only · secured by your CRM access</small></div><button onClick={() => setChatOpen(false)} aria-label="Close assistant">×</button></div>
      <div className="assistant-messages">{chatMessages.map((message, index) => <div key={index} className={`chat-bubble ${message.role}`}>{message.content}</div>)}{chatBusy && <div className="chat-bubble assistant typing">Looking that up…</div>}</div>
      {chatError && <div className="assistant-error">{chatError}</div>}
      <form className="assistant-compose" onSubmit={sendChat}><input value={chatInput} onChange={event => setChatInput(event.target.value)} placeholder="Ask about your CRM…" maxLength={1000} disabled={chatBusy} aria-label="Message the CRM assistant"/><button type="submit" disabled={chatBusy || !chatInput.trim()}>Send</button></form>
      <div className="assistant-disclaimer">The assistant can query CRM data but cannot change records.</div>
    </section>}
  </div>;
}

function Stat({label,value,note}:{label:string;value:string;note:string}) {
  return <div className="stat"><div className="stat-label">{label}</div><div className="stat-value">{value}</div><div className="stat-note">{note}</div></div>;
}
function OpportunityTable({items}:{items:Opportunity[]}) {
  return <table><thead><tr><th>OPPORTUNITY</th><th>STAGE</th><th>AMOUNT</th></tr></thead><tbody>{items.map(o=><tr key={o.id}><td><b>{o.name}</b></td><td><span className="pill">{o.stage}</span></td><td>{o.amount.toLocaleString("en-NZ",{style:"currency",currency:"NZD",maximumFractionDigits:0})}</td></tr>)}{items.length===0&&<tr><td colSpan={3} className="empty">No opportunities yet.</td></tr>}</tbody></table>;
}
