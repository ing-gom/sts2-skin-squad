using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Assets;

namespace Sts2SkinSquad;

/// <summary>
/// Puts the squad on the two non-combat screens that show the whole party in co-op: the campfire
/// and the shop.
///
/// These are NOT the combat rig. A character has three separate Spine skeletons — combat,
/// <c>restsite_{char}</c> (act-specific idle loops) and <c>{char}_shop</c> — living in different
/// scenes with different node scripts, so each screen needs its own placement and its own skin rig.
/// </summary>
internal static class SquadRooms
{
    /// <summary>Marks the nodes this mod creates, so the NRestSiteCharacter._Ready patch knows them.</summary>
    public const string NodePrefix = "SkinSquad_Room_";

    // Mirrors NMerchantRoom.AfterRoomIsLoaded.
    private const float ShopColumnStep = -275f;
    private const float ShopRowXStep = -140f;
    private const float ShopRowYStep = -50f;

    private static readonly Color DimColor = new(0.5f, 0.5f, 0.5f);

    /// <summary>
    /// Adds squad members around the campfire.
    ///
    /// The room owns four fixed slot containers (<c>BgContainer/Character_1..4</c>) and solo play
    /// fills only the first, so the squad simply takes the remaining ones. That also caps the
    /// campfire squad at three, which happens to match the mod's own maximum.
    /// </summary>
    public static void PopulateRestSite(NRestSiteRoom room)
    {
        if (!SkinSquadService.Enabled || SkinSquadService.DoubleCount <= 0) return;

        try
        {
            IRunState? state = RunManager.Instance?.State;
            if (state == null) return;
            if (state.Players.Count != 1) return;                  // single-player only, as everywhere else
            if (room.characterAnims.Count != 1) return;            // room did not lay out a solo party

            CharacterModel? playerCharacter = state.Players[0].Character;
            if (playerCharacter == null) return;

            List<Control> containers = room._characterContainers;
            int placed = 0;

            for (int i = 0; i < SkinSquadService.DoubleCount; i++)
            {
                int slot = i + 1;                                   // slot 0 is the real player
                if (slot >= containers.Count) break;

                // Per-slot isolation: one member that cannot be built must not cost the others.
                // (It did once — FlipX threw on slot 1 and the whole room lost slot 2 with it.)
                try
                {
                    Appearance look = Appearance.Parse(SkinSquadService.SlotTokens.ElementAtOrDefault(i));
                    CharacterModel model = look.ResolveCharacter() ?? playerCharacter;

                    NRestSiteCharacter? node = TryCreateRestSiteActor(model, slot);
                    if (node == null) continue;

                    containers[slot].AddChildSafely(node);
                    node.Position = Vector2.Zero;
                    if (slot % 2 == 1) node.FlipX();                // the game alternates facing per slot

                    PlayActLoop(node, state);
                    ApplyRestSiteSkin(node, look);
                    placed++;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[{MainFile.ModId}] rest site slot {slot} failed: {ex.Message}");
                }
            }

            if (placed > 0)
                MainFile.Logger.Info($"[{MainFile.ModId}] rest site: {placed} squad member(s) placed.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] rest site squad failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Instantiates a campfire actor WITHOUT a Player.
    ///
    /// <c>NRestSiteCharacter.Create</c> demands one, and its <c>_Ready</c> dereferences
    /// <c>Player.RunState</c> and branches on <c>Player.Character is Necrobinder</c> — which would
    /// throw here, because our actor's character need not match the player's (an Ironclad double
    /// beside a Necrobinder player would go looking for <c>%NecroFire</c> on the Ironclad scene).
    /// Rather than fabricate a Player, the scene is instantiated directly and
    /// <see cref="Patches.NRestSiteCharacterReadyPatch"/> skips <c>_Ready</c> for these nodes;
    /// everything a decorative actor actually needs is done here instead.
    /// </summary>
    private static NRestSiteCharacter? TryCreateRestSiteActor(CharacterModel model, int slot)
    {
        try
        {
            var node = PreloadManager.Cache.GetScene(model.RestSiteAnimPath)
                .Instantiate<NRestSiteCharacter>(PackedScene.GenEditState.Disabled);
            node.Name = $"{NodePrefix}Rest_{slot}";
            InitDecorativeActor(node);
            return node;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] rest site actor for {model.Id.Entry} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Does the Player-independent half of the <c>_Ready</c> we skip.
    ///
    /// Skipping <c>_Ready</c> leaves the node's own field references null, and its public methods
    /// still expect them — <c>FlipX</c> dereferences <c>_controlRoot</c>, which is exactly how the
    /// first version lost a campfire member. Resolving them here keeps the node internally
    /// consistent. The hitbox is then neutralised: a decoration must never take a click or the
    /// keyboard focus meant for the real character.
    /// </summary>
    private static void InitDecorativeActor(NRestSiteCharacter node)
    {
        node._controlRoot = node.GetNodeOrNull<Control>("ControlRoot");
        node.Hitbox = node.GetNodeOrNull<Control>("%Hitbox");

        if (node.Hitbox != null)
        {
            node.Hitbox.MouseFilter = Control.MouseFilterEnum.Ignore;
            node.Hitbox.FocusMode = Control.FocusModeEnum.None;
        }
    }

    /// <summary>Starts the act's campfire loop, the part of the skipped _Ready that we do want.</summary>
    private static void PlayActLoop(NRestSiteCharacter node, IRunState state)
    {
        string loop = state.CurrentActIndex switch
        {
            0 => "overgrowth_loop",
            1 => "hive_loop",
            2 => "glory_loop",
            _ => "overgrowth_loop",     // the game throws on an unexpected act; a double just idles
        };

        foreach (Node2D spine in SpineChildren(node))
        {
            try
            {
                var track = new MegaSprite(spine).GetAnimationState().SetAnimation(loop);
                // Desynchronise the loops so the squad does not breathe in lockstep. Rng.Chaotic is
                // time-seeded and independent of the run seed, exactly as the game's own code here.
                track?.SetTrackTime(track.GetAnimationEnd() * MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextFloat());
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] rest site loop '{loop}' failed: {ex.Message}");
            }
        }
    }

    private static void ApplyRestSiteSkin(NRestSiteCharacter node, Appearance look)
    {
        RigFiles? files = look.RigFor(SkinRig.RestSite);
        if (files == null) return;      // skin ships no campfire rig: the vanilla one is correct enough

        foreach (Node2D spine in SpineChildren(node))
        {
            if (SpineSwap.ApplyToSprite(spine, files.AtlasFile, files.SkelFile, restartAnimation: null)) break;
        }
    }

    /// <summary>
    /// Direct SpineSprite children, matching the room's own <c>GetChildSpineNodes</c>. Necrobinder's
    /// Osty hangs off a plain Node2D, so it is not caught here.
    /// </summary>
    private static IEnumerable<Node2D> SpineChildren(Node node)
        => node.GetChildren().OfType<Node2D>().Where(n => n.GetClass() == "SpineSprite");

    /// <summary>
    /// Adds squad members to the shop, re-running the game's own grid over the larger party.
    ///
    /// Much simpler than the campfire: <c>NMerchantCharacter</c> carries no Player reference at all,
    /// so any character's shop scene can just be instantiated.
    /// </summary>
    public static void PopulateShop(NMerchantRoom room)
    {
        if (!SkinSquadService.Enabled || SkinSquadService.DoubleCount <= 0) return;

        try
        {
            IRunState? state = RunManager.Instance?.State;
            if (state == null || state.Players.Count != 1) return;

            List<NMerchantCharacter> members = room._playerVisuals.Where(GodotObject.IsInstanceValid).ToList();
            if (members.Count != 1) return;

            CharacterModel? playerCharacter = state.Players[0].Character;
            if (playerCharacter == null) return;

            for (int i = 0; i < SkinSquadService.DoubleCount; i++)
            {
                Appearance look = Appearance.Parse(SkinSquadService.SlotTokens.ElementAtOrDefault(i));
                CharacterModel model = look.ResolveCharacter() ?? playerCharacter;

                NMerchantCharacter? node = TryCreateShopActor(model, i);
                if (node == null) continue;

                room._characterContainer.AddChildSafely(node);
                room._characterContainer.MoveChildSafely(node, 0);
                ApplyShopSkin(node, look);
                members.Add(node);
                room._playerVisuals.Add(node);
            }

            // Position-swap mode: stand the first double in the front grid spot and the played
            // character behind it, matching the combat formation. members[0] is the real player's
            // own shop visual, but LayoutShop only writes its Position — no reparenting — so
            // reordering the list here is as safe as it is for a double.
            if (SkinSquadService.SwapFront && members.Count >= 2)
                (members[0], members[1]) = (members[1], members[0]);

            LayoutShop(members);
            MainFile.Logger.Info($"[{MainFile.ModId}] shop: party of {members.Count} placed.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] shop squad failed: {ex.Message}");
        }
    }

    private static NMerchantCharacter? TryCreateShopActor(CharacterModel model, int index)
    {
        try
        {
            var node = PreloadManager.Cache.GetScene(model.MerchantAnimPath)
                .Instantiate<NMerchantCharacter>(PackedScene.GenEditState.Disabled);
            node.Name = $"{NodePrefix}Shop_{index}";
            return node;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] shop actor for {model.Id.Entry} failed: {ex.Message}");
            return null;
        }
    }

    private static void ApplyShopSkin(NMerchantCharacter node, Appearance look)
    {
        RigFiles? files = look.RigFor(SkinRig.Shop);
        if (files == null) return;

        foreach (Node2D spine in SpineChildren(node))
        {
            if (SpineSwap.ApplyToSprite(spine, files.AtlasFile, files.SkelFile, restartAnimation: null)) break;
        }
    }

    /// <summary>
    /// Transliteration of the grid in <c>NMerchantRoom.AfterRoomIsLoaded</c>, run over the whole
    /// squad so the real player shifts to make room instead of the doubles piling on top of it.
    /// </summary>
    private static void LayoutShop(List<NMerchantCharacter> members)
    {
        int cols = Mathf.CeilToInt(Mathf.Sqrt(members.Count));
        for (int row = 0; row < cols; row++)
        {
            float x = ShopRowXStep * row;
            for (int col = 0; col < cols; col++)
            {
                int index = row * cols + col;
                if (index >= members.Count) break;

                NMerchantCharacter member = members[index];
                member.Position = new Vector2(x, ShopRowYStep * row);
                if (row > 0) member.Modulate = DimColor;
                x += ShopColumnStep;
            }
        }
    }
}
