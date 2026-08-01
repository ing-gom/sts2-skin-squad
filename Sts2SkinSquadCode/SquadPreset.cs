using System;
using System.Collections.Generic;
using System.Linq;

namespace Sts2SkinSquad;

/// <summary>
/// One saved squad line-up.
///
/// Presets exist because a squad that suits one character rarely suits the next: without them the
/// only way to switch was to re-pick every member by hand each run. Each preset keeps its own size
/// and its own per-member looks, and switching between them is immediate.
/// </summary>
internal sealed class SquadPreset
{
    public SquadPreset(string name, int count, IEnumerable<string>? slots = null)
    {
        Name = name;
        Count = Math.Clamp(count, 0, SkinSquadService.MaxDoubles);
        Slots = Normalize(slots);
    }

    public string Name { get; set; }

    public int Count { get; set; }

    /// <summary>Always exactly <see cref="SkinSquadService.MaxDoubles"/> entries, so callers can
    /// index it without checking — a short array from an older config is padded on load.</summary>
    public string[] Slots { get; }

    private static string[] Normalize(IEnumerable<string>? slots)
    {
        var result = new string[SkinSquadService.MaxDoubles];
        List<string> given = slots?.ToList() ?? new List<string>();
        for (int i = 0; i < result.Length; i++)
            result[i] = i < given.Count && !string.IsNullOrWhiteSpace(given[i]) ? given[i] : Appearance.SelfToken;
        return result;
    }

    /// <summary>
    /// A new preset, deliberately unnamed. The tab strip renders a blank name as a translated
    /// "Squad N" derived from its position, so storing that text here instead would freeze the
    /// label into whichever language the preset was created in — and it would be a name the user
    /// never typed, sitting in the rename box as if they had.
    /// </summary>
    public static SquadPreset Default() =>
        new(string.Empty, SkinSquadService.DefaultDoubles);
}
