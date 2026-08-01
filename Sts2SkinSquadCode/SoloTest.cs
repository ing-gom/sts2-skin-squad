// solo-verify harness for Sts2SkinSquad. Armed only when `selftest.sp.flag` sits next to the DLL.
//
// Starts a single-player run as Necrobinder (the one character with a permanent pet, so the same
// run exercises both the plain double and the pet-copying path), forces a Monster room, and then
// asserts on the live combat scene: the doubles exist, they stand to the LEFT of the real player
// the way co-op teammates do, each carries its own copy of every pet, and — the load-bearing one —
// the combat itself still sees exactly one player.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace Sts2SkinSquad;

internal static class SoloTest
{
    private const string Tag = "Sts2SkinSquad";
    private const double StepTimeoutSec = 90;

    private static readonly StringBuilder _out = new();
    private static bool _started, _done;
    private static string _step = "(not started)";
    private static DateTime _stepAt = DateTime.UtcNow;

    private static string ModDir() => Path.GetDirectoryName(typeof(SoloTest).Assembly.Location) ?? ".";

    public static void ArmIfRequested()
    {
        try
        {
            if (!File.Exists(Path.Combine(ModDir(), "selftest.sp.flag"))) return;
            W("solo selftest armed");
            Poll();
        }
        catch (Exception e) { Log($"solo arm failed: {e.Message}"); }
    }

    private static void Poll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || _done) return;
        try { Tick(tree); } catch (Exception e) { W("tick exception: " + e.Message); }
        if (!_done) tree.CreateTimer(2.0).Timeout += Poll;
    }

    private static void Tick(SceneTree tree)
    {
        var run = RunManager.Instance;
        if (!_started && (run == null || !run.IsInProgress))
        {
            if (NGame.Instance == null) { W("waiting for NGame…"); return; }
            if (ModelCount() == 0) { W("waiting for ModelDb to populate…"); return; }
            if (FindNode<NMainMenu>(tree.Root) == null) { W("waiting for main menu…"); return; }
            _started = true;
            Step("checking the character-select UI");
            TaskHelper.RunSafely(CheckUiThenRun(tree));
            return;
        }

        if (_started && !_done && (DateTime.UtcNow - _stepAt).TotalSeconds > StepTimeoutSec)
        {
            W($"WATCHDOG: no progress for {StepTimeoutSec:F0}s at step '{_step}' — flushing partial result.");
            W($"WATCHDOG: overlay on top = {TopScreenName()} (a selection screen here = an unanswered prompt).");
            Flush(false);
        }
    }

    private static void Step(string name)
    {
        _step = name;
        _stepAt = DateTime.UtcNow;
        W($"— {name}");
    }

    private static int ModelCount()
    {
        try
        {
            var f = typeof(ModelDb).GetField("_contentById", BindingFlags.NonPublic | BindingFlags.Static);
            return (f?.GetValue(null) as System.Collections.IDictionary)?.Count ?? 0;
        }
        catch { return 0; }
    }

    /// <summary>Live count of pets belonging to the local player — the poll condition for "the
    /// summon landed". Enemies are excluded via PetOwner, not via IsPlayer.</summary>
    private static int CountLocalPets(NCombatRoom room)
    {
        try
        {
            var nodes = room.CreatureNodes.Where(GodotObject.IsInstanceValid).ToList();
            var me = nodes.FirstOrDefault(c => c.Entity is { IsPlayer: true });
            if (me?.Entity?.Player == null) return 0;
            return nodes.Count(c => c.Entity is { IsPlayer: false } e && e.PetOwner == me.Entity.Player);
        }
        catch { return 0; }
    }

    /// <summary>
    /// On-screen height of an actor: skeleton bounds times the sprite scale. Bounds alone are in
    /// skeleton-local units, so two bodies authored at different pixel scales look identical there
    /// and only diverge once the node scale is folded in.
    /// </summary>
    private static float RenderedHeight(NCreatureVisuals visuals)
    {
        try
        {
            if (visuals.GetNodeOrNull<Node2D>("%Visuals") is not { } body) return 0f;
            if (body.GetClass() != "SpineSprite") return 0f;
            GodotObject? skeleton = body.Call("get_skeleton").AsGodotObject();
            if (skeleton == null) return 0f;
            return skeleton.Call("get_bounds").AsRect2().Size.Y * Math.Abs(body.Scale.Y);
        }
        catch { return 0f; }
    }

    /// <summary>
    /// Enters a room, screenshots it, and asserts the squad showed up there. Generic over the room
    /// node because the campfire and the shop differ only in which type to wait for.
    /// </summary>
    private static async Task<bool> CheckRoom<TRoom>(RoomType type, string shot, string label, string what,
                                                     Func<Node, int> countSquad) where TRoom : Node
    {
        try
        {
            var run = RunManager.Instance;
            if (run == null) { W($"assert {label}: no run -> FAIL"); return false; }

            await run.EnterRoomDebug(type, showTransition: false);

            TRoom? room = null;
            for (int i = 0; i < 40 && room == null; i++)
            {
                await Task.Delay(250);
                if (Engine.GetMainLoop() is SceneTree tree) room = FindNode<TRoom>(tree.Root);
            }
            if (room == null) { W($"assert {label}: never reached the {what} -> FAIL"); return false; }

            await Task.Delay(1200);
            await Shot(shot);

            int found = countSquad(room);
            bool okRoom = found == SkinSquadService.DoubleCount;
            W($"assert {label}: {what} squad members={found}, expected={SkinSquadService.DoubleCount} -> {(okRoom ? "OK" : "FAIL")}");
            return okRoom;
        }
        catch (Exception e)
        {
            W($"assert {label}: {what} threw {e.Message} -> FAIL");
            return false;
        }
    }

    /// <summary>Recursively counts nodes this mod added to a room, identified by name prefix.</summary>
    private static int CountSquadNodes(Node root)
    {
        int n = root.Name.ToString().StartsWith(SquadRooms.NodePrefix, StringComparison.Ordinal) ? 1 : 0;
        foreach (Node c in root.GetChildren()) n += CountSquadNodes(c);
        return n;
    }

    /// <summary>Name of the animation an actor is currently playing, or "" if none.</summary>
    private static string CurrentAnimation(NCreatureVisuals visuals)
    {
        try
        {
            if (!visuals.HasSpineAnimation) return "";
            return visuals.SpineAnimation.GetCurrentTrack()?.GetAnimation()?.GetName() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>Depth-first search for a node by name, wherever it ended up in the tree.</summary>
    private static Node? FindByName(Node root, string name)
    {
        if (root.Name.ToString() == name) return root;
        foreach (Node c in root.GetChildren())
        {
            Node? r = FindByName(c, name);
            if (r != null) return r;
        }
        return null;
    }

    private static T? FindNode<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
        {
            var r = FindNode<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>
    /// Drives the character-select UI before the run starts: opens the screen, finds our Squad
    /// button, opens the picker and screenshots it. Purely additive — whatever it finds, the run
    /// test still follows, so a UI regression cannot mask a gameplay one.
    /// </summary>
    private static async Task CheckUiThenRun(SceneTree tree)
    {
        try
        {
            var menu = FindNode<NMainMenu>(tree.Root);
            if (menu == null) { W("ui: no main menu"); }
            else
            {
                // The character-select screen is a submenu; pushing it through the menu's own stack
                // is what makes _Ready (and therefore our button patch) run, exactly as the
                // Singleplayer button does.
                var pushed = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
                pushed.InitializeSingleplayer();
                menu.SubmenuStack.Push(pushed);

                NCharacterSelectScreen? screen = null;
                for (int i = 0; i < 30 && screen is null; i++)
                {
                    await Task.Delay(250);
                    screen = FindNode<NCharacterSelectScreen>(tree.Root);
                }

                if (screen is null) W("ui: character select never opened");
                else
                {
                    await Task.Delay(800);
                    // Recursive, not HasNode: once Skin Manager is installed the button is
                    // reparented into that mod's menu bar, so it is no longer a direct child of
                    // the screen and a path lookup reports it missing.
                    _uiButtonFound = FindByName(screen, "SkinSquadButton") != null;
                    _uiDragOk = CheckDragClamp(screen);
                    W($"ui: squad button present = {_uiButtonFound}");
                    await Shot("0_charselect");

                    if (_uiButtonFound)
                    {
                        // Put a real skin in slot 1 so the opening preview has something to render;
                        // the default "Same as me" has no figure and would prove nothing.
                        var firstSkin = SkinCatalog.Variants.FirstOrDefault();
                        if (firstSkin != null) SkinSquadService.SlotTokens[0] = firstSkin.Id;

                        Ui.SquadModal.Open();
                        await Task.Delay(900);
                        _uiModalOpened = FindNode<Ui.SquadModal>(tree.Root) != null;
                        W($"ui: picker modal opened = {_uiModalOpened}");
                        await Shot("0_picker");
                        _uiPreviewOk = await CheckPreviewSizes(tree);
                        NModalContainer.Instance?.Clear();
                        await Task.Delay(400);

                        // Empty state: with no members there are no rows, so the picker must say
                        // something rather than render as a blank plate. Screenshotted because that
                        // is the only way to prove the placeholder actually shows.
                        int keep = SkinSquadService.DoubleCount;
                        SkinSquadService.DoubleCount = 0;
                        Ui.SquadModal.Open();
                        await Task.Delay(700);
                        await Shot("0_picker_empty");
                        NModalContainer.Instance?.Clear();
                        await Task.Delay(300);
                        SkinSquadService.DoubleCount = keep;
                    }
                }
            }
        }
        catch (Exception e) { W("ui check threw: " + e.Message); }

        Step("starting single-player run");
        await StartRunThenTest();
    }

    private static bool _uiButtonFound, _uiModalOpened, _uiDragOk, _uiPreviewOk;

    /// <summary>
    /// Hovers several looks in turn and checks they all render at the same height.
    ///
    /// The point of the preview is comparison, so inconsistent sizing defeats it — and these
    /// skeletons ARE authored at wildly different pixel scales, so "looks fine" for one skin says
    /// nothing about the next. Measured rather than eyeballed for exactly that reason.
    /// </summary>
    private static async Task<bool> CheckPreviewSizes(SceneTree tree)
    {
        try
        {
            var modal = FindNode<Ui.SquadModal>(tree.Root);
            Ui.SkinPreview? panel = modal?.PreviewPanel;
            if (panel == null) { W("ui: no preview panel"); return false; }

            var sample = SkinCatalog.Variants.Take(5).ToList();
            if (sample.Count < 2) { W("ui: too few skins to compare preview sizes -> SKIP"); return true; }

            var heights = new List<(string id, float h)>();
            foreach (SkinVariant v in sample)
            {
                panel.Show(v.Id);
                // Debounce (120ms) plus the fit retries have to finish before measuring.
                await Task.Delay(700);
                heights.Add((v.Id, panel.FittedHeight()));
            }

            var good = heights.Where(x => x.h > 1f).ToList();
            float lo = good.Count > 0 ? good.Min(x => x.h) : 0f;
            float hi = good.Count > 0 ? good.Max(x => x.h) : 0f;
            bool uniform = good.Count == heights.Count && lo > 1f && hi / lo <= 1.25f;

            W($"ui: preview heights [{string.Join(", ", heights.Select(x => $"{x.id}={x.h:F0}"))}] " +
              $"spread={(lo > 0 ? hi / lo : 0):F2}x -> {(uniform ? "uniform" : "INCONSISTENT")}");
            return uniform;
        }
        catch (Exception e) { W("ui: preview size check threw " + e.Message); return false; }
    }

    /// <summary>
    /// Exercises the standalone button's drag placement without a real mouse: park a throwaway
    /// button off-screen and check it gets pulled back.
    ///
    /// Needed as a separate probe because on a machine with Skin Manager installed the real button
    /// is reparented into that mod's panel within a second, so the draggable path never runs here.
    /// Clamping is the part that actually bites — a position saved at one resolution can land off
    /// screen at another, leaving the button unreachable.
    /// </summary>
    private static bool CheckDragClamp(Control screen)
    {
        Ui.SquadDragButton? probe = null;
        try
        {
            probe = new Ui.SquadDragButton { Name = "SkinSquadDragProbe", Text = "probe" };
            probe.CustomMinimumSize = new Vector2(160, 56);
            screen.AddChild(probe);
            probe.AnchorLeft = probe.AnchorRight = probe.AnchorTop = probe.AnchorBottom = 1f;
            probe.OffsetLeft = -390; probe.OffsetRight = -230;
            probe.OffsetTop = -323; probe.OffsetBottom = -267;

            probe.ApplyDelta(new Vector2(9000, 9000));      // far off the bottom-right corner
            Rect2 view = probe.GetViewportRect();
            Rect2 got = probe.GetGlobalRect();

            bool onScreen = got.Position.X >= 0 && got.Position.Y >= 0
                            && got.End.X <= view.Size.X + 1 && got.End.Y <= view.Size.Y + 1;
            W($"ui: drag clamp — pushed +9000,+9000, ended at {got} in viewport {view.Size} -> {(onScreen ? "on screen" : "OFF SCREEN")}");
            return onScreen;
        }
        catch (Exception e) { W("ui: drag clamp probe threw " + e.Message); return false; }
        finally { if (probe != null && GodotObject.IsInstanceValid(probe)) probe.QueueFree(); }
    }

    private static async Task StartRunThenTest()
    {
        bool ok = false;
        try
        {
            // Necrobinder by preference: it is the only character that brings a pet (Osty) into
            // combat, so this single run covers the pet-copying path as well as the plain one.
            CharacterModel character =
                ModelDb.AllCharacters.FirstOrDefault(c => c.Id.Entry == "NECROBINDER")
                ?? ModelDb.AllCharacters.First();
            W($"character: {character.Id.Entry}");

            var acts = ActModel.GetDefaultList().ToList();
            if (NGame.Instance == null) { W("NGame vanished before the run could start"); Flush(false); return; }
            await NGame.Instance.StartNewSingleplayerRun(character, shouldSave: false, acts,
                Array.Empty<ModifierModel>(), "SOLOTEST", GameMode.Standard, 0);
            await Task.Delay(3000);

            var run = RunManager.Instance;
            if (run?.IsInProgress != true || (run.State?.Players?.Count ?? 0) == 0)
            { W("run did not start"); Flush(false); return; }
            var player = run.State!.Players.First();
            W($"run started: {player.Character?.Id.Entry}, floor {run.State.TotalFloor}");

            StartAutomation();
            await Shot("1_run");

            // ── setup: force two DIFFERENT appearances so the v0.2 paths are actually exercised ──
            Step("configuring squad appearances");
            SkinSquadService.Enabled = true;
            SkinSquadService.DoubleCount = 2;

            // Slot 0: another vanilla character (no extraction involved).
            string otherCharacter = ModelDb.AllCharacters
                .Select(c => c.Id.Entry)
                .FirstOrDefault(e => e != character.Id.Entry) ?? Appearance.SelfToken;
            SkinSquadService.SlotTokens[0] = otherCharacter;

            // Slot 1: a skin mod, preferring one built for the character we're playing so the swap
            // is applied to the body it was authored against.
            var skins = SkinCatalog.Variants.ToList();
            string skinToken = (skins.FirstOrDefault(v => v.CharacterId == character.Id.Entry) ?? skins.FirstOrDefault())?.Id
                               ?? Appearance.SelfToken;
            SkinSquadService.SlotTokens[1] = skinToken;
            W($"appearance catalog: {skins.Count} skin variant(s) [{string.Join(", ", skins.Select(v => v.Id))}]");
            W($"slots: [0]={otherCharacter}  [1]={skinToken}");

            // ── setup: force a combat room so NCombatRoom.CreateAllyNodes runs ──────────────────
            Step("entering a Monster room");
            await run.EnterRoomDebug(RoomType.Monster, showTransition: false);
            for (int i = 0; i < 40 && NCombatRoom.Instance == null; i++) await Task.Delay(250);
            await Task.Delay(1500);   // let the ally nodes settle and a frame draw

            NCombatRoom? room = NCombatRoom.Instance;
            if (room == null) { W("assert: FAILED — never reached a combat room"); Flush(false); return; }

            // Pets are summoned during combat setup, i.e. AFTER the ally nodes are built, so give
            // the Osty time to actually arrive before asserting anything about pet mirroring.
            Step("waiting for pets to be summoned");
            for (int i = 0; i < 24 && CountLocalPets(room) == 0; i++) await Task.Delay(500);
            await Task.Delay(800);
            await Shot("2_combat");

            // ── assertions ─────────────────────────────────────────────────────────────────────
            Step("asserting squad state");

            List<NCreature> allies = room.CreatureNodes.Where(GodotObject.IsInstanceValid).ToList();
            List<NCreature> realPlayers = allies.Where(c => c.Entity is { IsPlayer: true }).ToList();
            // ★ Must filter by PetOwner, not just "not a player": by assert time CreateEnemyNodes has
            //   run, so CreatureNodes also holds the enemies. An earlier version counted 4 enemies as
            //   4 pets and reported a false FAIL.
            List<NCreature> realPets = realPlayers.Count == 1
                ? allies.Where(c => c.Entity is { IsPlayer: false } e && e.PetOwner == realPlayers[0].Entity.Player).ToList()
                : new List<NCreature>();

            // A. The combat must still be a one-player combat. This is the whole safety claim:
            //    doubles are entity-free, so they must never appear as creatures or players.
            int statePlayers = run.State.Players.Count;
            bool aOk = realPlayers.Count == 1 && statePlayers == 1;
            W($"assert A: combat players={realPlayers.Count}, run players={statePlayers} -> {(aOk ? "OK" : "FAIL")}");

            Control? container = realPlayers.Count == 1 ? realPlayers[0].GetParent() as Control : null;
            if (container == null) { W("assert: FAILED — no ally container"); Flush(false); return; }

            var doubles = container.GetChildren().OfType<NCreatureVisuals>()
                .Where(n => n.Name.ToString().StartsWith("SkinSquad_Double_", StringComparison.Ordinal))
                .ToList();
            var petCopies = container.GetChildren().OfType<NCreatureVisuals>()
                .Where(n => n.Name.ToString().StartsWith("SkinSquad_Pet_", StringComparison.Ordinal))
                .ToList();

            // B. Every configured double actually spawned.
            bool bOk = doubles.Count == SkinSquadService.DoubleCount;
            W($"assert B: doubles={doubles.Count}, expected={SkinSquadService.DoubleCount} -> {(bOk ? "OK" : "FAIL")}");

            // C. Doubles stand to the LEFT of the controlled character — allies grow leftwards
            //    (negative x) from the container origin, exactly like real co-op teammates.
            float realX = realPlayers[0].Position.X;
            var xs = doubles.Select(d => d.Position.X).ToList();
            bool cOk = doubles.Count > 0 && xs.All(x => x < realX);
            W($"assert C: real x={realX:F1}, doubles x=[{string.Join(", ", xs.Select(x => x.ToString("F1")))}] " +
              $"(all left of real) -> {(cOk ? "OK" : "FAIL")}");

            // D. Pets are copied per double, not shared. Vacuous if this character has no pet —
            //    reported as SKIP rather than a silent pass so the log can't be misread.
            int petsPerPlayer = realPets.Count;
            bool dOk;
            if (petsPerPlayer == 0)
            {
                dOk = true;
                W("assert D: no pet ever reached this combat -> SKIP (pet mirroring UNTESTED, not proven)");
            }
            else
            {
                dOk = petCopies.Count == petsPerPlayer * doubles.Count;
                W($"assert D: real pets={petsPerPlayer}, pet copies={petCopies.Count}, " +
                  $"expected={petsPerPlayer * doubles.Count} -> {(dOk ? "OK" : "FAIL")}");
            }

            // E. Doubles are drawn: a spine actor with no skeleton would be an invisible pass.
            bool eOk = doubles.All(d => d.HasSpineAnimation) && doubles.All(d => d.Visible);
            W($"assert E: all doubles have spine animation and are visible -> {(eOk ? "OK" : "FAIL")}");

            // Geometry dump: layout asserts only compare x, so a member that renders at the wrong
            // SIZE or HEIGHT still passes them. These numbers are what tells the two apart.
            void DumpActor(string tag, NCreatureVisuals v, Vector2 pos)
            {
                var inner = v.GetNodeOrNull<Node2D>("%Visuals");
                W($"  geom {tag,-22} scene={Path.GetFileName(v.SceneFilePath ?? "?"),-20} " +
                  $"pos=({pos.X:F0},{pos.Y:F0}) scale={v.Scale.X:F2} " +
                  $"innerScale={(inner != null ? inner.Scale.X.ToString("F3") : "n/a")} " +
                  $"bounds={(v.Bounds != null ? $"{v.Bounds.Size.X:F0}x{v.Bounds.Size.Y:F0}" : "n/a")}");
            }
            DumpActor("REAL", realPlayers[0].Visuals, realPlayers[0].Position);
            foreach (var d in doubles.OrderBy(d => d.Name.ToString())) DumpActor(d.Name.ToString(), d, d.Position);

            // F. Slot 0 became a DIFFERENT vanilla character — proves per-slot appearance works and
            //    that a non-played character's combat scene loads on demand.
            var byName = doubles.ToDictionary(d => d.Name.ToString(), d => d);
            string scene0 = byName.TryGetValue("SkinSquad_Double_0", out var d0) ? d0.SceneFilePath ?? "" : "";
            bool fOk = otherCharacter == Appearance.SelfToken
                       || scene0.Contains(otherCharacter.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
            W($"assert F: double 0 wants '{otherCharacter}', scene='{scene0}' -> {(fOk ? "OK" : "FAIL")}");

            // G. Slot 1 wears a skin mod: the variant unpacked to disk AND the actor still has a
            //    working spine skeleton after the swap (a failed swap leaves SpineBody null).
            bool gOk;
            if (skinToken == Appearance.SelfToken)
            {
                gOk = true;
                W("assert G: no skin variants installed -> SKIP (skin swapping UNTESTED, not proven)");
            }
            else
            {
                SkinVariant? v = SkinCatalog.Find(skinToken);
                RigFiles? combat = v != null ? SkinCatalog.TryMaterialize(v, SkinRig.Combat) : null;
                bool onDisk = combat != null && File.Exists(combat.AtlasFile) && File.Exists(combat.SkelFile);
                bool alive = byName.TryGetValue("SkinSquad_Double_1", out var d1) && d1.HasSpineAnimation;
                gOk = onDisk && alive;
                W($"assert G: skin '{skinToken}' materialized={onDisk}, skeleton alive after swap={alive} -> {(gOk ? "OK" : "FAIL")}");
            }

            // H. Every double renders at a believable size. Added because asserts A–G ALL passed
            //    while a swapped skin stood at 42% of the vanilla body's height: the layout only
            //    compares x, so nothing before this looked at how big anyone actually is.
            //    Tolerance depends on whether the double is the SAME character. A skin must match its
            //    own character's height closely (that is the bug this catches). A different
            //    character legitimately differs — Ironclad measured 1.61x a Necrobinder — so it only
            //    gets a loose "not absurd" band. Using one tight band for both flagged that as a bug.
            float realH = RenderedHeight(realPlayers[0].Visuals);
            string realScene = realPlayers[0].Visuals.SceneFilePath ?? "";
            bool hOk = realH > 1f;
            var hDetail = new List<string>();
            foreach (var d in doubles)
            {
                float h = RenderedHeight(d);
                bool sameCharacter = (d.SceneFilePath ?? "") == realScene;
                float lo = sameCharacter ? 0.6f : 0.3f;
                float hi = sameCharacter ? 1.6f : 3.0f;
                bool fits = realH > 1f && h > realH * lo && h < realH * hi;
                if (!fits) hOk = false;
                hDetail.Add($"{d.Name.ToString().Replace("SkinSquad_Double_", "#")}={h:F0}" +
                            $"({(sameCharacter ? "same" : "other")}char,{h / Math.Max(realH, 1f):F2}x{(fits ? "" : " OUT")})");
            }
            W($"assert H: real height={realH:F0}, doubles=[{string.Join(", ", hDetail)}] -> {(hOk ? "OK" : "FAIL")}");

            // I. Animation mirroring: driving the real character's trigger must move the doubles off
            //    their idle. Checked by animation NAME, not by "did we call something" — a mirrored
            //    trigger that silently no-ops (missing animator, stale skeleton after a skin swap)
            //    would otherwise look identical to a working one.
            Step("mirroring an animation trigger");
            var idleBefore = doubles.ToDictionary(d => d.Name.ToString(), CurrentAnimation);
            realPlayers[0].SetAnimationTrigger("Attack");
            await Task.Delay(400);
            var afterAttack = doubles.ToDictionary(d => d.Name.ToString(), CurrentAnimation);

            var moved = doubles.Select(d => d.Name.ToString())
                .Where(n => afterAttack[n] != idleBefore[n] && afterAttack[n].Length > 0)
                .ToList();
            bool iOk = moved.Count == doubles.Count;
            W($"assert I: trigger 'Attack' -> " +
              string.Join(", ", doubles.Select(d => $"{d.Name.ToString().Replace("SkinSquad_Double_", "#")}:" +
                                                    $"{idleBefore[d.Name.ToString()]}->{afterAttack[d.Name.ToString()]}")) +
              $" ({moved.Count}/{doubles.Count} moved) -> {(iOk ? "OK" : "FAIL")}");
            // M. The controlled character owns the right edge and the top of the draw order.
            //    Regression guard: pushing each double's pet "clear of its body" sent them rightwards
            //    into the player — a double at x=-473 put its Osty at x=-236, right of the player at
            //    x=-324. Every squad node (doubles AND their pet copies) must stay left of the player.
            var squadNodes = container.GetChildren().OfType<NCreatureVisuals>()
                .Where(n => n.Name.ToString().StartsWith("SkinSquad_", StringComparison.Ordinal))
                .ToList();
            float mine = realPlayers[0].Position.X;
            var intruders = squadNodes.Where(n => n.Position.X >= mine).Select(n => $"{n.Name}@{n.Position.X:F0}").ToList();

            int myIndex = realPlayers[0].GetIndex();
            var above = squadNodes.Where(n => n.GetIndex() > myIndex).Select(n => n.Name.ToString()).ToList();

            bool mOk = intruders.Count == 0 && above.Count == 0;
            W($"assert M: player x={mine:F0}, squad nodes={squadNodes.Count}; " +
              $"right-of-player=[{string.Join(", ", intruders)}]; drawn-above=[{string.Join(", ", above)}] -> {(mOk ? "OK" : "FAIL")}");

            await Shot("4_attack");

            // J/K. The two non-combat screens that show the whole party in co-op. Counted by node
            //      name so a member that was created but never parented cannot pass.
            Step("entering the rest site");
            bool jOk = await CheckRoom<NRestSiteRoom>(RoomType.RestSite, "5_restsite", "J", "rest site",
                room => CountSquadNodes(room));

            Step("entering the shop");
            bool kOk = await CheckRoom<NMerchantRoom>(RoomType.Shop, "6_shop", "K", "shop",
                room => CountSquadNodes(room));

            // L. The in-game picker: a button on the character-select screen that opens the modal.
            bool lOk = _uiButtonFound && _uiModalOpened && _uiDragOk && _uiPreviewOk;
            W($"assert L: squad button={_uiButtonFound}, picker opened={_uiModalOpened}, " +
              $"drag stays on screen={_uiDragOk}, preview sizes uniform={_uiPreviewOk} -> {(lOk ? "OK" : "FAIL")}");

            ok = aOk && bOk && cOk && dOk && eOk && fOk && gOk && hOk && iOk && jOk && kOk && lOk && mOk;
            W($"assert -> {ok}");

            await Shot("3_final");
            W("=== solo test done ===");
            Flush(ok);
        }
        catch (Exception e) { W("test exception: " + e); Flush(false); }
    }

    #region selection automation (selector + screen pump)
    private static readonly HashSet<string> _pumpIgnore = new();
    private const int PumpGraceMs = 4000;

    private static IDisposable? _selectorScope;
    private static bool _pumpRunning;

    private static void StartAutomation()
    {
        EnsureSelector();
        if (_pumpRunning) return;
        _pumpRunning = true;
        int handlers = ScreenHandlers().Count;
        TaskHelper.RunSafely(PumpLoop());
        W($"selection automation on (selector + {handlers} screen handler(s), grace {PumpGraceMs}ms)");
    }

    private static void EnsureSelector()
    {
        try
        {
            if (CardSelectCmd.Selector != null) return;
            _selectorScope = CardSelectCmd.PushSelector(new AutoSelector());
        }
        catch (Exception e) { W("selector push failed: " + e.Message); }
    }

    private sealed class AutoSelector : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var list = options.ToList();
            int n = Math.Min(maxSelect, list.Count);
            if (n < minSelect) n = Math.Min(minSelect, list.Count);
            W($"  [selector] auto-picked {n}/{list.Count}: [{string.Join(", ", list.Take(n).Select(c => c.Id.Entry))}]");
            return Task.FromResult<IEnumerable<CardModel>>(list.Take(n).ToList());
        }

        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
        {
            var pick = options.FirstOrDefault()?.Card;
            W($"  [selector] auto-picked card reward: {pick?.Id.Entry ?? "(none)"}");
            return new CardRewardSelection { card = pick, alternative = null };
        }
    }

    private static async Task PumpLoop()
    {
        var rng = new Rng(1u);
        object? seen = null;
        var seenAt = DateTime.UtcNow;
        int attempts = 0;
        while (!_done)
        {
            await Task.Delay(500);
            try
            {
                EnsureSelector();
                object? top = NOverlayStack.Instance?.Peek();
                if (top == null) { seen = null; attempts = 0; continue; }
                if (!ReferenceEquals(top, seen)) { seen = top; seenAt = DateTime.UtcNow; attempts = 0; continue; }
                if ((DateTime.UtcNow - seenAt).TotalMilliseconds < PumpGraceMs) continue;

                string name = top.GetType().Name;
                if (_pumpIgnore.Contains(name)) continue;
                if (attempts >= 3)
                {
                    if (attempts == 3) { attempts++; W($"  [pump] {name} will not close after 3 attempts — leaving it"); }
                    continue;
                }
                attempts++;
                W($"  [pump] auto-handling unattended screen: {name} (attempt {attempts})");
                await HandleScreen(top, rng);
                seenAt = DateTime.UtcNow;
            }
            catch (Exception e) { W("  [pump] " + e.Message); }
        }
    }

    private static async Task HandleScreen(object screen, Rng rng)
    {
        if (!ScreenHandlers().TryGetValue(screen.GetType(), out var handler))
        {
            W($"  [pump] no AutoSlay handler for {screen.GetType().Name} — cannot auto-dismiss.");
            return;
        }
        var ht = handler.GetType();
        var timeout = ht.GetProperty("Timeout")?.GetValue(handler) as TimeSpan? ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(timeout);
        var task = ht.GetMethod("HandleAsync")?.Invoke(handler, new object[] { rng, cts.Token }) as Task;
        if (task == null) { W($"  [pump] {ht.Name}.HandleAsync not invokable"); return; }
        await task;
        W($"  [pump] handled {screen.GetType().Name}");
    }

    private static Dictionary<Type, object>? _screenHandlers;

    private static Dictionary<Type, object> ScreenHandlers()
    {
        if (_screenHandlers != null) return _screenHandlers;
        var map = new Dictionary<Type, object>();
        try
        {
            var asm = typeof(CardSelectCmd).Assembly;
            var iface = asm.GetType("MegaCrit.Sts2.Core.AutoSlay.Handlers.IScreenHandler");
            if (iface == null) { W("  [pump] AutoSlay handlers not found in this game build"); return _screenHandlers = map; }
            Type?[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || t.IsInterface || !iface.IsAssignableFrom(t)) continue;
                if (t.GetConstructor(Type.EmptyTypes) == null) continue;
                var h = Activator.CreateInstance(t);
                if (h != null && t.GetProperty("ScreenType")?.GetValue(h) is Type st) map[st] = h;
            }
            W($"  [pump] {map.Count} AutoSlay screen handler(s) available");
        }
        catch (Exception e) { W("  [pump] handler discovery failed: " + e.Message); }
        return _screenHandlers = map;
    }

    private static string TopScreenName()
    {
        try { return NOverlayStack.Instance?.Peek()?.GetType().Name ?? "(none)"; } catch { return "(unavailable)"; }
    }
    #endregion

    private static async Task Shot(string name)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            await Task.Delay(120);
            var img = tree.Root.GetTexture()?.GetImage();
            if (img == null) { W($"shot {name}: null image"); return; }
            string p = Path.Combine(ModDir(), $"selftest.sp.{name}.png");
            var err = img.SavePng(p);
            W($"shot {name}: {(err == Error.Ok ? $"saved {img.GetWidth()}x{img.GetHeight()} -> {Path.GetFileName(p)}" : "err " + err)}");
        }
        catch (Exception e) { W($"shot {name} failed: {e.Message}"); }
    }

    private static void W(string line) { _out.AppendLine(line); Log($"SOLO | {line}"); }
    private static void Log(string s) { MainFile.Logger.Info($"[{Tag}] {s}"); }

    private static void Flush(bool ok)
    {
        if (_done) return;
        _done = true;
        _selectorScope?.Dispose();
        _selectorScope = null;
        _out.Insert(0, (ok ? "RESULT: OK\n" : "RESULT: FAIL\n"));
        try { File.WriteAllText(Path.Combine(ModDir(), "selftest.sp.txt"), _out.ToString()); } catch { }
    }
}
