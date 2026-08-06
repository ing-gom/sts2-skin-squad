using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Characters;

namespace Sts2SkinSquad;

/// <summary>
/// Re-implementation of the game's own co-op party placement
/// (<c>NCombatRoom.PositionPlayersAndPets</c>) over <see cref="PartySlot"/>s instead of
/// <c>NCreature</c>s.
///
/// Why not just call the game's method: it builds its groups by testing
/// <c>creatureNode.Entity.IsPlayer</c> and then resolves every non-player node through
/// <c>list.First(p =&gt; p.player.Entity.Player == creature.Entity.PetOwner)</c>. Our doubles
/// carry no entity at all, so they'd be classified as pets with no owner and that
/// <c>First(...)</c> would throw. Mirroring the math keeps the doubles entity-free, which is the
/// whole point of the design — no combat state, no HP bar, no targeting.
///
/// The arithmetic below is deliberately a line-for-line transliteration so the squad lines up
/// exactly like a real co-op party. Same constants: 960 reference half-width, 70 spacing,
/// 150 minimum left margin, y=200 front row, 120 total back-row rise, 0.33 back-row x drift.
/// </summary>
internal static class PartyLayout
{
    private const float ReferenceHalfWidth = 960f;
    private const float DefaultSpacing = 70f;
    private const float MinMargin = 150f;
    private const float FrontRowY = 200f;
    private const float BackRowRise = 120f;
    private const float BackRowDrift = 0.33f;
    private const float PetInset = 20f;
    private const float PetDropY = 10f;
    private const float NecrobinderPlayerShift = 150f;
    private const float NecrobinderTargetAdvance = 100f;

    /// <summary>Breathing room on each side of a squad member's Osty, so it reads as standing
    /// beside its owner rather than merged into it.</summary>
    private const float OstyGap = 22f;

    /// <summary>Reservation width when a mirrored pet's bounds are not measurable yet.</summary>
    private const float FallbackPetWidth = 140f;

    private static readonly Color DimColor = new(0.5f, 0.5f, 0.5f);

    /// <param name="slots">Party members, front-to-back. Index 0 must be the real local player.</param>
    /// <param name="scaling">Encounter camera scaling (<c>Encounter.GetCameraScaling()</c>).</param>
    /// <param name="fullyCenterPlayers">Encounter flag; centres on the first member instead of the group.</param>
    /// <param name="localOstyOffset">
    /// Where the PLAYED character's Osty stands relative to it, from
    /// <c>NCreature.GetOstyOffsetFromPlayer</c>. Null when the party has no Osty. Squad members do
    /// not use it — their Osty gets a reserved slot sized from its own width instead.
    /// </param>
    /// <param name="dimBackRows">Apply the game's grey-out to members behind the front row.</param>
    /// <param name="swapFront">
    /// Position-swap mode. When set (and a squad member exists), the first squad member is stood in
    /// the front (main) spot and the controlled character drops into the slot immediately behind it.
    /// </param>
    public static void Apply(
        IReadOnlyList<PartySlot> slots,
        float scaling,
        bool fullyCenterPlayers,
        Vector2? localOstyOffset,
        bool dimBackRows,
        bool swapFront = false)
    {
        if (slots.Count == 0) return;
        if (scaling <= 0f) scaling = 1f;

        // ── Position-swap mode ─────────────────────────────────────────────────────────────────
        // Index 0 is always the controlled character; index 1 is the first squad member. Swapping
        // just those two entries stands the member in the front (main) spot and the played character
        // one place behind it. It is only a reordering of the SAME slots: the real player keeps
        // IsLocalReal, so its Osty hand-placement and its top-of-draw pinning travel with it to
        // whatever position it lands in, and the member now at index 0 gets the ordinary member
        // treatment. Index 1 falls in row 0 for every supported party size (2–4), so the played
        // character never ends up dimmed as a back-row figure.
        //
        // Off by default. The solo-verify's assert M (no squad node right of, or drawn above, the
        // player) only holds while this is off — which is the shipped default — because the whole
        // point here is to place a member to the player's right.
        if (swapFront && slots.Count >= 2)
        {
            var reordered = slots.ToList();
            (reordered[0], reordered[1]) = (reordered[1], reordered[0]);
            slots = reordered;
        }

        float available = ReferenceHalfWidth / scaling;
        float spacing = DefaultSpacing;
        int cols = (int)Math.Ceiling(Math.Sqrt(slots.Count));
        int rows = (int)Math.Ceiling((double)slots.Count / cols);

        // Width of the front row only — the game sizes the whole block from it.
        float frontRowWidth = slots.Take(cols).Sum(s => s.Width);
        float blockWidth = frontRowWidth + (cols - 1) * spacing;
        float drift = frontRowWidth * BackRowDrift;
        float perRowX = rows > 1 ? drift / (rows - 1) : 0f;
        float perRowY = rows > 1 ? BackRowRise / (rows - 1) : 0f;

        float startX;
        if (fullyCenterPlayers)
        {
            startX = slots[0].Width * -0.5f;
        }
        else
        {
            startX = Math.Max((available - blockWidth) * 0.5f, MinMargin);

            // A full second row makes the block effectively wider by the drift.
            if (slots.Count >= cols * 2) frontRowWidth += drift;

            // Overflow: squeeze the spacing rather than letting the party run off-screen.
            if (startX + blockWidth > available && cols > 1)
            {
                spacing = (available - MinMargin - frontRowWidth) / (cols - 1);
                blockWidth = frontRowWidth + (cols - 1) * spacing;
                startX = (available - blockWidth) * 0.5f;
            }
        }

        // Allies grow leftwards from the container origin, so every x below is negated.
        for (int row = 0; row < cols; row++)
        {
            float targetX = startX + perRowX * row;

            for (int col = 0; col < cols; col++)
            {
                int index = row * cols + col;
                if (index >= slots.Count) break;

                PartySlot slot = slots[index];
                float width = slot.Width;
                slot.Position = new Vector2(-targetX - width * 0.5f, FrontRowY - perRowY * row);

                List<PartySlot> pets = slot.Pets;

                PartySlot? osty = pets.FirstOrDefault(p => p.IsOsty);

                // ── The played character: the game's own treatment, untouched ──────────────
                //
                // Its Osty is thrown well out to the right (the offset is ~150-250px on top of half
                // a hitbox) and the character steps 150px left to clear the space. That is what
                // vanilla solo looks like, so it is left exactly as it is.
                //
                // The gate is the character, not the pet: the game runs PositionLocalPlayerOsty
                // even before the Osty has spawned, so keying off the pet would stand the character
                // 150px right of its vanilla spot for that window.
                if (slot.IsLocalReal && (osty != null || IsNecrobinder(slot)))
                {
                    if (osty != null) pets = pets.Where(p => p != osty).ToList();

                    float playerY = slot.Position.Y;
                    slot.Position = new Vector2(slot.Position.X - NecrobinderPlayerShift, playerY);
                    if (osty != null && localOstyOffset.HasValue)
                        osty.Position = new Vector2(-targetX, playerY) + localOstyOffset.Value;
                    targetX += NecrobinderTargetAdvance;
                }
                // ── A squad member: its Osty gets a reserved slot immediately to its right ──
                //
                // Not the treatment above. Borrowing that threw a member's Osty ~470px away, which
                // put it nearer the neighbour than its own owner — the pet stopped reading as
                // belonging to anybody. Not the game's generic pet spot either: that overlaps the
                // owner outright (measured at 100px), which is what made an Osty look like it was
                // standing behind its character.
                //
                // Instead the member is pushed left by exactly the Osty's footprint, and the Osty
                // takes the space that opens up. Because the reservation happens BEFORE the member
                // is placed, the row reads right-to-left as
                //     [member i-1] gap [osty i] gap [member i] …
                // with no two figures sharing x. Every Osty is visible, each one adjacent to its
                // own owner, and none can reach the played character — member 1's reservation ends
                // where the game's own spacing already put the gap.
                else if (osty != null)
                {
                    pets = pets.Where(p => p != osty).ToList();

                    // Bounds can still be empty on the frame a mirrored pet is added; a zero-width
                    // reservation would collapse the slot and stack the Osty on its owner.
                    float ostyWidth = osty.Width > 1f ? osty.Width : FallbackPetWidth;
                    float reserved = ostyWidth + OstyGap * 2f;
                    float ownerRightEdge = targetX;

                    targetX += reserved;
                    slot.Position = new Vector2(-targetX - width * 0.5f, FrontRowY - perRowY * row);
                    osty.Position = new Vector2(
                        -ownerRightEdge - OstyGap - ostyWidth * 0.5f,
                        slot.Position.Y + PetDropY);
                }

                // Every other pet: line-for-line with the game, no mirroring for doubles.
                //
                // This deliberately drops an earlier "fix" that fanned a double's pets leftwards to
                // dodge the played character. The collision it was working around came from the
                // −150 shift above, not from this formula, and mirroring stood the pet somewhere
                // co-op never puts it.
                float petSpread = pets.Count > 1 ? width / (pets.Count - 1) : 0f;
                for (int i = 0; i < pets.Count; i++)
                {
                    PartySlot pet = pets[i];
                    pet.Position = new Vector2(
                        -targetX + PetInset - i * petSpread - pet.Width * 0.5f,
                        slot.Position.Y + PetDropY);
                }

                if (row > 0 && dimBackRows)
                {
                    slot.Dim(DimColor);
                    foreach (PartySlot pet in pets) pet.Dim(DimColor);
                    // Explicit: the Osty was lifted out of `pets` above, so the loop cannot reach it
                    // and a back-row member would otherwise stand dimmed beside a bright pet.
                    osty?.Dim(DimColor);
                }

                targetX += width + spacing;
            }
        }

        // Depth: repeatedly moving each member to index 0 leaves the last-processed (backmost)
        // member drawn first, i.e. furthest back. Pets sit directly in front of their owner.
        foreach (PartySlot slot in slots)
        {
            slot.Node.GetParent()?.MoveChildSafely(slot.Node, 0);
            for (int i = 0; i < slot.Pets.Count; i++)
            {
                PartySlot pet = slot.Pets[i];
                pet.Node.GetParent()?.MoveChildSafely(pet.Node, i + 1);

                // The game hides the bounds marker on pets that aren't the local player's.
                if (!slot.IsLocalReal) pet.HideBounds();
            }
        }

        BringLocalPlayerToFront(slots);
    }

    /// <summary>
    /// Whether this member is a Necrobinder, matching the game's
    /// <c>player.Entity.Player.Character is Necrobinder</c> test. False for a double, which carries
    /// no entity — and correctly so: the branch it gates is local-player-only in the game.
    /// </summary>
    private static bool IsNecrobinder(PartySlot slot)
    {
        try { return slot.Entity?.Player?.Character is Necrobinder; }
        catch { return false; }
    }

    /// <summary>
    /// Forces the controlled character and its pets to the very front of the draw order.
    ///
    /// The pass above interleaves each member with its pets, which leaves the final ordering
    /// dependent on how many pets each one has — a squad member's Osty could end up painted over
    /// the character the player is actually controlling. The one thing that must never be ambiguous
    /// is which figure is you, so it is pinned last (drawn on top) explicitly. Pets stay in front of
    /// their own owner within the group, matching the game.
    /// </summary>
    private static void BringLocalPlayerToFront(IReadOnlyList<PartySlot> slots)
        => BringToFront(slots.FirstOrDefault(s => s.IsLocalReal));

    /// <summary>
    /// Pins one member (and its pets) to the end of the container's child list, i.e. drawn on top.
    /// Called again whenever pets are added mid-combat, since each addition appends to the list.
    /// </summary>
    public static void BringToFront(PartySlot? me)
    {
        if (me == null || !GodotObject.IsInstanceValid(me.Node)) return;
        if (me.Node.GetParent() is not Node parent) return;

        parent.MoveChildSafely(me.Node, parent.GetChildCount() - 1);
        foreach (PartySlot pet in me.Pets)
            if (GodotObject.IsInstanceValid(pet.Node)) parent.MoveChildSafely(pet.Node, parent.GetChildCount() - 1);
    }
}
