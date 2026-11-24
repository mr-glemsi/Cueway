# Privacy Policy

**Effective Date:** November 24, 2026  
**Applies to:** Cueway (Windows Desktop Application) and Cueway Companion (Browser Extension for Chromium and Firefox)

---

## English

### 1. Zero Personal Data Collection
Cueway and Cueway Companion **do not collect, store, sell, share, or transmit** any personal data, browsing history, analytics, crash telemetry, cookies, or tracking identifiers to the developer or any third-party server.

### 2. Local-Only Communication (`127.0.0.1`)
Cueway Companion communicates **exclusively on your own computer** over a loopback-only WebSocket connection (`ws://127.0.0.1:47621`) directly with the local Cueway desktop application:
- Incoming WebSocket handshakes from arbitrary websites are strictly rejected (`403 Forbidden`); only browser-extension origins (`chrome-extension://`, `moz-extension://`, `extension://`) are accepted.
- Cueway makes **no outbound internet connections** from the browser extension.

### 3. Minimal In-Memory Media Metadata
To let you control individual media tabs from desktop media keys and Cueway's dashboard, Cueway Companion reads the following information **only in volatile memory** and forwards it solely to local Cueway:
- Active media state (`playing`, `paused`, `muted`, `volume`, audio/video presence)
- Media title and artist/channel name (if exposed by the player)
- Tab origin and pathname (**query parameters `?v=...`, tokens, and URL `#fragments` are stripped** before leaving the tab)

None of your browsing history, form inputs, passwords, or non-media pages are logged or persisted to disk by the extension.

### 4. Local Settings Storage
The Cueway desktop app stores only your local application preferences (theme, language, hotkeys, and discovered media player priority order) in `%LocalAppData%\Cueway\cueway_state.json` on your own PC.

### 5. No Remote Code
All scripts in Cueway Companion are static and bundled inside the extension package. No remote JavaScript or external code is fetched or executed.

---

## Türkçe

### 1. Kişisel Veri Toplanmaz
Cueway ve Cueway Companion tarayıcı eklentisi geliştiriciye veya herhangi bir üçüncü taraf sunucuya **hiçbir kişisel veri, tarama geçmişi, analiz, telemetri, çerez veya takip kimliği toplamaz, iletmez ve satmaz**.

### 2. Yalnızca Yerel Bilgisayar İçi İletişim (`127.0.0.1`)
Cueway Companion yalnızca kendi bilgisayarınızdaki yerel WebSocket köprüsü (`ws://127.0.0.1:47621`) üzerinden yerel Cueway masaüstü uygulamasıyla konuşur:
- Dış web sitelerinden gelen bağlantılar `403 Forbidden` ile reddedilir; yalnızca tarayıcı eklenti kökenleri (`chrome-extension://`, `moz-extension://`, `extension://`) kabul edilir.
- Eklenti internete hiçbir ağ isteği göndermez.

### 3. Yalnızca Bellek İçi Asgari Medya Bilgisi
Medya tuşlarıyla tarayıcı sekmelerini bağımsız kontrol edebilmeniz için eklenti yalnızca medya durumu, başlık/kanal bilgisi ve sorgu parametreleri temizlenmiş (`origin + pathname`, `?` ve `#` olmadan) adres bilgisini geçici olarak yerel uygulamaya iletir; hiçbir tarama geçmişi diske yazılmaz.

### 4. Uzak Kod Yoktur
Eklenti içindeki tüm kodlar yerel ve açık kaynaklıdır; dışarıdan kod indirilip çalıştırılmaz.
