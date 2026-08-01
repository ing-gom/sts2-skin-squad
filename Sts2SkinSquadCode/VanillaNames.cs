using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using Sts2SkinSquad.Pck;

namespace Sts2SkinSquad;

/// <summary>
/// The characters' real names, read out of the game's own archive.
///
/// <c>CharacterModel.Title</c> cannot be trusted for this. It resolves through the live
/// <c>characters</c> localisation table, and a skin mod that renames the character it replaces
/// overwrites the entry — so with the raye skin installed even the VANILLA option read
/// "Sky Striker Raye", which is precisely the thing the vanilla option exists to be distinct from.
///
/// The base pck still holds the untouched table, per language, so it is read from there once.
/// </summary>
internal static class VanillaNames
{
    private const string Fallback = "eng";

    private static Dictionary<string, string>? _titles;
    private static string? _loadedFor;

    /// <summary>
    /// The stock title for a character id (e.g. IRONCLAD), or null for anything the base game does
    /// not define — custom characters from character mods, which have no vanilla name to protect.
    /// </summary>
    public static string? TitleFor(string characterId)
    {
        string language = CurrentLanguage();
        if (_titles == null || _loadedFor != language)
        {
            _titles = Load(language) ?? Load(Fallback) ?? new Dictionary<string, string>();
            _loadedFor = language;
        }
        return _titles.TryGetValue(characterId, out string? title) ? title : null;
    }

    private static string CurrentLanguage()
    {
        try { return (LocManager.Instance?.Language ?? Fallback).ToLowerInvariant(); }
        catch { return Fallback; }
    }

    private static Dictionary<string, string>? Load(string language)
    {
        try
        {
            string? pck = FindBasePck();
            if (pck == null) return null;

            Dictionary<string, PckEntry>? index = PckFileExtractor.TryReadIndex(pck);
            if (index == null) return null;

            // Entries are stored with and without the res:// prefix depending on the archive.
            string suffix = $"localization/{language}/characters.json";
            string? key = null;
            foreach (string k in index.Keys)
                if (k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { key = k; break; }
            if (key == null) return null;

            byte[]? bytes = PckFileExtractor.TryRead(pck, index[key]);
            if (bytes == null) return null;

            var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using JsonDocument doc = JsonDocument.Parse(bytes);
            foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
            {
                const string marker = ".title";
                if (!entry.Name.EndsWith(marker, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                titles[entry.Name.Substring(0, entry.Name.Length - marker.Length)] = entry.Value.GetString() ?? "";
            }

            MainFile.Logger.Info($"[{MainFile.ModId}] vanilla character names loaded for '{language}' ({titles.Count}).");
            return titles;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] could not read vanilla character names ({ex.Message}); using the live table.");
            return null;
        }
    }

    private static string? FindBasePck()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            if (exeDir.Length == 0) return null;
            foreach (string candidate in new[]
                     {
                         Path.Combine(exeDir, "SlayTheSpire2.pck"),
                         Path.Combine(exeDir, "..", "Resources", "SlayTheSpire2.pck"),   // macOS bundle
                     })
            {
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { /* fall through */ }
        return null;
    }
}
