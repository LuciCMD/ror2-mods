using System;
using System.Collections.Generic;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NotEnoughInterface
{
    // Lives on the lobby's tab area (Overview, Skills, Loadout). Each tab panel sits in its own scroll view:
    // while its content fits it keeps the game's own layout (filling the area), and once it doesn't the panel
    // grows and scrolls. A controller's selection is kept in view
    internal sealed class TabScroller : MonoBehaviour
    {
        private const float WheelSpeed = 40f;
        private const float SelectionMargin = 12f;
        private const float OverviewMargin = 48f;

        private readonly List<Tab> tabs = new List<Tab>();
        private MPEventSystemLocator? locator;
        private GameObject? lastSelected;

        // Moves each panel under a new masked viewport; the panels stay where the game's code expects them
        internal static TabScroller? Attach(RectTransform contentPanel, MPEventSystemLocator? locator, IEnumerable<RectTransform> panels)
        {
            if (contentPanel.GetComponent<TabScroller>())
            {
                return null;
            }

            TabScroller scroller = contentPanel.gameObject.AddComponent<TabScroller>();
            scroller.locator = locator;
            foreach (RectTransform panel in panels)
            {
                scroller.tabs.Add(Tab.Create(contentPanel, panel));
            }

            return scroller;
        }

        private void LateUpdate()
        {
            foreach (Tab tab in tabs)
            {
                tab.Sync();
            }

            FollowSelection();
        }

        private void FollowSelection()
        {
            GameObject? selected = locator && locator!.eventSystem ? locator.eventSystem.currentSelectedGameObject : null;
            if (selected == lastSelected)
            {
                return;
            }

            lastSelected = selected;
            if (!selected)
            {
                return;
            }

            foreach (Tab tab in tabs)
            {
                if (selected!.transform.IsChildOf(tab.Panel))
                {
                    tab.Reveal((RectTransform)selected.transform);
                    return;
                }
            }
        }

        private sealed class Tab
        {
            private static readonly Vector3[] Corners = new Vector3[4];

            private readonly RectTransform viewport;
            private readonly ScrollRect scroll;
            private readonly TMP_Text? overviewText;

            private Tab(RectTransform viewport, RectTransform panel, ScrollRect scroll)
            {
                this.viewport = viewport;
                Panel = panel;
                this.scroll = scroll;

                // The Overview tab is one text block with no layout group of its own
                overviewText = panel.GetComponent<LayoutGroup>() ? null : panel.GetComponentInChildren<TMP_Text>(true);
            }

            internal RectTransform Panel { get; }

            internal static Tab Create(RectTransform contentPanel, RectTransform panel)
            {
                var go = new GameObject($"NEI Viewport ({panel.name})", typeof(RectTransform));
                var viewport = (RectTransform)go.transform;
                viewport.SetParent(contentPanel, false);
                viewport.SetSiblingIndex(panel.GetSiblingIndex());
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = viewport.offsetMax = Vector2.zero;
                go.AddComponent<RectMask2D>();
                go.AddComponent<TabViewport>();

                // Clear, but lets the wheel and drags over empty space reach the scroll view
                Image catcher = go.AddComponent<Image>();
                catcher.color = Color.clear;

                // Hover descriptions hang outside the panel's right edge; inside the mask they'd be clipped away.
                // The tab area is the same size, so their anchoring still holds there
                for (int i = panel.childCount - 1; i >= 0; i--)
                {
                    Transform child = panel.GetChild(i);
                    if (child.name.StartsWith("DescriptionPanel", StringComparison.Ordinal))
                    {
                        child.SetParent(contentPanel, false);
                    }
                }

                panel.SetParent(viewport, false);
                panel.anchorMin = new Vector2(0f, 1f);
                panel.anchorMax = Vector2.one;
                panel.pivot = new Vector2(0.5f, 1f);
                panel.anchoredPosition = Vector2.zero;
                panel.sizeDelta = new Vector2(0f, viewport.rect.height);

                ScrollRect scroll = go.AddComponent<ScrollRect>();
                scroll.viewport = viewport;
                scroll.content = panel;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = WheelSpeed;
                scroll.inertia = false;
                foreach (RefreshCanvasDrawOrder outline in panel.GetComponentsInChildren<RefreshCanvasDrawOrder>(true))
                {
                    TabViewport.Unlift(outline);
                }

                return new Tab(viewport, panel, scroll);
            }

            // The viewport follows its panel's visibility, so only the open tab catches the wheel; the panel is
            // as tall as the area, or as its content when that's taller
            internal void Sync()
            {
                bool open = Panel.gameObject.activeSelf;
                if (viewport.gameObject.activeSelf != open)
                {
                    viewport.gameObject.SetActive(open);
                }

                if (!open)
                {
                    return;
                }

                float height = Math.Max(viewport.rect.height, Needed());
                if (Math.Abs(Panel.rect.height - height) > 0.5f)
                {
                    Panel.sizeDelta = new Vector2(0f, height);
                }
            }

            internal void Reveal(RectTransform target)
            {
                if (Panel.rect.height <= viewport.rect.height + 0.5f)
                {
                    return;
                }

                target.GetWorldCorners(Corners);
                Vector3 top = viewport.InverseTransformPoint(Corners[1]);
                Vector3 bottom = viewport.InverseTransformPoint(Corners[0]);
                Rect view = viewport.rect;
                float shift = 0f;
                if (top.y + SelectionMargin > view.yMax)
                {
                    shift = top.y + SelectionMargin - view.yMax;
                }
                else if (bottom.y - SelectionMargin < view.yMin)
                {
                    shift = bottom.y - SelectionMargin - view.yMin;
                }

                if (shift != 0f)
                {
                    float max = Panel.rect.height - view.height;
                    float y = Mathf.Clamp(Panel.anchoredPosition.y - shift, 0f, max);
                    Panel.anchoredPosition = new Vector2(Panel.anchoredPosition.x, y);
                    scroll.velocity = Vector2.zero;
                }
            }

            private float Needed()
            {
                if (overviewText)
                {
                    float width = Math.Max(1f, overviewText!.rectTransform.rect.width);
                    return overviewText.GetPreferredValues(overviewText.text, width, 0f).y + OverviewMargin;
                }

                return LayoutUtility.GetPreferredHeight(Panel);
            }
        }
    }
}
