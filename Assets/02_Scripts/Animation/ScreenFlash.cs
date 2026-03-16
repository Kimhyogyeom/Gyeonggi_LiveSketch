using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 캐릭터 등장 시 화면 전체 번쩍 효과.
/// 씬에 하나만 배치하면 ScreenFlash.Instance.Flash()로 호출 가능.
/// </summary>
public class ScreenFlash : MonoBehaviour
{
    public static ScreenFlash Instance { get; private set; }

    [Tooltip("번쩍 색상 (기본 흰색)")]
    [SerializeField] private Color flashColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("최대 알파 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float maxAlpha = 0.7f;

    [Tooltip("밝아지는 시간 (초)")]
    [SerializeField] private float fadeInDuration = 0.08f;

    [Tooltip("어두워지는 시간 (초)")]
    [SerializeField] private float fadeOutDuration = 0.25f;

    private Image _flashImage;
    private Coroutine _flashRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        CreateFlashCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void CreateFlashCanvas()
    {
        var canvasGO = new GameObject("ScreenFlashCanvas");
        canvasGO.transform.SetParent(transform);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var imgGO = new GameObject("FlashImage");
        imgGO.transform.SetParent(canvasGO.transform, false);

        var rect = imgGO.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        _flashImage = imgGO.AddComponent<Image>();
        _flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        _flashImage.raycastTarget = false;
    }

    public void Flash()
    {
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashRoutine());
    }

    public void Flash(Color color, float alpha = -1f)
    {
        flashColor = color;
        if (alpha >= 0f) maxAlpha = alpha;
        Flash();
    }

    IEnumerator FlashRoutine()
    {
        // 밝아지기
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(0f, maxAlpha, t / fadeInDuration);
            _flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, a);
            yield return null;
        }

        // 어두워지기
        t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(maxAlpha, 0f, t / fadeOutDuration);
            _flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, a);
            yield return null;
        }

        _flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        _flashRoutine = null;
    }
}
