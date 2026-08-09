using System.Collections;
using System.Collections.Generic;
using GaeBullBing.Core.Monsters;
using GaeBullBing.Core.Towers;
using GaeBullBing.Core.Data;
using GaeBullBing.Presentation.Board;
using GaeBullBing.Presentation.Audio;
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
        [Header("Regular Spawn Leaves")]
        [SerializeField] private Sprite spawnLeafSprite;
        [SerializeField, Min(1)] private int spawnLeafCount = 7;
        [SerializeField, Min(.01f)] private float spawnLeafBurstDuration = .1f;
        [SerializeField, Min(.01f)] private float spawnLeafSettleDuration = .5f;
        [SerializeField, Min(0f)] private float spawnLeafBurstDistance = .62f;
        [SerializeField, Min(0f)] private float spawnLeafFallDistance = .42f;
        [SerializeField, Range(0f, 90f)] private float spawnLeafMinimumAngle = 15f;
        [SerializeField, Range(0f, 90f)] private float spawnLeafMaximumAngle = 55f;
        [SerializeField, Min(.01f)] private float spawnLeafScale = .9f;
        [SerializeField] private Vector3 spawnLeafOriginOffset = new(0f, .2f, 0f);

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
            if (state == null || !views.TryGetValue(state.InstanceId, out var view))
                yield break;

            var audio = AudioManager.Instance;
            audio?.PlayAt(
                state.IsBoss ? audio.Monster.BossSpawn : audio.Monster.Spawn,
                view.transform.position);

            if (state.IsBoss)
                yield return PlayBossSpawnEntrance(state.InstanceId);
            else
            {
                view.PrepareRegularSpawnEntrance();
                StartCoroutine(PlaySpawnLeaves(view.RegularSpawnStartPosition));
                yield return view.PlayRegularSpawnEntrance();
            }
        }

        private IEnumerator PlaySpawnLeaves(Vector3 origin)
        {
            origin += spawnLeafOriginOffset;
            var leafSprite = spawnLeafSprite != null ? spawnLeafSprite : bossFeatherSprite;
            if (leafSprite == null || boardView == null || spawnLeafCount <= 0)
                yield break;

            var forward = (boardView.GetWorldPosition(1) -
                           boardView.GetWorldPosition(0)).normalized;
            var leaves = new List<(Transform Transform, SpriteRenderer Renderer,
                Vector3 Direction, float Distance, float Fall, float Sway,
                float RotationSpeed)>();

            for (var index = 0; index < spawnLeafCount; index++)
            {
                var leafObject = new GameObject("Monster Spawn Leaf");
                leafObject.transform.SetParent(transform, false);
                leafObject.transform.position = origin +
                    new Vector3(Random.Range(-.05f, .05f), Random.Range(-.03f, .05f), 0f);
                leafObject.transform.localScale = Vector3.one *
                    spawnLeafScale * Random.Range(.8f, 1.1f);
                leafObject.transform.rotation = Quaternion.Euler(
                    0f, 0f, Random.Range(-35f, 35f));

                var renderer = leafObject.AddComponent<SpriteRenderer>();
                renderer.sprite = leafSprite;
                renderer.color = Color.white;
                renderer.sortingOrder = 32758;

                float angle;
                if (index == 0)
                    angle = 0f;
                else
                {
                    var side = index % 2 == 0 ? 1f : -1f;
                    angle = side * Random.Range(
                        spawnLeafMinimumAngle,
                        Mathf.Max(spawnLeafMinimumAngle, spawnLeafMaximumAngle));
                }

                var direction = Quaternion.Euler(0f, 0f, angle) * forward;
                leaves.Add((
                    leafObject.transform,
                    renderer,
                    direction,
                    spawnLeafBurstDistance * Random.Range(.75f, 1.15f),
                    spawnLeafFallDistance * Random.Range(.75f, 1.2f),
                    Random.Range(.025f, .08f),
                    Random.Range(-220f, 220f)));
            }

            for (var elapsed = 0f;
                 elapsed < spawnLeafBurstDuration;
                 elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / spawnLeafBurstDuration);
                var burst = 1f - Mathf.Pow(1f - progress, 3f);
                foreach (var leaf in leaves)
                {
                    if (leaf.Transform == null)
                        continue;
                    leaf.Transform.position = origin +
                        leaf.Direction * (leaf.Distance * burst);
                    leaf.Transform.Rotate(
                        0f, 0f, leaf.RotationSpeed * Time.deltaTime);
                }
                yield return null;
            }

            var settleStarts = new Vector3[leaves.Count];
            for (var index = 0; index < leaves.Count; index++)
                settleStarts[index] = leaves[index].Transform.position;

            for (var elapsed = 0f;
                 elapsed < spawnLeafSettleDuration;
                 elapsed += Time.deltaTime)
            {
                var progress = Mathf.Clamp01(elapsed / spawnLeafSettleDuration);
                var fall = progress * progress;
                for (var index = 0; index < leaves.Count; index++)
                {
                    var leaf = leaves[index];
                    if (leaf.Transform == null)
                        continue;

                    var perpendicular = new Vector3(
                        -leaf.Direction.y,
                        leaf.Direction.x,
                        0f);
                    leaf.Transform.position = settleStarts[index] +
                        leaf.Direction * (.1f * progress) +
                        perpendicular * (Mathf.Sin(progress * Mathf.PI * 2f) *
                                         leaf.Sway) +
                        Vector3.down * (leaf.Fall * fall);
                    leaf.Transform.Rotate(
                        0f, 0f, leaf.RotationSpeed * .45f * Time.deltaTime);

                    var alpha = 1f - Mathf.InverseLerp(.55f, 1f, progress);
                    leaf.Renderer.color = new Color(1f, 1f, 1f, alpha);
                }
                yield return null;
            }

            foreach (var leaf in leaves)
                if (leaf.Transform != null)
                    Destroy(leaf.Transform.gameObject);
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

            var audio = AudioManager.Instance;
            if (result.Distance > 0)
                audio?.PlayAt(audio.Monster.Move, view.transform.position);

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
                audio?.PlayAt(audio.Monster.Escape, view.transform.position);
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
            var audio = AudioManager.Instance;
            audio?.PlayAt(audio.Monster.BossFeather, target);
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
            if (result.Damage > 0 && !result.Killed)
            {
                var audio = AudioManager.Instance;
                audio?.PlayAt(audio.Monster.Hit, view.transform.position);
            }
        }

        public IEnumerator CompleteAttack(TowerAttackResult result)
        {
            if (!views.TryGetValue(result.TargetInstanceId, out var view))
                yield break;

            if (result.KnockbackApplied && result.KnockbackFromTile != result.KnockbackToTile)
            {
                var audio = AudioManager.Instance;
                audio?.PlayAt(audio.Status.Knockback, view.transform.position);
                yield return view.PlayKnockback(result.KnockbackFromTile, result.KnockbackToTile);
            }
            if (result.Killed)
            {
                var tile = view.CurrentTileIndex;
                var audio = AudioManager.Instance;
                audio?.PlayAt(audio.Monster.Capture, view.transform.position);
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
