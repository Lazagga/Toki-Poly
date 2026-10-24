using System;
using System.Collections.Generic;
using GaeBullBing.Core.Game;
using GaeBullBing.Core.Monsters;

namespace GaeBullBing.Core.Towers
{
    /// <summary>Opt-in, bounded diagnostics. A normal game does not allocate a recorder.</summary>
    public sealed class CombatTrace
    {
        public const int EntryLimit = 3000;
        public readonly List<CombatTraceEntry> Entries = new();
        public int DroppedEntries { get; private set; }
        public int Phase { get; private set; }
        public string Source { get; private set; } = "system";

        public void Add(string message, int towerId = 0, int targetId = 0)
        {
            if (Entries.Count >= EntryLimit) { DroppedEntries++; return; }
            Entries.Add(new CombatTraceEntry(Phase, Source, towerId, targetId, message));
        }

        public IDisposable Enter(int phase, string source, int towerId = 0, MonsterState target = null)
            => new Step(this, phase, source, towerId, target);

        public static IDisposable StepFor(GameState state, int phase, string source,
            int towerId = 0, MonsterState target = null) => state.CombatTrace?.Enter(phase, source, towerId, target);

        public static string Describe(MonsterState m) =>
            $"HP {m.CurrentHealth:0.###}/{m.MaxHealth:0.###}, tile {m.CurrentTileIndex}, " +
            $"burn {m.BurnStacks}/{m.MaxBurnStacks}, frost {m.FrostbiteStacks}, freeze {m.FrozenMovesRemaining}, " +
            $"shock {m.Shocked}, stun {m.StunnedMovesRemaining}, slow {m.NextMoveReduction}, " +
            $"freeze immunity {m.FreezeImmuneThisTurn}/{m.FreezeImmunityPending}, " +
            $"knockback immunity {m.KnockbackConsumed}/{m.KnockbackImmunityPending}, " +
            $"store {m.DamageStoreMarked}:{m.StoredDamage:0.###}";

        private sealed class Step : IDisposable
        {
            private readonly CombatTrace trace;
            private readonly int oldPhase, tower;
            private readonly string oldSource, before;
            private readonly MonsterState target;
            private bool disposed;
            public Step(CombatTrace trace, int phase, string source, int tower, MonsterState target)
            {
                this.trace = trace; this.tower = tower; this.target = target;
                oldPhase = trace.Phase; oldSource = trace.Source;
                trace.Phase = phase; trace.Source = source;
                before = target == null ? null : Describe(target);
                trace.Add("Begin" + (before == null ? "" : ": " + before), tower, target?.InstanceId ?? 0);
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (target != null)
                {
                    var after = Describe(target);
                    trace.Add(before == after ? "End: state unchanged" : "End: " + after, tower, target.InstanceId);
                }
                trace.Phase = oldPhase; trace.Source = oldSource;
            }
        }
    }

    public readonly struct CombatTraceEntry
    {
        public CombatTraceEntry(int phase, string source, int towerId, int targetId, string message)
        { Phase = phase; Source = source; TowerId = towerId; TargetId = targetId; Message = message; }
        public int Phase { get; }
        public string Source { get; }
        public int TowerId { get; }
        public int TargetId { get; }
        public string Message { get; }
        public override string ToString() =>
            $"[{(Phase == 0 ? "SYS" : Phase.ToString())}: {Source}] T#{TowerId} M#{TargetId} — {Message}";
    }
}
