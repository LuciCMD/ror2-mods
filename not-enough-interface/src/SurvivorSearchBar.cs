using System.Linq;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotEnoughInterface
{
    // A slim row above the survivor grid: a search box, and page buttons that only show when there are pages.
    // Styled from the grid's own panel and the lobby's buttons and font
    internal sealed class SurvivorSearchBar : MonoBehaviour
    {
        private const float RowHeight = 40f;
        private const float PagerButtonWidth = 44f;
        private const float PageLabelWidth = 64f;
        private const string BackButtonPath = "SafeArea/FooterPanel/NakedButton (Quit)";

        private SurvivorGrid? grid;
        private GameObject? pager;
        private TextMeshProUGUI? pageLabel;

        internal static SurvivorSearchBar? Attach(CharacterSelectBarController bar, SurvivorGrid grid)
        {
            Transform? gridPanel = bar.transform.parent;
            Transform? leftPanel = gridPanel != null ? gridPanel.parent : null;
            if (gridPanel == null || leftPanel == null)
            {
                return null;
            }

            var row = new GameObject("NEI Survivor Search", typeof(RectTransform));
            row.transform.SetParent(leftPanel, false);
            row.transform.SetSiblingIndex(gridPanel.GetSiblingIndex());
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.minHeight = RowHeight;
            element.preferredHeight = RowHeight;
            element.flexibleHeight = 0f;
            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            SurvivorSearchBar searchBar = row.AddComponent<SurvivorSearchBar>();
            searchBar.grid = grid;
            TMP_FontAsset? font = bar.GetComponentInParent<Canvas>()?.rootCanvas.GetComponentsInChildren<TextMeshProUGUI>(true)
                .Select(t => t.font).FirstOrDefault(f => f);
            SearchField.Create(row.transform, bar.GetComponent<Image>(), font, "Search Survivors", text => grid.SetQuery(text));
            searchBar.BuildPager(row.transform, bar.GetComponentInParent<CharacterSelectController>(), font);
            return searchBar;
        }

        internal void Show(int page, int pages)
        {
            if (pager)
            {
                pager!.SetActive(pages > 1);
            }

            if (pageLabel)
            {
                pageLabel!.text = $"{page + 1}/{pages}";
            }
        }

        // Copies of the lobby's Back button, so the arrows look and sound like the game's
        private void BuildPager(Transform row, CharacterSelectController? lobby, TMP_FontAsset? font)
        {
            pager = new GameObject("Pager", typeof(RectTransform));
            pager.transform.SetParent(row, false);
            HorizontalLayoutGroup layout = pager.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            Transform? back = lobby ? lobby!.transform.Find(BackButtonPath) : null;
            MPButton? template = back != null ? back.GetComponent<MPButton>() : null;
            if (template)
            {
                Arrow(template!, "Previous Page", "<", -1);
            }

            pageLabel = SearchField.Text((RectTransform)pager.transform, "Page", font, "1/1");
            pageLabel.alignment = TextAlignmentOptions.Center;
            pageLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = PageLabelWidth;
            if (template)
            {
                Arrow(template!, "Next Page", ">", 1);
            }

            pager.SetActive(false);
        }

        private void Arrow(MPButton template, string name, string label, int delta)
        {
            MPButton button = ButtonCopy.Create(template, name, label);
            button.transform.SetParent(pager!.transform, false);
            if (!button.TryGetComponent(out LayoutElement element))
            {
                element = button.gameObject.AddComponent<LayoutElement>();
            }

            element.preferredWidth = PagerButtonWidth;
            element.minWidth = PagerButtonWidth;
            button.onClick.AddListener(() => grid?.Turn(delta));
        }
    }
}
