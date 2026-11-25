(() => {
  const WMR_VERSION = 4;
  if (window.__wmrVersion === WMR_VERSION) return;
  try { window.__wmrCleanup?.(); } catch {}
  window.__wmrVersion = WMR_VERSION;

  const api = globalThis.browser ?? globalThis.chrome;
  const FEED_HOSTS = /(^|\.)(x\.com|twitter\.com|instagram\.com|tiktok\.com|reddit\.com|facebook\.com)$/i;

  const PLAY_PAUSE_SELECTORS = [
    "#play-pause-button",
    ".ytp-play-button",
    "[data-testid='control-button-playpause']",
    ".playControl",
    "[data-a-target='player-play-pause-button']",
    "[data-testid='playButton']",
    "button[aria-label*='Play']",
    "button[aria-label*='Pause']",
    "button[aria-label*='Oynat']",
    "button[aria-label*='Duraklat']"
  ];

  const NEXT_SELECTORS = [
    ".next-button.ytmusic-player-bar",
    "ytmusic-player-bar tp-yt-paper-icon-button.next-button",
    ".ytp-next-button",
    "[data-testid='control-button-skip-forward']",
    ".skipControl__next"
  ];

  const PREV_SELECTORS = [
    ".previous-button.ytmusic-player-bar",
    "ytmusic-player-bar tp-yt-paper-icon-button.previous-button",
    ".ytp-prev-button",
    "[data-testid='control-button-skip-back']",
    ".skipControl__previous"
  ];

  let lastActiveEl = null;
  let lastMediaSnapshot = null;
  let hadMediaOnHost = "";
  let missingSince = 0;
  let lastSerialized = "";
  let watcherId = 0;
  let watcherIntervalMs = 0;
  let lastShadowScanTs = 0;
  let debounceTimer = 0;
  let titleObs = null;

  function normalizedHost(rawHost = location.hostname) {
    return (rawHost || "").toLowerCase().replace(/^(www|m|mobile)\./, "");
  }

  function isFeedSite() {
    return FEED_HOSTS.test(location.hostname || "");
  }

  function isExplicitMediaPath() {
    const p = (location.pathname || "").toLowerCase();
    return p.includes("/status/") || p.includes("/video/") || p.includes("/reel/") || p.includes("/reels/");
  }

  function isDedicatedWatchUrl() {
    const h = normalizedHost();
    const p = (location.pathname || "").toLowerCase();
    if (h === "music.youtube.com" || h === "open.spotify.com" || h === "soundcloud.com" || h === "netflix.com") {
      return true;
    }
    if (h.includes("youtube.com") && (p.startsWith("/watch") || p.startsWith("/shorts/") || p.startsWith("/live/"))) {
      return true;
    }
    if ((h === "x.com" || h === "twitter.com") && (p.includes("/status/") || p.startsWith("/i/spaces"))) {
      return true;
    }
    if ((h === "twitch.tv" || h === "kick.com") && p.length > 1) {
      return true;
    }
    return false;
  }

  function collectFromShadowHosts(hosts, out) {
    for (const host of hosts) {
      if (host?.shadowRoot) {
        const inner = host.shadowRoot.querySelectorAll("video, audio");
        for (const el of inner) out.push(el);
      }
    }
  }

  function collectMediaElements(forceShadowScan = false) {
    const out = [];
    const direct = document.querySelectorAll("video, audio");
    for (const el of direct) {
      out.push(el);
    }
    if (out.length > 0) return out;

    const knownHosts = document.querySelectorAll("shreddit-player, shreddit-player-2, reddit-pdp-media, ytmusic-player");
    collectFromShadowHosts(knownHosts, out);
    if (out.length > 0) return out;

    if (!forceShadowScan && document.hidden) {
      return out;
    }

    const now = Date.now();
    if (!forceShadowScan && now - lastShadowScanTs < 6000) {
      return out;
    }
    lastShadowScanTs = now;

    const queue = [document];
    let visited = 0;
    while (queue.length > 0 && visited < 1500) {
      const root = queue.shift();
      const nodes = root.querySelectorAll("*");
      for (const host of nodes) {
        visited++;
        if (visited > 1500) break;
        if (host.shadowRoot) {
          const inner = host.shadowRoot.querySelectorAll("video, audio");
          for (const el of inner) out.push(el);
          if (out.length === 0) queue.push(host.shadowRoot);
        }
      }
    }
    return out;
  }

  function isValidMediaElement(el) {
    if (!el || !el.isConnected) return false;
    const hasSrc = Boolean(el.currentSrc || el.src || el.srcObject);
    const dur = Number.isFinite(el.duration) ? el.duration : 0;
    return hasSrc && (el.readyState > 0 || dur > 0 || !el.paused);
  }

  function pickPrimaryMediaElement(forceShadowScan = false) {
    const candidates = collectMediaElements(forceShadowScan).filter(isValidMediaElement);
    if (candidates.length === 0) return null;

    const feedSite = isFeedSite();
    const stickyHost = hadMediaOnHost === normalizedHost() || isExplicitMediaPath();
    if (candidates.length === 1) {
      const only = candidates[0];
      const muted = only.muted || only.volume === 0;
      if (feedSite && muted && only !== lastActiveEl && !stickyHost) {
        return null;
      }
      return only;
    }

    let best = null;
    let bestScore = -1;

    for (const el of candidates) {
      const playing = !el.paused && !el.ended;
      const muted = el.muted || el.volume === 0;

      if (feedSite && muted && el !== lastActiveEl && !stickyHost) {
        continue;
      }

      let score = 1;
      if (playing && !muted) score += 1000;
      if (el === lastActiveEl) score += 500;
      if (playing) score += 200;

      if (el.tagName === "VIDEO") {
        if (el.classList.contains("html5-main-video") || el.closest("ytd-reel-video-renderer[is-active]")) {
          score += 150;
        }
        if (!document.hidden) {
          const rect = el.getBoundingClientRect();
          const area = Math.max(0, Math.min(rect.width * rect.height, 2_000_000));
          score += Math.floor(area / 10000);
        }
      } else {
        score += 50;
      }

      if (score > bestScore) {
        bestScore = score;
        best = el;
      }
    }

    return best;
  }

  function getYouTubePlayer() {
    const host = (location.hostname || "").toLowerCase();
    if (!host.includes("youtube.com")) return null;

    const isShorts = (location.pathname || "").startsWith("/shorts/");
    const order = isShorts ? ["#shorts-player", "#movie_player"] : ["#movie_player", "#shorts-player"];
    let fallback = null;
    for (const sel of order) {
      const node = document.querySelector(sel);
      if (!node) continue;
      const player = node.wrappedJSObject || node;
      if (!player || typeof player.setVolume !== "function") continue;
      const rect = node.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0) {
        return player;
      }
      if (!fallback) fallback = player;
    }
    return fallback;
  }

  function syncYouTubeMusicSlider(pct) {
    const slider = document.querySelector("ytmusic-player-bar #volume-slider");
    if (!slider) return;
    const unwrapped = slider.wrappedJSObject || slider;
    try {
      unwrapped.value = pct;
      unwrapped.dispatchEvent(new Event("change", { bubbles: true }));
      unwrapped.dispatchEvent(new Event("input", { bubbles: true }));
    } catch {}
  }

  function readPlayerVolumeState(el) {
    const ytPlayer = getYouTubePlayer();
    if (ytPlayer) {
      try {
        if (typeof ytPlayer.getVolume === "function" && typeof ytPlayer.isMuted === "function") {
          const rawVol = Number(ytPlayer.getVolume());
          const ytMuted = Boolean(ytPlayer.isMuted());
          if (Number.isFinite(rawVol)) {
            const norm = Math.max(0, Math.min(1, rawVol / 100));
            return { volume: norm, isMuted: ytMuted || norm === 0 };
          }
        }
      } catch {}
    }

    const sliderNode =
      document.querySelector("ytmusic-player-bar #volume-slider") ||
      document.querySelector(".ytp-volume-panel");
    if (sliderNode) {
      const rawNow = Number(sliderNode.getAttribute("aria-valuenow"));
      if (Number.isFinite(rawNow)) {
        const norm = Math.max(0, Math.min(1, rawNow / 100));
        return { volume: norm, isMuted: Boolean(el.muted || norm === 0) };
      }
    }

    const volume = Number.isFinite(el.volume) ? Math.max(0, Math.min(1, el.volume)) : 1;
    const isMuted = Boolean(el.muted || volume === 0);
    return { volume, isMuted };
  }

  function applyYouTubeVolume(action, delta) {
    const ytPlayer = getYouTubePlayer();
    if (!ytPlayer) return false;

    try {
      if (action === "set_volume" && Number.isFinite(Number(delta))) {
        const pct = Math.max(0, Math.min(100, Math.round(Number(delta))));
        ytPlayer.setVolume(pct);
        if (pct > 0 && typeof ytPlayer.unMute === "function") ytPlayer.unMute();
        else if (pct === 0 && typeof ytPlayer.mute === "function") ytPlayer.mute();
        syncYouTubeMusicSlider(pct);
        return true;
      }

      if (action === "adjust_volume" && Number.isFinite(Number(delta))) {
        const cur = typeof ytPlayer.getVolume === "function" ? Number(ytPlayer.getVolume()) : 100;
        const base = ytPlayer.isMuted?.() && delta > 0 && cur <= 1 ? 0 : (Number.isFinite(cur) ? cur : 100);
        const pct = Math.max(0, Math.min(100, Math.round(base + Number(delta))));
        ytPlayer.setVolume(pct);
        if (pct > 0 && typeof ytPlayer.unMute === "function") ytPlayer.unMute();
        else if (pct === 0 && typeof ytPlayer.mute === "function") ytPlayer.mute();
        syncYouTubeMusicSlider(pct);
        return true;
      }

      if (action === "toggle_mute") {
        const muted = Boolean(ytPlayer.isMuted?.());
        if (muted) {
          ytPlayer.unMute?.();
          const cur = typeof ytPlayer.getVolume === "function" ? Number(ytPlayer.getVolume()) : 50;
          if (!Number.isFinite(cur) || cur <= 1) {
            ytPlayer.setVolume(50);
            syncYouTubeMusicSlider(50);
          }
        } else {
          ytPlayer.mute?.();
        }
        return true;
      }
    } catch {}

    return false;
  }

  function applyHtml5Volume(primaryEl, action, delta) {
    const targets = [];
    if (primaryEl) targets.push(primaryEl);
    for (const candidate of collectMediaElements()) {
      if (candidate !== primaryEl && isValidMediaElement(candidate) && (!candidate.paused || candidate === lastActiveEl)) {
        targets.push(candidate);
      }
    }

    for (const el of targets) {
      if (action === "set_volume" && Number.isFinite(Number(delta))) {
        const vol = Math.max(0, Math.min(1, Number(delta) / 100));
        el.volume = vol;
        if (vol > 0 && el.muted) el.muted = false;
      } else if (action === "adjust_volume" && Number.isFinite(Number(delta))) {
        const cur = Number.isFinite(el.volume) ? el.volume : 1;
        const vol = Math.max(0, Math.min(1, cur + Number(delta) / 100));
        el.volume = vol;
        if (vol > 0 && el.muted) el.muted = false;
      } else if (action === "toggle_mute") {
        const nextMuted = !el.muted;
        el.muted = nextMuted;
        if (!nextMuted && el.volume === 0) el.volume = 0.5;
      }
      try {
        el.dispatchEvent(new Event("volumechange", { bubbles: true }));
      } catch {}
    }

    if (targets.length === 0 && lastMediaSnapshot) {
      if (action === "set_volume" && Number.isFinite(Number(delta))) {
        const vol = Math.max(0, Math.min(1, Number(delta) / 100));
        lastMediaSnapshot.volume = vol;
        lastMediaSnapshot.isMuted = vol === 0;
      } else if (action === "toggle_mute") {
        lastMediaSnapshot.isMuted = !lastMediaSnapshot.isMuted;
      }
    }
  }

  function textOf(selector) {
    const el = document.querySelector(selector);
    const txt = el?.textContent?.trim();
    return txt || "";
  }

  function cleanDocTitle() {
    return (document.title || "").replace(/^\(\d+\+?\)\s*/, "").trim();
  }

  function extractMetadata() {
    const host = (location.hostname || "").toLowerCase();
    let title = "";
    let artist = "";

    if (host.includes("music.youtube.com")) {
      title = textOf("ytmusic-player-bar .title");
      artist = textOf("ytmusic-player-bar .byline");
    } else if (host.includes("youtube.com")) {
      title =
        textOf("ytd-reel-video-renderer[is-active] h2.title") ||
        textOf("h1.ytd-watch-metadata yt-formatted-string") ||
        textOf("#title h1 yt-formatted-string");
      artist =
        textOf("ytd-reel-video-renderer[is-active] ytd-channel-name a") ||
        textOf("#owner #channel-name a");
    } else if (host.includes("open.spotify.com")) {
      title = textOf("[data-testid='context-item-info-title']");
      artist = textOf("[data-testid='context-item-info-subtitles']");
    } else if (host.includes("soundcloud.com")) {
      title = textOf(".playbackSoundBadge__titleLink span:last-child") || textOf(".playbackSoundBadge__titleLink");
      artist = textOf(".playbackSoundBadge__lightLink");
    }

    try {
      const msMeta = navigator.mediaSession?.metadata;
      if (msMeta) {
        if (!title && msMeta.title) title = msMeta.title.trim();
        if (!artist && msMeta.artist) artist = msMeta.artist.trim();
      }
    } catch {}

    if (!title) {
      title = cleanDocTitle();
    }

    return { title, artist };
  }

  function sanitizeUrl() {
    try {
      const u = new URL(location.href);
      return u.origin + u.pathname;
    } catch {
      return "";
    }
  }

  function buildState() {
    const el = pickPrimaryMediaElement();
    const cleanUrl = sanitizeUrl();
    const host = normalizedHost();

    if (!el) {
      if (lastMediaSnapshot && lastMediaSnapshot.host === host) {
        const now = Date.now();
        if (!missingSince) missingSince = now;
        if (document.hidden || isDedicatedWatchUrl() || now - missingSince < 4500) {
          return {
            ...lastMediaSnapshot,
            hasMedia: true,
            url: cleanUrl,
            title: lastMediaSnapshot.title || cleanDocTitle(),
            isPlaying: false,
            isPaused: true,
            keepSticky: true
          };
        }
        lastMediaSnapshot = null;
        hadMediaOnHost = "";
      }
      return { hasMedia: false, url: cleanUrl };
    }

    missingSince = 0;
    hadMediaOnHost = host;
    const { title, artist } = extractMetadata();
    const isPlaying = !el.paused && !el.ended;
    const { volume, isMuted } = readPlayerVolumeState(el);
    const hasVideo = el.tagName === "VIDEO";
    const duration = Number.isFinite(el.duration) ? el.duration : 0;
    const currentTime = Number.isFinite(el.currentTime) ? el.currentTime : 0;

    const state = {
      hasMedia: true,
      url: cleanUrl,
      title,
      artist,
      isPlaying,
      isPaused: !isPlaying,
      isMuted,
      volume,
      hasVideo,
      hasAudio: true,
      duration,
      currentTime
    };

    lastMediaSnapshot = { ...state, host };
    return state;
  }

  function reportState(force = false) {
    if (window.__wmrVersion !== WMR_VERSION) return;
    const state = buildState();
    const signature = `${state.hasMedia ? 1 : 0}|${state.url || ""}|${state.title || ""}|${state.artist || ""}|${state.isPlaying ? 1 : 0}|${state.isMuted ? 1 : 0}|${Math.round((state.volume ?? 1) * 100)}|${state.hasVideo ? 1 : 0}`;

    if (force || signature !== lastSerialized) {
      lastSerialized = signature;
      try {
        const res = api.runtime.sendMessage({ type: "media_state", state });
        if (res && typeof res.catch === "function") res.catch(() => {});
      } catch {}
    }

    ensureWatcher(state.hasMedia);
  }

  function scheduleReport(force = false) {
    if (debounceTimer) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => reportState(force), 60);
  }

  function ensureWatcher(active) {
    if (!active) {
      if (watcherId) {
        clearInterval(watcherId);
        watcherId = 0;
        watcherIntervalMs = 0;
      }
      return;
    }

    const desiredMs = document.hidden ? 5000 : 2000;
    if (watcherId && watcherIntervalMs === desiredMs) return;
    if (watcherId) clearInterval(watcherId);
    watcherIntervalMs = desiredMs;
    watcherId = setInterval(() => reportState(false), desiredMs);
  }

  function clickFirst(selectors) {
    for (const sel of selectors) {
      const btn = document.querySelector(sel);
      if (btn && !btn.disabled) {
        btn.click();
        return true;
      }
    }
    return false;
  }

  async function executeCommand(action, delta) {
    const el = pickPrimaryMediaElement(true) || lastActiveEl || collectMediaElements(true)[0] || null;
    if (el) lastActiveEl = el;

    switch (action) {
      case "play":
        if (el) {
          try {
            await el.play();
            if (el.paused) clickFirst(PLAY_PAUSE_SELECTORS);
          } catch {
            clickFirst(PLAY_PAUSE_SELECTORS);
          }
        } else {
          clickFirst(PLAY_PAUSE_SELECTORS);
        }
        break;

      case "pause":
        if (el) {
          try { el.pause(); } catch {}
          setTimeout(() => {
            if (!el.paused) clickFirst(PLAY_PAUSE_SELECTORS);
            reportState(true);
          }, 40);
        }
        break;

      case "toggle":
        if (el && !el.paused && !el.ended) {
          await executeCommand("pause", 0);
          return;
        } else {
          await executeCommand("play", 0);
          return;
        }

      case "next":
        clickFirst(NEXT_SELECTORS);
        break;

      case "previous":
        if (!clickFirst(PREV_SELECTORS) && el) {
          el.currentTime = 0;
        }
        break;

      case "seek":
        if (el && Number.isFinite(delta)) {
          const maxDur = Number.isFinite(el.duration) && el.duration > 0 ? el.duration : el.currentTime + Math.abs(delta);
          el.currentTime = Math.max(0, Math.min(maxDur, (el.currentTime || 0) + Number(delta)));
        }
        break;

      case "set_volume":
      case "adjust_volume":
      case "toggle_mute":
        if (!applyYouTubeVolume(action, delta)) {
          applyHtml5Volume(el, action, delta);
        }
        break;
    }

    reportState(true);
    setTimeout(() => reportState(true), 150);
  }

  function handleMediaEvent(ev) {
    if (window.__wmrVersion !== WMR_VERSION) return;
    const target = ev.target;
    if (target && (target.tagName === "VIDEO" || target.tagName === "AUDIO")) {
      if (ev.type === "play" || ev.type === "playing" || ev.type === "volumechange") {
        const unmuted = !target.muted && target.volume > 0;
        if (!target.paused && (unmuted || !isFeedSite())) {
          lastActiveEl = target;
          hadMediaOnHost = normalizedHost();
        }
      }
    }
    scheduleReport(false);
  }

  function handleVisibilityChange() {
    if (window.__wmrVersion !== WMR_VERSION) return;
    if (lastMediaSnapshot || watcherId) {
      ensureWatcher(true);
    }
    if (!document.hidden) {
      scheduleReport(true);
    }
  }

  const events = ["play", "playing", "pause", "ended", "volumechange", "loadedmetadata", "emptied"];
  for (const evt of events) {
    document.addEventListener(evt, handleMediaEvent, true);
  }
  document.addEventListener("visibilitychange", handleVisibilityChange, true);

  const titleEl = document.querySelector("title");
  if (titleEl && typeof MutationObserver !== "undefined") {
    titleObs = new MutationObserver(() => {
      if (window.__wmrVersion === WMR_VERSION && (lastMediaSnapshot || document.querySelector("video, audio"))) {
        scheduleReport(false);
      }
    });
    titleObs.observe(titleEl, { childList: true, characterData: true, subtree: true });
  }

  function onRuntimeMessage(msg, _sender, sendResponse) {
    if (window.__wmrVersion !== WMR_VERSION || !msg) return;
    if (msg.type === "query_state") {
      reportState(true);
      sendResponse({ ok: true });
      return true;
    }
    if (msg.type === "wmr_cmd") {
      executeCommand(msg.action, msg.delta).then(() => sendResponse({ ok: true }));
      return true;
    }
  }

  api.runtime.onMessage.addListener(onRuntimeMessage);

  window.__wmrCleanup = () => {
    if (watcherId) { clearInterval(watcherId); watcherId = 0; }
    watcherIntervalMs = 0;
    if (debounceTimer) { clearTimeout(debounceTimer); debounceTimer = 0; }
    try { titleObs?.disconnect(); } catch {}
    for (const evt of events) {
      document.removeEventListener(evt, handleMediaEvent, true);
    }
    document.removeEventListener("visibilitychange", handleVisibilityChange, true);
    try { api.runtime.onMessage.removeListener(onRuntimeMessage); } catch {}
  };

  scheduleReport(true);
})();
