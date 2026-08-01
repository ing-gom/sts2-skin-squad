using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace Sts2SkinSquad.Localization;

/// <summary>
/// Every player-visible string in the mod, in the languages the game ships.
///
/// Two consumers, one table:
/// <list type="bullet">
/// <item><see cref="Get"/> — panel and button text, resolved at call time, so a language switch
/// takes effect the next time the picker is opened.</item>
/// <item><see cref="Map"/> — the mod-settings entries, which want a language-keyed dictionary
/// handed to <c>ModConfigBridge.LocalizedLabels</c>/<c>LocalizedDescriptions</c> instead.</item>
/// </list>
///
/// Resolution order is current language, then English, then the raw key — a key missing from a
/// translation degrades to English rather than to a blank label. Following the sister-mod
/// convention, the table is keyed by <c>LocManager.Language</c> uppercased (3-letter codes).
/// The 14 languages the game ships: eng zhs deu esp fra ita jpn kor pol ptb rus spa tha tur.
/// ZHT and POR are carried as well: they are not in <c>LocManager.Languages</c>, but they cost
/// nothing and cover the codes a future build (or a platform mapping) might report.
/// </summary>
internal static class Strings
{
    private const string Fallback = "ENG";

    public static string Get(string key, params object[] args)
    {
        string raw = Lookup(key);
        if (args == null || args.Length == 0) return raw;
        try { return string.Format(raw, args); }
        catch { return raw; }
    }

    /// <summary>
    /// One key across every language except English, lower-cased the way <c>LocManager.Language</c>
    /// reports it. English is deliberately absent: the settings framework takes it as the entry's
    /// plain label and falls back to it for anything the dictionary does not cover.
    /// </summary>
    public static Dictionary<string, string> Map(string key)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, Dictionary<string, string>> table in Tables)
        {
            if (string.Equals(table.Key, Fallback, StringComparison.OrdinalIgnoreCase)) continue;
            if (table.Value.TryGetValue(key, out string? text)) map[table.Key.ToLowerInvariant()] = text;
        }
        return map;
    }

    private static string Lookup(string key)
    {
        string lang = CurrentLanguage();
        if (Tables.TryGetValue(lang, out Dictionary<string, string>? table) && table.TryGetValue(key, out string? v))
            return v;
        if (Tables.TryGetValue(Fallback, out Dictionary<string, string>? eng) && eng.TryGetValue(key, out string? ev))
            return ev;
        return key;
    }

    private static string CurrentLanguage()
    {
        try { return (LocManager.Instance?.Language ?? Fallback).ToUpperInvariant(); }
        catch { return Fallback; }
    }

    // "Skin Squad" itself is left untranslated everywhere it names the mod — it is the name on the
    // Workshop page, and a player looking for the mod's own controls should find the same word.
    private static readonly Dictionary<string, Dictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENG"] = new()
        {
            ["cfg_enabled_label"] = "Enable Squad",
            ["cfg_enabled_desc"] = "Stand visual-only companions beside your character, so a solo run looks like a party. Single-player only; purely cosmetic.",
            ["members_label"] = "Squad members",
            ["cfg_dim_label"] = "Dim back rows",
            ["cfg_dim_desc"] = "Grey out members standing behind the front row, the way the game greys out distant co-op teammates.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — click to configure, drag to move.",
            ["tip_click"] = "Skin Squad — click to configure.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Close",
            ["modal_empty"] = "Squad is empty.\nUse + above to add a member.",
            ["modal_no_skins"] = "No character skin mods found — only the game's own characters are listed.\nInstall skin mods and restart to add them here.",
            ["look_self"] = "Same as me",
            ["look_self_caption"] = "Same as me\n(whichever character you pick)",
            ["look_random"] = "Random",
            ["look_missing"] = "Unavailable — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "preset name",
            ["preset_delete"] = "Delete",
            ["preset_save"] = "Save",
            ["preset_new_tip"] = "New preset",
            ["source_vanilla"] = "vanilla",
        },
        ["KOR"] = new()
        {
            ["cfg_enabled_label"] = "Squad 사용",
            ["cfg_enabled_desc"] = "조작하는 캐릭터 옆에 외형 전용 동료를 세워, 혼자서도 파티처럼 보이게 합니다. 싱글플레이 전용이며 전투에는 전혀 관여하지 않습니다.",
            ["members_label"] = "Squad 인원",
            ["cfg_dim_label"] = "뒷줄 어둡게",
            ["cfg_dim_desc"] = "앞줄보다 뒤에 선 동료를 어둡게 표시합니다. 협동 플레이에서 멀리 있는 팀원을 어둡게 처리하는 방식과 같습니다.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — 클릭하면 설정, 드래그하면 위치 이동.",
            ["tip_click"] = "Skin Squad — 클릭하면 설정.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "닫기",
            ["modal_empty"] = "Squad가 비어 있습니다.\n위의 + 로 추가하세요.",
            ["modal_no_skins"] = "캐릭터 스킨 모드를 찾지 못했습니다 — 게임 기본 캐릭터만 표시됩니다.\n스킨 모드를 설치하고 게임을 다시 시작하면 목록에 추가됩니다.",
            ["look_self"] = "나와 같은 모습",
            ["look_self_caption"] = "나와 같은 모습\n(고른 캐릭터를 따라감)",
            ["look_random"] = "무작위",
            ["look_missing"] = "사용할 수 없음 — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "프리셋 이름",
            ["preset_delete"] = "삭제",
            ["preset_save"] = "저장",
            ["preset_new_tip"] = "새 프리셋",
            ["source_vanilla"] = "기본",
        },
        ["JPN"] = new()
        {
            ["cfg_enabled_label"] = "Squad を表示",
            ["cfg_enabled_desc"] = "操作キャラクターの隣に見た目だけの仲間を並べ、ひとりでもパーティのように見せます。シングルプレイ専用で、戦闘には一切影響しません。",
            ["members_label"] = "Squad の人数",
            ["cfg_dim_label"] = "後列を暗くする",
            ["cfg_dim_desc"] = "前列より後ろにいる仲間を暗く表示します。協力プレイで離れた仲間が暗く表示されるのと同じ処理です。",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — クリックで設定、ドラッグで移動。",
            ["tip_click"] = "Skin Squad — クリックで設定。",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "閉じる",
            ["modal_empty"] = "Squad が空です。\n上の + で追加してください。",
            ["modal_no_skins"] = "キャラクタースキンModが見つかりません — ゲーム標準のキャラクターのみ表示されます。\nスキンModを導入して再起動すると一覧に追加されます。",
            ["look_self"] = "自分と同じ",
            ["look_self_caption"] = "自分と同じ\n(選んだキャラクターに追従)",
            ["look_random"] = "ランダム",
            ["look_missing"] = "利用できません — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "プリセット名",
            ["preset_delete"] = "削除",
            ["preset_save"] = "保存",
            ["preset_new_tip"] = "新しいプリセット",
            ["source_vanilla"] = "標準",
        },
        ["ZHS"] = new()
        {
            ["cfg_enabled_label"] = "启用 Squad",
            ["cfg_enabled_desc"] = "在你操作的角色旁边站立仅有外观的小队成员，让单人游戏看起来像一支队伍。仅限单人游戏，纯装饰性，不影响战斗。",
            ["members_label"] = "Squad 人数",
            ["cfg_dim_label"] = "后排变暗",
            ["cfg_dim_desc"] = "让站在前排之后的成员变暗，与游戏中远处合作队友的处理方式一致。",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — 点击进行设置，拖动可移动位置。",
            ["tip_click"] = "Skin Squad — 点击进行设置。",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "关闭",
            ["modal_empty"] = "Squad 为空。\n用上方的 + 添加成员。",
            ["modal_no_skins"] = "未找到角色皮肤模组 — 仅列出游戏自带的角色。\n安装皮肤模组并重启游戏后即可在此显示。",
            ["look_self"] = "与我相同",
            ["look_self_caption"] = "与我相同\n（跟随你所选的角色）",
            ["look_random"] = "随机",
            ["look_missing"] = "不可用 — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "预设名称",
            ["preset_delete"] = "删除",
            ["preset_save"] = "保存",
            ["preset_new_tip"] = "新建预设",
            ["source_vanilla"] = "原版",
        },
        ["ZHT"] = new()
        {
            ["cfg_enabled_label"] = "啟用 Squad",
            ["cfg_enabled_desc"] = "在你操作的角色旁邊站立僅有外觀的小隊成員，讓單人遊戲看起來像一支隊伍。僅限單人遊戲，純裝飾性，不影響戰鬥。",
            ["members_label"] = "Squad 人數",
            ["cfg_dim_label"] = "後排變暗",
            ["cfg_dim_desc"] = "讓站在前排之後的成員變暗，與遊戲中遠處合作隊友的處理方式一致。",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — 點擊進行設定，拖曳可移動位置。",
            ["tip_click"] = "Skin Squad — 點擊進行設定。",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "關閉",
            ["modal_empty"] = "Squad 為空。\n用上方的 + 新增成員。",
            ["modal_no_skins"] = "未找到角色外觀模組 — 僅列出遊戲內建的角色。\n安裝外觀模組並重新啟動遊戲後即可在此顯示。",
            ["look_self"] = "與我相同",
            ["look_self_caption"] = "與我相同\n（跟隨你所選的角色）",
            ["look_random"] = "隨機",
            ["look_missing"] = "無法使用 — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "預設名稱",
            ["preset_delete"] = "刪除",
            ["preset_save"] = "儲存",
            ["preset_new_tip"] = "新增預設",
            ["source_vanilla"] = "原版",
        },
        ["DEU"] = new()
        {
            ["cfg_enabled_label"] = "Squad aktivieren",
            ["cfg_enabled_desc"] = "Stellt rein optische Gefährten neben deinen Charakter, damit ein Solo-Run wie eine Gruppe aussieht. Nur im Einzelspieler; rein kosmetisch.",
            ["members_label"] = "Squad-Mitglieder",
            ["cfg_dim_label"] = "Hintere Reihen abdunkeln",
            ["cfg_dim_desc"] = "Dunkelt Mitglieder hinter der vorderen Reihe ab – so wie das Spiel entfernte Koop-Mitspieler abdunkelt.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad – klicken zum Einstellen, ziehen zum Verschieben.",
            ["tip_click"] = "Skin Squad – klicken zum Einstellen.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Schließen",
            ["modal_empty"] = "Das Squad ist leer.\nMit + oben ein Mitglied hinzufügen.",
            ["modal_no_skins"] = "Keine Charakter-Skin-Mods gefunden – es werden nur die spieleigenen Charaktere aufgeführt.\nSkin-Mods installieren und neu starten, um sie hier hinzuzufügen.",
            ["look_self"] = "Wie ich",
            ["look_self_caption"] = "Wie ich\n(folgt dem gewählten Charakter)",
            ["look_random"] = "Zufällig",
            ["look_missing"] = "Nicht verfügbar – {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "Name der Vorlage",
            ["preset_delete"] = "Löschen",
            ["preset_save"] = "Speichern",
            ["preset_new_tip"] = "Neue Vorlage",
            ["source_vanilla"] = "Original",
        },
        ["FRA"] = new()
        {
            ["cfg_enabled_label"] = "Activer le Squad",
            ["cfg_enabled_desc"] = "Place à côté de votre personnage des compagnons purement visuels, pour qu'une partie solo ait l'air d'un groupe. Solo uniquement ; purement cosmétique.",
            ["members_label"] = "Membres du Squad",
            ["cfg_dim_label"] = "Assombrir les rangs arrière",
            ["cfg_dim_desc"] = "Assombrit les membres situés derrière le premier rang, comme le jeu le fait pour les coéquipiers éloignés en coop.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad – cliquez pour configurer, faites glisser pour déplacer.",
            ["tip_click"] = "Skin Squad – cliquez pour configurer.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Fermer",
            ["modal_empty"] = "Le Squad est vide.\nUtilisez + ci-dessus pour ajouter un membre.",
            ["modal_no_skins"] = "Aucun mod de skin trouvé – seuls les personnages du jeu sont listés.\nInstallez des mods de skin et redémarrez pour les ajouter ici.",
            ["look_self"] = "Comme moi",
            ["look_self_caption"] = "Comme moi\n(suit le personnage choisi)",
            ["look_random"] = "Aléatoire",
            ["look_missing"] = "Indisponible – {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nom du préréglage",
            ["preset_delete"] = "Supprimer",
            ["preset_save"] = "Enregistrer",
            ["preset_new_tip"] = "Nouveau préréglage",
            ["source_vanilla"] = "original",
        },
        ["SPA"] = new()
        {
            ["cfg_enabled_label"] = "Activar el Squad",
            ["cfg_enabled_desc"] = "Coloca junto a tu personaje compañeros puramente visuales, para que una partida en solitario parezca un grupo. Solo en un jugador; puramente estético.",
            ["members_label"] = "Miembros del Squad",
            ["cfg_dim_label"] = "Oscurecer filas traseras",
            ["cfg_dim_desc"] = "Atenúa a los miembros situados detrás de la primera fila, igual que el juego atenúa a los compañeros lejanos en cooperativo.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad: haz clic para configurar, arrastra para mover.",
            ["tip_click"] = "Skin Squad: haz clic para configurar.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Cerrar",
            ["modal_empty"] = "El Squad está vacío.\nUsa + arriba para añadir un miembro.",
            ["modal_no_skins"] = "No se han encontrado mods de aspectos: solo se muestran los personajes del juego.\nInstala mods de aspectos y reinicia para añadirlos aquí.",
            ["look_self"] = "Igual que yo",
            ["look_self_caption"] = "Igual que yo\n(sigue al personaje elegido)",
            ["look_random"] = "Aleatorio",
            ["look_missing"] = "No disponible: {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nombre del preajuste",
            ["preset_delete"] = "Eliminar",
            ["preset_save"] = "Guardar",
            ["preset_new_tip"] = "Nuevo preajuste",
            ["source_vanilla"] = "original",
        },
        ["ESP"] = new()
        {
            ["cfg_enabled_label"] = "Activar el Squad",
            ["cfg_enabled_desc"] = "Coloca junto a tu personaje compañeros puramente visuales, para que una partida en solitario parezca un grupo. Solo en un jugador; puramente estético.",
            ["members_label"] = "Miembros del Squad",
            ["cfg_dim_label"] = "Oscurecer filas traseras",
            ["cfg_dim_desc"] = "Atenúa a los miembros situados detrás de la primera fila, igual que el juego atenúa a los compañeros lejanos en cooperativo.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad: haz clic para configurar, arrastra para mover.",
            ["tip_click"] = "Skin Squad: haz clic para configurar.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Cerrar",
            ["modal_empty"] = "El Squad está vacío.\nUsa + arriba para añadir un miembro.",
            ["modal_no_skins"] = "No se han encontrado mods de aspectos: solo se muestran los personajes del juego.\nInstala mods de aspectos y reinicia para añadirlos aquí.",
            ["look_self"] = "Igual que yo",
            ["look_self_caption"] = "Igual que yo\n(sigue al personaje elegido)",
            ["look_random"] = "Aleatorio",
            ["look_missing"] = "No disponible: {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nombre del preajuste",
            ["preset_delete"] = "Eliminar",
            ["preset_save"] = "Guardar",
            ["preset_new_tip"] = "Nuevo preajuste",
            ["source_vanilla"] = "original",
        },
        ["ITA"] = new()
        {
            ["cfg_enabled_label"] = "Attiva lo Squad",
            ["cfg_enabled_desc"] = "Affianca al tuo personaggio compagni puramente visivi, così una partita in singolo sembra un gruppo. Solo giocatore singolo; puramente estetico.",
            ["members_label"] = "Membri dello Squad",
            ["cfg_dim_label"] = "Scurisci le file posteriori",
            ["cfg_dim_desc"] = "Scurisce i membri dietro la prima fila, come il gioco fa con i compagni lontani in cooperativa.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad – clicca per configurare, trascina per spostare.",
            ["tip_click"] = "Skin Squad – clicca per configurare.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Chiudi",
            ["modal_empty"] = "Lo Squad è vuoto.\nUsa + qui sopra per aggiungere un membro.",
            ["modal_no_skins"] = "Nessuna mod di skin trovata: sono elencati solo i personaggi del gioco.\nInstalla mod di skin e riavvia per aggiungerle qui.",
            ["look_self"] = "Come me",
            ["look_self_caption"] = "Come me\n(segue il personaggio scelto)",
            ["look_random"] = "Casuale",
            ["look_missing"] = "Non disponibile – {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nome del preset",
            ["preset_delete"] = "Elimina",
            ["preset_save"] = "Salva",
            ["preset_new_tip"] = "Nuovo preset",
            ["source_vanilla"] = "originale",
        },
        ["PTB"] = new()
        {
            ["cfg_enabled_label"] = "Ativar o Squad",
            ["cfg_enabled_desc"] = "Coloca ao lado do seu personagem companheiros apenas visuais, para que uma partida solo pareça um grupo. Somente um jogador; puramente cosmético.",
            ["members_label"] = "Membros do Squad",
            ["cfg_dim_label"] = "Escurecer fileiras de trás",
            ["cfg_dim_desc"] = "Escurece os membros atrás da primeira fileira, como o jogo faz com colegas distantes no cooperativo.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad – clique para configurar, arraste para mover.",
            ["tip_click"] = "Skin Squad – clique para configurar.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Fechar",
            ["modal_empty"] = "O Squad está vazio.\nUse + acima para adicionar um membro.",
            ["modal_no_skins"] = "Nenhum mod de skin encontrado — apenas os personagens do próprio jogo são listados.\nInstale mods de skin e reinicie para adicioná-los aqui.",
            ["look_self"] = "Igual a mim",
            ["look_self_caption"] = "Igual a mim\n(segue o personagem escolhido)",
            ["look_random"] = "Aleatório",
            ["look_missing"] = "Indisponível — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nome da predefinição",
            ["preset_delete"] = "Excluir",
            ["preset_save"] = "Salvar",
            ["preset_new_tip"] = "Nova predefinição",
            ["source_vanilla"] = "original",
        },
        ["POR"] = new()
        {
            ["cfg_enabled_label"] = "Ativar o Squad",
            ["cfg_enabled_desc"] = "Coloca ao lado da sua personagem companheiros apenas visuais, para que uma partida a solo pareça um grupo. Apenas um jogador; puramente cosmético.",
            ["members_label"] = "Membros do Squad",
            ["cfg_dim_label"] = "Escurecer filas de trás",
            ["cfg_dim_desc"] = "Escurece os membros atrás da primeira fila, tal como o jogo faz com colegas distantes em cooperativo.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad – clique para configurar, arraste para mover.",
            ["tip_click"] = "Skin Squad – clique para configurar.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Fechar",
            ["modal_empty"] = "O Squad está vazio.\nUse + acima para adicionar um membro.",
            ["modal_no_skins"] = "Não foram encontrados mods de aspetos — apenas as personagens do próprio jogo são listadas.\nInstale mods de aspetos e reinicie para os adicionar aqui.",
            ["look_self"] = "Igual a mim",
            ["look_self_caption"] = "Igual a mim\n(segue a personagem escolhida)",
            ["look_random"] = "Aleatório",
            ["look_missing"] = "Indisponível — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nome da predefinição",
            ["preset_delete"] = "Eliminar",
            ["preset_save"] = "Guardar",
            ["preset_new_tip"] = "Nova predefinição",
            ["source_vanilla"] = "original",
        },
        ["POL"] = new()
        {
            ["cfg_enabled_label"] = "Włącz Squad",
            ["cfg_enabled_desc"] = "Ustawia obok twojej postaci wyłącznie wizualnych towarzyszy, dzięki czemu gra jednoosobowa wygląda jak drużyna. Tylko w trybie jednoosobowym; czysto kosmetyczne.",
            ["members_label"] = "Liczba członków Squad",
            ["cfg_dim_label"] = "Przyciemniaj tylne rzędy",
            ["cfg_dim_desc"] = "Przyciemnia członków stojących za pierwszym rzędem, tak jak gra przyciemnia oddalonych towarzyszy w kooperacji.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad – kliknij, aby skonfigurować, przeciągnij, aby przesunąć.",
            ["tip_click"] = "Skin Squad – kliknij, aby skonfigurować.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Zamknij",
            ["modal_empty"] = "Squad jest pusty.\nUżyj + powyżej, aby dodać członka.",
            ["modal_no_skins"] = "Nie znaleziono modów ze skórkami postaci – na liście są tylko postacie z gry.\nZainstaluj mody ze skórkami i uruchom grę ponownie, aby je tu dodać.",
            ["look_self"] = "Tak jak ja",
            ["look_self_caption"] = "Tak jak ja\n(zgodnie z wybraną postacią)",
            ["look_random"] = "Losowo",
            ["look_missing"] = "Niedostępne – {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "nazwa zestawu",
            ["preset_delete"] = "Usuń",
            ["preset_save"] = "Zapisz",
            ["preset_new_tip"] = "Nowy zestaw",
            ["source_vanilla"] = "oryginał",
        },
        ["RUS"] = new()
        {
            ["cfg_enabled_label"] = "Включить Squad",
            ["cfg_enabled_desc"] = "Ставит рядом с вашим персонажем чисто визуальных спутников, чтобы одиночная игра выглядела как отряд. Только в одиночной игре; исключительно косметика.",
            ["members_label"] = "Участники Squad",
            ["cfg_dim_label"] = "Затемнять задние ряды",
            ["cfg_dim_desc"] = "Затемняет участников, стоящих позади первого ряда, — так же, как игра затемняет дальних союзников в кооперативе.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — щёлкните, чтобы настроить, перетащите, чтобы переместить.",
            ["tip_click"] = "Skin Squad — щёлкните, чтобы настроить.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Закрыть",
            ["modal_empty"] = "Squad пуст.\nДобавьте участника кнопкой + выше.",
            ["modal_no_skins"] = "Моды со скинами персонажей не найдены — показаны только персонажи самой игры.\nУстановите моды со скинами и перезапустите игру, чтобы они появились здесь.",
            ["look_self"] = "Как у меня",
            ["look_self_caption"] = "Как у меня\n(следует за выбранным персонажем)",
            ["look_random"] = "Случайно",
            ["look_missing"] = "Недоступно — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "название пресета",
            ["preset_delete"] = "Удалить",
            ["preset_save"] = "Сохранить",
            ["preset_new_tip"] = "Новый пресет",
            ["source_vanilla"] = "оригинал",
        },
        ["THA"] = new()
        {
            ["cfg_enabled_label"] = "เปิดใช้งาน Squad",
            ["cfg_enabled_desc"] = "วางเพื่อนร่วมทีมที่เป็นภาพล้วนไว้ข้างตัวละครของคุณ เพื่อให้การเล่นคนเดียวดูเหมือนมีทีม ใช้ได้เฉพาะโหมดผู้เล่นคนเดียว และเป็นการตกแต่งเท่านั้น",
            ["members_label"] = "จำนวนสมาชิก Squad",
            ["cfg_dim_label"] = "ทำให้แถวหลังมืดลง",
            ["cfg_dim_desc"] = "ทำให้ร่างที่ยืนอยู่หลังแถวหน้ามืดลง เช่นเดียวกับที่เกมทำกับเพื่อนร่วมทีมที่อยู่ไกลในโหมดร่วมมือ",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — คลิกเพื่อตั้งค่า ลากเพื่อย้ายตำแหน่ง",
            ["tip_click"] = "Skin Squad — คลิกเพื่อตั้งค่า",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "ปิด",
            ["modal_empty"] = "Squad ว่างเปล่า\nกด + ด้านบนเพื่อเพิ่มสมาชิก",
            ["modal_no_skins"] = "ไม่พบม็อดสกินตัวละคร — แสดงเฉพาะตัวละครของเกมเท่านั้น\nติดตั้งม็อดสกินแล้วเริ่มเกมใหม่เพื่อเพิ่มในรายการนี้",
            ["look_self"] = "เหมือนกับฉัน",
            ["look_self_caption"] = "เหมือนกับฉัน\n(ตามตัวละครที่คุณเลือก)",
            ["look_random"] = "สุ่ม",
            ["look_missing"] = "ไม่พร้อมใช้งาน — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "ชื่อพรีเซ็ต",
            ["preset_delete"] = "ลบ",
            ["preset_save"] = "บันทึก",
            ["preset_new_tip"] = "พรีเซ็ตใหม่",
            ["source_vanilla"] = "ดั้งเดิม",
        },
        ["TUR"] = new()
        {
            ["cfg_enabled_label"] = "Squad'ı etkinleştir",
            ["cfg_enabled_desc"] = "Karakterinizin yanına yalnızca görsel takım arkadaşları dizer; böylece tek oyunculu bir oyun takım gibi görünür. Sadece tek oyunculu; tamamen kozmetik.",
            ["members_label"] = "Squad üyeleri",
            ["cfg_dim_label"] = "Arka sıraları karart",
            ["cfg_dim_desc"] = "Ön sıranın arkasında duran üyeleri karartır; oyunun ortak oyunda uzaktaki takım arkadaşlarını karartmasıyla aynı şekilde.",
            ["btn_squad"] = "Squad",
            ["tip_drag"] = "Skin Squad — yapılandırmak için tıklayın, taşımak için sürükleyin.",
            ["tip_click"] = "Skin Squad — yapılandırmak için tıklayın.",
            ["modal_title"] = "Skin Squad",
            ["modal_close"] = "Kapat",
            ["modal_empty"] = "Squad boş.\nÜye eklemek için yukarıdaki + düğmesini kullanın.",
            ["modal_no_skins"] = "Karakter görünüm modu bulunamadı — yalnızca oyunun kendi karakterleri listelenir.\nGörünüm modları yükleyip oyunu yeniden başlatın.",
            ["look_self"] = "Benimle aynı",
            ["look_self_caption"] = "Benimle aynı\n(seçtiğiniz karaktere göre)",
            ["look_random"] = "Rastgele",
            ["look_missing"] = "Kullanılamıyor — {0}",
            ["preset_default_name"] = "Squad {0}",
            ["preset_name_placeholder"] = "ön ayar adı",
            ["preset_delete"] = "Sil",
            ["preset_save"] = "Kaydet",
            ["preset_new_tip"] = "Yeni ön ayar",
            ["source_vanilla"] = "orijinal",
        },
    };
}
