const api = globalThis.browser ?? globalThis.chrome;
const WS_URL = "ws://127.0.0.1:47621";
const BRIDGE_PROTOCOL_VERSION = 4;
const BUILTIN_ICON_HOSTS = /(^|\.)(youtube\.com|youtu\.be|x\.com|twitter\.com|twitch\.tv|kick\.com|spotify\.com|soundcloud\.com|netflix\.com|instagram\.com|tiktok\.com|reddit\.com|facebook\.com)$/i;

const tabStates = new Map();
const tabFrames = new Map();
const tabMeta = new Map();
const hostIconCache = new Map();
const hostIconPending = new Set();
const hostIconAttempts = new Map();
let ws = null;
let connected = false;
let reconnectDelayMs = 1000;
let reconnectTimer = null;
let connectTimeout = null;
let heartbeatTimer = null;
let flushTimer = null;

function serializeTabState(st) {
  return {
    tabId: st.tabId,
    windowId: st.windowId,
    url: st.url,
    title: st.title,
    artist: st.artist,
    siteName: st.siteName || "",
    iconDataUrl: st.iconDataUrl || "",
    isPlaying: Boolean(st.isPlaying),
    isPaused: Boolean(st.isPaused),
    isMuted: Boolean(st.isMuted),
    volume: Number.isFinite(Number(st.volume)) ? Number(st.volume) : 1,
    hasVideo: Boolean(st.hasVideo),
    hasAudio: st.hasAudio !== false,
    duration: Number(st.duration) || 0,
    currentTime: Number(st.currentTime) || 0
  };
}

function detectBrowserKey() {
  const ua = navigator.userAgent || "";
  if (ua.includes("Zen/")) return "zen";
  if (ua.includes("Edg/")) return "msedge";
  if (ua.includes("OPR/") || ua.includes("Opera/")) return "opera";
  if (ua.includes("Vivaldi/")) return "vivaldi";
  if (ua.includes("Firefox/")) return "firefox";
  const brands = navigator.userAgentData?.brands;
  if (Array.isArray(brands) && brands.some((b) => /brave/i.test(b.brand))) {
    return "brave";
  }
  return "chrome";
}

const BROWSER_KEY = detectBrowserKey();

function sendWs(payload) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(payload));
      return true;
    } catch {}
  }
  return false;
}

function scheduleFlush() {
  if (flushTimer) clearTimeout(flushTimer);
  flushTimer = setTimeout(() => {
    flushTimer = null;
    sendWs({
      type: "tabs",
      browser: BROWSER_KEY,
      version: BRIDGE_PROTOCOL_VERSION,
      tabs: Array.from(tabStates.values(), serializeTabState)
    });
  }, 60);
}

async function injectContentScript(tabId) {
  if (api.scripting?.executeScript) {
    await api.scripting.executeScript({
      target: { tabId, allFrames: true },
      files: ["content.js"]
    });
  } else if (api.tabs?.executeScript) {
    await api.tabs.executeScript(tabId, { file: "content.js", allFrames: true });
  }
}

function removeTabTracking(tabId) {
  const had = tabStates.delete(tabId);
  tabFrames.delete(tabId);
  tabMeta.delete(tabId);
  return had;
}

async function pruneClosedTabs() {
  if (tabStates.size === 0 && tabFrames.size === 0 && tabMeta.size === 0) return;
  let allTabs = [];
  try {
    allTabs = await api.tabs.query({});
  } catch {
    return;
  }

  const liveIds = new Set();
  for (const t of allTabs) {
    if (t?.id && !t.discarded && typeof t.url === "string" &&
        (t.url.startsWith("http://") || t.url.startsWith("https://"))) {
      liveIds.add(t.id);
    }
  }

  let removed = false;
  for (const id of new Set([...tabStates.keys(), ...tabFrames.keys(), ...tabMeta.keys()])) {
    if (!liveIds.has(id)) {
      if (removeTabTracking(id)) removed = true;
    }
  }
  if (removed) scheduleFlush();
}

async function scanAndInjectTabs(forceInject = false) {
  await pruneClosedTabs();
  let tabs = [];
  try {
    tabs = await api.tabs.query({ url: ["http://*/*", "https://*/*"] });
  } catch {
    return;
  }

  for (const tab of tabs) {
    if (!tab.id) continue;
    if (forceInject) {
      try {
        await injectContentScript(tab.id);
      } catch {
        // Restricted pages throw cleanly
      }

      try {
        await api.tabs.sendMessage(tab.id, { type: "query_state" });
      } catch {
        // Tab without injectable content script
      }
    } else {
      try {
        await api.tabs.sendMessage(tab.id, { type: "query_state" });
      } catch {
        try {
          await injectContentScript(tab.id);
          await api.tabs.sendMessage(tab.id, { type: "query_state" });
        } catch {
          // Tab without injectable content script
        }
      }
    }
  }
}

async function focusTab(tabId) {
  if (!tabId) return;
  let tab = null;
  try {
    tab = await api.tabs.get(tabId);
  } catch {}

  if (tab?.hidden && typeof api.tabs?.show === "function") {
    try {
      await api.tabs.show(tabId);
    } catch {}
  }

  try {
    await api.tabs.update(tabId, { active: true });
  } catch {}

  try {
    const check = await api.tabs.get(tabId);
    if (check && !check.active) {
      if (check.hidden && typeof api.tabs?.show === "function") {
        await api.tabs.show(tabId);
        await api.tabs.update(tabId, { active: true });
      } else if (typeof api.tabs?.highlight === "function" && Number.isInteger(check.index)) {
        await api.tabs.highlight({ windowId: check.windowId, tabs: [check.index] });
      }
    }
    tab = check || tab;
  } catch {}

  if (tab?.windowId && api.windows?.update) {
    try {
      await api.windows.update(tab.windowId, { focused: true });
    } catch {}
  }

  try {
    await api.tabs.sendMessage(tabId, { type: "query_state" });
  } catch {}
}

async function forwardCommand(tabId, action, delta) {
  if (!tabId) return;
  const payload = { type: "wmr_cmd", action, delta: delta || 0 };
  const frameId = tabStates.get(tabId)?._frameId;
  const sendOpts = Number.isInteger(frameId) && frameId >= 0 ? { frameId } : undefined;

  if (frameId === -1 && action === "toggle_mute") {
    try {
      const t = await api.tabs.get(tabId);
      await api.tabs.update(tabId, { muted: !t?.mutedInfo?.muted });
    } catch {}
    return;
  }

  try {
    if (sendOpts) await api.tabs.sendMessage(tabId, payload, sendOpts);
    else await api.tabs.sendMessage(tabId, payload);
  } catch {
    try {
      await injectContentScript(tabId);
      if (sendOpts) await api.tabs.sendMessage(tabId, payload, sendOpts);
      else await api.tabs.sendMessage(tabId, payload);
    } catch {
      if (action === "toggle_mute") {
        try {
          const t = await api.tabs.get(tabId);
          await api.tabs.update(tabId, { muted: !t?.mutedInfo?.muted });
        } catch {}
      }
    }
  }
}

function clearConnectTimeout() {
  if (connectTimeout) {
    clearTimeout(connectTimeout);
    connectTimeout = null;
  }
}

function connect() {
  if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
    return;
  }

  if (reconnectTimer) {
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  clearConnectTimeout();

  let socket;
  try {
    socket = new WebSocket(WS_URL);
    ws = socket;
  } catch {
    scheduleReconnect();
    return;
  }

  connectTimeout = setTimeout(() => {
    connectTimeout = null;
    if (ws === socket && socket.readyState === WebSocket.CONNECTING) {
      try {
        socket.close();
      } catch {}
    }
  }, 5000);

  socket.onopen = () => {
    if (ws !== socket) return;
    clearConnectTimeout();
    connected = true;
    reconnectDelayMs = 1000;
    sendWs({ type: "hello", browser: BROWSER_KEY, version: BRIDGE_PROTOCOL_VERSION });
    scheduleFlush();
    scanAndInjectTabs();

    if (heartbeatTimer) clearInterval(heartbeatTimer);
    heartbeatTimer = setInterval(() => {
      sendWs({ type: "ping", browser: BROWSER_KEY, version: BRIDGE_PROTOCOL_VERSION });
    }, 20000);
  };

  socket.onmessage = (event) => {
    if (ws !== socket) return;
    let msg;
    try {
      msg = JSON.parse(event.data);
    } catch {
      return;
    }
    if (msg?.type === "cmd") {
      if (msg.action === "focus") {
        focusTab(msg.tabId);
      } else {
        forwardCommand(msg.tabId, msg.action, msg.delta);
      }
    }
  };

  socket.onclose = () => {
    if (ws !== socket) return;
    clearConnectTimeout();
    connected = false;
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
    ws = null;
    scheduleReconnect();
  };

  socket.onerror = () => {
    if (ws !== socket) return;
    clearConnectTimeout();
    try {
      socket.close();
    } catch {}
  };
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  const delay = reconnectDelayMs;
  reconnectDelayMs = Math.min(3000, Math.round(reconnectDelayMs * 1.5));
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, delay);
}

function sanitizeUrl(rawUrl) {
  if (!rawUrl) return "";
  try {
    const u = new URL(rawUrl);
    return u.origin + u.pathname;
  } catch {
    return "";
  }
}

function normHost(rawUrl) {
  if (!rawUrl) return "";
  try {
    return new URL(rawUrl).hostname.toLowerCase().replace(/^(www|m|mobile)\./, "");
  } catch {
    return "";
  }
}

function getEffectiveMeta(tabId, tabObj) {
  const prev = tabMeta.get(tabId) || { url: "", title: "", siteName: "", favIconUrl: "", audible: false };
  if (tabObj) {
    if (tabObj.url && (tabObj.url.startsWith("http://") || tabObj.url.startsWith("https://"))) {
      const clean = sanitizeUrl(tabObj.url);
      const prevHost = normHost(prev.url);
      const nextHost = normHost(clean);
      if (prevHost && nextHost && prevHost !== nextHost) {
        prev.siteName = "";
        prev.favIconUrl = "";
      }
      prev.url = clean;
    }
    if (tabObj.title) prev.title = tabObj.title;
    if (tabObj.favIconUrl) prev.favIconUrl = tabObj.favIconUrl;
    if (typeof tabObj.audible === "boolean") prev.audible = tabObj.audible;
    tabMeta.set(tabId, prev);
  }
  return prev;
}

function pickWinningFrame(framesMap) {
  if (!framesMap || framesMap.size === 0) return null;
  let best = null;
  let bestScore = -1;
  for (const fr of framesMap.values()) {
    if (!fr || !fr.hasMedia) continue;
    let score = 1;
    if (fr.isPlaying && !fr.isMuted) score += 1000;
    else if (fr.isPlaying) score += 400;
    if (fr.frameId === 0) score += 60;
    if (fr.hasVideo) score += 40;
    if ((fr.duration || 0) >= 120) score += 120;
    if (!best || score > bestScore || (score === bestScore && (fr.updatedAt || 0) > (best.updatedAt || 0))) {
      best = fr;
      bestScore = score;
    }
  }
  return best;
}

function loadImageElement(src) {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error("img load failed"));
    img.src = src;
  });
}

async function convertFaviconUrlToPngDataUrl(rawUrl) {
  if (!rawUrl || (!rawUrl.startsWith("data:image/") && !rawUrl.startsWith("http://") && !rawUrl.startsWith("https://"))) {
    return "";
  }

  let objectUrl = "";
  try {
    let imgSrc = rawUrl;
    if (!rawUrl.startsWith("data:")) {
      const resp = await fetch(rawUrl, { cache: "force-cache", credentials: "omit" });
      if (!resp.ok) return "";
      const blob = await resp.blob();
      if (!blob || blob.size === 0 || blob.size > 512 * 1024) return "";
      objectUrl = URL.createObjectURL(blob);
      imgSrc = objectUrl;
    }

    const img = await loadImageElement(imgSrc);
    const size = 48;
    const canvas = document.createElement("canvas");
    canvas.width = size;
    canvas.height = size;
    const ctx = canvas.getContext("2d");
    if (!ctx) return "";
    ctx.clearRect(0, 0, size, size);
    const bw = Math.max(1, img.naturalWidth || img.width || size);
    const bh = Math.max(1, img.naturalHeight || img.height || size);
    const pad = 2;
    const avail = size - pad * 2;
    const ratio = Math.min(avail / bw, avail / bh);
    const dw = Math.max(1, Math.round(bw * ratio));
    const dh = Math.max(1, Math.round(bh * ratio));
    const dx = Math.round((size - dw) / 2);
    const dy = Math.round((size - dh) / 2);
    ctx.drawImage(img, dx, dy, dw, dh);

    const dataUrl = canvas.toDataURL("image/png");
    if (!dataUrl || !dataUrl.startsWith("data:image/png;base64,") || dataUrl.length > 65536) {
      return "";
    }
    return dataUrl;
  } finally {
    if (objectUrl) {
      try { URL.revokeObjectURL(objectUrl); } catch {}
    }
  }
}

async function ensureFaviconForTab(tabId, tabUrl, rawFavIconUrl) {
  const host = normHost(tabUrl);
  if (!host || BUILTIN_ICON_HOSTS.test(host) || hostIconCache.has(host) || hostIconPending.has(host)) {
    return;
  }
  if ((hostIconAttempts.get(host) || 0) >= 2) return;

  let iconUrl = rawFavIconUrl || "";
  if (!iconUrl) {
    try {
      const t = await api.tabs.get(tabId);
      iconUrl = t?.favIconUrl || "";
      if (iconUrl) {
        const meta = getEffectiveMeta(tabId, t);
        meta.favIconUrl = iconUrl;
      }
    } catch {}
  }
  if (!iconUrl) return;

  hostIconPending.add(host);
  hostIconAttempts.set(host, (hostIconAttempts.get(host) || 0) + 1);
  try {
    const dataUrl = await convertFaviconUrlToPngDataUrl(iconUrl);
    if (dataUrl) {
      if (hostIconCache.size >= 64) {
        const oldest = hostIconCache.keys().next().value;
        if (oldest) hostIconCache.delete(oldest);
      }
      hostIconCache.set(host, dataUrl);
      let updated = false;
      for (const [id, st] of tabStates.entries()) {
        if (normHost(st.url) === host && st.iconDataUrl !== dataUrl) {
          st.iconDataUrl = dataUrl;
          tabStates.set(id, st);
          updated = true;
        }
      }
      if (updated) scheduleFlush();
    }
  } catch {
  } finally {
    hostIconPending.delete(host);
  }
}

function recomputeTabState(tabId, tabObj) {
  const meta = getEffectiveMeta(tabId, tabObj);
  const frames = tabFrames.get(tabId);
  const best = pickWinningFrame(frames);

  if (!best) {
    if (meta.audible && meta.url) {
      const prev = tabStates.get(tabId);
      const host = normHost(meta.url);
      const iconDataUrl = hostIconCache.get(host) || prev?.iconDataUrl || "";
      tabStates.set(tabId, {
        tabId,
        windowId: tabObj?.windowId || prev?.windowId || 0,
        url: meta.url,
        title: tabObj?.title || meta.title || prev?.title || "",
        artist: prev?.artist || "",
        siteName: meta.siteName || prev?.siteName || "",
        iconDataUrl,
        isPlaying: true,
        isPaused: false,
        isMuted: Boolean(tabObj?.mutedInfo?.muted ?? prev?.isMuted),
        volume: prev?.volume ?? 1,
        hasVideo: true,
        hasAudio: true,
        duration: 0,
        currentTime: 0,
        _frameId: -1
      });
      ensureFaviconForTab(tabId, meta.url, meta.favIconUrl);
      scheduleFlush();
      return;
    }

    if (tabStates.delete(tabId)) {
      scheduleFlush();
    }
    return;
  }

  const finalUrl = sanitizeUrl(tabObj?.url || meta.url || best.url || "");
  if (!finalUrl) return;
  const host = normHost(finalUrl);
  const siteName = meta.siteName || (best.frameId === 0 ? best.siteName : "") || "";
  const title = best.frameId === 0
    ? (best.title || tabObj?.title || meta.title || "")
    : (tabObj?.title || meta.title || best.title || "");
  const iconDataUrl = hostIconCache.get(host) || tabStates.get(tabId)?.iconDataUrl || "";

  tabStates.set(tabId, {
    tabId,
    windowId: tabObj?.windowId || tabStates.get(tabId)?.windowId || 0,
    url: finalUrl,
    title,
    artist: best.artist || "",
    siteName,
    iconDataUrl,
    isPlaying: Boolean(best.isPlaying),
    isPaused: Boolean(best.isPaused),
    isMuted: Boolean(best.isMuted),
    volume: Number.isFinite(Number(best.volume)) ? Math.max(0, Math.min(1, Number(best.volume))) : 1,
    hasVideo: Boolean(best.hasVideo),
    hasAudio: best.hasAudio !== false,
    duration: Number(best.duration) || 0,
    currentTime: Number(best.currentTime) || 0,
    _frameId: best.frameId
  });

  ensureFaviconForTab(tabId, finalUrl, meta.favIconUrl);
  scheduleFlush();
}

api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg) return;

  if (msg.type === "page_meta" && sender.tab?.id) {
    const tabId = sender.tab.id;
    const meta = getEffectiveMeta(tabId, sender.tab);
    if (msg.url) {
      const clean = sanitizeUrl(msg.url);
      if (clean) meta.url = clean;
    }
    if (msg.title) meta.title = msg.title;
    if (msg.siteName) meta.siteName = msg.siteName;
    if (sender.tab.favIconUrl) meta.favIconUrl = sender.tab.favIconUrl;
    tabMeta.set(tabId, meta);
    if (tabStates.has(tabId)) {
      recomputeTabState(tabId, sender.tab);
    }
    sendResponse({ ok: true });
    return true;
  }

  if (msg.type === "media_state" && sender.tab?.id) {
    const tabId = sender.tab.id;
    const frameId = typeof sender.frameId === "number" ? sender.frameId : 0;
    const st = msg.state;
    getEffectiveMeta(tabId, sender.tab);

    let frames = tabFrames.get(tabId);
    if (!frames) {
      frames = new Map();
      tabFrames.set(tabId, frames);
    }

    if (st && st.hasMedia) {
      if (frameId === 0 && st.siteName) {
        const meta = tabMeta.get(tabId);
        if (meta) meta.siteName = st.siteName;
      }
      frames.set(frameId, {
        ...st,
        frameId,
        updatedAt: Date.now()
      });
      recomputeTabState(tabId, sender.tab);
    } else if (frames.has(frameId) && st?.keepSticky) {
      const existing = frames.get(frameId);
      existing.isPlaying = false;
      existing.isPaused = true;
      existing.updatedAt = Date.now();
      frames.set(frameId, existing);
      recomputeTabState(tabId, sender.tab);
    } else {
      const deleted = frames.delete(frameId);
      if (frames.size === 0) tabFrames.delete(tabId);
      if (deleted || tabStates.has(tabId)) {
        recomputeTabState(tabId, sender.tab);
      }
    }
    sendResponse({ ok: true });
    return true;
  }

  if (msg.type === "popup_get_status") {
    sendResponse({
      connected,
      browser: BROWSER_KEY,
      tabs: Array.from(tabStates.values(), serializeTabState)
    });
    return true;
  }

  if (msg.type === "popup_reconnect") {
    clearConnectTimeout();
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    const prev = ws;
    ws = null;
    try {
      prev?.close();
    } catch {}
    connected = false;
    reconnectDelayMs = 1000;
    connect();
    scanAndInjectTabs();
    sendResponse({ ok: true });
    return true;
  }
});

api.tabs.onRemoved.addListener((tabId) => {
  if (removeTabTracking(tabId)) {
    scheduleFlush();
  }
});

api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.discarded === true) {
    if (removeTabTracking(tabId)) {
      scheduleFlush();
    }
    return;
  }

  if (changeInfo.url) {
    const newUrl = changeInfo.url;
    if (!newUrl.startsWith("http://") && !newUrl.startsWith("https://")) {
      if (removeTabTracking(tabId)) {
        scheduleFlush();
      }
      return;
    }

    const cur = tabStates.get(tabId);
    const oldHost = normHost(cur?.url || tabMeta.get(tabId)?.url);
    const newHost = normHost(newUrl);
    if (oldHost && newHost && oldHost !== newHost) {
      if (removeTabTracking(tabId)) {
        scheduleFlush();
      }
    } else {
      getEffectiveMeta(tabId, tab);
      api.tabs.sendMessage(tabId, { type: "query_state" }).catch(() => {});
    }
  }

  const tabUrl = tab?.url || "";
  const isHttp = tabUrl.startsWith("http://") || tabUrl.startsWith("https://");

  if (isHttp && (changeInfo.favIconUrl || changeInfo.title)) {
    getEffectiveMeta(tabId, tab);
    if (tabStates.has(tabId)) {
      recomputeTabState(tabId, tab);
    }
  }

  if (changeInfo.status === "complete" && isHttp) {
    getEffectiveMeta(tabId, tab);
    if (tabStates.has(tabId)) {
      recomputeTabState(tabId, tab);
      api.tabs.sendMessage(tabId, { type: "query_state" }).catch(() => {});
    }
  }

  if (typeof changeInfo.audible === "boolean" && isHttp) {
    const meta = getEffectiveMeta(tabId, tab);
    meta.audible = changeInfo.audible;
    recomputeTabState(tabId, tab);

    if (changeInfo.audible === true) {
      api.tabs.sendMessage(tabId, { type: "query_state" }).catch(() => {
        injectContentScript(tabId)
          .then(() => api.tabs.sendMessage(tabId, { type: "query_state" }))
          .catch(() => {});
      });
    }
  }
});

api.runtime.onInstalled.addListener(() => {
  connect();
  scanAndInjectTabs(true);
});

api.runtime.onStartup?.addListener(() => {
  connect();
  scanAndInjectTabs();
});

if (api.alarms) {
  api.alarms.create("wmr_keepalive", { periodInMinutes: 0.5 });
  api.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === "wmr_keepalive") {
      pruneClosedTabs();
      if (!ws || (ws.readyState !== WebSocket.OPEN && ws.readyState !== WebSocket.CONNECTING)) {
        connect();
      } else if (ws.readyState === WebSocket.OPEN) {
        sendWs({ type: "ping", browser: BROWSER_KEY, version: BRIDGE_PROTOCOL_VERSION });
      }
    }
  });
}

connect();
