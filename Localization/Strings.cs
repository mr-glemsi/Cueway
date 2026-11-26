namespace Cueway.Localization;

internal enum AppLanguage
{
    Tr = 0,
    En = 1,
    Zh = 2,
    Es = 3,
    Ru = 4,
    De = 5,
    Fr = 6,
    Ja = 7,
    Ko = 8
}

internal readonly record struct LanguageOption(AppLanguage Language, string Code, string NativeName);

internal static class Strings
{
    private static volatile int _current = (int)AppLanguage.Tr;

    public static AppLanguage Current => (AppLanguage)_current;

    public static string CurrentCode => Current switch
    {
        AppLanguage.En => "en",
        AppLanguage.Zh => "zh",
        AppLanguage.Es => "es",
        AppLanguage.Ru => "ru",
        AppLanguage.De => "de",
        AppLanguage.Fr => "fr",
        AppLanguage.Ja => "ja",
        AppLanguage.Ko => "ko",
        _ => "tr"
    };

    public static string PreferredFontName => Current switch
    {
        AppLanguage.Zh => "Microsoft YaHei UI",
        AppLanguage.Ja => "Yu Gothic UI",
        AppLanguage.Ko => "Malgun Gothic",
        _ => "Segoe UI"
    };

    public static readonly LanguageOption[] Languages =
    [
        new(AppLanguage.Tr, "tr", "Türkçe"),
        new(AppLanguage.En, "en", "English"),
        new(AppLanguage.Zh, "zh", "中文"),
        new(AppLanguage.Es, "es", "Español"),
        new(AppLanguage.Ru, "ru", "Русский"),
        new(AppLanguage.De, "de", "Deutsch"),
        new(AppLanguage.Fr, "fr", "Français"),
        new(AppLanguage.Ja, "ja", "日本語"),
        new(AppLanguage.Ko, "ko", "한국어")
    ];

    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "tr";
        return code.Trim().ToLowerInvariant() switch
        {
            "en" => "en",
            "zh" or "zh-cn" or "zh-hans" => "zh",
            "es" => "es",
            "ru" => "ru",
            "de" => "de",
            "fr" => "fr",
            "ja" or "ja-jp" or "jp" => "ja",
            "ko" or "ko-kr" or "kr" => "ko",
            _ => "tr"
        };
    }

    public static void SetLanguage(string? code)
    {
        AppLanguage lang = NormalizeCode(code) switch
        {
            "en" => AppLanguage.En,
            "zh" => AppLanguage.Zh,
            "es" => AppLanguage.Es,
            "ru" => AppLanguage.Ru,
            "de" => AppLanguage.De,
            "fr" => AppLanguage.Fr,
            "ja" => AppLanguage.Ja,
            "ko" => AppLanguage.Ko,
            _ => AppLanguage.Tr
        };
        Interlocked.Exchange(ref _current, (int)lang);
    }

    private static string T(
        string tr, string en, string zh, string es, string ru,
        string de, string fr, string ja, string ko)
        => Current switch
        {
            AppLanguage.En => en,
            AppLanguage.Zh => zh,
            AppLanguage.Es => es,
            AppLanguage.Ru => ru,
            AppLanguage.De => de,
            AppLanguage.Fr => fr,
            AppLanguage.Ja => ja,
            AppLanguage.Ko => ko,
            _ => tr
        };

    // Window & Tabs
    public static string WindowTitle => T(
        "Cueway — Medya Yönlendirici",
        "Cueway — Media Router",
        "Cueway — 媒体路由器",
        "Cueway — Enrutador Multimedia",
        "Cueway — Медиа-маршрутизатор",
        "Cueway — Medien-Router",
        "Cueway — Routeur Média",
        "Cueway — メディアルーター",
        "Cueway — 미디어 라우터");

    public static string TabDashboard => T("Kontrol Paneli", "Dashboard", "控制台", "Panel", "Панель", "Übersicht", "Tableau", "ダッシュボード", "대시보드");
    public static string TabSettings => T("Ayarlar", "Settings", "设置", "Ajustes", "Настройки", "Einstellungen", "Paramètres", "設定", "설정");
    public static string BtnExit => T("Çıkış", "Exit", "退出", "Salir", "Выход", "Beenden", "Quitter", "終了", "종료");

    // Header Badges
    public static string BadgeSoloActive => T("SOLO AKTİF", "SOLO ON", "独奏开启", "SOLO ACTIVO", "СОЛО ВКЛ", "SOLO AN", "SOLO ACTIF", "SOLO 有効", "SOLO 켜짐");
    public static string BadgeGameGuard => T("OYUN KORUMASI", "GAME GUARD", "游戏保护", "MODO JUEGO", "РЕЖИМ ИГРЫ", "SPIELSCHUTZ", "MODE JEU", "ゲーム保護", "게임 보호");

    // Status Pills & Metadata
    public static string StatusPlaying => T("Oynatılıyor", "Playing", "正在播放", "Reproduciendo", "Воспроизведение", "Wiedergabe", "Lecture", "再生中", "재생 중");
    public static string StatusPaused => T("Duraklatıldı", "Paused", "已暂停", "Pausado", "На паузе", "Pausiert", "En pause", "一時停止", "일시정지");
    public static string StatusMuted => T("Sessiz", "Muted", "已静音", "Silenciado", "Без звука", "Stumm", "Muet", "ミュート", "음소거");
    public static string StatusClosed => T("Kapatıldı", "Closed", "已关闭", "Cerrado", "Закрыто", "Geschlossen", "Fermé", "終了", "닫힘");
    public static string ModeManualLock => T("Manuel Kilit", "Manual Lock", "手动锁定", "Bloqueo Manual", "Ручной замок", "Manuelle Sperre", "Verrou Manuel", "手動ロック", "수동 잠금");
    public static string ModeAuto => T("Otomatik Mod", "Auto Priority", "自动优先", "Modo Automático", "Автоприоритет", "Auto-Priorität", "Priorité Auto", "自動優先", "자동 우선순위");
    public static string MediaPlayingFallback => T("Medya oynatılıyor", "Playing media", "正在播放媒体", "Reproduciendo", "Воспроизведение", "Medienwiedergabe", "Lecture en cours", "メディアを再生中", "미디어 재생 중");

    // Dashboard Section Headings & Buttons
    public static string SecLivePlayer => T("CANLI OYNATICI", "LIVE PLAYER", "实时播放器", "REPRODUCTOR EN VIVO", "АКТИВНЫЙ ПЛЕЕР", "LIVE-PLAYER", "LECTEUR EN DIRECT", "ライブプレーヤー", "실시간 플레이어");
    public static string SecNowPlaying => T("EN SON OYNATILAN", "LAST PLAYED", "最近播放", "ÚLTIMA REPRODUCCIÓN", "ПОСЛЕДНЕЕ ВОСПРОИЗВЕДЕНИЕ", "ZULETZT GESPIELT", "DERNIÈRE LECTURE", "最近の再生", "최근 재생");
    public static string NoNowPlayingTitle => T("Henüz Oynatılan Medya Yok", "No Recent Playback", "暂无播放记录", "Sin reproducción reciente", "Пока ничего не воспроизводилось", "Noch keine Wiedergabe", "Aucune lecture récente", "再生履歴はまだありません", "최근 재생 기록 없음");
    public static string NoNowPlayingSub => T(
        "Bir uygulama veya sekmede medya oynatıldığında en son oynatılan burada görünür.",
        "When any app or browser tab plays media, the most recently played item appears here.",
        "当任意应用或标签页播放媒体时，最近播放的内容会显示在此处。",
        "Cuando alguna aplicación o pestaña reproduzca algo, lo último reproducido aparecerá aquí.",
        "Когда приложение или вкладка запустит медиа, последнее воспроизведение появится здесь.",
        "Sobald eine App oder ein Tab Medien abspielt, erscheint der letzte Eintrag hier.",
        "Dès qu'une application ou un onglet lit un média, le dernier élément lu apparaît ici.",
        "アプリやタブでメディアを再生すると、最後に再生した項目がここに表示されます。",
        "앱이나 탭에서 미디어를 재생하면 가장 최근에 재생한 항목이 여기에 표시됩니다.");
    public static string BadgePriorityTarget => T("ÖNCELİKLİ", "PRIORITY", "优先", "PRIORIDAD", "ПРИОРИТЕТ", "PRIORITÄT", "PRIORITÉ", "優先", "우선");

    public static string SecActiveTarget => T("AKTİF HEDEF", "ACTIVE TARGET", "当前目标", "DESTINO ACTIVO", "АКТИВНАЯ ЦЕЛЬ", "AKTIVES ZIEL", "CIBLE ACTIVE", "アクティブターゲット", "활성 대상");
    public static string NoActiveTargetTitle => T("Aktif Hedef Yok", "No Active Target", "无活动目标", "Sin destino activo", "Нет активной цели", "Kein aktives Ziel", "Aucune cible active", "アクティブな対象なし", "활성 대상 없음");
    public static string NoActiveTargetSub => T(
        "Herhangi bir medya oynatıldığında tuşlar otomatik olarak yönlendirilir.",
        "Media keys are routed automatically once any player starts playing.",
        "任意播放器开始播放后，媒体按键将自动路由。",
        "Las teclas multimedia se enrutarán automáticamente al reproducir algo.",
        "Клавиши будут направляться автоматически при запуске любого медиаплеера.",
        "Medientasten werden automatisch geleitet, sobald ein Player startet.",
        "Les touches média seront routées automatiquement dès qu'un lecteur démarre.",
        "いずれかのプレーヤーで再生を開始すると、メディアキーが自動的にルーティングされます。",
        "미디어 플레이어가 재생을 시작하면 미디어 키가 자동으로 라우팅됩니다.");

    public static string BtnLock => T("Kilitle", "Lock", "锁定", "Fijar", "Закрепить", "Fixieren", "Verrouiller", "ロック", "잠금");
    public static string BtnLocked => T("Kilitli", "Locked", "已锁定", "Fijado", "Закреплено", "Fixiert", "Verrouillé", "ロック中", "잠금됨");
    public static string BtnUnlock => T("Kilidi Aç", "Unlock", "解锁", "Desbloquear", "Снять замок", "Entsperren", "Déverrouiller", "ロック解除", "잠금 해제");

    public static string MetaSourcePrefix => T("Kaynak", "Source", "来源", "Fuente", "Источник", "Quelle", "Source", "ソース", "소스");
    public static string SourceBrowserBridge => T("Tarayıcı Eklentisi", "Browser Extension", "浏览器扩展", "Extensión", "Расширение", "Browser-Erweiterung", "Extension", "ブラウザ拡張", "브라우저 확장");
    public static string SourceAudioSession => T("Ses Oturumu", "Audio Session", "音频会话", "Sesión de audio", "Аудиосессия", "Audiositzung", "Session audio", "オーディオ", "오디오 세션");
    public static string MetaVolumePrefix => T("Ses", "Vol", "音量", "Vol", "Громк.", "Lautst.", "Vol", "音量", "음량");
    public static string MetaVideoScrubReady => T("Video Sarma Hazır", "Scrub Ready", "支持快进快退", "Avance listo", "Перемотка готова", "Spulen bereit", "Avance prête", "シーク対応", "탐색 준비됨");
    public static string TagVideo => T("Video", "Video", "视频", "Video", "Видео", "Video", "Vidéo", "動画", "비디오");

    public static string SecLiveSessions => T("CANLI MEDYA OTURUMLARI", "LIVE MEDIA SESSIONS", "实时媒体会话", "SESIONES EN VIVO", "АКТИВНЫЕ СЕССИИ", "AKTIVE MEDIENSITZUNGEN", "SESSIONS EN DIRECT", "ライブメディアセッション", "실시간 미디어 세션");
    public static string BtnRefresh => T("Yenile", "Refresh", "刷新", "Actualizar", "Обновить", "Aktualisieren", "Actualiser", "更新", "새로고침");
    public static string EmptySessionsTitle => T(
        "Şu anda canlı bir medya oturumu algılanmadı.",
        "No live media sessions detected right now.",
        "当前未检测到活动的媒体会话。",
        "No se detectaron sesiones multimedia activas.",
        "Активные медиасессии сейчас не обнаружены.",
        "Derzeit keine aktiven Mediensitzungen erkannt.",
        "Aucune session média active détectée pour le moment.",
        "現在アクティブなメディアセッションはありません。",
        "현재 감지된 실시간 미디어 세션이 없습니다.");
    public static string EmptySessionsSub => T(
        "Zen Browser, Spotify, Stremio, VLC, mpv veya başka bir oynatıcıda ses/video başlattığınızda otomatik olarak burada listelenir.",
        "Start audio or video in Zen Browser, Spotify, Stremio, VLC, mpv, or any player to see it here.",
        "在 Zen Browser、Spotify、Stremio、VLC、mpv 或任意播放器中播放音视频后将自动显示在此处。",
        "Inicie audio o video en Zen Browser, Spotify, Stremio, VLC, mpv o cualquier reproductor para verlo aquí.",
        "Запустите аудио или видео в Zen Browser, Spotify, Stremio, VLC, mpv или любом плеере, чтобы увидеть его здесь.",
        "Starten Sie Audio oder Video in Zen Browser, Spotify, Stremio, VLC, mpv oder einem Player.",
        "Lancez un média dans Zen Browser, Spotify, Stremio, VLC, mpv ou un autre lecteur pour l'afficher ici.",
        "Zen Browser、Spotify、Stremio、VLC、mpv などのプレーヤーで音声や動画を再生すると自動的に表示されます。",
        "Zen Browser, Spotify, Stremio, VLC, mpv 또는 기타 플레이어에서 미디어를 재생하면 자동으로 표시됩니다.");

    public static string SecPriorityList => T("UYGULAMA ÖNCELİK SIRASI", "APPLICATION PRIORITY ORDER", "应用优先级顺序", "ORDEN DE PRIORIDAD", "ПОРЯДОК ПРИОРИТЕТА", "PRIORITÄTSREIHENFOLGE", "ORDRE DE PRIORITÉ", "アプリの優先順位", "애플리케이션 우선순위");
    public static string BtnResetHistory => T("Geçmişi Sıfırla", "Reset History", "重置历史", "Reiniciar historial", "Сбросить список", "Verlauf zurücksetzen", "Réinitialiser", "履歴をリセット", "기록 초기화");
    public static string EmptyPriorityTitle => T(
        "Henüz keşfedilen uygulama yok (sıfır sabit liste).",
        "No discovered applications yet (zero hardcoded list).",
        "尚未发现应用（无预设固定列表）。",
        "Aún no hay aplicaciones detectadas (sin lista fija).",
        "Приложения ещё не обнаружены (без предустановленного списка).",
        "Noch keine erkannten Anwendungen (keine feste Liste).",
        "Aucune application découverte pour l'instant (aucune liste fixe).",
        "検出されたアプリはまだありません（固定リストなし）。",
        "아직 검색된 앱이 없습니다 (고정 목록 없음).");
    public static string EmptyPrioritySub => T(
        "Bilgisayarınızda gerçekten oynattığınız uygulamalar .exe yoluyla birlikte otomatik yakalanıp sıralama için buraya kaydedilir.",
        "Apps you actually play media in are automatically recorded here with their .exe path.",
        "只有您实际播放过媒体的应用才会连同 .exe 路径自动记录在此以供排序。",
        "Las apps en las que reproduzca contenido se guardarán aquí automáticamente con su ruta .exe.",
        "Приложения, в которых вы воспроизводили медиа, автоматически сохраняются здесь с путём .exe.",
        "Nur tatsächlich genutzte Medien-Apps werden automatisch samt .exe-Pfad gespeichert.",
        "Seules les applications réellement utilisées sont enregistrées ici avec leur chemin .exe.",
        "実際にメディアを再生したアプリのみが .exe パスとともに自動登録されます。",
        "실제로 미디어를 재생한 앱만 .exe 경로와 함께 자동으로 여기에 기록됩니다.");

    // Settings Sections
    public static string SecLanguage => T("DİL / LANGUAGE", "LANGUAGE / DİL", "语言 / LANGUAGE", "IDIOMA / LANGUAGE", "ЯЗЫК / LANGUAGE", "SPRACHE / LANGUAGE", "LANGUE / LANGUAGE", "言語 / LANGUAGE", "언어 / LANGUAGE");
    public static string LanguageTitle => T("Uygulama Dili", "Application Language", "界面语言", "Idioma de la aplicación", "Язык интерфейса", "Anwendungssprache", "Langue de l'application", "アプリの表示言語", "애플리케이션 언어");
    public static string LanguageSub => T(
        "Arayüz, sistem tepsisi ve bildirim dilini anında değiştirin",
        "Switch the interface, system tray, and notification language instantly",
        "即时切换界面、系统托盘菜单与屏幕提示语言",
        "Cambie al instante el idioma de la interfaz, bandeja y notificaciones",
        "Мгновенное переключение языка интерфейса, трея и уведомлений",
        "Sprache für Oberfläche, Infobereich und Benachrichtigungen sofort wechseln",
        "Changez instantanément la langue de l'interface, du menu et des notifications",
        "画面、システムトレイ、通知の言語を即座に切り替えます",
        "인터페이스, 시스템 트레이 및 알림 언어를 즉시 전환합니다");

    // Appearance & Language
    public static string SecAppearance => T("GÖRÜNÜM VE DİL", "APPEARANCE & LANGUAGE", "外观与语言", "APARIENCIA E IDIOMA", "ОФОРМЛЕНИЕ И ЯЗЫК", "ERSCHEINUNGSBILD & SPRACHE", "APPARENCE ET LANGUE", "外観と言語", "모양 및 언어");
    public static string ThemeTitle => T("Renk Teması", "Color Theme", "色彩主题", "Tema de color", "Цветовая тема", "Farbthema", "Thème de couleur", "カラーテーマ", "색상 테마");
    public static string ThemeSub => T(
        "Uygulama arayüzü, başlık çubuğu ve ekran bildirimleri için tercih ettiğiniz renk paletini seçin",
        "Choose your preferred palette for the app, titlebar, guide dialog, and OSD alerts",
        "为主窗口、标题栏、引导弹窗与屏幕提示选择喜欢的配色方案",
        "Elige tu paleta preferida para la aplicación, barra de título y notificaciones OSD",
        "Выберите палитру для окна, заголовка, окна обучения и экранных уведомлений",
        "Wählen Sie das Farbschema für Oberfläche, Titelleiste, Anleitung und OSD",
        "Choisissez la palette pour l'interface, la barre de titre, le guide et l'OSD",
        "アプリ本体、タイトルバー、ガイド画面、OSD 通知の配色を選択します",
        "앱, 제목 표시줄, 가이드 창 및 OSD 알림에 사용할 색상 팔레트를 선택하세요");
    public static string ThemeName(string themeId)
    {
        var custom = UI.Theme.FindById(themeId);
        if (custom?.CustomName != null)
            return custom.CustomName;

        return themeId.ToLowerInvariant() switch
        {
            "light" => T("Açık", "Light", "浅色", "Claro", "Светлая", "Hell", "Clair", "ライト", "라이트"),
            "nord" => "Nord",
            "emerald" => T("Zümrüt", "Emerald", "翡翠绿", "Esmeralda", "Изумруд", "Smaragd", "Émeraude", "エメラルド", "에메랄드"),
            "amethyst" => T("Ametist", "Amethyst", "紫水晶", "Amatista", "Аметист", "Amethyst", "Améthyste", "アメジスト", "자수정"),
            "amber" => T("Kehribar", "Amber", "琥珀金", "Ámbar", "Янтарь", "Bernstein", "Ambre", "アンバー", "앰버"),
            "crimson" => T("Kızıl", "Crimson", "绯红", "Carmesí", "Рубин", "Karmesin", "Carmin", "クリムゾン", "크림슨"),
            "sand" => T("Kum Açık", "Warm Sand", "暖沙浅", "Arena", "Песок", "Sand Hell", "Sable", "サンド", "샌드"),
            "midnight" => T("Gece Yarısı", "Midnight", "午夜黑", "Medianoche", "Полночь", "Mitternacht", "Minuit", "ミッドナイト", "미드나잇"),
            "ocean" => T("Okyanus", "Deep Ocean", "深海蓝", "Océano", "Океан", "Ozean", "Océan", "オーシャン", "오션"),
            "sakura" => T("Sakura", "Sakura", "樱花粉", "Sakura", "Сакура", "Sakura", "Sakura", "さくら", "사쿠라"),
            "slate" => T("Buz Grisi", "Frost Slate", "冰霜灰", "Pizarra", "Сланец", "Schiefer Hell", "Ardoise", "スレート", "슬레이트"),
            _ => T("Koyu", "Dark", "深色", "Oscuro", "Тёмная", "Dunkel", "Sombre", "ダーク", "다크")
        };
    }

    public static string BtnCustomTheme => T("+ Özel", "+ Custom", "+ 自定义", "+ Personal", "+ Своя", "+ Eigene", "+ Perso", "+ カスタム", "+ 사용자");
    public static string MenuCreateCustomTheme => T("+ Özel Tema Oluştur", "+ Create Custom Theme", "+ 创建自定义主题", "+ Crear tema", "+ Создать тему", "+ Eigenes Design", "+ Créer un thème", "+ カスタム作成", "+ 새 테마 만들기");
    public static string ThemeStudioTitle => T("Özel Tema Stüdyosu", "Custom Theme Studio", "自定义主题工坊", "Estudio de Temas", "Студия тем", "Theme-Studio", "Studio de Thèmes", "テーマスタジオ", "테마 스튜디오");
    public static string ThemeStudioSub => T("Renklerini seç, canlı önizle ve kaydet", "Pick your colors, preview live, and save", "挑选色彩、实时预览并保存", "Elige colores, previsualiza y guarda", "Выберите цвета, оцените и сохраните", "Farben wählen, live testen und speichern", "Choisissez vos couleurs et enregistrez", "色を選んでライブプレビュー＆保存", "색상을 고르고 실시간 미리보기 후 저장");
    public static string ThemeFieldName => T("Tema Adı", "Theme Name", "主题名称", "Nombre", "Название", "Name", "Nom du thème", "テーマ名", "테마 이름");
    public static string ThemeDefaultCustomName => T("Özel Tema", "Custom Theme", "自定义主题", "Tema propio", "Моя тема", "Eigenes Design", "Thème perso", "マイテーマ", "내 테마");
    public static string ThemeFieldAccent => T("Ana Vurgu Rengi", "Accent Color", "强调色", "Color de acento", "Акцент", "Akzentfarbe", "Couleur d'accent", "アクセントカラー", "강조 색상");
    public static string ThemeFieldBg => T("Arka Plan Rengi", "Background Color", "背景色", "Color de fondo", "Фон окна", "Hintergrund", "Arrière-plan", "背景色", "배경 색상");
    public static string ThemeFieldCard => T("Kart Yüzey Rengi", "Card Surface Color", "卡片背景色", "Color de tarjeta", "Фон карточек", "Kartenfarbe", "Fond des cartes", "カード背景色", "카드 배경색");
    public static string BtnPickColor => T("Seç...", "Pick...", "选色...", "Elegir...", "Выбор...", "Wählen...", "Choisir...", "選択...", "선택...");
    public static string BtnAutoCard => T("Otomatik", "Auto", "自动", "Auto", "Авто", "Auto", "Auto", "自動", "자동");
    public static string ThemePreviewHeading => T("CANLI ÖNİZLEME", "LIVE PREVIEW", "实时预览", "VISTA PREVIA", "ПРЕДПРОСМОТР", "LIVE-VORSCHAU", "APERÇU EN DIRECT", "プレビュー", "미리보기");
    public static string BtnSaveTheme => T("Temayı Kaydet", "Save Theme", "保存主题", "Guardar tema", "Сохранить", "Speichern", "Enregistrer", "保存", "테마 저장");
    public static string BtnDeleteTheme => T("Sil", "Delete", "删除", "Eliminar", "Удалить", "Löschen", "Supprimer", "削除", "삭제");
    public static string BtnEditTheme => T("Düzenle", "Edit", "编辑", "Editar", "Изменить", "Bearbeiten", "Modifier", "編集", "편집");
    public static string BtnCancel => T("İptal", "Cancel", "取消", "Cancelar", "Отмена", "Abbrechen", "Annuler", "キャンセル", "취소");
    public static string MsgCustomThemeSaved(string name) => T(
        $"\"{name}\" teması kaydedildi ve uygulandı.",
        $"Theme \"{name}\" saved and applied.",
        $"主题“{name}”已保存并应用。",
        $"Tema \"{name}\" guardado y aplicado.",
        $"Тема «{name}» сохранена и применена.",
        $"Design \"{name}\" gespeichert und aktiviert.",
        $"Thème « {name} » enregistré et appliqué.",
        $"テーマ「{name}」を保存して適用しました。",
        $"테마 \"{name}\" 저장 및 적용 완료.");
    public static string MsgCustomThemeDeleted(string name) => T(
        $"\"{name}\" özel teması silindi.",
        $"Custom theme \"{name}\" deleted.",
        $"自定义主题“{name}”已删除。",
        $"Tema personalizado \"{name}\" eliminado.",
        $"Тема «{name}» удалена.",
        $"Design \"{name}\" gelöscht.",
        $"Thème « {name} » supprimé.",
        $"テーマ「{name}」を削除しました。",
        $"사용자 테마 \"{name}\" 삭제됨.");

    public static string SecSystemStartup => T("SİSTEM VE BAŞLANGIÇ", "SYSTEM & STARTUP", "系统与启动", "SISTEMA E INICIO", "СИСТЕМА И ЗАПУСК", "SYSTEM & AUTOSTART", "SYSTÈME & DÉMARRAGE", "システムと起動", "시스템 및 시작");
    public static string SetStartupTitle => T("Windows ile Başlat", "Start with Windows", "开机自动启动", "Iniciar con Windows", "Запускать с Windows", "Mit Windows starten", "Démarrer avec Windows", "Windows 起動時に自動起動", "Windows 시작 시 자동 실행");
    public static string SetStartupSub => T(
        "Bilgisayar açıldığında Cueway'i otomatik olarak başlat",
        "Launch Cueway automatically when you sign in to Windows",
        "登录 Windows 时自动启动 Cueway",
        "Iniciar Cueway automáticamente al encender el equipo",
        "Автоматически запускать Cueway при входе в Windows",
        "Cueway beim Anmelden automatisch starten",
        "Lancer Cueway automatiquement à l'ouverture de session",
        "Windows にサインインしたとき Cueway を自動起動します",
        "Windows 로그인 시 Cueway를 자동으로 실행합니다");

    public static string SetStartMinTitle => T("Sistem Tepsisinde Küçültülmüş Başlat", "Start Minimized to System Tray", "启动时最小化到托盘", "Iniciar minimizado en bandeja", "Запускать свёрнутым в трей", "Minimiert im Infobereich starten", "Démarrer réduit dans la zone de notification", "最小化してトレイに起動", "시스템 트레이로 최소화하여 시작");
    public static string SetStartMinSub => T(
        "Uygulama açıldığında pencereyi açmadan doğrudan sistem tepsisinde başlat (--minimized)",
        "Start silently in the system tray without opening the main window (--minimized)",
        "启动时不打开主窗口，直接静默运行在系统托盘中 (--minimized)",
        "Iniciar directamente en la bandeja del sistema sin abrir la ventana (--minimized)",
        "Запускать сразу в системном трее без показа главного окна (--minimized)",
        "Direkt im Infobereich starten, ohne das Hauptfenster anzuzeigen (--minimized)",
        "Démarrer directement dans la zone de notification sans ouvrir la fenêtre (--minimized)",
        "起動時にメイン画面を開かずシステムトレイに常駐させます (--minimized)",
        "시작할 때 기본 창을 열지 않고 시스템 트레이에서 바로 실행합니다 (--minimized)");

    public static string SetMinCloseTitle => T("Kapat (✕) Tuşunda Tepsiye Küçült", "Minimize to Tray on Close (✕)", "点击关闭按钮时最小化到托盘", "Minimizar a bandeja al cerrar (✕)", "Сворачивать в трей при закрытии (✕)", "Beim Schließen (✕) in Infobereich minimieren", "Réduire dans la barre à la fermeture (✕)", "閉じる (✕) でトレイに最小化", "닫기 (✕) 시 트레이로 최소화");
    public static string SetMinCloseSub => T(
        "Kapat düğmesine basıldığında uygulamayı kapatmak yerine sistem tepsisinde çalışmaya devam et",
        "Keep running in the system tray instead of exiting when closing the window",
        "点击窗口关闭按钮时隐藏到系统托盘继续运行，而不是退出程序",
        "Seguir ejecutándose en la bandeja en lugar de cerrar la aplicación",
        "Продолжать работу в трее вместо закрытия программы при нажатии ✕",
        "Beim Schließen im Infobereich weiterlaufen statt zu beenden",
        "Continuer en arrière-plan au lieu de quitter lors de la fermeture",
        "ウィンドウを閉じても終了せずシステムトレイで常駐を続けます",
        "창을 닫을 때 종료하지 않고 시스템 트레이에서 계속 실행합니다");

    public static string SetTopmostTitle => T("Pencereyi Her Zaman Üstte Tut", "Always Keep Window on Top", "窗口始终置顶", "Mantener ventana siempre visible", "Поверх всех окон", "Fenster immer im Vordergrund", "Toujours garder au premier plan", "ウィンドウを常に最前面に表示", "창을 항상 위에 고정");
    public static string SetTopmostSub => T(
        "Kontrol paneli penceresini diğer pencerelerin üzerinde sabit tut",
        "Pin the control panel window above other desktop windows",
        "将控制面板窗口固定显示在其他窗口之上",
        "Mantener la ventana del panel por encima de otras ventanas",
        "Закрепить окно панели управления поверх других окон",
        "Das Bedienfeld über anderen Desktop-Fenstern fixieren",
        "Épingler la fenêtre au-dessus des autres fenêtres",
        "コントロールパネルを常に他のウィンドウより前面に固定します",
        "제어판 창을 다른 창들 위에 항상 고정합니다");

    // Elevation & Task Scheduler Card
    public static string ElevCardTitle => T("Yönetici Yetkisi ve Görev Zamanlayıcı (UIPI)", "Administrator Elevation & Task Scheduler (UIPI)", "管理员权限与计划任务 (UIPI)", "Permisos y Programador de Tareas (UIPI)", "Права администратора и Планировщик (UIPI)", "Administratorrechte & Aufgabenplanung (UIPI)", "Élévation & Planificateur de tâches (UIPI)", "管理者権限とタスクスケジューラ (UIPI)", "관리자 권한 및 작업 스케줄러 (UIPI)");
    public static string ElevCardSub => T(
        "Başlangıç görevini Yönetici İzinli (oyunlar için önerilen) veya Normal yetkili kurun",
        "Register startup task with Elevated (recommended for games) or Normal privileges",
        "可选择管理员权限（推荐）或普通用户权限注册开机登录任务",
        "Crea una tarea de inicio con privilegios elevados o normales",
        "Создаёт задачу автозапуска с правами администратора или обычными правами",
        "Erstellt Anmeldeaufgabe mit erhöhten oder normalen Rechten",
        "Enregistre une tâche d'ouverture avec droits élevés ou normaux",
        "管理者権限（推奨）または通常権限でログオン起動タスクを登録します",
        "관리자 권한(권장) 또는 일반 사용자 권한으로 로그온 시작 작업을 등록합니다");
    public static string BadgeAdmin => T("Yönetici", "Elevated", "管理员", "Admin", "Админ", "Admin", "Admin", "管理者", "관리자");
    public static string BadgeStandard => T("Standart", "Standard", "标准权限", "Estándar", "Обычный", "Standard", "Standard", "標準", "표준");
    public static string BadgeTaskInstalledElevated => T("Görev: Yönetici", "Task: Elevated", "任务：管理员", "Tarea: Admin", "Задача: Админ", "Aufgabe: Admin", "Tâche : Admin", "タスク: 管理者", "작업: 관리자");
    public static string BadgeTaskInstalledNormal => T("Görev: Normal", "Task: Normal", "任务：普通", "Tarea: Normal", "Задача: Обычная", "Aufgabe: Normal", "Tâche : Normal", "タスク: 通常", "작업: 일반");
    public static string BadgeTaskMissing => T("Kayıtlı Değil", "Not Installed", "未注册", "Sin tarea", "Нет задачи", "Nicht erstellt", "Non installée", "未登録", "미등록");
    public static string BtnTaskInstall => T("Görev Kur", "Install Task", "安装任务", "Crear tarea", "Создать задачу", "Aufgabe erstellen", "Créer tâche", "タスク登録", "작업 설치");
    public static string BtnTaskRemove => T("Görevi Kaldır", "Remove Task", "移除任务", "Eliminar tarea", "Удалить задачу", "Aufgabe entfernen", "Supprimer", "タスク削除", "작업 제거");
    public static string BtnTaskBusy => T("İşleniyor...", "Working...", "处理中...", "Procesando...", "Выполнение...", "Läuft...", "En cours...", "処理中...", "처리 중...");
    public static string BtnRestartAdmin => T("Yönetici Başlat", "Run as Admin", "管理员重启", "Abrir como Admin", "От имени админа", "Als Admin starten", "Relancer Admin", "管理者で再起動", "관리자 실행");
    public static string TaskOptElevatedTitle => T("Yönetici İzinli (Önerilen)", "Elevated (Recommended)", "管理员权限（推荐）", "Con permisos elevados (Rec.)", "С правами администратора", "Erhöhte Rechte (Empfohlen)", "Privilèges élevés (Recommandé)", "管理者権限（推奨）", "관리자 권한 (권장)");
    public static string TaskOptElevatedSub => T(
        "Oyun ve yönetici pencerelerinde tuşlar düşmez (tek sefer UAC)",
        "Keeps hotkeys active over games & elevated apps (1x UAC)",
        "在游戏与管理员窗口中热键不失效（创建时需一次 UAC）",
        "Mantiene los atajos activos sobre juegos y apps elevadas",
        "Хуки работают поверх игр и окон от имени администратора",
        "Tasten bleiben in Spielen & Admin-Fenstern aktiv",
        "Garde les raccourcis actifs sur jeux et fenêtres élevées",
        "ゲームや管理者権限アプリ上でも動作します（初回のみ UAC）",
        "게임 및 관리자 창에서도 단축키가 유지됩니다 (최초 1회 UAC)");
    public static string TaskOptNormalTitle => T("Normal Yetkili", "Normal Privilege", "普通用户权限", "Privilegios normales", "Обычные права", "Normale Rechte", "Privilèges normaux", "通常権限", "일반 권한");
    public static string TaskOptNormalSub => T(
        "Standart kullanıcı yetkisiyle başlar (UAC istemez)",
        "Runs with standard user rights (no UAC prompt)",
        "以标准用户权限自动启动（无需 UAC 确认）",
        "Inicia con permisos estándar sin avisos de UAC",
        "Запускается с обычными правами без запросов UAC",
        "Startet mit Standardrechten ohne UAC-Abfrage",
        "Démarre avec les droits standard sans invite UAC",
        "標準ユーザー権限で自動起動します（UAC 不要）",
        "표준 사용자 권한으로 자동 시작합니다 (UAC 없음)");

    // Media & Audio Section
    public static string SecMediaAudio => T("MEDYA VE SES KONTROLÜ", "MEDIA & AUDIO CONTROL", "媒体与音频控制", "CONTROL MULTIMEDIA Y AUDIO", "УПРАВЛЕНИЕ МЕДИА И ЗВУКОМ", "MEDIEN- & AUDIOSTEUERUNG", "CONTRÔLE MÉDIA & AUDIO", "メディアとオーディオ制御", "미디어 및 오디오 제어");
    public static string SetHookEnabledTitle => T("Medya Tuşlarını Yakala", "Capture Media Keys", "接管媒体按键", "Capturar teclas multimedia", "Перехват медиа-клавиш", "Medientasten abfangen", "Capturer les touches média", "メディアキーを捕捉", "미디어 키 캡처");
    public static string SetHookEnabledSub => T(
        "Klavye ve kulaklık medya tuşlarını (Oynat/Duraklat, Sonraki, Önceki) öncelik motoruna yönlendir",
        "Route keyboard and headset media buttons through the priority routing engine",
        "拦截键盘与耳机媒体按键（播放/暂停、上一曲、下一曲）并路由到优先级引擎",
        "Dirigir teclas multimedia del teclado y auriculares al motor de prioridades",
        "Перенаправлять клавиши клавиатуры и гарнитуры через движок приоритетов",
        "Tastatur- und Headset-Medientasten über die Prioritäts-Engine leiten",
        "Router les touches média du clavier et du casque via le moteur de priorité",
        "キーボードやヘッドセットのメディアキーを優先度ルーティングエンジンへ送ります",
        "키보드 및 헤드셋 미디어 버튼을 우선순위 라우팅 엔진으로 전달합니다");

    public static string SetTrayWheelTitle => T("Tepsi Tekerleği ile Uygulama Sesi", "Tray Icon Wheel App Volume", "托盘滚轮调节应用音量", "Volumen con rueda en bandeja", "Громкость цели колёсиком в трее", "App-Lautstärke per Mausrad im Tray", "Volume par molette sur l'icône", "トレイホイールでアプリ音量調整", "트레이 휠로 앱 음량 조절");
    public static string SetTrayWheelSub => T(
        "İmleç tepsi simgesi üzerindeyken fare tekerleğiyle yalnızca aktif hedefin sesini ayarla",
        "Scroll over the tray icon to adjust only the active target application's volume",
        "鼠标悬停在托盘图标上滚动滚轮时，仅调整当前目标应用的音量",
        "Desplace la rueda sobre el icono de la bandeja para ajustar solo el volumen del destino activo",
        "Прокрутка над значком в трее меняет громкость только активного целевого приложения",
        "Über dem Tray-Symbol scrollen, um nur die Lautstärke der aktiven Ziel-App zu regeln",
        "Faites défiler sur l'icône du plateau pour régler uniquement le volume de l'application cible",
        "トレイアイコン上でマウスホイールを回すとアクティブな対象の音量のみを調整します",
        "트레이 아이콘 위에서 마우스 휠을 굴리면 활성 대상 앱의 음량만 조절합니다");

    public static string SetVolStepTitle => T("Tepsi Ses Adımı", "Tray Volume Step", "托盘音量步进", "Paso de volumen", "Шаг громкости", "Lautstärkeschritt", "Pas de volume", "トレイ音量の刻み幅", "트레이 음량 단계");
    public static string SetVolStepSub => T(
        "Tepsi simgesi üzerinde her tekerlek kaydırmasında uygulanacak ses değişim yüzdesi",
        "Volume percentage change per mouse wheel notch over the tray icon",
        "托盘图标上每次滚轮滚动增减的音量百分比",
        "Porcentaje de cambio de volumen por cada giro de rueda sobre el icono",
        "Процент изменения громкости за один щелчок колеса мыши над значком",
        "Prozentuale Lautstärkeänderung pro Mausrad-Schritt über dem Tray-Symbol",
        "Pourcentage de variation du volume par cran de molette",
        "トレイアイコン上でのホイール 1 ノッチあたりの音量変化率",
        "트레이 아이콘 위에서 휠 한 칸당 변경되는 음량 비율");

    public static string SetOsdTitle => T("Ekran Bildirimleri (OSD)", "On-Screen Display (OSD)", "屏幕提示 (OSD)", "Notificaciones en pantalla (OSD)", "Экранные уведомления (OSD)", "Bildschirmanzeige (OSD)", "Notifications à l'écran (OSD)", "オンスクリーン表示 (OSD)", "화면 알림 (OSD)");
    public static string SetOsdSub => T(
        "Parça/video değişiminde veya ses ayarında sağ alt köşede bildirim penceresi göster",
        "Show a compact notification pill in the bottom-right corner on media or volume actions",
        "切歌、调节音量或切换状态时在屏幕右下角显示提示浮窗",
        "Mostrar una notificación compacta en la esquina inferior derecha",
        "Показывать компактное уведомление в правом нижнем углу экрана",
        "Kompakte Benachrichtigung unten rechts bei Medien- oder Lautstärkeaktionen anzeigen",
        "Afficher une notification compacte en bas à droite lors des actions",
        "再生操作や音量変更時に画面右下へコンパクトな通知を表示します",
        "미디어 또는 음량 조작 시 화면 오른쪽 아래에 간단한 알림을 표시합니다");

    public static string SetAutoPauseTitle => T("Arka Plan Oto-Duraklat", "Background Auto-Pause & Resume", "后台媒体自动暂停/恢复", "Pausa automática en segundo plano", "Автопауза и возобновление фона", "Hintergrund-Auto-Pause", "Pause automatique en arrière-plan", "バックグラウンド自動一時停止", "백그라운드 자동 일시정지");
    public static string SetAutoPauseSub => T(
        "Yüksek öncelikli medya çalmaya başladığında arka plandakini duraklat, bitince devam ettir",
        "Pause lower-priority playback when higher-priority media starts, and resume when it stops",
        "高优先级媒体开始播放时自动暂停后台媒体，停止后自动恢复播放",
        "Pausar medios secundarios al iniciar uno prioritario y reanudar al finalizar",
        "Ставить на паузу фоновые медиа при запуске более приоритетных и возобновлять после",
        "Niedrigere Priorität beim Start höherer Medien pausieren und danach fortsetzen",
        "Mettre en pause l'arrière-plan lorsqu'un média prioritaire démarre, puis reprendre",
        "高優先度の再生が始まると低優先度の再生を一時停止し、終了後に再開します",
        "높은 우선순위 미디어가 재생되면 배경 미디어를 일시정지하고 종료 시 다시 재생합니다");

    public static string SetHeadphoneTitle => T("Kulaklık Kopma Koruması", "Headphone Disconnect Guard", "耳机断开紧急暂停", "Protección al desconectar auriculares", "Защита при отключении наушников", "Kopfhörer-Trennschutz", "Protection déconnexion casque", "ヘッドホン切断ガード", "헤드폰 연결 해제 보호");
    public static string SetHeadphoneSub => T(
        "Varsayılan ses çıkış cihazı değiştiğinde veya bağlantısı koptuğunda tüm medyayı duraklat",
        "Immediately pause all playing sessions when the default audio output device disconnects or changes",
        "默认音频输出设备断开或切换时，立即暂停所有正在播放的媒体",
        "Pausar toda reproducción al desconectar o cambiar la salida de audio",
        "Экстренно ставить всё на паузу при отключении или смене устройства вывода",
        "Alle aktiven Medien pausieren, sobald sich das Standard-Audiogerät ändert",
        "Mettre en pause tous les médias si le périphérique audio par défaut change",
        "既定のオーディオ出力デバイスが切断・変更された際、再生中メディアを即座に一時停止します",
        "기본 오디오 출력 장치가 끊기거나 변경되면 재생 중인 모든 세션을 즉시 일시정지합니다");

    public static string SetSoloTitle => T("Solo Modu", "Solo Mode", "独奏模式", "Modo Solo", "Режим Соло", "Solo-Modus", "Mode Solo", "ソロモード", "솔로 모드");
    public static string SetSoloSub => T(
        "Aktif hedef dışındaki tüm açık uygulamaların sesini geçici olarak sustur",
        "Temporarily mute all running applications except the active target",
        "临时静音除当前活动目标以外的所有应用程序音频",
        "Silenciar temporalmente todas las aplicaciones excepto el destino activo",
        "Временно заглушить все приложения, кроме активной цели",
        "Alle anderen laufenden Anwendungen außer dem aktiven Ziel stummschalten",
        "Couper temporairement le son de toutes les applications sauf la cible active",
        "アクティブな対象以外の全アプリの音声を一時的にミュートします",
        "활성 대상을 제외한 모든 실행 중 앱의 소리를 일시적으로 음소거합니다");

    public static string SetGameGuardTitle => T("Tam Ekran Oyun Koruması", "Fullscreen Game Guard", "全屏游戏保护", "Protección de juegos en pantalla completa", "Защита в полноэкранных играх", "Vollbild-Spielschutz", "Protection jeux plein écran", "フルスクリーンゲーム保護", "전체 화면 게임 보호");
    public static string SetGameGuardSub => T(
        "Ön planda tam ekran bir oyun açıkken tarayıcıları atlayıp doğrudan müzik çalara odaklan",
        "Skip browsers and prefer dedicated music players while a fullscreen game is active",
        "检测到全屏游戏处于前台时自动跳过浏览器，优先控制音乐播放器",
        "Omitir navegadores y priorizar reproductores de música en juegos a pantalla completa",
        "Игнорировать браузеры и фокусироваться на музыкальном плеере во время полноэкранных игр",
        "Browser überspringen und Musikplayer bevorzugen, wenn ein Vollbildspiel läuft",
        "Ignorer les navigateurs et privilégier les lecteurs audio pendant un jeu plein écran",
        "フルスクリーンゲーム中はブラウザをスキップし音楽プレーヤーを優先します",
        "전체 화면 게임 실행 중에는 브라우저를 건너뛰고 음악 플레이어를 우선합니다");

    // Timeline Scrubbing Section
    public static string SecScrubbing => T("ZAMAN ÇİZELGESİ SARMA (TIMELINE SCRUBBING)", "TIMELINE SCRUBBING", "时间轴快进/快退", "AVANCE DE LÍNEA DE TIEMPO", "ПЕРЕМОТКА ВИДЕО (TIMELINE SCRUBBING)", "ZEITLEISTEN-SPULEN", "AVANCE / RETOUR RAPIDE", "タイムラインシーク", "타임라인 탐색");
    public static string SetScrubEnabledTitle => T("Video İleri / Geri Sarma", "Video Seek / Scrubbing", "视频快进/快退控制", "Avance/retroceso en video", "Перемотка видео вперёд/назад", "Video Vor-/Zurückspulen", "Avance/retour vidéo", "動画シーク操作", "비디오 앞/뒤 탐색");
    public static string SetScrubEnabledSub => T(
        "Video hedeflerde Next/Prev tuşuna basılı tutunca veya çift basınca parça atlamak yerine sar",
        "Long-press or double-tap Next/Prev on video targets to seek instead of skipping tracks",
        "在视频目标上长按或双击上一曲/下一曲时进行快进/快退，而非切换视频",
        "Mantenga pulsado o pulse dos veces Next/Prev en videos para avanzar o retroceder",
        "Удержание или двойное нажатие Next/Prev в видео перематывает вместо переключения",
        "Langes Drücken oder Doppeltippen auf Weiter/Zurück spult in Videos statt zu springen",
        "Appui long ou double appui sur Suivant/Précédent pour avancer/reculer dans les vidéos",
        "動画対象で「次/前」を長押し・2回押しするとスキップせず早送り/巻き戻しします",
        "비디오 대상에서 다음/이전 키를 길게 누르거나 두 번 누르면 건너뛰기 대신 탐색합니다");

    public static string SetSeekSecondsTitle => T("İleri / Geri Sarma Süresi", "Seek Step Duration", "每次快进/快退秒数", "Duración de salto", "Шаг перемотки", "Spuldauer", "Durée de saut", "シーク秒数", "탐색 간격 시간");
    public static string SetSeekSecondsSub => T(
        "Video oynatıcılarda Next/Prev tuşuna basılı tutulduğunda veya çift basıldığında sarılacak süre",
        "Number of seconds to seek forward or backward per scrub action",
        "每次长按或双击快进/快退跳跃的秒数",
        "Segundos a avanzar o retroceder en cada salto",
        "Количество секунд для перемотки вперёд или назад",
        "Sekunden pro Spulschritt vorwärts oder rückwärts",
        "Secondes d'avance ou de retour rapide",
        "シーク操作 1 回あたりの早送り・巻き戻し秒数",
        "탐색 동작 1회당 앞/뒤로 이동할 시간(초)");

    public static string SetLongPressTitle => T("Uzun Basış Eşiği", "Long-Press Threshold", "长按触发阈值", "Umbral de pulsación larga", "Порог долгого нажатия", "Schwellenwert für langes Drücken", "Seuil d'appui long", "長押し判定時間", "길게 누르기 기준 시간");
    public static string SetLongPressSub => T(
        "Parça atlamak yerine sarma moduna geçmek için gereken basılı tutma süresi",
        "Hold duration required to enter seek mode instead of skipping tracks",
        "从切换曲目转为触发时间轴快进/快退所需的按住毫秒数",
        "Tiempo necesario para activar el modo de avance al mantener pulsado",
        "Длительность удержания клавиши для перехода в режим перемотки",
        "Haltedauer, um in den Spulmodus statt Titelwechsel zu wechseln",
        "Durée de maintien requise pour activer la recherche rapide",
        "トラック切替ではなくシークモードに入るまでの長押しミリ秒数",
        "트랙 건너뛰기 대신 탐색 모드로 전환되는 데 필요한 누름 유지 시간");

    public static string UnitSeconds => T("sn", "s", "秒", "s", "с", "s", "s", "秒", "초");

    // Shortcuts Section & Hotkey Capture
    public static string SecGlobalHotkeys => T("KISAYOLLAR", "KEYBOARD SHORTCUTS", "快捷键设置", "ATAJOS DE TECLADO", "ГОРЯЧИЕ КЛАВИШИ", "TASTENKÜRZEL", "RACCOURCIS CLAVIER", "ショートカット", "단축키 설정");
    public static string SetHkToggleTitle => T("Cueway Penceresini Aç / Gizle", "Show / Hide Cueway Window", "打开 / 隐藏主窗口", "Mostrar u ocultar Cueway", "Показать / скрыть окно", "Fenster anzeigen / ausblenden", "Afficher / masquer la fenêtre", "ウィンドウを表示 / 非表示", "Cueway 창 열기 / 숨기기");
    public static string SetHkToggleSub => T(
        "Herhangi bir pencereden Cueway kontrol panelini hızlıca açar veya küçültür",
        "Quickly bring up or hide the Cueway window from anywhere",
        "从任意应用全局呼出或隐藏 Cueway 控制面板",
        "Abre o minimiza la ventana de Cueway desde cualquier lugar",
        "Быстрый вызов или скрытие окна Cueway из любого приложения",
        "Öffnet oder versteckt Cueway schnell aus jeder Anwendung",
        "Affiche ou masque rapidement Cueway depuis n'importe où",
        "どこからでも Cueway ウィンドウを素早く開閉します",
        "어디서나 Cueway 창을 빠르게 열거나 숨깁니다");
    public static string SetHkLockTitle => T("Manuel Kilit Aç / Kapat", "Toggle Manual Lock", "切换手动锁定", "Alternar bloqueo manual", "Ручной замок", "Manuelle Sperre umschalten", "Basculer le verrou manuel", "手動ロック切替", "수동 잠금 전환");
    public static string SetHkLockSub => T(
        "Medya tuşlarını seçili hedefe kilitlemek veya serbest bırakmak için kısayol",
        "Lock or unlock media routing to the current active target",
        "将媒体控制锁定到当前目标或恢复自动优先级",
        "Fija o libera el destino multimedia activo",
        "Закрепляет или снимает блокировку текущей медиацели",
        "Sperrt oder entsperrt das aktuelle aktive Medienziel",
        "Verrouille ou déverrouille la cible média active",
        "現在の対象への手動ロックを切り替えます",
        "현재 활성 미디어 대상에 대한 수동 잠금을 전환합니다");
    public static string SetHkSoloTitle => T("Solo Modunu Aç / Kapat", "Toggle Solo Mode", "切换独奏模式", "Alternar modo Solo", "Режим Соло", "Solo-Modus umschalten", "Basculer le mode Solo", "ソロモード切替", "솔로 모드 전환");
    public static string SetHkSoloSub => T(
        "Aktif hedef dışındaki tüm açık uygulamaların sesini susturur veya geri açar",
        "Mute or restore audio for all running apps except the active target",
        "一键静音或恢复除当前目标外的所有应用程序音量",
        "Silencia o restaura el audio de todas las apps excepto el destino activo",
        "Отключает или возвращает звук всем приложениям, кроме активной цели",
        "Schaltet alle Apps außer dem aktiven Ziel stumm oder stellt sie wieder her",
        "Coupe ou rétablit le son de toutes les apps sauf la cible active",
        "対象以外のすべてのアプリの音をミュートまたは解除します",
        "활성 대상 외의 모든 실행 중 앱 소리를 끄거나 켭니다");
    public static string SetHkPlayPauseTitle => T("Oynat / Duraklat Kısayolu", "Play / Pause Shortcut", "播放 / 暂停快捷键", "Atajo Reproducir / Pausar", "Воспр. / Пауза", "Wiedergabe / Pause-Kürzel", "Raccourci Lecture / Pause", "再生 / 一時停止ショートカット", "재생 / 일시정지 단축키");
    public static string SetHkPlayPauseSub => T(
        "Medya tuşu olmayan klavyeler için tek tuş (ör. End, F8, P) veya kombinasyon atayın",
        "Assign any single key (e.g. End, F8, P) or combo for keyboards without media keys",
        "为没有媒体键的键盘分配单个按键（如 End、F8、P）或组合键",
        "Asigna cualquier tecla (ej. End, F8, P) o combinación sin teclas multimedia",
        "Назначьте любую клавишу (напр. End, F8, P) или сочетание для клавиатур без медиа-кнопок",
        "Beliebige Einzeltaste (z. B. Ende, F8, P) oder Kombination zuweisen",
        "Attribuez une touche unique (ex. Fin, F8, P) ou une combinaison",
        "メディアキーがないキーボード向けに単一キー（End、F8、Pなど）または組み合わせを設定",
        "미디어 키가 없는 키보드용 단일 키(예: End, F8, P) 또는 조합 지정");
    public static string SetHkPromotePlayingTitle => T(
        "Oynayan Uygulamayı Önceliklendir",
        "Prioritize Playing App",
        "优先当前播放应用",
        "Priorizar app en reproducción",
        "Приоритет для играющего приложения",
        "Spielende App priorisieren",
        "Prioriser l'application en lecture",
        "再生中のアプリを最優先にする",
        "재생 중인 앱 우선순위 지정");
    public static string SetHkPromotePlayingSub => T(
        "O anda çalan uygulamayı veya sekmeyi öncelik sırasının en üstüne alır",
        "Moves the currently playing app or tab to top priority",
        "将当前正在播放的应用或标签页移至最高优先级",
        "Mueve la app o pestaña en reproducción a la máxima prioridad",
        "Перемещает играющее приложение или вкладку в начало списка приоритетов",
        "Setzt die aktuell spielende App oder den Tab auf höchste Priorität",
        "Place l'application ou l'onglet en cours de lecture en tête des priorités",
        "現在再生中のアプリやタブを最優先にします",
        "현재 재생 중인 앱이나 탭을 최우선 순위로 올립니다");
    public static string HotkeyAssignShort => T("+ Kısayol", "+ Hotkey", "+ 快捷键", "+ Atajo", "+ Клавиша", "+ Kürzel", "+ Raccourci", "+ キー設定", "+ 단축키");
    public static string HotkeyUnassigned => T("Atanmadı", "Not set", "未设置", "Sin asignar", "Не задано", "Nicht belegt", "Non défini", "未設定", "미지정");
    public static string HotkeyListening => T("Tuşa basın...", "Press keys...", "请按键...", "Pulsa teclas...", "Нажмите...", "Taste drücken...", "Appuyez...", "キー入力待ち...", "키를 누르세요...");
    public static string HotkeyHint => T(
        "Kısayola tıklayıp tuş kombinasyonuna basın (örn. Ctrl+Alt+Y)  •  Esc: İptal  •  Backspace / ×: Temizle",
        "Click a shortcut pill and press any combo (e.g. Ctrl+Alt+Y)  •  Esc: Cancel  •  Backspace / ×: Clear",
        "点击快捷键按钮并按下组合键（例如 Ctrl+Alt+Y） •  Esc：取消  •  Backspace / ×：清除",
        "Haz clic y pulsa una combinación (ej. Ctrl+Alt+Y)  •  Esc: Cancelar  •  Backspace / ×: Borrar",
        "Нажмите на кнопку и введите сочетание (напр. Ctrl+Alt+Y)  •  Esc: Отмена  •  Backspace / ×: Сброс",
        "Feld anklicken und Kombination drücken (z. B. Strg+Alt+Y)  •  Esc: Abbruch  •  Backspace / ×: Leeren",
        "Cliquez puis appuyez sur une combinaison (ex. Ctrl+Alt+Y)  •  Échap : Annuler  •  Retour / × : Effacer",
        "ボタンをクリックしてキーを押してください（例: Ctrl+Alt+Y） •  Esc: 取消  •  Backspace / ×: 解除",
        "단축키를 클릭하고 키 조합을 누르세요 (예: Ctrl+Alt+Y)  •  Esc: 취소  •  Backspace / ×: 지우기");
    public static string MsgHotkeyNeedsModifier => T(
        "Harf/sayı tuşları için Ctrl, Alt, Shift veya Win ile birlikte basın (F1-F12 ve Numpad tek basılabilir).",
        "Combine letters or numbers with Ctrl, Alt, Shift, or Win (F1-F12 and Numpad work alone).",
        "字母和数字键需搭配 Ctrl、Alt、Shift 或 Win 使用（F1-F12 与小键盘可单独使用）。",
        "Combina letras o números con Ctrl, Alt, Shift o Win (F1-F12 y Numpad funcionan solos).",
        "Буквы и цифры используйте с Ctrl, Alt, Shift или Win (F1-F12 и Numpad работают отдельно).",
        "Buchstaben/Zahlen mit Ctrl, Alt, Shift oder Win kombinieren (F1-F12 & Numpad gehen einzeln).",
        "Associez lettres/chiffres à Ctrl, Alt, Shift ou Win (F1-F12 et pavé numérique seuls).",
        "文字・数字キーは Ctrl/Alt/Shift/Win と組み合わせてください（F1〜F12 とテンキーは単体可）。",
        "문자/숫자 키는 Ctrl, Alt, Shift 또는 Win과 함께 누르세요 (F1-F12 및 숫자 패드 단독 가능).");

    // Tools & Shortcuts
    public static string SecToolsShortcuts => T("ARAÇLAR, KISAYOLLAR VE DURUM DOSYASI", "TOOLS, SHORTCUTS & STATE FILE", "工具、快捷键与状态文件", "HERRAMIENTAS, ATAJOS Y ESTADO", "ИНСТРУМЕНТЫ, КЛАВИШИ И ФАЙЛ СОСТОЯНИЯ", "WERKZEUGE, KÜRZEL & STATUSDATEI", "OUTILS, RACCOURCIS & ÉTAT", "ツール・ショートカット・設定ファイル", "도구, 단축키 및 상태 파일");
    public static string BtnRehook => T("Kancayı Yenile", "Refresh Hooks", "刷新钩子", "Refrescar ganchos", "Обновить хуки", "Hooks erneuern", "Actualiser hooks", "フック更新", "후크 새로고침");
    public static string BtnOpenConfigFolder => T("Ayar Klasörünü Aç", "Open Config Folder", "打开配置目录", "Abrir carpeta", "Папка настроек", "Konfig-Ordner öffnen", "Ouvrir le dossier", "設定フォルダを開く", "설정 폴더 열기");
    public static string BtnResetDefaults => T("Varsayılanlara Sıfırla", "Reset to Defaults", "恢复默认设置", "Restablecer ajustes", "Сбросить настройки", "Standardwerte", "Réinitialiser", "初期設定に戻す", "기본값 재설정");

    public static string ShortcutSecondary => T(
        "Ctrl / Shift + Medya Tuşu  →  Önceliği bozmadan 2. sıradaki oturuma tek seferlik komut gönderir",
        "Ctrl / Shift + Media Key  →  Sends a one-shot command to the 2nd target without changing priority",
        "Ctrl / Shift + 媒体键  →  不改变当前优先级，直接向第 2 顺位会话发送单次控制指令",
        "Ctrl / Shift + Tecla  →  Envía un comando directo a la 2.ª sesión sin cambiar la prioridad",
        "Ctrl / Shift + Медиа  →  Разовая команда 2-й сессии без смены порядка приоритета",
        "Strg / Umschalt + Medientaste  →  Einmaliger Befehl an das 2. Ziel ohne Prioritätswechsel",
        "Ctrl / Maj + Touche média  →  Commande directe à la 2e cible sans changer la priorité",
        "Ctrl / Shift + メディアキー  →  優先順を変えずに 2 番目の対象へ単発コマンド送信",
        "Ctrl / Shift + 미디어 키  →  우선순위 변경 없이 2순위 세션에 일회성 명령 전송");

    public static string FormatShortcutToggles(string toggleHk, string lockHk, string soloHk)
    {
        string none = HotkeyUnassigned;
        string t = string.IsNullOrEmpty(toggleHk) ? none : toggleHk;
        string l = string.IsNullOrEmpty(lockHk) ? none : lockHk;
        string s = string.IsNullOrEmpty(soloHk) ? none : soloHk;
        return Current switch
        {
            AppLanguage.En => $"{t} → Window   •   {l} → Manual Lock   •   {s} → Solo Mode",
            AppLanguage.Zh => $"{t} → 窗口   •   {l} → 手动锁定   •   {s} → 独奏模式",
            AppLanguage.Es => $"{t} → Ventana   •   {l} → Bloqueo   •   {s} → Modo Solo",
            AppLanguage.Ru => $"{t} → Окно   •   {l} → Замок   •   {s} → Режим Соло",
            AppLanguage.De => $"{t} → Fenster   •   {l} → Sperre   •   {s} → Solo-Modus",
            AppLanguage.Fr => $"{t} → Fenêtre   •   {l} → Verrou   •   {s} → Mode Solo",
            AppLanguage.Ja => $"{t} → 画面   •   {l} → ロック   •   {s} → ソロ",
            AppLanguage.Ko => $"{t} → 창 전환   •   {l} → 수동 잠금   •   {s} → 솔로 모드",
            _ => $"{t} → Pencere   •   {l} → Manuel Kilit   •   {s} → Solo Modu"
        };
    }

    public static string ShortcutTrayWheel => T(
        "Tepsi Simgesi Üzerinde Scroll  →  Yalnızca aktif hedef uygulamanın ses seviyesini değiştirir",
        "Scroll Over Tray Icon  →  Adjusts volume of the active target application only",
        "托盘图标上滚动滚轮  →  仅调节当前活动目标应用的音量",
        "Rueda sobre icono en bandeja  →  Ajusta solo el volumen de la aplicación objetivo activa",
        "Колесо над значком в трее  →  Меняет громкость только активного целевого приложения",
        "Mausrad über Tray-Symbol  →  Regelt nur die Lautstärke der aktiven Ziel-App",
        "Molette sur l'icône du plateau  →  Règle uniquement le volume de la cible active",
        "トレイアイコン上でホイール  →  アクティブな対象アプリの音量のみを変更",
        "트레이 아이콘 위 스크롤  →  활성 대상 애플리케이션의 음량만 조절");

    public static string StateFileLabel => T("Durum Dosyası", "State File", "状态文件", "Archivo de estado", "Файл состояния", "Statusdatei", "Fichier d'état", "状態ファイル", "상태 파일");

    // Tray Menu & Tooltips
    public static string TrayMenuOpen => T("Kontrol Panelini Aç", "Open Control Panel", "打开控制面板", "Abrir panel de control", "Открыть панель управления", "Bedienfeld öffnen", "Ouvrir le panneau", "コントロールパネルを開く", "제어판 열기");
    public static string TrayMenuLockBase => T("Manuel Kilit", "Manual Lock", "手动锁定", "Bloqueo manual", "Ручной замок", "Manuelle Sperre", "Verrou manuel", "手動ロック", "수동 잠금");
    public static string TrayMenuSoloBase => T("Solo Modu", "Solo Mode", "独奏模式", "Modo Solo", "Режим Соло", "Solo-Modus", "Mode Solo", "ソロモード", "솔로 모드");
    public static string TrayMenuSettings => T("Ayarlar", "Settings", "设置", "Ajustes", "Настройки", "Einstellungen", "Paramètres", "設定", "설정");
    public static string TrayMenuExit => T("Çıkış", "Exit", "退出", "Salir", "Выход", "Beenden", "Quitter", "終了", "종료");
    public static string TrayTipEmpty => T("Cueway — Aktif medya yok", "Cueway — No active media", "Cueway — 无活动媒体", "Cueway — Sin medios activos", "Cueway — Нет активных медиа", "Cueway — Keine aktiven Medien", "Cueway — Aucun média actif", "Cueway — アクティブなメディアなし", "Cueway — 활성 미디어 없음");

    // Feedback & OSD Messages
    public static string MsgHooksRefreshed => T("Klavye ve tepsi fare kancaları yenilendi.", "Keyboard and tray mouse hooks refreshed.", "键盘与托盘鼠标钩子已刷新。", "Ganchos de teclado y ratón actualizados.", "Хуки клавиатуры и мыши обновлены.", "Tastatur- und Maus-Hooks erneuert.", "Hooks clavier et souris actualisés.", "キーボードおよびトレイマウスフックを更新しました。", "키보드 및 트레이 마우스 후크가 새로고침되었습니다.");
    public static string MsgSettingsReset => T("Ayarlar varsayılan değerlere sıfırlandı (öncelik listesi korundu).", "Settings reset to defaults (priority list preserved).", "设置已恢复默认值（优先级列表已保留）。", "Ajustes restablecidos (lista de prioridad conservada).", "Настройки сброшены (список приоритетов сохранён).", "Einstellungen zurückgesetzt (Prioritätsliste behalten).", "Paramètres réinitialisés (liste de priorité conservée).", "設定を初期値に戻しました（優先順位リストは保持）。", "설정이 기본값으로 초기화되었습니다 (우선순위 목록 유지).");
    public static string MsgStartupOn => T("Otomatik başlatma etkinleştirildi.", "Startup enabled.", "已启用开机自启。", "Inicio automático activado.", "Автозапуск включён.", "Autostart aktiviert.", "Démarrage automatique activé.", "自動起動を有効にしました。", "자동 시작이 활성화되었습니다.");
    public static string MsgStartupOff => T("Otomatik başlatma devre dışı bırakıldı.", "Startup disabled.", "已禁用开机自启。", "Inicio automático desactivado.", "Автозапуск отключён.", "Autostart deaktiviert.", "Démarrage automatique désactivé.", "自動起動を無効にしました。", "자동 시작이 비활성화되었습니다.");
    public static string MsgStartupFail => T("Başlangıç ayarı güncellenemedi.", "Failed to update startup setting.", "无法更新开机启动设置。", "No se pudo actualizar el inicio.", "Не удалось обновить автозапуск.", "Autostart konnte nicht geändert werden.", "Impossible de modifier le démarrage.", "自動起動の設定を更新できませんでした。", "시작 설정을 업데이트하지 못했습니다.");
    public static string MsgRestartAdminCancelled => T("Yönetici olarak yeniden başlatma iptal edildi.", "Restart as Administrator cancelled.", "已取消管理员权限重启。", "Reinicio como administrador cancelado.", "Перезапуск с правами администратора отменён.", "Neustart als Administrator abgebrochen.", "Redémarrage administrateur annulé.", "管理者としての再起動をキャンセルしました。", "관리자 권한 재시작이 취소되었습니다.");

    public static string MsgTaskExeMissing => T("Uygulama yolu bulunamadı.", "Executable path not found.", "未找到程序路径。", "No se encontró el ejecutable.", "Путь к файлу не найден.", "Programmpfad nicht gefunden.", "Chemin de l'exécutable introuvable.", "実行ファイルパスが見つかりません。", "실행 파일 경로를 찾을 수 없습니다.");
    public static string MsgTaskInstalledElevated => T("Yönetici izinli başlangıç görevi kaydedildi.", "Elevated logon task installed.", "管理员权限开机任务已创建。", "Tarea elevada registrada.", "Задача с правами администратора создана.", "Aufgabe mit erhöhten Rechten installiert.", "Tâche élevée créée.", "管理者権限のログオンタスクを登録しました。", "관리자 권한 로그온 작업이 등록되었습니다.");
    public static string MsgTaskInstalledNormal => T("Normal yetkili başlangıç görevi kaydedildi.", "Normal-privilege logon task installed.", "普通权限开机任务已创建。", "Tarea estándar registrada.", "Задача с обычными правами создана.", "Aufgabe mit normalen Rechten installiert.", "Tâche normale créée.", "通常権限のログオンタスクを登録しました。", "일반 권한 로그온 작업이 등록되었습니다.");
    public static string MsgTaskInstallFail => T("Görev kaydedilemedi veya UAC iptal edildi.", "Task installation failed or UAC cancelled.", "创建任务失败或已取消 UAC 授权。", "Error al crear la tarea o UAC cancelado.", "Не удалось создать задачу или UAC отменён.", "Aufgabenerstellung fehlgeschlagen oder abgebrochen.", "Échec de création ou UAC annulé.", "タスクの登録に失敗または UAC がキャンセルされました。", "작업을 설치하지 못했거나 UAC가 취소되었습니다.");
    public static string MsgTaskInstallFailNormal => T("Normal başlangıç görevi kaydedilemedi.", "Failed to install normal logon task.", "无法创建普通权限开机任务。", "No se pudo crear la tarea estándar.", "Не удалось создать обычную задачу.", "Normale Aufgabe konnte nicht erstellt werden.", "Impossible de créer la tâche normale.", "通常タスクの登録に失敗しました。", "일반 작업을 등록하지 못했습니다.");
    public static string MsgTaskRemoved => T("Görev Zamanlayıcısı kaydı kaldırıldı.", "Scheduled task removed.", "计划任务已移除。", "Tarea programada eliminada.", "Задача планировщика удалена.", "Geplante Aufgabe entfernt.", "Tâche planifiée supprimée.", "スケジュールタスクを削除しました。", "예약된 작업이 제거되었습니다.");
    public static string MsgTaskRemoveFail => T("Görev kaldırılamadı.", "Failed to remove scheduled task.", "无法移除计划任务。", "No se pudo eliminar la tarea.", "Не удалось удалить задачу.", "Aufgabe konnte nicht entfernt werden.", "Impossible de supprimer la tâche.", "タスクを削除できませんでした。", "작업을 제거하지 못했습니다.");
    public static string MsgTaskCancelled => T("Görev kaldırma işlemi iptal edildi.", "Task removal cancelled.", "已取消移除任务。", "Eliminación cancelada.", "Удаление задачи отменено.", "Entfernen abgebrochen.", "Suppression annulée.", "タスク削除をキャンセルしました。", "작업 제거가 취소되었습니다.");

    // Companion Browser Extension Bridge
    public static string SecBrowserBridge => T(
        "TARAYICI EKLENTİ KÖPRÜSÜ (SEKMELERİ AYRI YÖNET)",
        "BROWSER EXTENSION BRIDGE (INDEPENDENT TABS)",
        "浏览器扩展桥接（独立控制标签页）",
        "PUENTE DE EXTENSIÓN (PESTAÑAS INDEPENDIENTES)",
        "МОСТ РАСШИРЕНИЯ БРАУЗЕРА (НЕЗАВИСИМЫЕ ВКЛАДКИ)",
        "BROWSER-ERWEITERUNGSBRÜCKE (EIGENE TABS)",
        "PONT D'EXTENSION NAVIGATEUR (ONGLETS INDÉPENDANTS)",
        "ブラウザ拡張ブリッジ（タブ個別制御）",
        "브라우저 확장 브리지 (개별 탭 제어)");
    public static string BridgeCardTitle => T(
        "Cueway Companion Eklentisi",
        "Cueway Companion Extension",
        "Cueway Companion 扩展",
        "Extensión Cueway Companion",
        "Расширение Cueway Companion",
        "Cueway Companion-Erweiterung",
        "Extension Cueway Companion",
        "Cueway Companion 拡張機能",
        "Cueway Companion 확장 프로그램");
    public static string BridgeCardSub => T(
        "Aynı tarayıcıdaki normal sekmeleri (YouTube, YouTube Music, X vb.) odak çalmadan bağımsız kontrol eder",
        "Controls normal tabs in the same browser independently without stealing focus",
        "在同一浏览器中独立控制各媒体标签页（YouTube、YouTube Music、X 等），不抢占窗口焦点",
        "Controla pestañas normales del mismo navegador de forma independiente sin robar el foco",
        "Управляет обычными вкладками одного браузера по отдельности без перехвата фокуса",
        "Steuert normale Tabs im selben Browser unabhängig und ohne Fokusverlust",
        "Contrôle chaque onglet média d'un même navigateur indépendamment sans voler le focus",
        "同一ブラウザ内の通常タブ（YouTube・YouTube Music・X 等）をフォーカスを奪わず個別に制御します",
        "같은 브라우저의 일반 탭(YouTube, YouTube Music, X 등)을 포커스를 뺏지 않고 독립적으로 제어합니다");
    public static string BadgeBridgeConnected(int browsers, int tabs) => Current switch
    {
        AppLanguage.En => $"Connected ({browsers} • {tabs} tabs)",
        AppLanguage.Zh => $"已连接 ({browsers} 浏览器 • {tabs} 标签页)",
        AppLanguage.Es => $"Conectado ({browsers} • {tabs} pestañas)",
        AppLanguage.Ru => $"Подкл. ({browsers} • {tabs} вкл.)",
        AppLanguage.De => $"Verbunden ({browsers} • {tabs} Tabs)",
        AppLanguage.Fr => $"Connecté ({browsers} • {tabs} onglets)",
        AppLanguage.Ja => $"接続済み ({browsers} • {tabs} タブ)",
        AppLanguage.Ko => $"연결됨 ({browsers} • {tabs}개 탭)",
        _ => $"Bağlı ({browsers} tarayıcı • {tabs} sekme)"
    };
    public static string BadgeBridgeDisconnected => T("Bağlı Değil", "Not Connected", "未连接", "Desconectado", "Не подключено", "Nicht verbunden", "Non connecté", "未接続", "연결 안 됨");
    public static string BtnInstallEdge => T("Edge (Mağaza)", "Edge Add-ons", "Edge 商店", "Edge Add-ons", "Edge Add-ons", "Edge Add-ons", "Edge Add-ons", "Edge アドオン", "Edge 추가 기능");
    public static string BtnInstallChromium => T("Chrome Kur", "Install Chrome", "安装 Chrome", "Instalar Chrome", "Уст. Chrome", "Chrome", "Installer Chrome", "Chrome 導入", "Chrome 설치");
    public static string BtnInstallFirefox => T("Firefox Kur", "Install Firefox", "安装 Firefox", "Instalar Firefox", "Уст. Firefox", "Firefox", "Installer Firefox", "Firefox 導入", "Firefox 설치");
    public static string BtnOpenExtFolder => T("Klasörü Aç", "Open Folder", "打开目录", "Abrir carpeta", "Папка", "Ordner öffnen", "Ouvrir dossier", "フォルダを開く", "폴더 열기");
    public static string MsgBridgeEdgeStoreOpened => T(
        "Microsoft Edge Add-ons sayfası açıldı — \"Al\" butonuna tıklayıp eklentiyi kurun.",
        "Microsoft Edge Add-ons page opened — click \"Get\" to install the extension.",
        "已打开 Microsoft Edge 加载项页面 — 点击“获取”即可安装扩展。",
        "Se abrió Microsoft Edge Add-ons — haz clic en \"Obtener\" para instalar.",
        "Открыта страница Microsoft Edge Add-ons — нажмите «Получить» для установки.",
        "Microsoft Edge Add-ons geöffnet — klicken Sie auf „Abrufen“ zum Installieren.",
        "Page Microsoft Edge Add-ons ouverte — cliquez sur « Obtenir » pour installer.",
        "Microsoft Edge アドオンページを開きました —「入手」をクリックしてインストールしてください。",
        "Microsoft Edge 추가 기능 페이지가 열렸습니다 — '받기'를 클릭해 설치하세요.");
    public static string MsgBridgeEdgeStoreFailed => T(
        "Edge mağaza sayfası açılamadı.",
        "Failed to open the Edge Add-ons page.",
        "无法打开 Edge 商店页面。",
        "No se pudo abrir la página de Edge Add-ons.",
        "Не удалось открыть страницу Edge Add-ons.",
        "Edge Add-ons-Seite konnte nicht geöffnet werden.",
        "Impossible d'ouvrir la page Edge Add-ons.",
        "Edge アドオンページを開けませんでした。",
        "Edge 추가 기능 페이지를 열지 못했습니다.");
    public static string MsgBridgeChromiumOpened(string browserName) => Current switch
    {
        AppLanguage.En => $"Extension path copied to clipboard & {browserName} opened: enable Developer mode → Load unpacked → Ctrl+V.",
        AppLanguage.Zh => $"扩展路径已复制到剪贴板并已打开 {browserName}：开启开发者模式 → 加载已解压的扩展 → Ctrl+V。",
        AppLanguage.Es => $"Ruta copiada al portapapeles y {browserName} abierto: active Modo desarrollador → Cargar descomprimida → Ctrl+V.",
        AppLanguage.Ru => $"Путь скопирован и открыт {browserName}: включите режим разработчика → Загрузить распакованное → Ctrl+V.",
        AppLanguage.De => $"Pfad kopiert & {browserName} geöffnet: Entwicklermodus an → Entpackte laden → Strg+V.",
        AppLanguage.Fr => $"Chemin copié et {browserName} ouvert : Mode développeur → Charger l'extension non empaquetée → Ctrl+V.",
        AppLanguage.Ja => $"パスをコピーして {browserName} を開きました：デベロッパーモード → パッケージ化されていない拡張機能を読み込む → Ctrl+V。",
        AppLanguage.Ko => $"경로가 복사되었고 {browserName}이(가) 열렸습니다: 개발자 모드 켜기 → 압축해제된 확장 프로그램 로드 → Ctrl+V.",
        _ => $"Eklenti yolu panoya kopyalandı & {browserName} açıldı: Geliştirici modunu açın → Paketlenmemiş yükle → Ctrl+V."
    };
    public static string MsgBridgeFirefoxOpened => T(
        "manifest.json yolu kopyalandı: Geçici Eklenti Yükle → açılan pencerede Ctrl+V yapın.",
        "manifest.json path copied: click Load Temporary Add-on → press Ctrl+V in the file dialog.",
        "manifest.json 路径已复制：点击“临时载入附加组件” → 在弹窗中按 Ctrl+V 选择即可。",
        "Ruta de manifest.json copiada: Cargar complemento temporal → pegue Ctrl+V en el diálogo.",
        "Путь к manifest.json скопирован: Загрузить временное дополнение → Ctrl+V в окне выбора.",
        "manifest.json-Pfad kopiert: Temporäres Add-on laden → Strg+V im Dateidialog.",
        "Chemin manifest.json copié : Charger un module temporaire → Ctrl+V dans la boîte de dialogue.",
        "manifest.json パスをコピーしました：「一時的なアドオンを読み込む」→ Ctrl+V で貼り付けてください。",
        "manifest.json 경로가 복사되었습니다: 임시 부가 기능 로드 → 파일 창에서 Ctrl+V를 누르세요.");
    public static string MsgBridgeFolderMissing => T(
        "BrowserExtension klasörü uygulama dizininde bulunamadı.",
        "BrowserExtension folder not found in the application directory.",
        "在程序目录中未找到 BrowserExtension 文件夹。",
        "No se encontró la carpeta BrowserExtension en el directorio de la aplicación.",
        "Папка BrowserExtension не найдена в каталоге приложения.",
        "Der Ordner BrowserExtension wurde im Programmverzeichnis nicht gefunden.",
        "Le dossier BrowserExtension est introuvable dans le répertoire de l'application.",
        "アプリケーションフォルダ内に BrowserExtension が見つかりませんでした。",
        "애플리케이션 폴더에서 BrowserExtension 폴더를 찾을 수 없습니다.");

    // Animated Extension Tutorial Dialog
    public static string GuideTitleChromium => T(
        "Chrome / Chromium Eklenti Kurulum Rehberi",
        "Chrome / Chromium Extension Setup Guide",
        "Chrome / Chromium 扩展安装指南",
        "Guía de extensión para Chrome / Chromium",
        "Установка расширения для Chrome / Chromium",
        "Chrome / Chromium-Erweiterung einrichten",
        "Guide d'installation Chrome / Chromium",
        "Chrome / Chromium 拡張機能セットアップガイド",
        "Chrome / Chromium 확장 프로그램 설치 가이드");
    public static string GuideTitleFirefox => T(
        "Firefox Eklenti Kurulum Rehberi",
        "Firefox Extension Setup Guide",
        "Firefox 扩展安装指南",
        "Guía de extensión para Firefox",
        "Установка расширения для Firefox",
        "Firefox-Erweiterung einrichten",
        "Guide d'installation Firefox",
        "Firefox 拡張機能セットアップガイド",
        "Firefox 확장 프로그램 설치 가이드");
    public static string GuideBadgeChromium => T("Video Rehber • Chromium", "Video Guide • Chromium", "视频指南 • Chromium", "Guía visual • Chromium", "Видеогид • Chromium", "Video-Anleitung • Chromium", "Guide animé • Chromium", "動画ガイド • Chromium", "영상 가이드 • Chromium");
    public static string GuideBadgeFirefox => T("Video Rehber • Firefox", "Video Guide • Firefox", "视频指南 • Firefox", "Guía visual • Firefox", "Видеогид • Firefox", "Video-Anleitung • Firefox", "Guide animé • Firefox", "動画ガイド • Firefox", "영상 가이드 • Firefox");
    public static string GuideConnectedNow => T(
        "Harika! Eklenti bağlandı — pencereyi kapatabilirsiniz",
        "Connected! Extension is live — you can close this guide",
        "已连接！扩展已就绪，现在可以关闭此窗口",
        "¡Conectado! La extensión ya funciona — puedes cerrar esta guía",
        "Подключено! Расширение активно — окно можно закрыть",
        "Verbunden! Erweiterung aktiv — Fenster kann geschlossen werden",
        "Connecté ! L'extension est active — vous pouvez fermer ce guide",
        "接続完了！拡張機能が稼働中です — このガイドを閉じられます",
        "연결 완료! 확장 프로그램이 작동 중입니다 — 이 창을 닫으셔도 됩니다");

    public static string GuideStepChromium1 => T(
        "1. Sağ üstten \"Geliştirici modu\" (Developer mode) anahtarını açın",
        "1. Enable \"Developer mode\" in the top-right / sidebar of Extensions",
        "1. 打开扩展管理页面右上角或侧边栏的“开发人员模式”开关",
        "1. Activa \"Modo de desarrollador\" en la página de extensiones",
        "1. Включите «Режим разработчика» на странице расширений",
        "1. Schalten Sie den \"Entwicklermodus\" auf der Erweiterungsseite ein",
        "1. Activez le « Mode développeur » sur la page des extensions",
        "1. 拡張機能ページ右上またはサイドバーの「デベロッパーモード」をオンにします",
        "1. 확장 프로그램 페이지 우측 상단/사이드바의 \"개발자 모드\"를 켭니다");
    public static string GuideStepChromium2 => T(
        "2. \"Paketlenmemiş öğe yükle\" (Load unpacked) butonuna tıklayın",
        "2. Click \"Load unpacked\" to open the folder picker",
        "2. 点击“加载已解压的扩展程序”按钮打开文件夹选择窗",
        "2. Pulsa \"Cargar descomprimida\" para abrir el selector de carpetas",
        "2. Нажмите «Загрузить распакованное расширение»",
        "2. Klicken Sie auf \"Entpackte Erweiterung laden\"",
        "2. Cliquez sur « Charger l'extension non empaquetée »",
        "2.「パッケージ化されていない拡張機能を読み込む」をクリックしてフォルダ選択を開きます",
        "2. \"압축해제된 확장 프로그램을 로드합니다\" 버튼을 클릭합니다");
    public static string GuideStepChromium3 => T(
        "3. Açılan pencerede Ctrl+V yapıştırın ve \"Klasör Seç\" deyin",
        "3. Press Ctrl+V to paste the path and click \"Select Folder\"",
        "3. 在弹窗中按 Ctrl+V 粘贴路径，然后点击“选择文件夹”完成",
        "3. Pega la ruta con Ctrl+V y pulsa \"Seleccionar carpeta\"",
        "3. Вставьте путь через Ctrl+V и нажмите «Выбор папки»",
        "3. Pfad mit Strg+V einfügen und \"Ordner auswählen\" klicken",
        "3. Collez le chemin avec Ctrl+V et cliquez sur « Sélectionner un dossier »",
        "3. 開いたダイアログで Ctrl+V で貼り付け、「フォルダーの選択」をクリックします",
        "3. 열린 창에서 Ctrl+V로 경로를 붙여넣고 \"폴더 선택\"을 클릭합니다");

    public static string GuideStepFirefox1 => T(
        "1. about:debugging sayfasında \"Geçici Eklenti Yükle...\"ye tıklayın",
        "1. Click \"Load Temporary Add-on...\" on about:debugging",
        "1. 在 about:debugging 页面点击“临时载入附加组件...”",
        "1. En about:debugging pulsa \"Cargar complemento temporal...\"",
        "1. На about:debugging нажмите «Загрузить временное дополнение...»",
        "1. Klicken Sie auf about:debugging auf \"Temporäres Add-on laden...\"",
        "1. Sur about:debugging, cliquez sur « Charger un module temporaire... »",
        "1. about:debugging ページで「一時的なアドオンを読み込む...」をクリックします",
        "1. about:debugging 페이지에서 \"임시 부가 기능 로드...\"를 클릭합니다");
    public static string GuideStepFirefox2 => T(
        "2. Açılan dosya penceresinde Ctrl+V (manifest.json) yapıp \"Aç\" deyin",
        "2. Press Ctrl+V (manifest.json) in the file picker and click \"Open\"",
        "2. 在文件对话框中按 Ctrl+V 粘贴 manifest.json 路径并点击“打开”",
        "2. Pega con Ctrl+V (manifest.json) y pulsa \"Abrir\"",
        "2. Вставьте путь к manifest.json (Ctrl+V) и нажмите «Открыть»",
        "2. Pfad per Strg+V (manifest.json) einfügen und \"Öffnen\" klicken",
        "2. Collez le chemin (manifest.json) avec Ctrl+V et cliquez sur « Ouvrir »",
        "2. ファイル選択で Ctrl+V (manifest.json) を貼り付けて「開く」をクリックします",
        "2. 파일 선택 창에서 Ctrl+V(manifest.json)를 붙여넣고 \"열기\"를 클릭합니다");
    public static string GuideStepFirefox3 => T(
        "3. Eklenti \"Çalışıyor\" görünür ve köprü otomatik Bağlı olur!",
        "3. Extension shows \"Running\" and Cueway connects instantly!",
        "3. 扩展显示“正在运行”后，Cueway 会立即自动连接！",
        "3. La extensión mostrará \"Ejecutándose\" y se conectará al instante",
        "3. Расширение перейдёт в «Выполняется» и сразу подключится!",
        "3. Status springt auf \"Aktiv\" und Cueway verbindet sich sofort!",
        "3. L'extension passe en « En cours » et Cueway se connecte !",
        "3. 拡張が「実行中」になると Cueway が自動で接続します！",
        "3. 확장이 \"실행 중\"으로 표시되면 Cueway가 자동으로 연결됩니다!");

    public static string GuideAnimDevMode => T("Geliştirici modu", "Developer mode", "开发人员模式", "Modo desarrollador", "Режим разработчика", "Entwicklermodus", "Mode développeur", "デベロッパーモード", "개발자 모드");
    public static string GuideAnimLoadUnpacked => T("Paketlenmemiş yükle", "Load unpacked", "加载已解压的", "Cargar descomprimida", "Загрузить распаков.", "Entpackte laden", "Charger non empaquetée", "パッケージ化されていない読み込み", "압축해제된 확장 로드");
    public static string GuideAnimSelectFolder => T("Klasör Seç", "Select Folder", "选择文件夹", "Seleccionar", "Выбрать папку", "Auswählen", "Sélectionner", "フォルダー選択", "폴더 선택");
    public static string GuideAnimLoadTemp => T("Geçici Eklenti Yükle...", "Load Temporary Add-on...", "临时载入附加组件...", "Cargar complemento...", "Загрузить временное...", "Temporäres Add-on...", "Charger module temp...", "一時的なアドオンを読み込む...", "임시 부가 기능 로드...");
    public static string GuideAnimOpenBtn => T("Aç", "Open", "打开", "Abrir", "Открыть", "Öffnen", "Ouvrir", "開く", "열기");
    public static string GuideAnimPasteHint => T("Ctrl+V ile yol yapıştırıldı", "Path pasted with Ctrl+V", "已按 Ctrl+V 粘贴路径", "Ruta pegada con Ctrl+V", "Путь вставлен (Ctrl+V)", "Pfad mit Strg+V eingefügt", "Chemin collé (Ctrl+V)", "Ctrl+V でパスを貼り付けました", "Ctrl+V로 경로가 붙여넣어졌습니다");
    public static string GuideAnimShortcutKey => T("Ctrl + V", "Ctrl + V", "Ctrl + V", "Ctrl + V", "Ctrl + V", "Strg + V", "Ctrl + V", "Ctrl + V", "Ctrl + V");
    public static string GuideAnimConnectedLive => T("● Bağlı (Canlı)", "● Connected (Live)", "● 已连接（实时）", "● Conectado (En vivo)", "● Подключено (Live)", "● Verbunden (Aktiv)", "● Connecté (Direct)", "● 接続済み (Live)", "● 연결됨 (실시간)");
    public static string GuideAnimActiveVer => T("● Etkin 1.0.0", "● Active 1.0.0", "● 已启用 1.0.0", "● Activa 1.0.0", "● Активно 1.0.0", "● Aktiv 1.0.0", "● Actif 1.0.0", "● 有効 1.0.0", "● 활성 1.0.0");
    public static string GuideAnimRunningConnected => T("● Çalışıyor • Bağlı", "● Running • Connected", "● 运行中 • 已连接", "● En ejecución • Conectado", "● Работает • Подкл.", "● Aktiv • Verbunden", "● Actif • Connecté", "● 実行中 • 接続済", "● 실행 중 • 연결됨");
    public static string GuideAnimRunning => T("● Çalışıyor", "● Running", "● 运行中", "● En ejecución", "● Выполняется", "● Läuft", "● En cours", "● 実行中", "● 실행 중");
    public static string GuideAnimFileNamePrompt => T("Dosya adı:", "File name:", "文件名:", "Nombre:", "Имя файла:", "Dateiname:", "Nom du fichier :", "ファイル名:", "파일 이름:");
    public static string GuideAnimDialogFolderTitle => T("Klasör Seçin", "Select Folder Dialog", "选择扩展文件夹", "Seleccionar carpeta", "Выбор папки", "Ordner wählen", "Choisir un dossier", "フォルダーの選択", "폴더 선택");
    public static string GuideAnimDialogFileTitle => T("Eklenti Dosyası Seçin", "Select Add-on File", "选择扩展文件", "Seleccionar archivo", "Выбор файла", "Datei wählen", "Choisir le fichier", "アドオンファイルを選択", "부가 기능 파일 선택");
    public static string GuideLangTag => T("TR", "EN", "中文", "ES", "RU", "DE", "FR", "JA", "KO");

    public static string GuideAnimCaptionChromium(int scene) => scene switch
    {
        0 => T(
            "Sağ üstten Geliştirici modu anahtarını açın",
            "Toggle Developer mode on in the Extensions page",
            "在扩展页面右上角开启“开发人员模式”开关",
            "Activa el Modo de desarrollador en Extensiones",
            "Включите Режим разработчика на странице расширений",
            "Entwicklermodus oben rechts einschalten",
            "Activez le Mode développeur en haut à droite",
            "拡張機能ページでデベロッパーモードをオンにします",
            "확장 프로그램 페이지에서 개발자 모드를 켭니다"),
        1 => T(
            "Paketlenmemiş yükle butonuna tıklayıp seçiciyi açın",
            "Click Load unpacked to open the folder picker",
            "点击“加载已解压的扩展”打开文件夹选择窗",
            "Pulsa Cargar descomprimida para abrir el selector",
            "Нажмите «Загрузить распакованное»",
            "Auf Entpackte Erweiterung laden klicken",
            "Cliquez sur Charger non empaquetée",
            "「パッケージ化されていない読み込み」をクリック",
            "압축해제된 확장 프로그램 로드를 클릭합니다"),
        _ => T(
            "Ctrl+V ile yolu yapıştırıp Klasör Seç'e basın — Bağlandı!",
            "Paste the path (Ctrl+V) and press Select Folder — Connected!",
            "按 Ctrl+V 粘贴路径并确认选择文件夹 — 连接成功！",
            "Pega la ruta (Ctrl+V) y confirma — ¡Conectado!",
            "Вставьте путь (Ctrl+V) и подтвердите — Подключено!",
            "Pfad einfügen (Strg+V) und bestätigen — Verbunden!",
            "Collez le chemin (Ctrl+V) et validez — Connecté !",
            "Ctrl+V で貼り付けてフォルダー選択 — 接続完了！",
            "Ctrl+V로 경로를 붙여넣고 폴더 선택 클릭 — 연결 완료!")
    };

    public static string GuideAnimCaptionFirefox(int scene) => scene switch
    {
        0 => T(
            "Geçici Eklenti Yükle... butonuna tıklayın",
            "Click Load Temporary Add-on... on about:debugging",
            "在 about:debugging 页面点击“临时载入附加组件...”",
            "Pulsa Cargar complemento temporal... en about:debugging",
            "Нажмите «Загрузить временное дополнение...»",
            "Klicken Sie auf Temporäres Add-on laden...",
            "Cliquez sur Charger un module temporaire...",
            "about:debugging で一時的なアドオンを読み込むをクリック",
            "about:debugging에서 임시 부가 기능 로드를 클릭합니다"),
        1 => T(
            "Ctrl+V (manifest.json) yapıştırıp Aç'a tıklayın",
            "Paste manifest.json path (Ctrl+V) and click Open",
            "按 Ctrl+V 粘贴 manifest.json 路径并点击“打开”",
            "Pega la ruta manifest.json (Ctrl+V) y pulsa Abrir",
            "Вставьте путь manifest.json (Ctrl+V) и нажмите Открыть",
            "Pfad manifest.json einfügen (Strg+V) und Öffnen klicken",
            "Collez manifest.json (Ctrl+V) puis cliquez sur Ouvrir",
            "manifest.json パスを Ctrl+V で貼り付け「開く」をクリック",
            "manifest.json 경로(Ctrl+V)를 붙여넣고 열기를 클릭합니다"),
        _ => T(
            "Eklenti Çalışıyor olarak başlar ve köprü hemen bağlanır!",
            "Extension starts Running and bridges live tabs automatically!",
            "扩展开始运行后，Cueway 自动连上全部标签页！",
            "¡La extensión arranca y conecta todas las pestañas!",
            "Расширение запущено — мост сразу подключается!",
            "Add-on läuft und verbindet alle Tabs automatisch!",
            "L'extension est lancée et connecte vos onglets !",
            "アドオンが実行中になり、全タブが自動接続されます！",
            "확장 프로그램이 실행 중이 되면 모든 탭이 자동 연결됩니다!")
    };

    public static string GuideBtnOpenAndCopy => T("Sayfayı Aç ve Yolu Kopyala", "Open Page & Copy Path", "打开页面并复制路径", "Abrir y copiar ruta", "Открыть и скопировать", "Seite öffnen & kopieren", "Ouvrir & copier chemin", "ページを開いてコピー", "페이지 열기 및 복사");
    public static string GuideBtnCopyPath => T("Yolu Kopyala", "Copy Path", "复制路径", "Copiar ruta", "Копировать путь", "Pfad kopieren", "Copier chemin", "パスをコピー", "경로 복사");
    public static string GuideBtnClose => T("Kapat", "Close", "关闭", "Cerrar", "Закрыть", "Schließen", "Fermer", "閉じる", "닫기");
    public static string MsgPathCopied => T("Kurulum yolu panoya kopyalandı (Ctrl+V ile yapıştırabilirsiniz).", "Path copied to clipboard (press Ctrl+V to paste).", "路径已复制到剪贴板（可直接按 Ctrl+V 粘贴）。", "Ruta copiada al portapapeles (Ctrl+V).", "Путь скопирован в буфер (Ctrl+V).", "Pfad in die Zwischenablage kopiert (Strg+V).", "Chemin copié (Ctrl+V pour coller).", "パスをクリップボードにコピーしました (Ctrl+V で貼り付け)。", "설치 경로가 클립보드에 복사되었습니다 (Ctrl+V로 붙여넣기).");

    // Engine & OSD Messages
    public static string OsdHeadphoneTitle => T("Kulaklık Koruması", "Headphone Guard", "耳机断开保护", "Protección de auriculares", "Защита наушников", "Kopfhörer-Schutz", "Protection casque", "ヘッドホン保護", "헤드폰 보호");
    public static string OsdHeadphoneBody => T("Ses çıkışı değişti — tüm medya duraklatıldı", "Audio output changed — all media paused", "音频输出已改变 — 已暂停所有媒体", "Salida de audio cambiada — medios en pausa", "Устройство вывода изменилось — всё на паузе", "Audioausgabe geändert — Medien pausiert", "Sortie audio changée — médias en pause", "音声出力が変更されました — 全て一時停止", "오디오 출력 변경됨 — 모든 미디어 일시정지");
    public static string OsdManualLockActive => T("Manuel kilit aktif", "Manual lock active", "手动锁定已开启", "Bloqueo manual activo", "Ручной замок активен", "Manuelle Sperre aktiv", "Verrou manuel actif", "手動ロック有効", "수동 잠금 활성화");
    public static string OsdAutoMode => T("Otomatik öncelik moduna geçildi", "Switched to auto priority mode", "已切换回自动优先级模式", "Modo automático activado", "Включён режим автоприоритета", "Auto-Prioritätsmodus aktiv", "Mode priorité auto activé", "自動優先モードに切替", "자동 우선순위 모드로 전환");
    public static string OsdPromotedTop => T("Öncelik #1 sırasına alındı", "Promoted to #1 priority", "已置顶为第 1 优先级", "Movido a prioridad #1", "Перемещено на 1-е место", "Auf Priorität #1 gesetzt", "Placé en priorité #1", "優先順位 1 位に移動しました", "우선순위 1위로 올렸습니다");
    public static string OsdSoloTitle => T("Solo Modu", "Solo Mode", "独奏模式", "Modo Solo", "Режим Соло", "Solo-Modus", "Mode Solo", "ソロモード", "솔로 모드");
    public static string OsdSoloNoTarget => T("Hedef yok", "No target", "无目标", "Sin destino", "Нет цели", "Kein Ziel", "Aucune cible", "対象なし", "대상 없음");
    public static string OsdSoloEnabled(string targetName) => Current switch
    {
        AppLanguage.En => $"Active (all except {targetName} muted)",
        AppLanguage.Zh => $"已开启（已静音除 {targetName} 外的其他应用）",
        AppLanguage.Es => $"Activo (todo silenciado excepto {targetName})",
        AppLanguage.Ru => $"Вкл (всё заглушено кроме {targetName})",
        AppLanguage.De => $"Aktiv (alles außer {targetName} stumm)",
        AppLanguage.Fr => $"Actif (tout coupé sauf {targetName})",
        AppLanguage.Ja => $"有効 ({targetName} 以外をミュート)",
        AppLanguage.Ko => $"활성 ({targetName} 외 모두 음소거)",
        _ => $"Aktif ({targetName} dışındakiler susturuldu)"
    };
    public static string OsdSoloDisabled => T("Kapatıldı — tüm sesler geri yüklendi", "Disabled — all audio restored", "已关闭 — 已恢复所有应用声音", "Desactivado — audio restaurado", "Выкл — звук восстановлен", "Deaktiviert — Audio wiederhergestellt", "Désactivé — audio restauré", "無効化 — すべての音量を復元", "비활성화 — 모든 오디오 복원됨");
    public static string OsdSecondaryTitle => T("İkincil Rota", "Secondary Route", "次级路由", "Ruta secundaria", "Вторичный маршрут", "Sekundär-Route", "Route secondaire", "セカンダリルート", "보조 경로");
    public static string OsdSecondaryEmpty => T("2. sırada aktif medya oturumu yok", "No secondary media session available", "没有可用的第 2 顺位媒体会话", "No hay una segunda sesión disponible", "Нет второй активной медиасессии", "Keine zweite Mediensitzung verfügbar", "Aucune session secondaire disponible", "2 番目のメディアセッションがありません", "사용 가능한 2순위 미디어 세션이 없습니다");
    public static string OsdSecondaryPrefix => T("[2. Rota]", "[2nd]", "[次级]", "[2.ª]", "[2-я]", "[2. Ziel]", "[2e]", "[2番目]", "[2순위]");
    public static string OsdActionPaused => T("Duraklatıldı", "Paused", "已暂停", "Pausado", "На паузе", "Pausiert", "En pause", "一時停止", "일시정지");
    public static string OsdActionPlaying => T("Oynatılıyor", "Playing", "正在播放", "Reproduciendo", "Воспроизведение", "Wiedergabe", "Lecture", "再生中", "재생 중");
    public static string OsdActionNext => T("Sonraki parça", "Next track", "下一首", "Siguiente", "Следующий трек", "Nächster Titel", "Suivant", "次のトラック", "다음 트랙");
    public static string OsdActionPrev => T("Önceki parça", "Previous track", "上一首", "Anterior", "Предыдущий трек", "Vorheriger Titel", "Précédent", "前のトラック", "이전 트랙");
    public static string OsdSeekForward(int sec) => Current switch
    {
        AppLanguage.En => $"+{sec}s forward",
        AppLanguage.Zh => $"快进 +{sec} 秒",
        AppLanguage.Es => $"+{sec}s adelante",
        AppLanguage.Ru => $"+{sec}с вперёд",
        AppLanguage.De => $"+{sec}s vorwärts",
        AppLanguage.Fr => $"+{sec}s avant",
        AppLanguage.Ja => $"+{sec}秒 早送り",
        AppLanguage.Ko => $"+{sec}초 앞으로",
        _ => $"+{sec} sn ileri sarıldı"
    };
    public static string OsdSeekBackward(int sec) => Current switch
    {
        AppLanguage.En => $"-{sec}s backward",
        AppLanguage.Zh => $"快退 -{sec} 秒",
        AppLanguage.Es => $"-{sec}s atrás",
        AppLanguage.Ru => $"-{sec}с назад",
        AppLanguage.De => $"-{sec}s rückwärts",
        AppLanguage.Fr => $"-{sec}s arrière",
        AppLanguage.Ja => $"-{sec}秒 巻き戻し",
        AppLanguage.Ko => $"-{sec}초 뒤로",
        _ => $"-{sec} sn geri sarıldı"
    };
    public static string OsdAppVolume(int pct) => Current switch
    {
        AppLanguage.En => $"App Volume: {pct}%",
        AppLanguage.Zh => $"应用音量: {pct}%",
        AppLanguage.Es => $"Volumen de app: {pct}%",
        AppLanguage.Ru => $"Громкость: {pct}%",
        AppLanguage.De => $"App-Lautstärke: {pct}%",
        AppLanguage.Fr => $"Volume app : {pct}%",
        AppLanguage.Ja => $"アプリ音量: {pct}%",
        AppLanguage.Ko => $"앱 음량: {pct}%",
        _ => $"Uygulama Sesi: %{pct}"
    };
    public static string OsdMuted => T("Sessize alındı", "Muted", "已静音", "Silenciado", "Без звука", "Stummgeschaltet", "Son coupé", "ミュートしました", "음소거됨");
    public static string OsdUnmuted => T("Ses açıldı", "Unmuted", "已取消静音", "Sonido activado", "Звук включён", "Stummschaltung aus", "Son réactivé", "ミュート解除", "음소거 해제됨");
    public static string FormatPercent(int pct) => Current == AppLanguage.Tr ? $"%{pct}" : $"{pct}%";

    // Auto-Updater & In-App Release Banner
    public static string UpdateBadgeNew => T("YENİ", "NEW", "新版", "NUEVO", "НОВОЕ", "NEU", "NOUVEAU", "新着", "새 버전");
    public static string UpdateAvailableBanner(string ver) => Current switch
    {
        AppLanguage.En => $"Cueway v{ver} is available — click to download",
        AppLanguage.Zh => $"Cueway v{ver} 新版本可用 — 点击立即下载",
        AppLanguage.Es => $"Cueway v{ver} disponible — haz clic para descargar",
        AppLanguage.Ru => $"Доступна Cueway v{ver} — нажмите для загрузки",
        AppLanguage.De => $"Cueway v{ver} verfügbar — zum Herunterladen klicken",
        AppLanguage.Fr => $"Cueway v{ver} disponible — cliquez pour télécharger",
        AppLanguage.Ja => $"Cueway v{ver} が利用可能です — クリックしてダウンロード",
        AppLanguage.Ko => $"Cueway v{ver} 업데이트 있음 — 클릭하여 다운로드",
        _ => $"Cueway v{ver} güncellemesi hazır — indirmek için tıklayın"
    };
    public static string UpdateBtnDownload => T("İndir", "Download", "下载", "Descargar", "Скачать", "Laden", "Télécharger", "ダウンロード", "다운로드");
    public static string UpdateDownloadingText(string ver, int pct, string progressMb) => Current switch
    {
        AppLanguage.En => $"Downloading v{ver}... {pct}% ({progressMb})",
        AppLanguage.Zh => $"正在下载 v{ver}... {pct}% ({progressMb})",
        AppLanguage.Es => $"Descargando v{ver}... {pct}% ({progressMb})",
        AppLanguage.Ru => $"Загрузка v{ver}... {pct}% ({progressMb})",
        AppLanguage.De => $"Lade v{ver}... {pct}% ({progressMb})",
        AppLanguage.Fr => $"Téléchargement v{ver}... {pct}% ({progressMb})",
        AppLanguage.Ja => $"v{ver} をダウンロード中... {pct}% ({progressMb})",
        AppLanguage.Ko => $"v{ver} 다운로드 중... {pct}% ({progressMb})",
        _ => $"v{ver} indiriliyor... %{pct} ({progressMb})"
    };
    public static string UpdateDownloadingIndetText(string ver, string doneMb) => Current switch
    {
        AppLanguage.En => $"Downloading v{ver}... ({doneMb})",
        AppLanguage.Zh => $"正在下载 v{ver}... ({doneMb})",
        AppLanguage.Es => $"Descargando v{ver}... ({doneMb})",
        AppLanguage.Ru => $"Загрузка v{ver}... ({doneMb})",
        AppLanguage.De => $"Lade v{ver}... ({doneMb})",
        AppLanguage.Fr => $"Téléchargement v{ver}... ({doneMb})",
        AppLanguage.Ja => $"v{ver} をダウンロード中... ({doneMb})",
        AppLanguage.Ko => $"v{ver} 다운로드 중... ({doneMb})",
        _ => $"v{ver} indiriliyor... ({doneMb})"
    };
    public static string UpdateBtnCancel => T("İptal", "Cancel", "取消", "Cancelar", "Отмена", "Abbrechen", "Annuler", "キャンセル", "취소");
    public static string UpdateReadyBanner(string ver) => Current switch
    {
        AppLanguage.En => $"Cueway v{ver} downloaded — ready to install",
        AppLanguage.Zh => $"Cueway v{ver} 下载完成 — 可以安装",
        AppLanguage.Es => $"Cueway v{ver} descargada — lista para instalar",
        AppLanguage.Ru => $"Cueway v{ver} загружена — готова к установке",
        AppLanguage.De => $"Cueway v{ver} geladen — bereit zur Installation",
        AppLanguage.Fr => $"Cueway v{ver} téléchargée — prête à installer",
        AppLanguage.Ja => $"Cueway v{ver} の準備ができました",
        AppLanguage.Ko => $"Cueway v{ver} 다운로드 완료 — 설치 준비됨",
        _ => $"Cueway v{ver} indirildi — kuruluma hazır"
    };
    public static string UpdateBtnInstall => T("Kur ve Yeniden Başlat", "Install & Restart", "安装并重启", "Instalar y reiniciar", "Установить и перезапустить", "Installieren & Neu starten", "Installer et redémarrer", "インストールして再起動", "설치 및 다시 시작");
    public static string UpdateFailedBanner => T("Güncelleme indirilemedi", "Update download failed", "更新下载失败", "Error al descargar la actualización", "Не удалось загрузить обновление", "Update-Download fehlgeschlagen", "Échec du téléchargement", "更新のダウンロードに失敗", "업데이트 다운로드 실패");
    public static string UpdateBtnRetry => T("Tekrar Dene", "Retry", "重试", "Reintentar", "Повторить", "Erneut", "Réessayer", "再試行", "다시 시도");

    public static string SetAutoUpdateTitle => T("Otomatik Güncelleme Kontrolü", "Automatic Update Checks", "自动检查更新", "Buscar actualizaciones automáticamente", "Автопроверка обновлений", "Automatisch nach Updates suchen", "Vérification automatique des mises à jour", "自動アップデート確認", "자동 업데이트 확인");
    public static string SetAutoUpdateSub => T(
        "Başlangıçta GitHub üzerinden yeni Cueway sürümü olup olmadığını sessizce kontrol et",
        "Check GitHub Releases silently at startup for new Cueway versions",
        "启动时自动检查 GitHub Releases 是否有新版本",
        "Comprueba en segundo plano si hay una nueva versión en GitHub",
        "Проверять новые релизы Cueway на GitHub при запуске",
        "Beim Start automatisch auf GitHub nach neuen Versionen suchen",
        "Vérifier automatiquement les nouvelles versions sur GitHub au démarrage",
        "起動時に GitHub の新しいリリースを自動確認します",
        "시작 시 GitHub 릴리스에서 새 버전을 자동으로 확인합니다");
    public static string SetUpdateCheckTitle => T("Uygulama Güncellemeleri", "Application Updates", "应用更新", "Actualizaciones de la app", "Обновления приложения", "Anwendungs-Updates", "Mises à jour", "アプリの更新", "애플리케이션 업데이트");
    public static string SetUpdateSubCurrent(string cur) => Current switch
    {
        AppLanguage.En => $"Installed version: v{cur}",
        AppLanguage.Zh => $"当前版本：v{cur}",
        AppLanguage.Es => $"Versión instalada: v{cur}",
        AppLanguage.Ru => $"Установлена версия: v{cur}",
        AppLanguage.De => $"Installierte Version: v{cur}",
        AppLanguage.Fr => $"Version installée : v{cur}",
        AppLanguage.Ja => $"現在のバージョン: v{cur}",
        AppLanguage.Ko => $"현재 버전: v{cur}",
        _ => $"Mevcut sürüm: v{cur}"
    };
    public static string SetUpdateSubChecking => T("GitHub üzerinden yeni sürüm kontrol ediliyor...", "Checking GitHub for updates...", "正在检查更新...", "Comprobando actualizaciones en GitHub...", "Проверка обновлений на GitHub...", "Suche nach Updates auf GitHub...", "Recherche de mises à jour sur GitHub...", "GitHub で更新を確認中...", "GitHub에서 업데이트 확인 중...");
    public static string SetUpdateSubUpToDate(string cur) => Current switch
    {
        AppLanguage.En => $"You're up to date (v{cur})",
        AppLanguage.Zh => $"已是最新版本 (v{cur})",
        AppLanguage.Es => $"Están al día (v{cur})",
        AppLanguage.Ru => $"У вас актуальная версия (v{cur})",
        AppLanguage.De => $"Sie sind auf dem neuesten Stand (v{cur})",
        AppLanguage.Fr => $"Vous utilisez la dernière version (v{cur})",
        AppLanguage.Ja => $"最新バージョンです (v{cur})",
        AppLanguage.Ko => $"최신 버전입니다 (v{cur})",
        _ => $"Güncel sürüm kullanıyorsunuz (v{cur})"
    };
    public static string SetUpdateSubAvailable(string latest) => Current switch
    {
        AppLanguage.En => $"New release found: v{latest}",
        AppLanguage.Zh => $"发现新版本：v{latest}",
        AppLanguage.Es => $"Nueva versión encontrada: v{latest}",
        AppLanguage.Ru => $"Найдена новая версия: v{latest}",
        AppLanguage.De => $"Neue Version gefunden: v{latest}",
        AppLanguage.Fr => $"Nouvelle version disponible : v{latest}",
        AppLanguage.Ja => $"新しいバージョンが見つかりました: v{latest}",
        AppLanguage.Ko => $"새 버전 발견: v{latest}",
        _ => $"Yeni sürüm bulundu: v{latest}"
    };
    public static string SetUpdateSubFailed => T("GitHub sürüm sunucusuna ulaşılamadı", "Could not reach GitHub Releases", "无法连接到 GitHub 更新服务器", "No se pudo conectar con GitHub", "Не удалось связаться с GitHub", "GitHub konnte nicht erreicht werden", "Impossible de joindre GitHub", "GitHub に接続できませんでした", "GitHub 서버에 연결할 수 없습니다");
    public static string BtnCheckUpdates => T("Kontrol Et", "Check Now", "检查更新", "Buscar ahora", "Проверить", "Jetzt prüfen", "Vérifier", "今すぐ確認", "지금 확인");
    public static string BtnCheckingUpdates => T("Kontrol...", "Checking...", "检查中...", "Buscando...", "Проверка...", "Prüfe...", "Recherche...", "確認中...", "확인 중...");
    public static string TrayMenuUpdateAvailable(string ver) => Current switch
    {
        AppLanguage.En => $"Update Available (v{ver})",
        AppLanguage.Zh => $"发现新版本 (v{ver})",
        AppLanguage.Es => $"Actualización disponible (v{ver})",
        AppLanguage.Ru => $"Доступно обновление (v{ver})",
        AppLanguage.De => $"Update verfügbar (v{ver})",
        AppLanguage.Fr => $"Mise à jour disponible (v{ver})",
        AppLanguage.Ja => $"更新が利用可能 (v{ver})",
        AppLanguage.Ko => $"업데이트 사용 가능 (v{ver})",
        _ => $"Güncelleme Mevcut (v{ver})"
    };
    public static string OsdUpdateTitle => T("Cueway Güncellemesi", "Cueway Update", "Cueway 更新", "Actualización de Cueway", "Обновление Cueway", "Cueway-Update", "Mise à jour Cueway", "Cueway アップデート", "Cueway 업데이트");
    public static string OsdUpdateBody(string ver) => Current switch
    {
        AppLanguage.En => $"v{ver} available — open Cueway to download",
        AppLanguage.Zh => $"v{ver} 已发布 — 打开 Cueway 即可下载",
        AppLanguage.Es => $"v{ver} disponible — abre Cueway para descargar",
        AppLanguage.Ru => $"Доступна v{ver} — откройте Cueway",
        AppLanguage.De => $"v{ver} verfügbar — Cueway öffnen",
        AppLanguage.Fr => $"v{ver} disponible — ouvrez Cueway",
        AppLanguage.Ja => $"v{ver} が公開中 — Cueway でダウンロード",
        AppLanguage.Ko => $"v{ver} 출시됨 — Cueway에서 다운로드",
        _ => $"v{ver} hazır — indirmek için Cueway'i açın"
    };
    public static string MsgUpdateLaunchCancelled => T("Güncelleme kurulumu iptal edildi.", "Update installation cancelled.", "已取消更新安装。", "Instalación de actualización cancelada.", "Установка обновления отменена.", "Update-Installation abgebrochen.", "Installation annulée.", "アップデートのインストールをキャンセルしました。", "업데이트 설치가 취소되었습니다.");
}
