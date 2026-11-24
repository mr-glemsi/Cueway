const api = globalThis.browser ?? globalThis.chrome;

const NAMES = {
  msedge: "Microsoft Edge",
  chrome: "Google Chrome",
  brave: "Brave",
  opera: "Opera",
  vivaldi: "Vivaldi",
  zen: "Zen Browser",
  firefox: "Firefox / Zen"
};

async function refreshPopup() {
  const pill = document.getElementById("connPill");
  const connText = document.getElementById("connText");
  const browserMeta = document.getElementById("browserMeta");
  const tabList = document.getElementById("tabList");

  let status;
  try {
    status = await api.runtime.sendMessage({ type: "popup_get_status" });
  } catch {
    status = { connected: false, browser: "-", tabs: [] };
  }

  if (status?.connected) {
    pill.classList.add("ok");
    connText.textContent = "Connected";
  } else {
    pill.classList.remove("ok");
    connText.textContent = "Disconnected";
  }

  const bName = NAMES[status?.browser] || status?.browser || "-";
  const tabs = Array.isArray(status?.tabs) ? status.tabs : [];
  browserMeta.textContent = `${bName} • ${tabs.length} active media tab(s)`;

  tabList.replaceChildren();
  if (tabs.length === 0) {
    tabList.className = "empty";
    tabList.textContent = "No active video/audio tabs detected in this browser yet.";
    return;
  }

  tabList.className = "list";
  for (const t of tabs) {
    let host = "";
    try { host = new URL(t.url).hostname.replace(/^www\./, ""); } catch {}
    const subText = [t.artist, host].filter(Boolean).join(" • ") || String(t.url || "");

    const row = document.createElement("div");
    row.className = "tab-row";

    const dot = document.createElement("span");
    dot.className = "dot";
    dot.style.background = t.isPlaying ? "#22C55E" : "#9CA3AF";
    dot.style.flexShrink = "0";

    const info = document.createElement("div");
    info.className = "tab-info";

    const titleEl = document.createElement("div");
    titleEl.className = "tab-title";
    titleEl.textContent = String(t.title || host || "Media Tab");

    const subEl = document.createElement("div");
    subEl.className = "tab-sub";
    subEl.textContent = subText;

    info.appendChild(titleEl);
    info.appendChild(subEl);
    row.appendChild(dot);
    row.appendChild(info);
    tabList.appendChild(row);
  }
}

document.getElementById("btnReconnect").addEventListener("click", async () => {
  try {
    await api.runtime.sendMessage({ type: "popup_reconnect" });
  } catch {}
  setTimeout(refreshPopup, 250);
});

refreshPopup();
