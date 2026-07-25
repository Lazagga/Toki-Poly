using System.Collections.Generic;
using GaeBullBing.Core.Board;
using GaeBullBing.Core.Dice;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core;

namespace GaeBullBing.Core.Game
{
    public sealed class GameState
    {
        public const int DefaultEscapeLimit = 5;

        public int Round { get; set; }
        public int CompletedLaps { get; set; }
        public TurnPhase CurrentPhase { get; set; }
        public int EscapedMonsterCount { get; set; }
        public int EscapeLimit { get; set; } = DefaultEscapeLimit;
        public bool IsGameOver => CurrentPhase == TurnPhase.Defeat;
        public bool IsVictory => CurrentPhase == TurnPhase.Victory;
        public bool IsFinished => IsGameOver || IsVictory;
        public bool BossSpawned { get; set; }
        public bool BossDefeated { get; set; }
        public bool BossEscaped { get; set; }
        public int BossInstanceId { get; set; }
        public PlayerState Player { get; } = new();
        public BoardState Board { get; } = new();
        public List<MonsterState> Monsters { get; } = new();
        public DifficultyState Difficulty { get; } = new();
        public List<DiceState> Dice { get; } = new();
        public DiceInventoryState DiceInventory { get; } = new();

        public List<int> LastDiceResults { get; } = new();
        public int LastMoveDistance { get; set; }
        public Dictionary<TowerElement, float> PermanentTowerDamageRateBonuses { get; } = new();
        public Dictionary<TowerElement, int> PermanentTowerDamageFlatBonuses { get; } = new();
        public float PermanentAllTowerDamageRateBonus { get; private set; }
        private readonly float[] permanentLineTowerDamageRateBonuses = new float[4];

        public float GetPermanentTowerDamageRateBonus(TowerElement element) =>
            PermanentTowerDamageRateBonuses.TryGetValue(element, out var value) ? value : 0f;

        public void AddPermanentTowerDamageRateBonus(TowerElement element, float amount)
        {
            if (element == TowerElement.None || amount <= 0f) return;
            PermanentTowerDamageRateBonuses[element] = GetPermanentTowerDamageRateBonus(element) + amount;
        }

        public int GetPermanentTowerDamageFlatBonus(TowerElement element) =>
            PermanentTowerDamageFlatBonuses.TryGetValue(element, out var value) ? value : 0;

        public void AddPermanentTowerDamageFlatBonus(TowerElement element, int amount)
        {
            if (element == TowerElement.None || amount <= 0) return;
            PermanentTowerDamageFlatBonuses[element] = GetPermanentTowerDamageFlatBonus(element) + amount;
        }

        public void AddPermanentAllTowerDamageRateBonus(float amount)
        {
            if (amount > 0f) PermanentAllTowerDamageRateBonus += amount;
        }

        public float GetPermanentLineTowerDamageRateBonus(int line) =>
            line >= 0 && line < permanentLineTowerDamageRateBonuses.Length
                ? permanentLineTowerDamageRateBonuses[line]
                : 0f;

        public void AddPermanentLineTowerDamageRateBonus(int line, float amount)
        {
            if (line >= 0 && line < permanentLineTowerDamageRateBonuses.Length && amount > 0f)
                permanentLineTowerDamageRateBonuses[line] += amount;
        }

        public void ResetPermanentTowerBonuses()
        {
            PermanentTowerDamageRateBonuses.Clear();
            PermanentTowerDamageFlatBonuses.Clear();
            PermanentAllTowerDamageRateBonus = 0f;
            System.Array.Clear(permanentLineTowerDamageRateBonuses, 0, permanentLineTowerDamageRateBonuses.Length);
        }
    }

    public sealed class PlayerState
    {
        public int CurrentTileIndex { get; set; }
        public int GrowthPoints { get; set; }
        public int Gold { get; set; }
        public int DicePoints { get; set; }
    }
}
