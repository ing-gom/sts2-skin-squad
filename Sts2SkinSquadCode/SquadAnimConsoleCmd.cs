using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace Sts2SkinSquad;

/// <summary>
/// Forces an animation on the controlled character so the squad's mirroring can be eyeballed.
///
///   <c>squadanim</c>            list the triggers
///   <c>squadanim die</c>        death
///   <c>squadanim win</c>        the post-combat relaxed loop
///   <c>squadanim attack</c>     etc.
///
/// It drives the REAL character rather than the copies: every creature animation in the game funnels
/// through <c>NCreature.SetAnimationTrigger</c>, which this mod hooks, so firing it there is the same
/// path a real attack takes — and a copy that fails to follow is a genuine bug, not a test artefact.
///
/// The game auto-discovers AbstractConsoleCmd subclasses inside mods, so this needs no registration.
/// </summary>
public class SquadAnimConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "squadanim";
    public override string Args => "[idle|attack|cast|hurt|die|revive|win]";
    public override string Description => "Play an animation on your character and the Skin Squad copies.";
    public override bool IsNetworked => false;
    public override bool DebugOnly => false;

    /// <summary>Friendly name to the trigger the game's CreatureAnimator actually listens for.</summary>
    private static readonly (string alias, string trigger)[] Triggers =
    {
        ("idle",   "Idle"),
        ("attack", "Attack"),
        ("cast",   "Cast"),
        ("hurt",   "Hit"),
        ("die",    "Dead"),
        ("revive", "Revive"),
        ("win",    "Relaxed"),
    };

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            return new CmdResult(true, "squadanim " + string.Join(" | ", Triggers.Select(t => t.alias)));

        string wanted = args[0].Trim();
        string? trigger = Triggers
            .FirstOrDefault(t => string.Equals(t.alias, wanted, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(t.trigger, wanted, StringComparison.OrdinalIgnoreCase))
            .trigger;

        if (string.IsNullOrEmpty(trigger))
            return new CmdResult(false, $"unknown animation '{wanted}'. Try: {string.Join(", ", Triggers.Select(t => t.alias))}");

        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null) return new CmdResult(false, "not in a combat room — start a fight first.");

        NCreature? me = room.CreatureNodes
            .Where(GodotObject.IsInstanceValid)
            .FirstOrDefault(c => c.Entity is { IsPlayer: true });
        if (me == null) return new CmdResult(false, "no player creature in this room.");

        me.SetAnimationTrigger(trigger);

        // "Relaxed" is a special case worth calling out: the animator accepts it, but nothing in
        // combat ever fires it — the victory and shop screens build their own visuals and play
        // relaxed_loop on those directly. So this command is the only way to see it on the squad,
        // and the squad not showing it after a real victory is expected, not a defect.
        string note = trigger == "Relaxed"
            ? " (note: combat never fires this on its own — the victory screen uses separate visuals)"
            : "";
        return new CmdResult(true, $"played '{trigger}' on you and the squad{note}.");
    }
}
