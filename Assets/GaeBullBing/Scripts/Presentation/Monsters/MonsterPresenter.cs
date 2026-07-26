using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using GaeBullBing.Core.Data;
using GaeBullBing.Presentation.Board;
using UnityEngine;
using UnityEngine.Serialization;

namespace GaeBullBing.Presentation.Monsters
{
    public sealed class MonsterPresenter : MonoBehaviour
    {
        [SerializeField] private BoardTilemapView boardView;
        [SerializeField] private Sprite monsterSprite;
        [SerializeField] private Sprite bearFrontSprite;
        [SerializeField] private Sprite bearBackSprite;
        [SerializeField] private Sprite foxFrontSprite;
        [SerializeField] private Sprite foxBackSprite;
        [SerializeField] private Sprite squirrelFrontSprite;
        [SerializeField] private Sprite squirrelBackSprite;
        [FormerlySerializedAs("crowStandingSprite")]
        [SerializeField] private Sprite crowStandingFrontSprite;
        [SerializeField] private Sprite crowStandingBackSprite;
        [FormerlySerializedAs("crowFlyingSprite")]
        [SerializeField] private Sprite crowFlyingFrontSprite;
        [SerializeField] private Sprite crowFlyingBackSprite;
        [SerializeField] private Sprite bossFeatherSprite;

        private readonly Dictionary<int, MonsterBoardView> views = new();
        private readonly Dictionary<int, MonsterState> states = new();
        private readonly Dictionary<int, float> displayedHealth = new();
        private readonly Dictionary<int, OverflowIndicatorView> indicators = new();
        private int playerTileIndex;
        private bool hasPlayer;
        public bool TryGetViewTransform(int instanceId, out Transform viewTransform)
        {
            if (views.TryGetValue(instanceId, out var view))
            {
                viewTransform = view.transform;
                return true;
            }
            viewTransform = null;
            return false;
        }

        public void SetTransitionOffset(Vector3 offset)
        {
            foreach (var view in views.Values)
                view.SetTransitionOffset(offset);
        }
        private static readonly Vector3[][] Slots = {
            new[] { Vector3.zero },
            new[] { new Vector3(-.18f,0), new Vector3(.18f,0) },
            new[] { new Vector3(0,.12f), new Vector3(-.2f,-.1f), new Vector3(.2f,-.1f) },
            new[] { new Vector3(0,.15f), new Vector3(-.22f,0), new Vector3(.22f,0), new Vector3(0,-.15f) }
        };

        [SerializeField] private PlayerBoardView playerView;
        public void SetPlayerTile(int tileIndex) { playerTileIndex = tileIndex; hasPlayer = true; ReflowAll(); }

        public void Spawn(MonsterState state)
        {
            GetMonsterSprites(state.DefinitionId, out var frontSprite, out var backSprite,
                out var flightFrontSprite, out var flightBackSprite);
            var usePlayerAlignedArt = frontSprite != null;
            var monsterObject = new GameObject($"Monster {state.InstanceId} ({state.DefinitionId})");
            monsterObject.transform.SetParent(transform, false);
            var visual = new GameObject("Visual"); visual.transform.SetParent(monsterObject.transform, false);
            visual.transform.localScale = usePlayerAlignedArt ? Vector3.one : new Vector3(.3f, .68f, 1f);
            var renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = usePlayerAlignedArt ? frontSprite : monsterSprite;
            var view = monsterObject.AddComponent<MonsterBoardView>();
            view.Initialize(
                state.InstanceId,
                boardView,
                state.CurrentTileIndex,
                usePlayerAlignedArt ? Vector3.zero : new Vector3(0f, .22f, 0f),
                usePlayerAlignedArt ? frontSprite : monsterSprite,
                usePlayerAlignedArt ? backSprite : monsterSprite,
                state.IsBoss ? flightFrontSprite : null,
                state.IsBoss ? flightBackSprite : null,
                state.IsBoss);
            view.TileChanged += OnMonsterTileChanged;
            
            view.UpdateStatus(state);
view.UpdateHealth(state.CurrentHealth, state.MaxHealth);
            views.Add(state.InstanceId, view);
            states.Add(state.InstanceId, state);
            displayedHealth[state.InstanceId] = state.CurrentHealth;
        }

        public IEnumerator PlayBossSpawnEntrance(int instanceId)
        {
            if (views.TryGetValue(instanceId, out var view))
            {
                view.PrepareSpawnEntrance();
                yield return view.PlaySpawnEntrance();
            }
        }

        public IEnumerator SpawnWithEntrance(MonsterState state)
        {
            Spawn(state);
            if (state != null && state.IsBoss)
                yield return PlayBossSpawnEntrance(state.InstanceId);
        }

        private void GetMonsterSprites(string definitionId, out Sprite frontSprite, out Sprite backSprite,
            out Sprite flightFrontSprite, out Sprite flightBackSprite)
        {
            flightFrontSprite = null;
            flightBackSprite = null;
            switch (definitionId)
            {
                case "MON_001":
                    frontSprite = bearFrontSprite;
                    backSprite = bearBackSprite != null ? bearBackSprite : bearFrontSprite;
                    return;
                case "MON_002":
                    frontSprite = foxFrontSprite;
                    backSprite = foxBackSprite != null ? foxBackSprite : foxFrontSprite;
                    return;
                case "MON_003":
                    frontSprite = squirrelFrontSprite;
                    backSprite = squirrelBackSprite != null ? squirrelBackSprite : squirrelFrontSprite;
                    return;
                case "BOSS_001":
                    frontSprite = crowStandingFrontSprite != null ? crowStandingFrontSprite : monsterSprite;
                    backSprite = crowStandingBackSprite != null ? crowStandingBackSprite : frontSprite;
                    flightFrontSprite = crowFlyingFrontSprite != null ? crowFlyingFrontSprite : frontSprite;
                    flightBackSprite = crowFlyingBackSprite != null ? crowFlyingBackSprite : flightFrontSprite;
                    return;
                default:
                    frontSprite = null;
                    backSprite = null;
                    return;
            }
        }

        public IEnumerator Move(MonsterMoveResult result)
        {
            

            if (!views.TryGetValue(result.InstanceId, out var view))
                yield break;

            if (states.TryGetValue(result.InstanceId, out var movingState))
                view.UpdateStatus(movingState);

            if (result.IsBoss)
            {
                foreach (var featherEvent in result.FeatherEvents)
                    if (featherEvent.StepOffset == 0)
                        yield return PlayFeatherEvent(featherEvent, view);
                if (result.Distance > 0)
                    yield return view.MoveFlying(result.StartTileIndex, result.Distance, result.ReachedBase);
                foreach (var featherEvent in result.FeatherEvents)
                    if (featherEvent.StepOffset > 0)
                        yield return PlayFeatherEvent(featherEvent, view);
            }
            else
                yield return view.MoveSteps(result.StartTileIndex, result.Distance);
            if (result.ReachedBase)
            {
                if (result.IsBoss)
                    yield return view.PlayGoalArrival();
                view.TileChanged -= OnMonsterTileChanged;
                views.Remove(result.InstanceId);
                states.Remove(result.InstanceId);
                displayedHealth.Remove(result.InstanceId);
                Destroy(view.gameObject);
            }
        }

        private IEnumerator PlayFeatherEvent(BossFeatherEvent featherEvent, MonsterBoardView bossView)
        {
            var target = boardView.GetWorldPosition(featherEvent.TileIndex) + new Vector3(0f, .12f, 0f);
            var feather = new GameObject(featherEvent.Type == BossFeatherEventType.Drop
                ? "Boss Feather Drop"
                : "Boss Feather Recover");
            feather.transform.SetParent(transform, false);
            var renderer = feather.AddComponent<SpriteRenderer>();
            renderer.sprite = bossFeatherSprite;
            renderer.color = Color.white;
            renderer.sortingOrder = 32760;
            feather.transform.localScale = new Vector3(1.5f, 1.7f, 1f);
            feather.transform.rotation = Quaternion.Euler(0f, 0f, -25f);
            var dropping = featherEvent.Type == BossFeatherEventType.Drop;
            var start = target + Vector3.up * (dropping ? 2f : 0f);
            var end = dropping || bossView == null ? target : bossView.VisualCenterPosition;
            for (var elapsed = 0f; elapsed < .55f; elapsed += Time.deltaTime)
            {
                var progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .55f));
                feather.transform.position = Vector3.Lerp(start, end, progress);
                if (!dropping) renderer.color = new Color(1f, 1f, 1f, 1f - progress);
                yield return null;
            }
            boardView.SetBossFeatherVisual(featherEvent.TileIndex, dropping);
            Destroy(feather);
        }

        public void RefreshLayout() => ReflowAll();

public void RefreshStatuses()
        {
            foreach (var pair in states)
                if (views.TryGetValue(pair.Key, out var view))
                    view.UpdateStatus(pair.Value);
        }


        private void OnMonsterTileChanged(int previousTileIndex, int currentTileIndex)
        {
            ReflowTile(previousTileIndex);
            if (currentTileIndex != previousTileIndex)
                ReflowTile(currentTileIndex);
        }

        public IEnumerator ApplyAttack(TowerAttackResult result)
        {
            ApplyAttackAtImpact(result);
            yield return CompleteAttack(result);
        }

        public void ApplyAttackAtImpact(TowerAttackResult result)
        {
            if (!views.TryGetValue(result.TargetInstanceId, out var view))
                return;

            if (states.TryGetValue(result.TargetInstanceId, out var state))
            {
                var health = displayedHealth.TryGetValue(result.TargetInstanceId, out var current)
                    ? Mathf.Max(0f, current - result.Damage)
                    : Mathf.Max(0f, state.CurrentHealth);
                displayedHealth[result.TargetInstanceId] = health;
                
                view.UpdateStatus(state);
view.UpdateHealth(health, state.MaxHealth);
            }
            if (result.Damage > 0) StartCoroutine(view.PlayHit());
        }

        public IEnumerator CompleteAttack(TowerAttackResult result)
        {
            if (!views.TryGetValue(result.TargetInstanceId, out var view))
                yield break;

            if (result.KnockbackApplied && result.KnockbackFromTile != result.KnockbackToTile)
                yield return view.PlayKnockback(result.KnockbackFromTile, result.KnockbackToTile);
            if (result.Killed)
            {
                var tile = view.CurrentTileIndex;
                view.TileChanged -= OnMonsterTileChanged;
                views.Remove(result.TargetInstanceId);
                states.Remove(result.TargetInstanceId);
                displayedHealth.Remove(result.TargetInstanceId);
                Destroy(view.gameObject);
                ReflowTile(tile);
            }
        }

        private void ReflowAll() { for (var i = 0; i < GaeBullBing.Core.Board.BoardState.DefaultTileCount; i++) ReflowTile(i); }

        private void ReflowTile(int tileIndex)
        {
            var occupants = new List<MonsterBoardView>();
            foreach (var pair in views) if (pair.Value.CurrentTileIndex == tileIndex) occupants.Add(pair.Value);
            occupants.Sort((left, right) =>
            {
                var leftBoss = states.TryGetValue(left.InstanceId, out var leftState) &&
                               leftState.IsBoss;
                var rightBoss = states.TryGetValue(right.InstanceId, out var rightState) &&
                                rightState.IsBoss;
                var bossOrder = rightBoss.CompareTo(leftBoss);
                return bossOrder != 0
                    ? bossOrder
                    : left.InstanceId.CompareTo(right.InstanceId);
            });
            var playerHere = hasPlayer && playerTileIndex == tileIndex;
            var visibleMonsters = Mathf.Min(occupants.Count, playerHere ? 3 : 4);
            var totalVisible = visibleMonsters + (playerHere ? 1 : 0);
            if (totalVisible == 0) { UpdateOverflow(tileIndex, occupants, 0); return; }
            var slots = Slots[totalVisible - 1];
            if (playerHere && playerView != null) playerView.SetLayoutOffset(slots[0]);
            for (var i = 0; i < occupants.Count; i++)
            {
                var visible = i < visibleMonsters; occupants[i].SetVisible(visible);
                if (visible) occupants[i].SetLayoutOffset(slots[i + (playerHere ? 1 : 0)]);
            }
            UpdateOverflow(tileIndex, occupants, visibleMonsters);
            // Player owns slot zero; its layout hook is applied by GameController/player view.
        }

private void UpdateOverflow(int tileIndex, List<MonsterBoardView> occupants, int visibleCount)
        {
            if (indicators.TryGetValue(tileIndex, out var old))
            {
                indicators.Remove(tileIndex);
                Destroy(old.gameObject);
            }
            if (occupants.Count <= visibleCount) return;

            var go = new GameObject($"Overflow {tileIndex}");
            go.transform.SetParent(transform, false);
            go.transform.position = boardView.GetWorldPosition(tileIndex) + new Vector3(0, 1.08f);
            var indicator = go.AddComponent<OverflowIndicatorView>();
            indicator.Initialize(occupants.Count - visibleCount);
            indicators[tileIndex] = indicator;
        }




    }
}
