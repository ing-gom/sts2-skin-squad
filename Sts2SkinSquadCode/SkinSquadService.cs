using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2SkinSquad;

/// <summary>
/// Spawns visual-only copies of the player next to the real one and lays the whole group out
/// with the game's own co-op geometry.
///
/// A double is a bare <see cref="NCreatureVisuals"/> — the same node the game itself creates
/// off-combat (e.g. the game-over screen instantiates <c>player.Creature.CreateVisuals()</c>
/// directly). It has no <c>Creature</c> entity, so it never enters combat state, never gets a
/// health bar, intents, orbs, a hitbox, or targeting. Nothing here can change the run.
///
/// Pets arrive on a second path. <c>CreateAllyNodes</c> runs during <c>NCombatRoom._Ready</c>,
/// before combat is set up, so a summoned pet like Necrobinder's Osty does not exist yet — it is
/// added later through <c>NCombatRoom.AddCreature</c>. Both paths funnel into
/// <see cref="MirrorPet"/> so a squad member always brings its own pet rather than sharing one.
/// </summary>
internal static class SkinSquadService
{
    public const int MaxDoubles = 3;
    public const int DefaultDoubles = 2;

    public const string DoubleNamePrefix = "SkinSquad_Double_";
    public const string PetNamePrefix = "SkinSquad_Pet_";
    private const string IdleAnimation = "idle_loop";

    /// <summary>CreatureAnimator's death trigger name.</summary>
    private const string DeathTrigger = "Dead";

    public static bool Enabled { get; set; } = true;
    public static bool DimBackRows { get; set; } = true;

    /// <summary>
    /// Position-swap mode: stand the first squad member in the front (main) spot and drop the
    /// controlled character into the slot behind it. Purely visual — the played character is still
    /// the one real <c>NCreature</c>, it just stands one place back. Off by default.
    /// </summary>
    public static bool SwapFront { get; set; } = false;

    /// <summary>
    /// Saved line-ups. Never empty: the active preset IS the live configuration, so losing it would
    /// leave nothing for the combat code to read.
    /// </summary>
    public static List<SquadPreset> Presets { get; } = new() { SquadPreset.Default() };

    private static int _activePreset;

    /// <summary>Index of the preset currently in effect, always in range.</summary>
    public static int ActivePreset
    {
        get => Math.Clamp(_activePreset, 0, Math.Max(0, Presets.Count - 1));
        set => _activePreset = Math.Clamp(value, 0, Math.Max(0, Presets.Count - 1));
    }

    public static SquadPreset Active
    {
        get
        {
            if (Presets.Count == 0) Presets.Add(SquadPreset.Default());
            return Presets[ActivePreset];
        }
    }

    // Size and per-member looks read straight through to the active preset rather than being copied
    // into separate fields. One source of truth means switching presets cannot leave the combat code
    // reading a stale line-up.
    public static int DoubleCount
    {
        get => Active.Count;
        set => Active.Count = Math.Clamp(value, 0, MaxDoubles);
    }

    /// <summary>Per-slot appearance tokens; index 0 is the nearest double. See <see cref="Appearance"/>.</summary>
    public static string[] SlotTokens => Active.Slots;

    /// <summary>Doubles for the current combat room, front-to-back. Cleared on each new room.</summary>
    private static readonly List<PartySlot> _doubles = new();

    /// <summary>Source pet entity -> one copy per double, so removals can be mirrored too.</summary>
    private static readonly Dictionary<Creature, List<PartySlot>> _petCopies = new();

    private static Creature? _localPlayerEntity;

    /// <summary>The controlled character's slot, kept so its draw order can be re-asserted whenever
    /// pets arrive mid-combat and shuffle the child list.</summary>
    private static PartySlot? _realSlot;

    /// <summary>Encounter geometry captured at build time, so <see cref="ReapplyLayout"/> can
    /// reproduce exactly the same layout when a pet arrives mid-combat.</summary>
    private static float _scaling = 1f;
    private static bool _fullyCenterPlayers;

    /// <summary>Only ever drives the "Random" appearance option, which is cosmetic and outside the
    /// run's RNG on purpose — it must not consume seeded rolls.</summary>
    private static readonly Random _rng = new();

    /// <summary>
    /// Called after <c>NCombatRoom.CreateAllyNodes</c> has built (and positioned) the real allies.
    /// </summary>
    public static void PopulateSquad(NCombatRoom room)
    {
        Reset();
        if (!Enabled || DoubleCount <= 0) return;
        if (!GodotObject.IsInstanceValid(room)) return;

        List<NCreature> allies = room.CreatureNodes.Where(GodotObject.IsInstanceValid).ToList();
        List<NCreature> players = allies.Where(c => c.Entity is { IsPlayer: true }).ToList();

        // Single-player only, by design. A party of 2+ means real co-op or one of the
        // fake-multiplayer solo-squad mods is driving the run — in both cases the game is
        // already laying out several characters and adding more would fight it.
        if (players.Count != 1)
        {
            MainFile.Logger.Info($"[{MainFile.ModId}] skipped: party size is {players.Count}, squad is single-player only.");
            return;
        }

        NCreature realPlayer = players[0];
        Creature playerEntity = realPlayer.Entity;
        if (realPlayer.GetParent() is not Control container) return;

        // Re-entry guard: one squad per combat room.
        if (container.GetChildren().Any(c => c.Name.ToString().StartsWith(DoubleNamePrefix, StringComparison.Ordinal)))
            return;

        List<NCreature> realPets = allies.Where(c => IsLocalPet(c.Entity, playerEntity)).ToList();

        var realSlot = new PartySlot(realPlayer, realPlayer.Visuals, isLocalReal: true, playerEntity);
        foreach (NCreature pet in realPets)
            realSlot.Pets.Add(new PartySlot(pet, pet.Visuals, isLocalReal: false, pet.Entity)
            {
                IsOsty = pet.Entity?.Monster is Osty,
            });

        var slots = new List<PartySlot> { realSlot };
        for (int i = 0; i < DoubleCount; i++)
        {
            PartySlot? made = TryCreateDouble(playerEntity, container, i);
            if (made == null) continue;
            slots.Add(made);
            _doubles.Add(made);
        }

        if (_doubles.Count == 0)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] no doubles could be created; leaving the layout untouched.");
            return;
        }

        _localPlayerEntity = playerEntity;
        _realSlot = realSlot;

        _scaling = ReadCameraScaling(room);
        _fullyCenterPlayers = ReadFullyCenterPlayers(room);

        PartyLayout.Apply(
            slots,
            scaling: _scaling,
            fullyCenterPlayers: _fullyCenterPlayers,
            localOstyOffset: ResolveOstyOffset(realPets),
            dimBackRows: DimBackRows,
            swapFront: SwapFront);

        // Pets that already exist at room build time (rare — most are summoned later).
        foreach (NCreature pet in realPets) MirrorPet(pet.Entity);

        // Listens to the real character's animation_started signal and copies whatever begins.
        // Belt and braces on top of the trigger hook, which turned out to miss the death case.
        SquadAnimationFollower.Attach(realPlayer, _doubles);

        MainFile.Logger.Info(
            $"[{MainFile.ModId}] squad of {slots.Count} placed ({_doubles.Count} double(s), {realPets.Count} starting pet(s)).");

        ReportAnimationCoverage();
    }

    /// <summary>
    /// Gives every double its own copy of a pet the local player just gained. Called from the
    /// <c>AddCreature</c> hook, which is how summoned pets (Osty) actually reach the scene.
    /// </summary>
    public static void MirrorPet(Creature? pet)
    {
        if (pet == null || _doubles.Count == 0) return;
        if (!IsLocalPet(pet, _localPlayerEntity)) return;
        if (_petCopies.ContainsKey(pet)) return;   // already mirrored

        var copies = new List<PartySlot>();
        for (int i = 0; i < _doubles.Count; i++)
        {
            PartySlot owner = _doubles[i];
            if (!GodotObject.IsInstanceValid(owner.Node) || owner.Node.GetParent() is not Control container) continue;

            try
            {
                NCreatureVisuals? body = pet.CreateVisuals();
                if (body == null) continue;
                body.Name = $"{PetNamePrefix}{i}_{owner.Pets.Count}";
                container.AddChildSafely(body);

                // Monsters pick their skin from the model; NCreature normally does this for real
                // pets in its own _Ready, which our entity-less copy never runs.
                if (pet.Monster != null) body.SetUpSkin(pet.Monster);
                body.HideBoundsMarker();

                var slot = new PartySlot(body, body, isLocalReal: false, entity: null)
                {
                    Animator = BuildAnimator(body, character: null, monster: pet.Monster),
                    IsOsty = pet.Monster is Osty,
                };
                if (slot.Animator == null) StartIdle(body);
                owner.Pets.Add(slot);
                copies.Add(slot);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] pet copy for double #{i} failed: {ex.Message}");
            }
        }

        if (copies.Count == 0) return;
        _petCopies[pet] = copies;

        // ★Re-run the whole layout, not just a restack. A squad member's Osty now shifts its owner
        // and re-spaces every member after it, so placing the new pet on its own would leave the
        // party half-updated. The layout's depth pass ends by pinning the controlled character on
        // top, which is what the bare BringToFront here used to do.
        ReapplyLayout();
        MainFile.Logger.Info($"[{MainFile.ModId}] mirrored pet '{pet.Monster?.Id.Entry ?? "?"}' to {copies.Count} double(s).");
    }

    /// <summary>
    /// Moves the squad onto the game-over screen's own layer, alongside the real fighters.
    ///
    /// That screen (used for BOTH victory and defeat — it branches on
    /// <c>CurrentRoom.IsVictoryRoom</c>) lifts the combatants above its backstop by walking
    /// <c>NCombatRoom.CreatureNodes</c>, which never contains our entity-less copies. Left behind,
    /// they sit under the backstop while the real character plays out its ending on top: exactly the
    /// "only the main character does the death motion" report.
    ///
    /// The death pose is re-asserted here rather than trusted from combat, so the ending looks right
    /// whether or not the trigger reached a copy earlier. On a victory nothing is forced — everyone
    /// keeps the idle they finished the fight in, which is what the real character does too.
    /// </summary>
    public static void FollowToGameOver(Control? container)
    {
        if (container == null || _doubles.Count == 0) return;

        bool died = _localPlayerEntity?.IsDead ?? false;
        int moved = 0;

        foreach (PartySlot slot in _doubles)
        {
            foreach (PartySlot node in new[] { slot }.Concat(slot.Pets))
            {
                if (!GodotObject.IsInstanceValid(node.Node)) continue;
                try
                {
                    // Reparent keeps the global transform, so everyone stays where they stood.
                    if (node.Node is Node2D n2) n2.Reparent(container);
                    else if (node.Node is Control c) c.Reparent(container);
                    moved++;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Warn($"[{MainFile.ModId}] could not move '{node.Node.Name}' to the ending screen: {ex.Message}");
                }
            }

            if (died) Play(slot, DeathTrigger);   // whichever animation system this copy has
        }

        MainFile.Logger.Info($"[{MainFile.ModId}] ending screen: moved {moved} squad node(s) ({(died ? "defeat" : "victory")}).");
    }

    /// <summary>Drops the copies of a pet the game just removed, so dead pets don't leave ghosts.</summary>
    public static void RemoveMirroredPet(Creature? pet)
    {
        if (pet == null || !_petCopies.TryGetValue(pet, out List<PartySlot>? copies)) return;
        _petCopies.Remove(pet);

        foreach (PartySlot copy in copies)
        {
            foreach (PartySlot owner in _doubles) owner.Pets.Remove(copy);
            if (GodotObject.IsInstanceValid(copy.Node)) copy.Node.QueueFree();
        }
        ReapplyLayout();
    }

    private static void Reset()
    {
        _doubles.Clear();
        _petCopies.Clear();
        _localPlayerEntity = null;
        _realSlot = null;

        SquadAnimationFollower.Detach();
    }

    private static bool IsLocalPet(Creature? candidate, Creature? playerEntity)
        => candidate is { IsPlayer: false } && playerEntity?.Player != null && candidate.PetOwner == playerEntity.Player;

    private static PartySlot? TryCreateDouble(Creature playerEntity, Control container, int index)
    {
        try
        {
            // Appearance is resolved per slot, so slot 0 can be another character while slot 1
            // wears a skin mod and slot 2 stays a copy of you.
            Appearance look = Appearance.Parse(SlotTokenFor(index), _rng);

            NCreatureVisuals? body = look.CreateBody(playerEntity);
            if (body == null)
            {
                MainFile.Logger.Warn($"[{MainFile.ModId}] double #{index}: appearance '{look}' produced no visuals.");
                return null;
            }
            body.Name = $"{DoubleNamePrefix}{index}";
            container.AddChildSafely(body);

            // ★Order matters: the skin swap needs SpineBody, which NCreatureVisuals._Ready builds,
            // so it can only run once the node is in the tree. ApplySkin restarts the idle itself.
            look.ApplySkin(body);

            var slot = new PartySlot(body, body, isLocalReal: false, entity: null)
            {
                // Animator last: it binds to the CURRENT skeleton, so building it before the skin
                // swap would leave it driving data the sprite no longer holds.
                Animator = BuildAnimator(body, look.ResolveCharacter() ?? playerEntity.Player?.Character, monster: null),
                Player = FindAnimationPlayer(body),
            };

            if (slot.Animator == null) StartIdle(body);   // no animator: at least stand still, breathing
            MainFile.Logger.Info($"[{MainFile.ModId}] double #{index} appearance = {look} " +
                                 $"(animator={(slot.Animator != null ? "yes" : "no")}).");
            return slot;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] double #{index} failed to spawn: {ex.Message}");
            return null;
        }
    }

    private static string SlotTokenFor(int index)
        => index >= 0 && index < SlotTokens.Length ? SlotTokens[index] : Appearance.SelfToken;

    private static void StartIdle(NCreatureVisuals visuals)
    {
        if (visuals.HasSpineAnimation) visuals.SpineAnimation.SetAnimation(IdleAnimation, loop: true);
    }

    /// <summary>
    /// Logs, once per combat, which of the expected animations each copy's rig actually carries.
    ///
    /// Whether a copy can follow the character at all comes down to this, and it varies per skin —
    /// a rig with no "attack" cannot be made to play one by any amount of hooking. Printing it up
    /// front turns "syncing seems broken for some skins" into a one-line answer.
    /// </summary>
    private static void ReportAnimationCoverage()
    {
        try
        {
            foreach (PartySlot d in _doubles)
            {
                if (!GodotObject.IsInstanceValid(d.Visuals)) continue;
                Node2D? body = d.Visuals.GetNodeOrNull<Node2D>("%Visuals");
                string kind = body == null ? "no %Visuals node" : body.GetClass();

                // Spelled out because "0 animations" has two very different causes: a rig the game
                // can drive that simply lacks the clip, versus a character that is not Spine at all
                // and so cannot be driven by anything this mod does.
                if (body == null || !d.Visuals.HasSpineAnimation)
                {
                    MainFile.Logger.Info(
                        $"[{MainFile.ModId}] '{d.Visuals.Name}': NOT a spine rig (body={kind}, scene=" +
                        $"{System.IO.Path.GetFileName(d.Visuals.SceneFilePath ?? "?")}).");

                    // Dump what IS in there. The game drives creatures only through Spine, so a
                    // non-Spine character is animated by its own mod — or not at all. Which of the
                    // standard Godot animation nodes it uses decides whether the squad could follow
                    // it, and there is no way to know without looking.
                    MainFile.Logger.Info($"[{MainFile.ModId}]   rig contents: {DescribeTree(d.Visuals, 0)}");

                    if (d.Player != null && GodotObject.IsInstanceValid(d.Player))
                    {
                        string[] clips = d.Player.GetAnimationList();
                        string mapped = string.Join(", ", TriggerAliases.Keys
                            .Select(t => $"{t}->{ResolveClip(d.Player, t) ?? "(none)"}"));
                        MainFile.Logger.Info($"[{MainFile.ModId}]   AnimationPlayer '{d.Player.Name}' clips: [{string.Join(", ", clips)}]");
                        MainFile.Logger.Info($"[{MainFile.ModId}]   trigger mapping: {mapped}");
                    }
                    else
                    {
                        MainFile.Logger.Info($"[{MainFile.ModId}]   no AnimationPlayer either - this copy cannot be animated.");
                    }
                    continue;
                }

                var have = new HashSet<string>(SpineSwap.AnimationNames(body), StringComparer.OrdinalIgnoreCase);
                string missing = string.Join(", ", TriggerAnimations.Values.Where(a => !have.Contains(a)));
                MainFile.Logger.Info(
                    $"[{MainFile.ModId}] '{d.Visuals.Name}': spine body={kind}, animator={(d.Animator != null ? "yes" : "no")}, " +
                    $"{have.Count} animation(s)" + (missing.Length > 0 ? $", MISSING: {missing}" : ", all expected present"));
            }
        }
        catch (Exception ex) { MainFile.Logger.Warn($"[{MainFile.ModId}] animation coverage report failed: {ex.Message}"); }
    }

    /// <summary>
    /// Compact class-name sketch of a node subtree, for diagnosing an unfamiliar rig. Depth- and
    /// width-limited so an elaborate scene cannot flood the log.
    /// </summary>
    private static string DescribeTree(Node node, int depth)
    {
        if (depth > 3) return "...";
        var parts = new List<string>();
        int shown = 0;
        foreach (Node child in node.GetChildren())
        {
            if (++shown > 8) { parts.Add("..."); break; }
            string inner = DescribeTree(child, depth + 1);
            parts.Add($"{child.Name}:{child.GetClass()}" + (inner.Length > 0 ? $"[{inner}]" : ""));
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Trigger names to the animation each one selects, mirroring CharacterModel.GenerateAnimator.
    /// Used when a copy has no animator of its own, so mirroring still works on a rig the game's
    /// state machine could not be built for.
    /// </summary>
    private static readonly Dictionary<string, string> TriggerAnimations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Idle"] = "idle_loop",
        ["Attack"] = "attack",
        ["Cast"] = "cast",
        ["Hit"] = "hurt",
        ["Dead"] = "die",
        ["Relaxed"] = "relaxed_loop",
    };

    /// <summary>
    /// Clip names a trigger may go by on a non-Spine rig, best match first.
    ///
    /// ★Names cannot simply be copied across backends. The real character is usually a Spine rig
    /// playing "attack"; a sprite-based copy has whatever its author called it. The trigger is the
    /// only thing both sides share, so each backend resolves it in its own vocabulary.
    /// </summary>
    private static readonly Dictionary<string, string[]> TriggerAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Idle"] = new[] { "idle_loop", "idle", "stand" },
        ["Attack"] = new[] { "attack", "atk", "hit_attack" },
        ["Cast"] = new[] { "cast", "skill", "magic" },
        ["Hit"] = new[] { "hurt", "hit", "damage", "damaged" },
        ["Dead"] = new[] { "die", "death", "dead" },
        ["Relaxed"] = new[] { "relaxed_loop", "relaxed", "victory", "win" },
    };

    /// <summary>First AnimationPlayer inside an actor, or null. Non-Spine character mods put one
    /// beside the sprite (measured: "OddmeltAnim" in SlayTheUniverse's characters).</summary>
    private static AnimationPlayer? FindAnimationPlayer(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is AnimationPlayer player) return player;
            if (FindAnimationPlayer(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>Clip on this player that best answers <paramref name="trigger"/>, or null.</summary>
    private static string? ResolveClip(AnimationPlayer player, string trigger)
    {
        if (!TriggerAliases.TryGetValue(trigger, out string[]? aliases)) return null;
        string[] clips = player.GetAnimationList();
        if (clips.Length == 0) return null;

        foreach (string alias in aliases)
        {
            string? exact = clips.FirstOrDefault(c => string.Equals(c, alias, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
        }
        foreach (string alias in aliases)
        {
            string? loose = clips.FirstOrDefault(c => c.Contains(alias, StringComparison.OrdinalIgnoreCase));
            if (loose != null) return loose;
        }
        return null;
    }

    /// <summary>
    /// Replays one trigger on a copy, in whichever animation system that copy actually has.
    /// Prefers the game's own state machine, because that reproduces its blending and
    /// return-to-idle; then a plain AnimationPlayer for sprite-based character mods; then a direct
    /// Spine play for a rig the state machine could not be built for.
    /// </summary>
    private static void Play(PartySlot slot, string trigger)
    {
        if (slot.Animator != null) { slot.Animator.SetTrigger(trigger); return; }
        if (!GodotObject.IsInstanceValid(slot.Visuals)) return;

        if (slot.Player != null && GodotObject.IsInstanceValid(slot.Player))
        {
            string? clip = ResolveClip(slot.Player, trigger);
            if (clip != null) { slot.Player.Play(clip); return; }
        }

        if (!TriggerAnimations.TryGetValue(trigger, out string? animation)) return;
        if (slot.Visuals.GetNodeOrNull<Node2D>("%Visuals") is { } body) SpineSwap.PlayOn(body, animation);
    }

    /// <summary>
    /// Builds the game's own animation state machine for a copied actor, so mirrored triggers blend
    /// exactly like the real creature's rather than being hard-cut animation names.
    ///
    /// Gated on the skeleton actually having <c>idle_loop</c>: <c>CreatureAnimator</c>'s constructor
    /// seeks into the current track without a null check when its initial state is the idle, so a
    /// skin missing that animation would take the combat down with it. Returning null degrades to a
    /// still (but breathing) double instead.
    /// </summary>
    private static CreatureAnimator? BuildAnimator(NCreatureVisuals body, CharacterModel? character, MonsterModel? monster)
    {
        try
        {
            if (!body.HasSpineAnimation) return null;

            // Start the idle through PlayOn so a skeleton that names its idle something else still
            // gets going. ★This used to bail out when 'idle_loop' was absent, which quietly disabled
            // ALL mirroring for that copy — attack, hurt and death included. That is why syncing
            // looked skin-dependent.
            SpineSwap.PlayOn(body.GetNodeOrNull<Node2D>("%Visuals")!, IdleAnimation);

            if (character != null) return character.GenerateAnimator(body.SpineBody!);
            if (monster != null) return monster.GenerateAnimator(body.SpineBody!);
            return null;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] '{body.Name}': animator failed ({ex.Message}); mirroring disabled for it.");
            return null;
        }
    }

    /// <summary>
    /// Replays a real creature's animation trigger on its copies, so the squad swings, casts and
    /// flinches together with you instead of standing in a permanent idle.
    /// </summary>
    public static void MirrorTrigger(Creature? source, string trigger)
    {
        if (source == null || _doubles.Count == 0) return;

        try
        {
            if (source == _localPlayerEntity)
            {
                foreach (PartySlot d in _doubles) Play(d, trigger);
                return;
            }

            if (_petCopies.TryGetValue(source, out List<PartySlot>? copies))
                foreach (PartySlot c in copies) Play(c, trigger);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] trigger '{trigger}' mirror failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The played character's Osty offset, straight from the game. Squad members do not use it:
    /// the formula measures the OWNER's hitbox, and a double is a different character with a
    /// different width, so the layout sizes their Osty's slot from the pet itself instead.
    /// </summary>
    private static Vector2? ResolveOstyOffset(IEnumerable<NCreature> realPets)
    {
        NCreature? node = realPets.FirstOrDefault(p => p.Entity is { Monster: Osty });
        if (node?.Entity == null) return null;
        try { return NCreature.GetOstyOffsetFromPlayer(node.Entity); }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[{MainFile.ModId}] Osty offset unavailable ({ex.Message}); using the generic spot.");
            return null;
        }
    }

    /// <summary>
    /// Re-runs the whole party layout over the current members.
    ///
    /// ★Pets arrive mid-combat — an Osty is summoned, not present when the room is built — and a
    /// squad member's Osty now moves its owner, so a new pet cannot be placed on its own: the
    /// formation around it changes. This used to be a separate pet-only routine, which is exactly
    /// how the two placements drifted apart. The initial layout was corrected while the path that
    /// actually runs for a summoned Osty kept its own stale arithmetic, so the fix looked like it
    /// had not worked. One formula, one caller.
    /// </summary>
    private static void ReapplyLayout()
    {
        if (_realSlot == null || !GodotObject.IsInstanceValid(_realSlot.Node)) return;
        if (_doubles.Count == 0) return;

        var slots = new List<PartySlot> { _realSlot };
        slots.AddRange(_doubles.Where(d => GodotObject.IsInstanceValid(d.Node)));

        PartyLayout.Apply(
            slots,
            scaling: _scaling,
            fullyCenterPlayers: _fullyCenterPlayers,
            localOstyOffset: ResolveOstyOffset(_realSlot.Pets.Select(p => p.Node).OfType<NCreature>()),
            dimBackRows: DimBackRows,
            swapFront: SwapFront);
    }

    private static float ReadCameraScaling(NCombatRoom room)
    {
        try { return room._visuals.Encounter.GetCameraScaling(); }
        catch { return 1f; }
    }

    private static bool ReadFullyCenterPlayers(NCombatRoom room)
    {
        try { return room._visuals.Encounter.FullyCenterPlayers; }
        catch { return false; }
    }
}
