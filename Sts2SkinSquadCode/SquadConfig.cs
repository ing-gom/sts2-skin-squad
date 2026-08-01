using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using Godot;

namespace Sts2SkinSquad;

/// <summary>
/// The mod's own settings file, and the single source of truth for them.
///
/// Why not just lean on the settings framework: <c>ModConfigBridge</c> can read a stored value but
/// exposes no way to write one, so the in-game squad modal would have nowhere to persist a choice.
/// Adding a setter would mean a new ModKit API — and ModKit resolves first-wins across mods, so a
/// new API is unusable until every other installed consumer is rebuilt (learned the hard way when
/// the pck reader was briefly moved there).
///
/// So this file owns the values; the settings entries and the modal are two editors of it, both
/// writing here on change. Lives in the game's user data directory, never in the mod folder, since
/// a Workshop upload packages the mod folder.
/// </summary>
internal static class SquadConfig
{
    private const string FileName = "skin_squad.json";

    /// <summary>Set while the settings framework replays its stored values during registration, so
    /// its callbacks cannot overwrite the file before our own values are applied.</summary>
    public static bool Suppressed { get; set; }

    private sealed class PresetDto
    {
        public string name { get; set; } = "";
        public int count { get; set; } = SkinSquadService.DefaultDoubles;
        public List<string> slots { get; set; } = new();
    }

    private sealed class Dto
    {
        public bool enabled { get; set; } = true;
        public bool dimBackRows { get; set; } = true;

        /// <summary>Every saved line-up, in tab order. Absent from files written before presets
        /// existed, which is what the migration in <see cref="Load"/> keys off.</summary>
        public List<PresetDto> presets { get; set; } = new();
        public int activePreset { get; set; }

        /// <summary>
        /// The pre-preset shape: one line-up, stored flat. Kept for two reasons — <see cref="Load"/>
        /// migrates from it when <c>presets</c> is missing, and <see cref="Save"/> keeps it mirroring
        /// the active preset so that downgrading the mod finds a line-up rather than a default one.
        /// <c>presets</c> wins whenever it is present, so the two can never be read as contradictory.
        /// </summary>
        public int count { get; set; } = SkinSquadService.DefaultDoubles;
        public List<string> slots { get; set; } = new();

        /// <summary>Where the user dragged the standalone button, as a delta from its default
        /// anchored spot. A delta rather than an absolute position so it still means something at a
        /// different resolution; it is re-clamped on load regardless.</summary>
        public float buttonDx { get; set; }
        public float buttonDy { get; set; }
    }

    /// <summary>Saved drag offset for the standalone Squad button.</summary>
    public static Vector2 ButtonDelta { get; set; } = Vector2.Zero;

    private static string Path_ =>
        System.IO.Path.Combine(SafeUserDir(), FileName);

    private static string SafeUserDir()
    {
        try
        {
            string dir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        catch { /* fall through */ }
        return System.IO.Path.GetTempPath();
    }

    /// <summary>Reads the file into <see cref="SkinSquadService"/>. Missing file = defaults kept.</summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(Path_)) return;
            Dto? dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(Path_));
            if (dto == null) return;

            SkinSquadService.Enabled = dto.enabled;
            SkinSquadService.DimBackRows = dto.dimBackRows;
            ButtonDelta = new Vector2(dto.buttonDx, dto.buttonDy);

            // The constructor clamps the count and pads the slot array, so a hand-edited or
            // truncated entry cannot produce a preset the UI would index out of range.
            List<PresetDto> saved = dto.presets ?? new List<PresetDto>();
            bool migrated = saved.Count == 0;

            SkinSquadService.Presets.Clear();
            if (migrated)
            {
                // A file written before presets existed: its single line-up becomes preset one.
                // Left unnamed so the tab shows the translated default rather than a name in a
                // language the user never chose.
                SkinSquadService.Presets.Add(new SquadPreset(string.Empty, dto.count, dto.slots));
            }
            else
            {
                foreach (PresetDto p in saved)
                    SkinSquadService.Presets.Add(new SquadPreset(p.name ?? string.Empty, p.count, p.slots));
            }

            // Never leave the list empty — the active preset IS the live configuration, so an
            // empty "presets": [] in the file would otherwise leave the combat code nothing to read.
            if (SkinSquadService.Presets.Count == 0) SkinSquadService.Presets.Add(SquadPreset.Default());

            SkinSquadService.ActivePreset = dto.activePreset;   // the setter clamps into range

            MainFile.Logger.Info($"[{MainFile.ModId}] settings loaded from {FileName} " +
                                 $"({SkinSquadService.Presets.Count} preset(s), active {SkinSquadService.ActivePreset}" +
                                 $"{(migrated ? ", migrated from a pre-preset file" : "")}).");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] could not read {FileName} ({ex.Message}); using defaults.");
        }
    }

    /// <summary>Writes the current in-memory settings. No-op while <see cref="Suppressed"/>.</summary>
    public static void Save()
    {
        if (Suppressed) return;
        try
        {
            SquadPreset active = SkinSquadService.Active;
            var dto = new Dto
            {
                enabled = SkinSquadService.Enabled,
                dimBackRows = SkinSquadService.DimBackRows,

                presets = SkinSquadService.Presets
                    .Select(p => new PresetDto
                    {
                        name = p.Name,
                        count = p.Count,
                        slots = new List<string>(p.Slots),
                    })
                    .ToList(),
                activePreset = SkinSquadService.ActivePreset,

                // Mirror of the active preset, for an older build reading this file. See Dto.
                count = active.Count,
                slots = new List<string>(active.Slots),

                buttonDx = ButtonDelta.X,
                buttonDy = ButtonDelta.Y,
            };
            File.WriteAllText(Path_, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] could not write {FileName}: {ex.Message}");
        }
    }
}
