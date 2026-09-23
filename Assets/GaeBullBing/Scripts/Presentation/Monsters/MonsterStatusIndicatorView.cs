using System.Collections.Generic;
using GaeBullBing.Core.Monsters;
using UnityEngine;

namespace GaeBullBing.Presentation.Monsters
{
    public sealed class MonsterStatusIndicatorView : MonoBehaviour
    {
        private sealed class Icon
        {
            public GameObject Root;
            public SpriteRenderer Background;
            public SpriteRenderer Slash;
            public TextMesh Count;
        }

        private readonly List<Icon> icons = new();
        private Icon burn;
        private Icon frostbite;
        private Icon freeze;
        private Icon knockback;
        private Icon shock;
        private Transform iconRoot;
        private Sprite squareSprite;
        private bool visible = true;

        public void Initialize()
        {
            squareSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
                new Vector2(.5f, .5f), 1f);
            iconRoot = new GameObject("Status Icons").transform;
            iconRoot.SetParent(transform, false);

            burn = CreateIcon("Burn", new Color(.92f, .12f, .08f, 1f), true);
            frostbite = CreateIcon("Frostbite", new Color(.25f, .78f, 1f, 1f), true);
            freeze = CreateIcon("Freeze", new Color(.12f, .46f, .95f, 1f), false);
            knockback = CreateIcon("Knockback Immunity", new Color(.18f, .72f, .28f, 1f), false);
            shock = CreateIcon("Shock", new Color(.64f, .20f, .86f, 1f), false);
        }

        public void Refresh(MonsterState state)
        {
            if (state == null) return;

            SetIconActive(burn, state.BurnStacks > 0);
            if (burn.Count != null)
            {
                burn.Count.text = state.BurnStacks.ToString();
                burn.Count.gameObject.SetActive(state.BurnStacks > 0);
            }

            SetIconActive(frostbite, state.FrostbiteStacks > 0);
            if (frostbite.Count != null)
            {
                frostbite.Count.text = state.FrostbiteStacks.ToString();
                frostbite.Count.gameObject.SetActive(state.FrostbiteStacks > 0);
            }

            var freezeImmune = state.FreezeImmuneThisTurn || state.IsImmuneTo("freeze");
            SetIconActive(freeze, state.FrozenMovesRemaining > 0 || state.FreezeImmunityPending || freezeImmune);
            freeze.Slash.gameObject.SetActive(freeze.Root.activeSelf && freezeImmune);

            var knockbackImmune = state.KnockbackConsumed || state.IsImmuneTo("knockback");
            SetIconActive(knockback, knockbackImmune);
            knockback.Slash.gameObject.SetActive(knockback.Root.activeSelf);

            SetIconActive(shock, state.Shocked);
            LayoutActiveIcons();
        }

        public void SetLocalPosition(Vector3 position)
        {
            if (iconRoot != null) iconRoot.localPosition = position;
        }

        public void SetSortingOrder(int baseOrder)
        {
            for (var index = 0; index < icons.Count; index++)
            {
                var icon = icons[index];
                icon.Background.sortingOrder = baseOrder;
                if (icon.Slash != null) icon.Slash.sortingOrder = baseOrder + 1;
                if (icon.Count != null)
                {
                    var renderer = icon.Count.GetComponent<MeshRenderer>();
                    if (renderer != null) renderer.sortingOrder = baseOrder + 2;
                }
            }
        }

        public void SetVisible(bool value)
        {
            visible = value;
            if (iconRoot != null) iconRoot.gameObject.SetActive(value);
        }

        private Icon CreateIcon(string name, Color color, bool withCount)
        {
            var root = new GameObject(name);
            root.transform.SetParent(iconRoot, false);

            var background = root.AddComponent<SpriteRenderer>();
            background.sprite = squareSprite;
            background.color = color;
            root.transform.localScale = new Vector3(.085f, .085f, 1f);

            var slashObject = new GameObject("Immunity Slash");
            slashObject.transform.SetParent(root.transform, false);
            slashObject.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            slashObject.transform.localScale = new Vector3(.18f, 1.25f, 1f);
            var slash = slashObject.AddComponent<SpriteRenderer>();
            slash.sprite = squareSprite;
            slash.color = new Color(.94f, .96f, 1f, 1f);
            slashObject.SetActive(false);

            TextMesh count = null;
            if (withCount)
            {
                var countObject = new GameObject("Stack Count");
                countObject.transform.SetParent(root.transform, false);
                countObject.transform.localPosition = new Vector3(.48f, -.48f, -.01f);
                countObject.transform.localScale = Vector3.one * 8f;
                count = countObject.AddComponent<TextMesh>();
                count.anchor = TextAnchor.LowerRight;
                count.alignment = TextAlignment.Right;
                count.characterSize = .018f;
                count.fontSize = 42;
                count.color = Color.white;
            }

            var icon = new Icon { Root = root, Background = background, Slash = slash, Count = count };
            icons.Add(icon);
            root.SetActive(false);
            return icon;
        }

        private void SetIconActive(Icon icon, bool active)
        {
            icon.Root.SetActive(visible && active);
        }

        private void LayoutActiveIcons()
        {
            var active = new List<Icon>();
            foreach (var icon in icons)
                if (icon.Root.activeSelf) active.Add(icon);

            const float healthBarLeft = -.25f;
            const float iconHalfWidth = .0425f;
            var start = healthBarLeft + iconHalfWidth;
            for (var index = 0; index < active.Count; index++)
                active[index].Root.transform.localPosition = new Vector3(start + index * .11f, 0f, 0f);
        }
    }
}
