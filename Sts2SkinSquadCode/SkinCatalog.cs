using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Sts2SkinSquad.Pck;

namespace Sts2SkinSquad;

/// <summary>
/// The three separate Spine rigs a character has. They are different skeletons with different
/// animations, not poses of one rig, so a skin mod may ship any subset of them.
/// </summary>
internal enum SkinRig
{
    /// <summary>Combat body: <c>animations/characters/{char}/{char}</c>.</summary>
    Combat,
    /// <summary>Campfire body: <c>animations/rest_site/{char}/restsite_{char}</c>, act-specific loops.</summary>
    RestSite,
    /// <summary>Shop body: <c>animations/merchant/{char}/{char}_shop</c>.</summary>
    Shop,
}

/// <summary>
/// Where one rig lives inside a pck. The byte sizes are carried along as a cheap content
/// signature: the md5 in a Godot imported filename hashes the SOURCE PATH, not the bytes, so every
/// mod overriding the same character has an identical hash and it cannot identify a skin.
/// </summary>
internal sealed record RigSource(string SkelEntry, string AtlasEntry, long SkelSize, long AtlasSize);

/// <summary>Extracted, loadable file paths for one rig.</summary>
internal sealed record RigFiles(string AtlasFile, string SkelFile);

/// <summary>One character skin found inside some other mod's .pck, ready to be extracted.</summary>
internal sealed class SkinVariant
{
    public string Id { get; init; } = "";          // stable key used in config, e.g. "necrobinderSkin:necrobinder"
    public string Label { get; init; } = "";       // fallback display name
    public string CharacterId { get; init; } = ""; // vanilla character whose body it replaces, upper-case
    public string PckPath { get; init; } = "";

    /// <summary>Where the art came from: a mod's file name, or "vanilla" for the base game.</summary>
    public string SourceName { get; init; } = "";

    /// <summary>
    /// True for art read out of the game's own pck.
    ///
    /// These matter because a mounted skin REPLACES the stock character: with ironcladSkin
    /// installed, the plain "Ironclad" body already renders as that skin, so listing the character
    /// and the skin separately showed the same figure twice. Extracting the base skeleton gives a
    /// genuinely vanilla option that no mounted mod can shadow.
    /// </summary>
    public bool IsVanilla { get; init; }

    /// <summary>Rigs this skin actually ships. A skin with only a combat body is normal.</summary>
    public Dictionary<SkinRig, RigSource> Rigs { get; } = new();

    /// <summary>Filled lazily by <see cref="SkinCatalog.TryMaterialize"/>, per rig.</summary>
    public Dictionary<SkinRig, RigFiles> Extracted { get; } = new();

    /// <summary>Rigs whose extraction failed, so a broken one is not retried every room.</summary>
    public HashSet<SkinRig> Failed { get; } = new();

    public bool Has(SkinRig rig) => Rigs.ContainsKey(rig);
}

/// <summary>
/// Finds character skins shipped by other mods and unpacks them into files the bundled spine-godot
/// runtime can load directly.
///
/// This works because of what those .pck files actually contain, which is better than expected:
/// <list type="bullet">
/// <item><c>.spskel</c> is a RAW Spine 4.2.43 binary skeleton, with no Godot resource wrapper.</item>
/// <item><c>.spatlas</c> is a thin JSON wrapper holding the original libgdx atlas text under
/// <c>atlas_data</c>.</item>
/// <item><c>.ctex</c> is a Godot header followed by an embedded PNG or WebP.</item>
/// </list>
/// So "extraction" is: write the skeleton bytes verbatim, write <c>atlas_data</c> as a .atlas file,
/// and decode each atlas page's .ctex back to a .png named exactly as the atlas declares.
///
/// Skins are NOT mounted. Mounting is what makes two skins for one character impossible — they claim
/// the same res:// paths and the last mount wins. Reading raw bytes sidesteps that entirely, which is
/// what lets several doubles of the same character each wear a different skin.
/// </summary>
internal static class SkinCatalog
{
    /// <summary>Vanilla characters, matched case-insensitively against skeleton file names.</summary>
    private static readonly string[] KnownCharacters =
        { "ironclad", "silent", "defect", "watcher", "regent", "necrobinder" };

    /// <summary>Source label used for art taken from the game's own pck.</summary>
    public const string VanillaSource = "vanilla";

    private const string ImportedDir = ".godot/imported/";
    private const string ResPrefix = "res://";
    private const string CacheDirName = "skin_squad_cache";

    /// <summary>
    /// Filename of an entry inside <c>.godot/imported/</c>, or null if it is not one.
    ///
    /// ★Pcks are inconsistent about the <c>res://</c> prefix: some store
    /// <c>res://.godot/imported/x.spskel</c>, others <c>.godot/imported/x.spskel</c>. Matching only
    /// the prefixed form silently dropped four installed skin mods from the catalog, which showed up
    /// as "the picker barely lists anything". (The pck reader's own tail-scan hints list both forms,
    /// which was the clue.)
    /// </summary>
    private static string? ImportedName(string key)
    {
        string s = key.StartsWith(ResPrefix, StringComparison.OrdinalIgnoreCase)
            ? key.Substring(ResPrefix.Length)
            : key;
        return s.StartsWith(ImportedDir, StringComparison.OrdinalIgnoreCase)
            ? s.Substring(ImportedDir.Length)
            : null;
    }

    private static List<SkinVariant>? _variants;

    /// <summary>All discovered skins. Scans on first call; the result is cached for the session.</summary>
    public static IReadOnlyList<SkinVariant> Variants => _variants ??= Scan();

    public static SkinVariant? Find(string id)
        => Variants.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<SkinVariant> Scan()
    {
        var found = new List<SkinVariant>();
        var unreadable = new List<string>();
        int scanned = 0;

        foreach (string pck in EnumeratePcks())
        {
            scanned++;
            try { if (!ScanPck(pck, found)) unreadable.Add(Path.GetFileName(pck)); }
            catch (Exception ex) { MainFile.Logger.Warn($"[{MainFile.ModId}] skin scan failed for {Path.GetFileName(pck)}: {ex.Message}"); }
        }

        // Counted and named, not assumed: "3 variants" reads the same whether the scan covered 21
        // archives or 3, and those mean very different things when a user reports a missing skin.
        MainFile.Logger.Info($"[{MainFile.ModId}] scanned {scanned} pck(s); unreadable: " +
                             (unreadable.Count == 0 ? "none" : string.Join(", ", unreadable)));

        // The same mod is often installed twice — once unpacked under mods/, once as a Workshop
        // subscription — which would list it twice in the picker and make the id ambiguous. Keep the
        // first, i.e. the mods/ copy, matching the game's own local-wins load order.
        // Deduplicated by id AND by content signature, because the two copies frequently have
        // different filenames, which makes the id alone useless for spotting them.
        var deduped = new List<SkinVariant>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenContent = new HashSet<string>(StringComparer.Ordinal);
        int dropped = 0;
        foreach (SkinVariant v in found)
        {
            if (!seenIds.Add(v.Id) || !seenContent.Add(ContentKey(v))) { dropped++; continue; }
            deduped.Add(v);
        }
        if (dropped > 0) MainFile.Logger.Info($"[{MainFile.ModId}] dropped {dropped} duplicate variant(s) (same mod installed twice).");

        // Grouped by character with vanilla leading each run, so holding the arrow walks one
        // character's looks at a time instead of hopping between them.
        deduped = deduped
            .OrderBy(v => v.CharacterId, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(v => v.IsVanilla)
            .ThenBy(v => v.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        MainFile.Logger.Info($"[{MainFile.ModId}] skin catalog: {deduped.Count} variant(s) " +
                             $"[{string.Join(", ", deduped.Select(v => $"{v.Id}<{string.Join("+", v.Rigs.Keys)}>"))}]");
        return deduped;
    }

    /// <summary>
    /// Identity of the art a variant actually carries: the character it replaces plus the byte sizes
    /// of every rig it ships. Two installs of one mod match; two genuinely different skins for the
    /// same character do not (measured across eight installed ironclad / necrobinder / regent skins,
    /// all eight skeleton sizes were distinct).
    /// </summary>
    private static string ContentKey(SkinVariant v)
        => v.CharacterId + "|" + string.Join(",", v.Rigs.OrderBy(r => r.Key)
                                                  .Select(r => $"{r.Key}:{r.Value.SkelSize}:{r.Value.AtlasSize}"));

    private static bool ScanPck(string pckPath, List<SkinVariant> into)
    {
        Dictionary<string, PckEntry>? index = PckFileExtractor.TryReadIndex(pckPath);
        if (index == null) return false;

        string modName = Path.GetFileNameWithoutExtension(pckPath);
        bool isVanilla = string.Equals(modName, "SlayTheSpire2", StringComparison.OrdinalIgnoreCase);
        string source = isVanilla ? VanillaSource : modName;

        // Skeleton stems present in this pck, e.g. "necrobinder", "restsite_regent", "ironclad_shop".
        var stems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in index.Keys)
        {
            if (!key.EndsWith(".spskel", StringComparison.OrdinalIgnoreCase)) continue;
            string? file = ImportedName(key);
            if (file == null) continue;
            int dash = file.IndexOf(".skel-", StringComparison.OrdinalIgnoreCase);
            if (dash > 0) stems[file.Substring(0, dash)] = key;
        }
        if (stems.Count == 0) return true;

        foreach (string character in KnownCharacters)
        {
            var variant = new SkinVariant
            {
                Id = $"{source}:{character}",
                Label = $"{source} ({character})",
                CharacterId = character.ToUpperInvariant(),
                PckPath = pckPath,
                SourceName = source,
                IsVanilla = isVanilla,
            };

            // characterselect_* is deliberately not collected: the squad never appears on that screen.
            AddRig(variant, SkinRig.Combat, character);
            AddRig(variant, SkinRig.RestSite, "restsite_" + character);
            AddRig(variant, SkinRig.Shop, character + "_shop");

            // A skin with no combat rig is not a squad-usable character skin, whatever else it holds.
            if (variant.Has(SkinRig.Combat)) into.Add(variant);

            void AddRig(SkinVariant v, SkinRig rig, string stem)
            {
                if (!stems.TryGetValue(stem, out string? skelKey)) return;
                string? atlasKey = index.Keys.FirstOrDefault(k =>
                    k.EndsWith(".spatlas", StringComparison.OrdinalIgnoreCase) &&
                    ImportedName(k)?.StartsWith(stem + ".atlas-", StringComparison.OrdinalIgnoreCase) == true);
                if (atlasKey == null) return;
                v.Rigs[rig] = new RigSource(skelKey, atlasKey, index[skelKey].Size, index[atlasKey].Size);
            }
        }

        return true;
    }

    /// <summary>
    /// Unpacks one of a variant's rigs to disk on first use and returns the loadable file paths.
    /// Idempotent; a rig that fails once is marked and never retried.
    /// </summary>
    public static RigFiles? TryMaterialize(SkinVariant variant, SkinRig rig)
    {
        if (variant.Failed.Contains(rig)) return null;
        if (variant.Extracted.TryGetValue(rig, out RigFiles? done)) return done;
        if (!variant.Rigs.TryGetValue(rig, out RigSource? source)) return null;

        try
        {
            string dir = Path.Combine(CacheRoot(), Sanitize(variant.Id), rig.ToString().ToLowerInvariant());
            Directory.CreateDirectory(dir);

            Dictionary<string, PckEntry>? index = PckFileExtractor.TryReadIndex(variant.PckPath);
            if (index == null) return Fail(variant, rig, "pck index unreadable");

            // 1. Skeleton: raw Spine binary, written verbatim. The .skel extension matters —
            //    spine-godot rejects unknown extensions before it ever parses the bytes.
            if (!index.TryGetValue(source.SkelEntry, out PckEntry? skelEntry)) return Fail(variant, rig, "skeleton entry missing");
            byte[]? skelBytes = PckFileExtractor.TryRead(variant.PckPath, skelEntry);
            if (skelBytes == null) return Fail(variant, rig, "skeleton unreadable");
            string skelPath = Path.Combine(dir, "skin.skel");
            File.WriteAllBytes(skelPath, skelBytes);

            // 2. Atlas: unwrap the JSON and write the original libgdx text.
            if (!index.TryGetValue(source.AtlasEntry, out PckEntry? atlasEntry)) return Fail(variant, rig, "atlas entry missing");
            byte[]? atlasBytes = PckFileExtractor.TryRead(variant.PckPath, atlasEntry);
            if (atlasBytes == null) return Fail(variant, rig, "atlas unreadable");

            string atlasText;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(atlasBytes);
                atlasText = doc.RootElement.GetProperty("atlas_data").GetString() ?? "";
            }
            catch (Exception ex) { return Fail(variant, rig, $"atlas json: {ex.Message}"); }
            if (atlasText.Length == 0) return Fail(variant, rig, "atlas_data empty");

            string atlasPath = Path.Combine(dir, "skin.atlas");
            File.WriteAllText(atlasPath, atlasText);

            // 3. Pages: every page name declared in the atlas must exist beside it, under exactly
            //    the declared name — the atlas text is what spine-godot resolves against.
            int pages = 0;
            foreach (string page in PageNames(atlasText))
            {
                if (!TryWritePage(variant, index, dir, page)) return Fail(variant, rig, $"page '{page}' unavailable");
                pages++;
            }
            if (pages == 0) return Fail(variant, rig, "atlas declares no pages");

            var files = new RigFiles(atlasPath, skelPath);
            variant.Extracted[rig] = files;
            MainFile.Logger.Info($"[{MainFile.ModId}] materialized '{variant.Id}' {rig} rig ({pages} page(s)) -> {dir}");
            return files;
        }
        catch (Exception ex) { return Fail(variant, rig, ex.Message); }
    }

    private static readonly Dictionary<string, Texture2D?> _previewCache = new();

    /// <summary>
    /// Thumbnail for the picker, taken from the skin's own character-select art inside its pck.
    /// Null when the skin does not override that image, in which case the caller falls back to the
    /// base character's icon. Cached (including misses) so a picker redraw is not a pck re-read.
    /// </summary>
    public static Texture2D? TryPreview(SkinVariant variant)
    {
        if (_previewCache.TryGetValue(variant.Id, out Texture2D? cached)) return cached;

        Texture2D? tex = null;
        try
        {
            Dictionary<string, PckEntry>? index = PckFileExtractor.TryReadIndex(variant.PckPath);
            string wanted = "char_select_" + variant.CharacterId.ToLowerInvariant() + ".png-";
            string? key = index?.Keys.FirstOrDefault(k =>
                k.EndsWith(".ctex", StringComparison.OrdinalIgnoreCase) &&
                ImportedName(k)?.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) == true);

            if (key != null && index != null)
            {
                byte[]? ctex = PckFileExtractor.TryRead(variant.PckPath, index[key]);
                if (ctex != null)
                {
                    (CtexImageExtractor.CtexFormat fmt, byte[]? data) = CtexImageExtractor.ExtractEmbedded(ctex);
                    if (data != null)
                    {
                        var img = new Image();
                        Error err = fmt == CtexImageExtractor.CtexFormat.Png
                            ? img.LoadPngFromBuffer(data)
                            : img.LoadWebpFromBuffer(data);
                        if (err == Error.Ok) tex = ImageTexture.CreateFromImage(img);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] preview for '{variant.Id}' failed: {ex.Message}");
        }

        _previewCache[variant.Id] = tex;
        return tex;
    }

    /// <summary>
    /// Page declarations in a libgdx atlas are the non-indented lines ending in an image extension.
    /// Everything describing a region is indented with a tab.
    /// </summary>
    private static IEnumerable<string> PageNames(string atlasText)
    {
        foreach (string raw in atlasText.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '\t' || line[0] == ' ') continue;
            if (line.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                yield return line.Trim();
        }
    }

    private static bool TryWritePage(SkinVariant variant, Dictionary<string, PckEntry> index, string dir, string page)
    {
        // "necrobinder.png" -> ".godot/imported/necrobinder.png-<md5>.ctex" (with or without res://)
        string? ctexKey = index.Keys.FirstOrDefault(k =>
            k.EndsWith(".ctex", StringComparison.OrdinalIgnoreCase) &&
            ImportedName(k)?.StartsWith(page + "-", StringComparison.OrdinalIgnoreCase) == true);
        if (ctexKey == null) return false;

        byte[]? ctex = PckFileExtractor.TryRead(variant.PckPath, index[ctexKey]);
        if (ctex == null) return false;

        (CtexImageExtractor.CtexFormat fmt, byte[]? data) = CtexImageExtractor.ExtractEmbedded(ctex);
        if (data == null) return false;

        // The atlas names a .png, so a WebP payload has to be re-encoded rather than just renamed.
        string outPath = Path.Combine(dir, page);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? dir);

        if (fmt == CtexImageExtractor.CtexFormat.Png)
        {
            File.WriteAllBytes(outPath, data);
            return true;
        }

        var img = new Image();
        if (img.LoadWebpFromBuffer(data) != Error.Ok) return false;
        return img.SavePng(outPath) == Error.Ok;
    }

    private static RigFiles? Fail(SkinVariant variant, SkinRig rig, string why)
    {
        variant.Failed.Add(rig);
        MainFile.Logger.Warn($"[{MainFile.ModId}] skin '{variant.Id}' {rig} rig could not be materialized: {why}");
        return null;
    }

    /// <summary>
    /// Extracted skins live in the game's user data directory, NOT next to the mod DLL.
    /// Workshop uploads package the whole mod folder, so a cache written there would ship every
    /// other mod's extracted art inside this mod's release.
    /// </summary>
    private static string CacheRoot()
    {
        try
        {
            string userDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDir)) return Path.Combine(userDir, CacheDirName);
        }
        catch { /* fall through */ }
        return Path.Combine(Path.GetTempPath(), CacheDirName);
    }

    private static string Sanitize(string s)
        => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>
    /// Every .pck reachable on this install: the local mods folder plus Steam Workshop content.
    /// The workshop path is derived from the game directory rather than the Steam API so this stays
    /// a pure file-system concern.
    /// </summary>
    private static IEnumerable<string> EnumeratePcks()
    {
        var roots = new List<string>();
        string? basePck = null;
        try
        {
            string exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            if (exeDir.Length > 0)
            {
                foreach (string candidate in new[]
                         {
                             Path.Combine(exeDir, "SlayTheSpire2.pck"),
                             Path.Combine(exeDir, "..", "Resources", "SlayTheSpire2.pck"),  // macOS bundle
                         })
                {
                    if (File.Exists(candidate)) { basePck = candidate; break; }
                }

                roots.Add(Path.Combine(exeDir, "mods"));
                // <steam>/steamapps/common/Slay the Spire 2 -> <steam>/steamapps/workshop/content/2868840
                string? common = Directory.GetParent(exeDir)?.FullName;
                string? steamapps = common != null ? Directory.GetParent(common)?.FullName : null;
                if (steamapps != null) roots.Add(Path.Combine(steamapps, "workshop", "content", "2868840"));
            }
        }
        catch { /* fall through with whatever roots we got */ }

        // The game's own archive first, so the true vanilla look leads each character's run and wins
        // any dedupe tie. (Yielded out here rather than inside the try: C# forbids yield in a try
        // that has a catch.)
        if (basePck != null) yield return basePck;

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                MainFile.Logger.Info($"[{MainFile.ModId}] scan root missing: {root}");
                continue;
            }
            string[] files;
            try { files = Directory.GetFiles(root, "*.pck", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] scan root unreadable ({root}): {ex.Message}");
                continue;
            }
            MainFile.Logger.Info($"[{MainFile.ModId}] scan root {root}: {files.Length} pck(s).");
            foreach (string f in files) yield return f;
        }
    }
}
