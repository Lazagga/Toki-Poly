using System;
using UnityEngine;

namespace GaeBullBing.Core.Monsters
{
    // Interpretation rules only. Numerical passive strengths remain in Monster.json.
    [Serializable]
    public sealed class MonsterPassiveRules
    {
        [Tooltip("끄면 마리별 포획, 켜면 스폰 그룹 전원 포획 시 진행도 1 증가. 개별 보상은 그대로 지급합니다.")]
        public bool CountSpawnGroupAsOneKill;
        [Tooltip("잠수 중 타워의 직접 타겟에서 제외합니다. 잠수 중인 적도 클리어 판정에는 포함됩니다.")]
        public bool ExcludeSubmergedFromTargeting = true;
        [Tooltip("탈피가 전이로 받은 상태이상도 막습니다.")]
        public bool MoltBlocksTransferredStatuses = true;
        [Tooltip("화상 중첩 부여 한 번을 탈피 횟수 한 번으로 계산합니다.")]
        public bool MoltBlocksBurnStacks = true;
        [Tooltip("수달 등 물길 생성자도 물길의 이동 보너스를 받습니다.")]
        public bool WaterTrailAffectsCreators = true;
        [Tooltip("끄면 이동 시작 위치 기준. 켜면 이동 중 물길 진입 시에도 보너스를 한 번 반영합니다.")]
        public bool WaterTrailBonusOnEntry;
        [Tooltip("끄면 수풀 진입 칸마다 회복, 켜면 한 턴에 한 번만 회복합니다.")]
        public bool BushHealOncePerTurn;
        [Tooltip("끄면 최종 도착 타일에서만 낚아채고, 켜면 이동 중 진입한 타일마다 처리합니다.")]
        public bool SnatchOnEachEnteredTile;
        public bool PreserveSnatchedTowerUpgrades = true;
        [Tooltip("들고 있던 타워를 다른 속성의 일반 타일에도 내려놓을 수 있습니다.")]
        public bool AllowCrossElementTowerDrop = true;
        [Tooltip("사망/골인 시 원래 타일부터 반환 시도. 끄면 현재 위치부터 시도합니다. 차 있으면 빈 일반 타일을 찾습니다.")]
        public bool ReturnCarriedTowersToOrigin = true;
    }
}
