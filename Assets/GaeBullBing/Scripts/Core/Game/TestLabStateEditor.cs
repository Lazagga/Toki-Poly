using System;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;

namespace GaeBullBing.Core.Game
{
    // Validated DTOs keep invalid/partially entered UI fields out of live state.
    public sealed class TestMonsterSettings
    {
        public float MaxHealth, Health, Defense;
        public int Tile, Move, Distance, Burn, MaxBurn = 20, Frost, Freeze, Stun, Slow;
        public bool Shock, BurnImmune, FrostImmune, FreezeImmune, KnockbackImmune, ShockImmune;
        public bool FreezeImmuneThisTurn, FreezeImmunityPending, KnockbackConsumed, KnockbackPending;
        public int BurnSourceTowerId;

        public static TestMonsterSettings Read(MonsterState m) => new() {
            MaxHealth = m.MaxHealth, Health = m.CurrentHealth, Defense = m.BaseDefense,
            Tile = m.CurrentTileIndex, Move = m.MoveDistance, Distance = m.DistanceTravelled,
            Burn = m.BurnStacks, MaxBurn = m.MaxBurnStacks, Frost = m.FrostbiteStacks,
            Freeze = m.FrozenMovesRemaining, Stun = m.StunnedMovesRemaining, Slow = m.NextMoveReduction,
            Shock = m.Shocked, BurnImmune = m.IsImmuneTo("burn"), FrostImmune = m.IsImmuneTo("frostbite"),
            FreezeImmune = m.IsImmuneTo("freeze"), KnockbackImmune = m.IsImmuneTo("knockback"),
            ShockImmune = m.IsImmuneTo("shock"), FreezeImmuneThisTurn = m.FreezeImmuneThisTurn,
            FreezeImmunityPending = m.FreezeImmunityPending, KnockbackConsumed = m.KnockbackConsumed,
            KnockbackPending = m.KnockbackImmunityPending, BurnSourceTowerId = m.BurnSourceTowerId };

        public bool Apply(MonsterState m, int tileCount, out string error)
        {
            error = "Invalid monster values: HP > 0, HP <= max HP, nonnegative numbers and a valid tile required.";
            if (m == null || !Finite(MaxHealth) || !Finite(Health) || !Finite(Defense) ||
                MaxHealth <= 0f || Health <= 0f || Health > MaxHealth || Defense < 0f ||
                Tile < 0 || Tile >= tileCount || Move < 0 || Distance < 0 || Burn < 0 ||
                MaxBurn < 1 || Burn > MaxBurn || Frost < 0 || Freeze < 0 || Stun < 0 || Slow < 0) return false;
            m.MaxHealth = MaxHealth; m.CurrentHealth = Health; m.BaseDefense = Defense;
            m.CurrentTileIndex = Tile; m.MoveDistance = Move; m.DistanceTravelled = Distance;
            var previousBurn = m.BurnStacks;
            var previousOwner = m.BurnSourceTowerId;
            m.MaxBurnStacks = MaxBurn; m.BurnStacks = Burn;
            m.BurnSourceTowerId = BurnSourceTowerId;
            for (var i = previousOwner != BurnSourceTowerId ? 0 : previousBurn; i < m.BurnSourceTowerIds.Count; i++)
                m.BurnSourceTowerIds[i] = BurnSourceTowerId;
            m.FrostbiteStacks = Frost; m.FrozenMovesRemaining = Freeze; m.StunnedMovesRemaining = Stun;
            m.NextMoveReduction = Slow; m.Shocked = Shock;
            SetImmunity(m, "burn", BurnImmune); SetImmunity(m, "frostbite", FrostImmune);
            SetImmunity(m, "freeze", FreezeImmune); SetImmunity(m, "knockback", KnockbackImmune);
            SetImmunity(m, "shock", ShockImmune);
            m.FreezeImmuneThisTurn = FreezeImmuneThisTurn; m.FreezeImmunityPending = FreezeImmunityPending;
            m.KnockbackConsumed = KnockbackConsumed; m.KnockbackImmunityPending = KnockbackPending;
            m.KnockbackImmunityLine = KnockbackConsumed || KnockbackPending ? MonsterService.GetLine(Tile) : -1;
            error = null; return true;
        }
        private static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        private static void SetImmunity(MonsterState m, string id, bool enabled)
        { if (enabled) m.StatusImmunities.Add(id); else m.StatusImmunities.Remove(id); }

        public static void ClearStatuses(MonsterState m)
        {
            m.BurnStacks = 0; m.BurnSourceTowerId = 0; m.MaxBurnStacks = 20;
            m.FrostbiteStacks = 0; m.FrostbiteBurstThreshold = 3; m.FrostbiteBurstDamageMultiplier = 3f;
            m.FrozenMovesRemaining = 0; m.StunnedMovesRemaining = 0; m.NextMoveReduction = 0;
            m.Shocked = false; m.ShockDamageBonus = .3f; m.TileVulnerability = 0f;
            m.FreezeImmuneThisTurn = false; m.FreezeImmunityPending = false;
            m.KnockbackConsumed = false; m.KnockbackImmunityPending = false; m.KnockbackImmunityLine = -1;
            m.DamageStoreMarked = false; m.StoredDamage = 0; m.DamageStoreMultiplier = 0; m.DamageStoreSourceTowerId = 0;
            m.LineTeleportConsumed = false; m.TouchedFireThisMove = false;
            // Innate immunities/passives and HP/position are intentionally preserved.
        }
    }

    public sealed class TestTowerSettings
    {
        public int Cooldown, Charge, Overheat, Periodic, LastTarget;
        public int BonusAttacks, BonusTurns, PendingAttacks, PendingTurns, PermanentAttacks;
        public float ConsecutiveRate;
        public bool FeatherSealed;
        public static TestTowerSettings Read(TowerState t) => new() {
            Cooldown = t.AttackCooldownRounds, Charge = t.ChargeCount, Overheat = t.OverheatAttackTurns,
            Periodic = t.PeriodicTurnCount, LastTarget = t.LastAttackTargetId,
            ConsecutiveRate = t.ConsecutiveDamageBonus, FeatherSealed = t.IsFeatherSealed,
            BonusAttacks = t.BonusAttackCount, BonusTurns = t.BonusAttackTurnsRemaining,
            PendingAttacks = t.PendingBonusAttackCount, PendingTurns = t.PendingBonusAttackTurns,
            PermanentAttacks = t.PermanentBonusAttackCount };
        public bool Apply(TowerState t, out string error)
        {
            error = "Tower counters must be nonnegative, charge/overheat/consecutive values must fit effect caps.";
            if (t == null || Cooldown < 0 || Charge < 0 || Overheat < 0 || Periodic < 0 || LastTarget < 0 ||
                BonusAttacks < 0 || BonusTurns < 0 || PendingAttacks < 0 || PendingTurns < 0 || PermanentAttacks < 0 ||
                float.IsNaN(ConsecutiveRate) || float.IsInfinity(ConsecutiveRate) || ConsecutiveRate < 0 ||
                Charge > (t.HasEffect("charge_damage") ? Math.Max(1, t.GetEffectValue("charge_cap", 3)) : 0) ||
                Overheat > (t.HasEffect("overheat_cycle") ? Math.Max(1, t.GetEffectValue("overheat_cycle")) : 0) ||
                ConsecutiveRate > (t.HasEffect("consecutive_target_damage") ? t.GetEffectValue("consecutive_target_damage_cap", 100) / 100f : 0)) return false;
            t.AttackCooldownRounds = Cooldown; t.ChargeCount = Charge; t.OverheatAttackTurns = Overheat;
            t.PeriodicTurnCount = Periodic; t.LastAttackTargetId = LastTarget; t.ConsecutiveDamageBonus = ConsecutiveRate;
            t.IsFeatherSealed = FeatherSealed; t.BonusAttackCount = BonusAttacks; t.BonusAttackTurnsRemaining = BonusTurns;
            t.PendingBonusAttackCount = PendingAttacks; t.PendingBonusAttackTurns = PendingTurns;
            t.PermanentBonusAttackCount = PermanentAttacks; error = null; return true;
        }
        public static void ClearCounters(TowerState t)
        {
            t.AttackCooldownRounds = t.ChargeCount = t.OverheatAttackTurns = t.PeriodicTurnCount = 0;
            t.EffectReadyPhases.Clear();
            t.LastAttackTargetId = 0; t.ConsecutiveDamageBonus = t.ActiveChargeBonus = 0;
            t.AttacksThisTurn = t.BonusAttackCount = t.BonusAttackTurnsRemaining = 0;
            t.PendingBonusAttackCount = t.PendingBonusAttackTurns = 0;
            t.StepDamageRound = t.PassAttackRound = -1; t.TemporaryDamageBuffs.Clear(); t.IsFeatherSealed = false;
        }
    }
}
