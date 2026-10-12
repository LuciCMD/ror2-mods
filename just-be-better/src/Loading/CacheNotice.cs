using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JustBeBetter.Loading
{
    // A panel in the main menu's top right while the bundle cache makes its copies, so nobody has to read the
    // log to know why the disk is busy or whether quitting is safe
    // Ignores clicks and fades once done
    internal sealed class CacheNotice : MonoBehaviour
    {
        private const float Width = 420f;
        private const float Margin = 40f;
        private const float Padding = 18f;
        private const float DoneSeconds = 6f;
        private const float FailedSeconds = 12f;
        private const float FadeSeconds = 1f;
        private const int Radius = 12;

        private static readonly Color Panel = Hex(0x2B2A30, 0.95f);
        private static readonly Color Track = Hex(0x3C3B43, 1f);
        private static readonly Color Accent = Hex(0x7FB8FF, 1f);
        private static readonly Color Warn = Hex(0xFFC75C, 1f);
        private static readonly Color Text = Hex(0xECEAE6, 1f);
        private static readonly Color Muted = Hex(0xAAA7B0, 1f);

        private Func<string>? status;
        private CanvasGroup? group;
        private TextMeshProUGUI? title;
        private TextMeshProUGUI? body;
        private TextMeshProUGUI? detail;
        private RectTransform? fill;
        private Image? fillImage;
        private float finishedAt = -1f;
        private float showFor;

        // Only when the patcher half has copies to make this launch
        internal static void ShowIfBuilding()
        {
            if (AppDomain.CurrentDomain.GetData(PatcherKeys.CacheStatus) is not Func<string> status || Status.Parse(status()).State != "building")
            {
                return;
            }

            var host = new GameObject("Just_Be_Better Cache Notice");
            DontDestroyOnLoad(host);
            host.AddComponent<CacheNotice>().status = status;
        }

        private void Start()
        {
            Build();
        }

        private void Update()
        {
            if (status == null || group == null)
            {
                return;
            }

            Status now = Status.Parse(status());
            if (finishedAt < 0f && now.State == "building")
            {
                ShowProgress(now);
                return;
            }

            if (finishedAt < 0f)
            {
                finishedAt = Time.unscaledTime;
                showFor = now.Failures > 0 ? FailedSeconds : DoneSeconds;
                ShowDone(now);
            }

            float left = showFor - (Time.unscaledTime - finishedAt);
            group.alpha = Mathf.Clamp01(left / FadeSeconds);
            if (left <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void ShowProgress(Status now)
        {
            title!.text = "Speeding Up Your Next Launch";
            body!.text = "Making quick-loading copies of slow mod files. Play on as normal; if you quit first, it carries on next launch.";
            int step = Math.Min(now.Done + 1, now.Total);
            string percent = (now.Fraction * 100f).ToString("0", CultureInfo.InvariantCulture);
            detail!.text = $"Copying {now.Mod}, {step} of {now.Total} · {percent}%";
            SetBar(now.Fraction, Accent);
        }

        private void ShowDone(Status now)
        {
            if (now.Failures > 0)
            {
                title!.text = "Some Files Weren't Copied";
                body!.text = $"{now.Failures} of {now.Total} couldn't be copied, so those mods load the usual way. Details are in the log.";
                SetBar(1f, Warn);
            }
            else
            {
                title!.text = "Next Launch Will Be Faster";
                body!.text = "All copies made. Mods with slow files open quicker from now on.";
                SetBar(1f, Accent);
            }

            detail!.text = string.Empty;
            detail.gameObject.SetActive(false);
        }

        private void SetBar(float fraction, Color color)
        {
            fill!.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            fillImage!.color = color;
        }

        private void Build()
        {
            Sprite rounded = RoundedSprite();
            TMP_FontAsset? font = FindObjectsOfType<TextMeshProUGUI>().Select(t => t.font).FirstOrDefault(f => f);

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform panel = Child("Panel", transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-Margin, -Margin);
            panel.sizeDelta = new Vector2(Width, 0f);
            AddImage(panel, rounded, Panel, 1f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            int pad = (int)Padding;
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            title = Label("Title", panel, font, 22f, Text);
            title.fontStyle = FontStyles.Bold;
            body = Label("Body", panel, font, 17f, Muted);

            RectTransform track = Child("Track", panel);
            track.gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;
            AddImage(track, rounded, Track, 3f);
            fill = Child("Fill", track);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fillImage = AddImage(fill, rounded, Accent, 3f);

            detail = Label("Detail", panel, font, 15f, Muted);
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // A higher multiplier draws the same sprite with smaller corners
        private static Image AddImage(RectTransform target, Sprite sprite, Color color, float cornerScale)
        {
            Image image = target.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = cornerScale;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(string name, RectTransform parent, TMP_FontAsset? font, float size, Color color)
        {
            TextMeshProUGUI label = Child(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font)
            {
                label.font = font;
            }

            label.fontSize = size;
            label.color = color;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            return label;
        }

        // White rounded square, sliced at its corners and tinted per use
        private static Sprite RoundedSprite()
        {
            int size = (Radius * 2) + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Math.Max(Math.Max(Radius - x - 0.5f, x + 0.5f - (size - Radius)), 0f);
                    float dy = Math.Max(Math.Max(Radius - y - 0.5f, y + 0.5f - (size - Radius)), 0f);
                    float alpha = Mathf.Clamp01(Radius + 0.5f - Mathf.Sqrt((dx * dx) + (dy * dy)));
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(Radius, Radius, Radius, Radius));
        }

        private static Color Hex(int rgb, float alpha)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
        }

        // The patcher's status line: state, done, total, fraction, current mod, failures
        private readonly struct Status
        {
            private Status(string state, int done, int total, float fraction, string mod, int failures)
            {
                State = state;
                Done = done;
                Total = total;
                Fraction = fraction;
                Mod = mod;
                Failures = failures;
            }

            internal string State { get; }

            internal int Done { get; }

            internal int Total { get; }

            internal float Fraction { get; }

            internal string Mod { get; }

            internal int Failures { get; }

            internal static Status Parse(string line)
            {
                string[] parts = line.Split('\t');
                if (parts.Length != 6)
                {
                    return new Status("idle", 0, 0, 1f, string.Empty, 0);
                }

                int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int done);
                int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int total);
                float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float fraction);
                int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int failures);
                return new Status(parts[0], done, total, fraction, parts[4], failures);
            }
        }
    }
}
