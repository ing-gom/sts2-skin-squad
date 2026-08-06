using System;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using Sts2.ModKit.Bootstrap;
using Sts2.ModKit.Config;
using Sts2SkinSquad.Localization;

namespace Sts2SkinSquad;

/// <summary>
/// Entry point. Single-player cosmetic mod: stands visual-only copies of your character next to
/// you using the game's own co-op party geometry. No combat entity, no run state, no network
/// traffic — the doubles are spine actors and nothing else.
/// </summary>
[ModInitializer(nameof(Initialize))]
public class MainFile
{
    public const string ModId = "Sts2SkinSquad";

    private const string EntryKeyEnabled = "enabled";
    private const string EntryKeyDimBackRows = "dim_back_rows";
    private const string EntryKeySwapFront = "swap_front";

    public static readonly MegaCrit.Sts2.Core.Logging.Logger Logger
        = ModBootstrap.CreateLogger(ModId);

    public static void Initialize() =>
        ModBootstrap.Run(ModId, Logger, typeof(MainFile).Assembly, body: () =>
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;

            // Our own file is the source of truth; load before anything can read the values.
            SquadConfig.Load();

            // Defer config registration so ModConfig/RitsuLib finish their own Initialize first.
            tree.CreateTimer(0.0).Timeout += RegisterConfig;

#if SKINSQUAD_SELFTEST
            // Development only — the harness is compiled out of release builds entirely, so this
            // call cannot exist there. No-op unless selftest.sp.flag sits next to the DLL.
            SoloTest.ArmIfRequested();
#endif
        });

    /// <summary>
    /// Attaches per-language label and description text to the entry just added to
    /// <paramref name="builder"/>.
    ///
    /// ★WHY REFLECTION. ModKit resolves first-wins across every installed sister mod's bundled
    /// copy, so on a player's machine an older Sts2.ModKit without <c>LocalizedLabels</c> can
    /// shadow ours. A direct call would then throw <c>MissingMethodException</c> when this method
    /// is JITted and take the whole config registration down with it; through reflection an old
    /// ModKit merely means the settings stay English.
    /// </summary>
    private static void Localize(ConfigEntryBuilder builder, string labelKey, string descriptionKey)
    {
        try
        {
            Type t = builder.GetType();
            t.GetMethod("LocalizedLabels")?.Invoke(builder, new object[] { Strings.Map(labelKey) });
            t.GetMethod("LocalizedDescriptions")?.Invoke(builder, new object[] { Strings.Map(descriptionKey) });
        }
        catch (Exception ex)
        {
            Logger.Info($"[{ModId}] settings localization skipped (older ModKit loaded): {ex.Message}");
        }
    }

    private static void RegisterConfig()
    {
        // Scanning other mods' .pck files is file I/O; warm the catalog here so the first time the
        // picker is opened it is already built, rather than reading pcks on the UI thread.
        string[] looks = Appearance.Options();

        // Snapshot what the file gave us: Register() replays the framework's own stored values
        // through the callbacks, which would otherwise clobber them before we can re-apply.
        bool enabled = SkinSquadService.Enabled;
        bool dim = SkinSquadService.DimBackRows;
        bool swap = SkinSquadService.SwapFront;

        SquadConfig.Suppressed = true;
        try
        {
            // Per-entry statements rather than one fluent chain: Localize() needs the builder
            // reference, and it applies to whichever entry was appended last.
            //
            // English text is passed inline and stays the fallback for any language missing from
            // the tables; every other language comes from Strings.
            var b = ModConfigBridge.For(ModId, "Skin Squad", Logger);

            b.Toggle(EntryKeyEnabled, Strings.Get("cfg_enabled_label"),
                    defaultValue: enabled,
                    onChanged: v => { SkinSquadService.Enabled = v; SquadConfig.Save(); })
                .Description(Strings.Get("cfg_enabled_desc"));
            Localize(b, "cfg_enabled_label", "cfg_enabled_desc");

            // ★No squad-size entry here. Size belongs to a preset, and this screen has exactly one
            // slider — it would display the framework's own stored number (which the picker cannot
            // update, since ModConfigBridge exposes no setter) while silently writing to whichever
            // preset happens to be active. Only genuinely global switches live here now; size and
            // per-member looks are owned by the picker, which can show which preset it is editing.
            b.Toggle(EntryKeyDimBackRows, Strings.Get("cfg_dim_label"),
                    defaultValue: dim,
                    onChanged: v => { SkinSquadService.DimBackRows = v; SquadConfig.Save(); })
                .Description(Strings.Get("cfg_dim_desc"));
            Localize(b, "cfg_dim_label", "cfg_dim_desc");

            b.Toggle(EntryKeySwapFront, Strings.Get("cfg_swap_label"),
                    defaultValue: swap,
                    onChanged: v => { SkinSquadService.SwapFront = v; SquadConfig.Save(); })
                .Description(Strings.Get("cfg_swap_desc"));
            Localize(b, "cfg_swap_label", "cfg_swap_desc");

            b.Register();
        }
        finally
        {
            SquadConfig.Suppressed = false;
        }

        // Re-apply the file's values: whatever the framework replayed during Register() is not
        // authoritative here, because the in-game modal writes only to the file.
        SkinSquadService.Enabled = enabled;
        SkinSquadService.DimBackRows = dim;
        SkinSquadService.SwapFront = swap;

        Logger.Info($"[{ModId}] active (enabled={SkinSquadService.Enabled}, count={SkinSquadService.DoubleCount}, " +
                    $"slots=[{string.Join(", ", SkinSquadService.SlotTokens)}], " +
                    $"dimBackRows={SkinSquadService.DimBackRows}, swapFront={SkinSquadService.SwapFront}); " +
                    $"{looks.Length} appearance option(s): [{string.Join(", ", looks)}].");
    }
}
