# Cueway Companion — Tarayıcı Mağazası Yükleme Rehberi

Bu klasör **Mozilla Add-ons (Firefox / Zen Browser)** ve **Microsoft Edge Add-ons (ücretsiz)** başvurusu için gereken tüm paketleri ve görselleri hazır içerir.

---

## Hazır Paketler & Görseller

| Dosya | Kullanım Yeri |
| :--- | :--- |
| `StoreSubmission\Cueway-Companion-Firefox-AMO-v1.2.6.zip` | Mozilla AMO yükleme paketi (Firefox & Zen) |
| `StoreSubmission\Cueway-Companion-Edge-Chrome-v1.2.6.zip` | Microsoft Edge Add-ons / Chrome Web Store yükleme paketi |
| `StoreSubmission\Assets\edge-store-logo-300x300.png` | Edge Partner Center mağaza logosu (`300x300`) |
| `StoreSubmission\Assets\store-promo-440x280.png` | Mağaza tanıtım görseli (`440x280`) |
| `StoreSubmission\Assets\store-screenshot-1280x800.png` | Mağaza ekran görüntüsü (`1280x800`) |
| `PRIVACY.md` | Gizlilik Politikası (GitHub repo yükleyince bunun URL'sini ver) |

---

## 1. Mozilla Add-ons (Firefox / Zen Browser) — ÜCRETSİZ

Giriş: `https://addons.mozilla.org/developers/addon/submit/distribution`

### Hangi Seçeneği Seçmelisin?
* **"On your own" (Önerilen Hızlı Yol — Liste Dışı İmzalı `.xpi`):**
  1. `"On your own"` seç → `Cueway-Companion-Firefox-AMO-v1.2.6.zip` yükle.
  2. Kaynak kod isterse hayır de (kod küçültülmemiş/minify edilmemiş, açık).
  3. Genellikle birkaç dakika içinde imzalı **`.xpi`** dosyası çıkar.
  4. İndirdiğin dosyanın adını `cueway-companion.xpi` yapıp şu klasöre koy:
     `C:\projects\Cueway\BrowserExtension\Firefox\cueway-companion.xpi`
  5. Artık Cueway içinden **"Firefox / Zen Kur"** dediğinde Zen/Firefox otomatik olarak üstten tek tıkla **"Ekle"** bildirimini çıkarır ve kalıcı olarak kurulur!
* **"On this site" (Herkese Açık AMO Mağazası):**
  * Kullanıcıların doğrudan `addons.mozilla.org` aramasından bulup otomatik güncelleme almasını istersen bunu seç (inceleme biraz daha uzun sürebilir). Onay linkini uygulamaya da bağlayabiliriz.

### Mozilla AMO Kopyala-Yapıştır Alanları

**Name:**
```
Cueway Companion
```

**Summary (en fazla 250 karakter):**
```
Connects Firefox & Zen media tabs to the Cueway desktop app so each playing tab can be routed, controlled, muted, and focused independently.
```

**Categories:**
* `Other` veya `Music & Videos`

**Notes to Reviewer:**
```
Cueway Companion is a zero-telemetry local companion extension for the open-source Cueway Windows desktop media router (https://127.0.0.1:47621).

Why <all_urls> / http(s) content script access is needed:
Users stream audio/video across arbitrary websites (YouTube, YouTube Music, X, Twitch, Kick, Netflix, SoundCloud, Vimeo, local Plex/Jellyfin web UIs, news/podcast players, etc.). The extension must inspect HTML5 <video>/<audio> state on whichever site the user plays media and execute play/pause/volume/next/prev/focus commands without stealing window focus.

Data handling & privacy:
- No personal data is collected, logged, or sent to any remote server (data_collection_permissions.required = ["none"]).
- Query parameters and URL fragments are stripped (origin + pathname only) before forwarding active media tab state strictly to localhost ws://127.0.0.1:47621.
- Before opening ws://127.0.0.1:47621, the extension performs a lightweight localhost-only HTTP liveness check (http://127.0.0.1:47621/health) to avoid browser WebSocket reconnect backoff delays.
- If the Cueway desktop app is not running, the extension simply stays idle in disconnected mode and can still list local media tabs in its toolbar popup.
```

---

## 2. Microsoft Edge Add-ons — ÜCRETSİZ

Giriş: `https://partner.microsoft.com/dashboard/microsoftedge`

1. Microsoft hesabınla ücretsiz Edge Developer kaydı oluştur.
2. **Create new extension** → Packages → **`Cueway-Companion-Edge-Chrome-v1.2.6.zip`** yükle.
3. **Store listing**:
   * Logo (`300x300`): `StoreSubmission\Assets\edge-store-logo-300x300.png`
   * Promotional tile (`440x280`): `StoreSubmission\Assets\store-promo-440x280.png`
   * Screenshot (`1280x800`): `StoreSubmission\Assets\store-screenshot-1280x800.png`
4. **Privacy Policy URL**:
   * GitHub reposunu açtıktan sonra `PRIVACY.md` dosyasının URL'sini (örn. `https://github.com/<kullanici>/Cueway/blob/main/PRIVACY.md`) yapıştır.

### Edge Partner Center Kopyala-Yapıştır Alanları

**Short Description:**
```
Connects Edge media tabs to the Cueway Windows app for independent per-tab playback, volume, mute, and focus routing.
```

**Description:**
```
Cueway Companion turns every active video or audio tab in Microsoft Edge into an independent target inside the Cueway Windows desktop media router.

Key Features:
• Per-Tab Control: Route hardware media keys, play/pause, next/previous, and seek specifically to YouTube, YouTube Music, X, Twitch, or any HTML5 player tab without stealing browser focus.
• Real Player Volume & Mute: Adjust individual tab player volume directly from Cueway's dashboard or system tray without muting other tabs.
• Instant Tab Focus: Click any media session inside Cueway to bring that exact browser window and tab straight to the foreground.
• Privacy First: Connects strictly over localhost (ws://127.0.0.1:47621) to your local Cueway app. Query strings/tokens are stripped, and zero data is ever transmitted to external servers.

Requires the Cueway Windows desktop application.
```

**Search Terms (Keywords):**
```
media keys, media router, tab volume, youtube control, cueway
```

**Permission Justifications (Properties -> Privacy & Permissions):**
* **`tabs`**: Needed to read active media tab titles/IDs, detect background audible tabs cleanly via `tabs.onUpdated`, and switch to a specific media tab (`tabs.update` / `windows.update`) when the user clicks Focus in Cueway.
* **`scripting`**: Needed to inject the static `content.js` on install/reconnect and run the built-in `MAIN`-world volume handler for YouTube/YouTube Music `#movie_player` APIs (zero remote code).
* **`alarms`**: Used for MV3 service-worker keepalive every 30 seconds to maintain the local WebSocket link to `ws://127.0.0.1:47621`.
* **`<all_urls>` host permission**: Users play audio/video across arbitrary streaming, social, and self-hosted sites beyond just YouTube/Twitch. Broad match allows Cueway to detect and control HTML5 `<video>` / `<audio>` elements on any webpage the user visits.

**Notes for Certification:**
```
This extension bridges Microsoft Edge to the companion Windows desktop app Cueway via loopback-only WebSocket (ws://127.0.0.1:47621), preceded by a lightweight localhost-only HTTP liveness probe (http://127.0.0.1:47621/health) to avoid browser WebSocket reconnect backoff delays. It collects zero personal data, performs zero external network requests, strips URL query parameters/fragments before local transfer, and executes no remote code. When the local desktop app is not running, opening the extension popup cleanly shows "Disconnected" while listing local media tabs in the browser.
```

---

## 3. Onay Sonrası Entegrasyon (Önemli Politika Notu)
* Microsoft Edge Add-ons onayından sonra mağaza linkini/Extension ID'sini bana ver; Cueway içindeki **"Edge / Chrome Kur"** butonunu doğrudan resmi Edge Mağaza sayfasını açacak şekilde güncelleyelim.
* *(Not: Microsoft mağaza politikaları gereği tüketici uygulamalarının kayıt defteriyle sessiz/zorla dış eklenti eklemesi istenmeyen yazılım uyarısına takılabileceği için en temiz ve sorunsuz yol resmi mağaza sayfasına veya imzalı `.xpi`'ye tek tıkla yönlendirmektir.)*
