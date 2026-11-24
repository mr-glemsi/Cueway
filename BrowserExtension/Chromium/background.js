const api = globalThis.browser ?? globalThis.chrome;
const WS_URL = "ws://127.0.0.1:47621";
const BRIDGE_PROTOCOL_VERSION = 3;

const tabStates = new Map();
let ws = null;
let connected = false;
let reconnectDelayMs = 2000;
let reconnectTimer = null;
let heartbeatTimer = null;
let flushTimer = null;

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
      tabs: Array.from(tabStates.values())
    });
  }, 60);
}

async function injectContentScript(tabId) {
  if (api.scripting?.executeScript) {
    await api.scripting.executeScript({
      target: { tabId },
      files: ["content.js"]
    });
  } else if (api.tabs?.executeScript) {
    await api.tabs.executeScript(tabId, { file: "content.js" });
  }
}

async function scanAndInjectTabs(forceInject = false) {
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
        // Restricted pages (edge://, chrome://, extension stores) throw cleanly
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

  try {
    await api.tabs.update(tabId, { active: true });
  } catch {}

  try {
    const check = await api.tabs.get(tabId);
    if (check && !check.active && typeof api.tabs?.highlight === "function" && Number.isInteger(check.index)) {
      await api.tabs.highlight({ windowId: check.windowId, tabs: [check.index] });
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

function applyMainWorldPlayerVolume(action, delta) {
  const host = (location.hostname || "").toLowerCase();
  if (!host.includes("youtube.com")) {
    return { handled: false };
  }

  const isShorts = (location.pathname || "").startsWith("/shorts/");
  const order = isShorts ? ["#shorts-player", "#movie_player"] : ["#movie_player", "#shorts-player"];
  let player = null;
  let fallback = null;
  for (const sel of order) {
    const node = document.querySelector(sel);
    if (!node || typeof node.setVolume !== "function") continue;
    const rect = node.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) {
      player = node;
      break;
    }
    if (!fallback) fallback = node;
  }
  player = player || fallback;

  if (!player) {
    return { handled: false };
  }

  const syncSlider = (pct) => {
    const slider = document.querySelector("ytmusic-player-bar #volume-slider");
    if (!slider) return;
    try {
      slider.value = pct;
      slider.dispatchEvent(new Event("change", { bubbles: true }));
      slider.dispatchEvent(new Event("input", { bubbles: true }));
    } catch {}
  };

  try {
    if (action === "set_volume") {
      const pct = Math.max(0, Math.min(100, Math.round(Number(delta) || 0)));
      player.setVolume(pct);
      if (pct > 0 && typeof player.unMute === "function") player.unMute();
      else if (pct === 0 && typeof player.mute === "function") player.mute();
      syncSlider(pct);
      return { handled: true, volume: pct / 100, isMuted: pct === 0 };
    }

    if (action === "adjust_volume") {
      const cur = typeof player.getVolume === "function" ? Number(player.getVolume()) : 100;
      const base = player.isMuted?.() && delta > 0 && cur <= 1 ? 0 : (Number.isFinite(cur) ? cur : 100);
      const pct = Math.max(0, Math.min(100, Math.round(base + (Number(delta) || 0))));
      player.setVolume(pct);
      if (pct > 0 && typeof player.unMute === "function") player.unMute();
      else if (pct === 0 && typeof player.mute === "function") player.mute();
      syncSlider(pct);
      return { handled: true, volume: pct / 100, isMuted: pct === 0 };
    }

    if (action === "toggle_mute") {
      const wasMuted = Boolean(player.isMuted?.());
      if (wasMuted) {
        player.unMute?.();
        let pct = typeof player.getVolume === "function" ? Number(player.getVolume()) : 50;
        if (!Number.isFinite(pct) || pct <= 1) {
          pct = 50;
          player.setVolume(50);
          syncSlider(50);
        }
        return { handled: true, volume: pct / 100, isMuted: false };
      } else {
        player.mute?.();
        const cur = typeof player.getVolume === "function" ? Number(player.getVolume()) : 100;
        return { handled: true, volume: (Number.isFinite(cur) ? cur : 100) / 100, isMuted: true };
      }
    }
  } catch {}

  return { handled: false };
}

async function applyTabVolumeCommand(tabId, action, delta) {
  if (!tabId) return;

  if (api.scripting?.executeScript) {
    try {
      const results = await api.scripting.executeScript({
        target: { tabId },
        world: "MAIN",
        func: applyMainWorldPlayerVolume,
        args: [action, Number(delta) || 0]
      });
      const res = results?.[0]?.result;
      if (res?.handled) {
        const cur = tabStates.get(tabId);
        if (cur) {
          cur.volume = Math.max(0, Math.min(1, Number(res.volume)));
          cur.isMuted = Boolean(res.isMuted);
          scheduleFlush();
        }
        api.tabs.sendMessage(tabId, { type: "query_state" }).catch(() => {});
        return;
      }
    } catch {}
  }

  await forwardCommand(tabId, action, delta);
}

async function forwardCommand(tabId, action, delta) {
  if (!tabId) return;
  const payload = { type: "wmr_cmd", action, delta: delta || 0 };
  try {
    await api.tabs.sendMessage(tabId, payload);
  } catch {
    try {
      await injectContentScript(tabId);
      await api.tabs.sendMessage(tabId, payload);
    } catch {}
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

  try {
    ws = new WebSocket(WS_URL);
  } catch {
    scheduleReconnect();
    return;
  }

  ws.onopen = () => {
    connected = true;
    reconnectDelayMs = 2000;
    sendWs({ type: "hello", browser: BROWSER_KEY, version: BRIDGE_PROTOCOL_VERSION });
    scheduleFlush();
    scanAndInjectTabs();

    if (heartbeatTimer) clearInterval(heartbeatTimer);
    heartbeatTimer = setInterval(() => {
      sendWs({ type: "ping", browser: BROWSER_KEY, version: BRIDGE_PROTOCOL_VERSION });
    }, 20000);
  };

  ws.onmessage = (event) => {
    let msg;
    try {
      msg = JSON.parse(event.data);
    } catch {
      return;
    }
    if (msg?.type === "cmd") {
      if (msg.action === "focus") {
        focusTab(msg.tabId);
      } else if (msg.action === "set_volume" || msg.action === "adjust_volume" || msg.action === "toggle_mute") {
        applyTabVolumeCommand(msg.tabId, msg.action, msg.delta);
      } else {
        forwardCommand(msg.tabId, msg.action, msg.delta);
      }
    }
  };

  ws.onclose = () => {
    connected = false;
    if (heartbeatTimer) {
      clearInterval(heartbeatTimer);
      heartbeatTimer = null;
    }
    ws = null;
    scheduleReconnect();
  };

  ws.onerror = () => {
    try {
      ws?.close();
    } catch {}
  };
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  const delay = reconnectDelayMs;
  reconnectDelayMs = Math.min(8000, Math.round(reconnectDelayMs * 1.5));
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

api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (!msg) return;

  if (msg.type === "media_state" && sender.tab?.id) {
    const tabId = sender.tab.id;
    const st = msg.state;
    if (st && st.hasMedia) {
      tabStates.set(tabId, {
        tabId,
        windowId: sender.tab.windowId || 0,
        url: sanitizeUrl(st.url || sender.tab.url || ""),
        title: st.title || sender.tab.title || "",
        artist: st.artist || "",
        isPlaying: Boolean(st.isPlaying),
        isPaused: Boolean(st.isPaused),
        isMuted: Boolean(st.isMuted),
        volume: Number.isFinite(Number(st.volume)) ? Math.max(0, Math.min(1, Number(st.volume))) : 1,
        hasVideo: Boolean(st.hasVideo),
        hasAudio: st.hasAudio !== false,
        duration: Number(st.duration) || 0,
        currentTime: Number(st.currentTime) || 0
      });
    } else if (tabStates.has(tabId) && (!sender.tab.active || st?.keepSticky)) {
      const existing = tabStates.get(tabId);
      existing.isPlaying = false;
      existing.isPaused = true;
      tabStates.set(tabId, existing);
    } else {
      tabStates.delete(tabId);
    }
    scheduleFlush();
    sendResponse({ ok: true });
    return true;
  }

  if (msg.type === "popup_get_status") {
    sendResponse({
      connected,
      browser: BROWSER_KEY,
      tabs: Array.from(tabStates.values())
    });
    return true;
  }

  if (msg.type === "popup_reconnect") {
    try {
      ws?.close();
    } catch {}
    ws = null;
    reconnectDelayMs = 2000;
    connect();
    scanAndInjectTabs();
    sendResponse({ ok: true });
    return true;
  }
});

api.tabs.onRemoved.addListener((tabId) => {
  if (tabStates.delete(tabId)) {
    scheduleFlush();
  }
});

api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.url && tabStates.has(tabId)) {
    const cur = tabStates.get(tabId);
    const oldHost = normHost(cur?.url);
    const newHost = normHost(changeInfo.url);
    if (oldHost && newHost && oldHost !== newHost) {
      tabStates.delete(tabId);
      scheduleFlush();
    }
  }

  const tabUrl = tab?.url || "";
  const isHttp = tabUrl.startsWith("http://") || tabUrl.startsWith("https://");

  if (changeInfo.audible === true) {
    if (tabStates.has(tabId)) {
      const cur = tabStates.get(tabId);
      if (cur && !cur.isPlaying) {
        cur.isPlaying = true;
        cur.isPaused = false;
        tabStates.set(tabId, cur);
        scheduleFlush();
      }
    } else if (isHttp) {
      tabStates.set(tabId, {
        tabId,
        windowId: tab?.windowId || 0,
        url: sanitizeUrl(tabUrl),
        title: tab?.title || "",
        artist: "",
        isPlaying: true,
        isPaused: false,
        isMuted: Boolean(tab?.mutedInfo?.muted),
        volume: 1,
        hasVideo: true,
        hasAudio: true,
        duration: 0,
        currentTime: 0
      });
      scheduleFlush();
    }

    if (isHttp) {
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
      if (!ws || ws.readyState !== WebSocket.OPEN) {
        connect();
      } else {
        sendWs({ type: "ping", browser: BROWSER_KEY, version: BRIDGE_PROTOCOL_VERSION });
      }
    }
  });
}

connect();
