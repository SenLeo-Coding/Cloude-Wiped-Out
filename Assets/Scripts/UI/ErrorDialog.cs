using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CloudGame.UI
{
    /// <summary>
    /// Dialogo modal de error. Se construye en tiempo de ejecucion (no requiere
    /// cambios en la escena), pausa la escena con <c>Time.timeScale = 0</c> y bloquea
    /// el input del resto de la interfaz hasta que el usuario confirma.
    /// </summary>
    public class ErrorDialog : MonoBehaviour
    {
        private const int CanvasSortingOrder = 5000;
        private const float FadeDuration = 0.15f;
        private const float PanelWidth = 900f;
        private const float PanelHeight = 460f;
        private const string DefaultTitle = "ALGO SALIÓ MAL";
        private const string DefaultConfirmLabel = "ENTENDIDO";

        private static readonly Color PanelColor = new Color32(0x2E, 0x24, 0x24, 0xFF);
        private static readonly Color BlockerColor = new Color32(0x00, 0x00, 0x00, 0xA0);
        private static readonly Color TitleColor = new Color32(0xF2, 0x7A, 0x6E, 0xFF);
        private static readonly Color MessageColor = new Color32(0xF2, 0xEC, 0xEC, 0xFF);
        private static readonly Color DetailColor = new Color32(0xAF, 0xA2, 0xA2, 0xFF);
        private static readonly Color ButtonColor = new Color32(0xC8, 0x6A, 0x4A, 0xFF);
        private static readonly Color ButtonTextColor = new Color32(0xFF, 0xF6, 0xF2, 0xFF);

        private enum Phase
        {
            Closed,
            FadingIn,
            Visible,
            FadingOut
        }

        private static ErrorDialog instance;

        private CanvasGroup canvasGroup;
        private Text titleText;
        private Text messageText;
        private Text detailText;
        private Text confirmLabelText;
        private Button confirmButton;

        private Phase phase = Phase.Closed;
        private bool isOpen;
        private bool ownsPause;
        private float pausedTimeScale = 1f;

        /// <summary>True mientras el dialogo esta visible y bloqueando la escena.</summary>
        public static bool IsOpen => instance != null && instance.isOpen;

        /// <summary>
        /// Evita que una pausa abandonada al detener el Play Mode deje el Editor
        /// congelado. Este proyecto nunca arranca con timeScale en 0.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnPlayStart()
        {
            instance = null;
            Time.timeScale = 1f;
        }

        /// <summary>Muestra el dialogo y pausa la escena hasta que se confirme.</summary>
        public static void Show(string title, string message, string detail = null, string confirmLabel = null)
        {
            GetOrCreate().Open(title, message, detail, confirmLabel);
        }

        /// <summary>Cierra el dialogo si esta abierto y restaura el tiempo.</summary>
        public static void Dismiss()
        {
            if (instance != null)
            {
                instance.RequestClose();
            }
        }

        private static ErrorDialog GetOrCreate()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindFirstObjectByType<ErrorDialog>();
            if (instance != null)
            {
                return instance;
            }

            GameObject root = new GameObject("ErrorDialog", typeof(ErrorDialog));
            DontDestroyOnLoad(root);
            instance = root.GetComponent<ErrorDialog>();
            instance.Build();
            return instance;
        }

        private void Update()
        {
            switch (phase)
            {
                case Phase.FadingIn:
                    AdvanceAlpha(1f);
                    break;
                case Phase.FadingOut:
                    AdvanceAlpha(0f);
                    break;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            EndPause();
        }

        private void AdvanceAlpha(float target)
        {
            canvasGroup.alpha = Mathf.MoveTowards(
                canvasGroup.alpha,
                target,
                Time.unscaledDeltaTime / FadeDuration);

            if (!Mathf.Approximately(canvasGroup.alpha, target))
            {
                return;
            }

            canvasGroup.alpha = target;

            if (target >= 1f)
            {
                phase = Phase.Visible;
                return;
            }

            FinishClose();
        }

        private void Open(string title, string message, string detail, string confirmLabel)
        {
            titleText.text = string.IsNullOrEmpty(title) ? DefaultTitle : title;
            messageText.text = message ?? string.Empty;
            confirmLabelText.text = string.IsNullOrEmpty(confirmLabel) ? DefaultConfirmLabel : confirmLabel;

            bool hasDetail = !string.IsNullOrEmpty(detail);
            detailText.text = hasDetail ? detail : string.Empty;
            detailText.gameObject.SetActive(hasDetail);

            canvasGroup.gameObject.SetActive(true);
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            isOpen = true;
            phase = Phase.FadingIn;
            BeginPause();

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(confirmButton.gameObject);
            }
        }

        private void RequestClose()
        {
            if (!isOpen)
            {
                return;
            }

            if (phase == Phase.FadingOut)
            {
                return;
            }

            phase = Phase.FadingOut;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            EndPause();
        }

        private void FinishClose()
        {
            canvasGroup.alpha = 0f;
            canvasGroup.gameObject.SetActive(false);
            isOpen = false;
            phase = Phase.Closed;
        }

        private void BeginPause()
        {
            if (ownsPause)
            {
                return;
            }

            pausedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            ownsPause = true;
        }

        private void EndPause()
        {
            if (!ownsPause)
            {
                return;
            }

            Time.timeScale = pausedTimeScale;
            ownsPause = false;
        }

        private void Build()
        {
            Font font = ResolveFont();
            Sprite panelSprite = ResolveSpriteFromScene("AuthCard");
            Sprite buttonSprite = ResolveSpriteFromScene("AuthSignInButton")
                ?? ResolveSpriteFromScene("AuthSignUpButton")
                ?? ResolveSpriteFromScene("AuthGuestButton");

            GameObject root = CreateUiObject("ErrorDialogCanvas", transform);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            GameObject blocker = CreateUiObject("Blocker", root.transform);
            StretchRect(blocker.GetComponent<RectTransform>());
            Image blockerImage = blocker.AddComponent<Image>();
            blockerImage.color = BlockerColor;
            blockerImage.raycastTarget = true;

            GameObject panel = CreateUiObject("Panel", root.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            Image panelImage = panel.AddComponent<Image>();
            if (panelSprite != null)
            {
                panelImage.sprite = panelSprite;
                panelImage.color = Color.white;
            }
            else
            {
                panelImage.color = PanelColor;
            }

            titleText = CreateLabel(
                "Title",
                panel.transform,
                font,
                34,
                FontStyle.Bold,
                TitleColor,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -36f),
                new Vector2(PanelWidth - 80f, 56f));

            messageText = CreateLabel(
                "Message",
                panel.transform,
                font,
                26,
                FontStyle.Normal,
                MessageColor,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            messageText.rectTransform.offsetMin = new Vector2(50f, 168f);
            messageText.rectTransform.offsetMax = new Vector2(-50f, -118f);

            detailText = CreateLabel(
                "Detail",
                panel.transform,
                font,
                18,
                FontStyle.Normal,
                DetailColor,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 172f),
                new Vector2(PanelWidth - 80f, 30f));

            confirmButton = CreateConfirmButton(panel.transform, font, buttonSprite);
            confirmLabelText = confirmButton.GetComponentInChildren<Text>();

            confirmButton.onClick.AddListener(RequestClose);

            canvasGroup.gameObject.SetActive(false);
        }

        private Button CreateConfirmButton(Transform parent, Font font, Sprite buttonSprite)
        {
            GameObject buttonObject = CreateUiObject("ConfirmButton", parent);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0f);
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 40f);
            buttonRect.sizeDelta = new Vector2(320f, 116f);

            Image buttonImage = buttonObject.AddComponent<Image>();
            if (buttonSprite != null)
            {
                buttonImage.sprite = buttonSprite;
                buttonImage.color = Color.white;
            }
            else
            {
                buttonImage.color = ButtonColor;
            }

            buttonImage.raycastTarget = true;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;

            GameObject label = CreateUiObject("Label", buttonObject.transform);
            StretchRect(label.GetComponent<RectTransform>());
            Text labelText = label.AddComponent<Text>();
            labelText.font = font;
            labelText.fontSize = 26;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = ButtonTextColor;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.raycastTarget = false;
            labelText.supportRichText = false;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;

            return button;
        }

        private static Text CreateLabel(
            string labelName,
            Transform parent,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            GameObject labelObject = CreateUiObject(labelName, parent);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Text text = labelObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static GameObject CreateUiObject(string objectName, Transform parent)
        {
            GameObject created = new GameObject(objectName, typeof(RectTransform));
            RectTransform rect = created.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            rect.SetParent(parent, false);
            return created;
        }

        private static void StretchRect(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Font ResolveFont()
        {
            Text[] sceneTexts = FindObjectsByType<Text>(FindObjectsSortMode.None);
            foreach (Text sceneText in sceneTexts)
            {
                if (sceneText.font != null)
                {
                    return sceneText.font;
                }
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>
        /// Reutiliza el arte que ya existe en la escena para que el diálogo no desentone.
        /// El sprite se estira igual que en el login, así que el borde de corte no es necesario.
        /// </summary>
        private static Sprite ResolveSpriteFromScene(string objectName)
        {
            Image[] sceneImages = FindObjectsByType<Image>(FindObjectsSortMode.None);

            foreach (Image sceneImage in sceneImages)
            {
                if (sceneImage.gameObject.name == objectName && sceneImage.sprite != null)
                {
                    return sceneImage.sprite;
                }
            }

            return null;
        }
    }
}
