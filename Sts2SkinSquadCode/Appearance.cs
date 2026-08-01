using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Sts2SkinSquad.Localization;

namespace Sts2SkinSquad;

/// <summary>
/// What a single squad member looks like. Three sources, cheapest first:
/// <list type="bullet">
/// <item><b>Self</b> — the character you are playing, exactly as you look.</item>
/// <item><b>Character</b> — another vanilla character's stock body. Free: the game already ships
/// every character's combat scene, so this needs no extraction at all.</item>
/// <item><b>Skin</b> — a skin mod's spine data applied to the body it was built for, via a
/// per-instance skeleton swap (see <see cref="SkinCatalog"/>).</item>
/// </list>
/// </summary>
internal readonly struct Appearance
{
    public const string SelfToken = "Self";
    public const string RandomToken = "Random";

    public enum Kind { Self, Character, Skin }

    private Appearance(Kind kind, string value) { Source = kind; Value = value; }

    public Kind Source { get; }

    /// <summary>Character id for <see cref="Kind.Character"/>, variant id for <see cref="Kind.Skin"/>.</summary>
    public string Value { get; }

    public static Appearance Self() => new(Kind.Self, SelfToken);
    public static Appearance Character(string characterId) => new(Kind.Character, characterId);
    public static Appearance Skin(string variantId) => new(Kind.Skin, variantId);

    /// <summary>Parses a config token back into an appearance, falling back to Self.</summary>
    public static Appearance Parse(string? token, Random? rng = null)
    {
        if (string.IsNullOrWhiteSpace(token) || token == SelfToken) return Self();
        if (token == RandomToken) return PickRandom(rng);
        if (SkinCatalog.Find(token) != null) return Skin(token);
        if (AllCharacterIds().Any(id => string.Equals(id, token, StringComparison.OrdinalIgnoreCase)))
            return Character(token);
        return Self();
    }

    private static Appearance PickRandom(Random? rng)
    {
        // Draws from the same list the picker shows, so Random cannot land on something that is not
        // offered — or pick a duplicate entry that the list itself already excludes.
        string[] pool = Options().Where(o => o != RandomToken).ToArray();
        if (pool.Length == 0) return Self();
        rng ??= new Random();
        string pick = pool[rng.Next(pool.Length)];
        return pick == SelfToken ? Self() : Parse(pick, rng);
    }

    /// <summary>Config dropdown options, in the order the settings UI should show them.</summary>
    public static string[] Options()
    {
        var opts = new List<string> { SelfToken, RandomToken };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Every look the catalog found: the game's own art for each character, plus each mod skin.
        // Grouped by character with vanilla first (see SkinCatalog.Scan).
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SkinVariant v in SkinCatalog.Variants)
        {
            if (!seen.Add(v.Id)) continue;
            opts.Add(v.Id);
            if (v.IsVanilla) covered.Add(v.CharacterId);
        }

        // Characters the catalog has no vanilla entry for — custom characters from character mods,
        // which live in their own namespace rather than the base archive. Listed by plain id so they
        // are still selectable. Base characters are NOT added here: they already appear above as a
        // vanilla entry, and adding them again is exactly the duplicate that made two list positions
        // render the same figure.
        foreach (string id in AllCharacterIds())
            if (!covered.Contains(id) && seen.Add(id)) opts.Add(id);

        return opts.ToArray();
    }

    /// <summary>
    /// Human-readable name for a token. Skin ids look like <c>necrobinderSkin:necrobinder</c>, which
    /// is fine as a stable config key and terrible to read, so the catalog's label is used instead.
    /// </summary>
    public static string DisplayName(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token == SelfToken) return Strings.Get("look_self");
        if (token == RandomToken) return Strings.Get("look_random");

        SkinVariant? variant = SkinCatalog.Find(token);
        if (variant != null)
        {
            // "Ironclad · vanilla" / "Ironclad · Mesugaki" — character first so a run of skins for
            // one character reads as a group, source second so they are told apart at a glance.
            // Only the vanilla marker is translated; a skin mod's own name is left as the author
            // wrote it, which is how a player recognises it in their mod list.
            string who = CharacterTitle(variant.CharacterId) ?? variant.CharacterId;
            string source = variant.SourceName == SkinCatalog.VanillaSource
                ? Strings.Get("source_vanilla")
                : variant.SourceName;
            return $"{who} · {source}";
        }

        // Nothing in the catalog and no such character: the skin (or character) mod that provided
        // this look is not loaded right now. The token is deliberately NOT rewritten here — the mod
        // may simply be mid-download, switched off for one session, or missed by a pck scan, and
        // silently resetting it would destroy a line-up the player cannot get back. Parse() already
        // renders these as Self, so this label is what makes the panel agree with the figure; the
        // raw id rides along because it is the only clue left as to which mod to reinstall.
        return CharacterTitle(token) ?? Strings.Get("look_missing", token!);
    }

    /// <summary>Thumbnail for the picker, or null when nothing suitable is available.</summary>
    public static Texture2D? Preview(string? token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token) || token == SelfToken || token == RandomToken) return null;

            SkinVariant? variant = SkinCatalog.Find(token);
            if (variant != null)
                return SkinCatalog.TryPreview(variant) ?? FindCharacter(variant.CharacterId)?.IconTexture;

            return FindCharacter(token)?.IconTexture;
        }
        catch { return null; }
    }

    /// <summary>Character model for an id, or null. Public for the preview panel.</summary>
    public static CharacterModel? FindCharacterModel(string id) => FindCharacter(id);

    /// <summary>Localised character name, or null when the id is not a known character.</summary>
    private static string? CharacterTitle(string id)
    {
        // ★The stock name first, straight from the game's archive. The live table is not safe here:
        // a skin mod that renames the character it replaces overwrites that entry, so with the raye
        // skin installed the VANILLA option read "Sky Striker Raye" — the one name it must not.
        // Every entry is "<vanilla character> · <source>", so the left half has to stay vanilla.
        string? stock = VanillaNames.TitleFor(id);
        if (!string.IsNullOrWhiteSpace(stock)) return stock;

        // Custom characters have no base-game entry; their live title is the only name they have.
        CharacterModel? model = FindCharacter(id);
        if (model == null) return null;
        // GetFormattedText, not ToString: the latter prints a debug representation into the UI.
        try { return model.Title.GetFormattedText(); }
        catch { return model.Id.Entry; }
    }

    private static IEnumerable<string> AllCharacterIds()
    {
        // ModelDb.AllCharacters is derived from the character list and throws if a character mod is
        // broken, so a failure here degrades to "no other characters offered" rather than killing
        // the settings screen.
        try { return ModelDb.AllCharacters.Select(c => c.Id.Entry).ToList(); }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] character list unavailable: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Which character's rig this appearance uses. Null means "the character being played", which
    /// the caller resolves through the player entity.
    ///
    /// A skin resolves to the character it was authored for: it replaces one specific rig, so it
    /// has to sit on that body — bone names would not line up on anyone else's.
    /// </summary>
    public CharacterModel? ResolveCharacter()
    {
        try
        {
            return Source switch
            {
                Kind.Character => FindCharacter(Value),
                Kind.Skin => FindCharacter(SkinCatalog.Find(Value)?.CharacterId ?? ""),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] appearance '{Value}' character lookup failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Builds the spine actor's node. Falls back to the played character's own visuals whenever
    /// anything is missing, so a squad member never silently vanishes.
    ///
    /// A skin is NOT applied here — see <see cref="ApplySkin"/>. Swapping skeleton data needs the
    /// node's <c>_Ready</c> to have run (that is where <c>SpineBody</c> is built), which only
    /// happens once it is added to the tree.
    /// </summary>
    public NCreatureVisuals? CreateBody(Creature playerEntity)
    {
        try
        {
            NCreatureVisuals? body = ResolveCharacter()?.CreateVisuals();
            if (body != null) return body;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] appearance '{Value}' body failed: {ex.Message}");
        }

        return playerEntity.CreateVisuals();
    }

    /// <summary>
    /// Applies the skin's combat skeleton to an actor already in the scene tree. No-op for the Self
    /// and Character sources. Failures are logged and leave the stock body in place.
    /// </summary>
    public void ApplySkin(NCreatureVisuals body)
    {
        RigFiles? files = RigFor(SkinRig.Combat);
        if (files == null) return;
        if (!SpineSwap.Apply(body, files.AtlasFile, files.SkelFile))
            MainFile.Logger.Warn($"[{MainFile.ModId}] skin '{Value}' did not apply; keeping the stock body.");
    }

    /// <summary>
    /// Extracted files for one rig, or null when this appearance is not a skin, the skin does not
    /// ship that rig, or extraction failed. A skin with a combat body but no campfire body is
    /// completely normal, so callers just fall back to the vanilla rig.
    /// </summary>
    public RigFiles? RigFor(SkinRig rig)
    {
        if (Source != Kind.Skin) return null;
        try
        {
            SkinVariant? variant = SkinCatalog.Find(Value);
            if (variant == null || !variant.Has(rig)) return null;
            return SkinCatalog.TryMaterialize(variant, rig);   // logs its own failure reason
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] skin '{Value}' {rig} rig threw: {ex.Message}");
            return null;
        }
    }

    private static CharacterModel? FindCharacter(string id)
    {
        try { return ModelDb.AllCharacters.FirstOrDefault(c => string.Equals(c.Id.Entry, id, StringComparison.OrdinalIgnoreCase)); }
        catch { return null; }
    }

    public override string ToString() => Source == Kind.Self ? SelfToken : $"{Source}:{Value}";
}
