using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// 스폰 안내 배너: 오른쪽에서 슈-웅 등장 → 네온사인 텍스트 → 왼쪽으로 퇴장
/// </summary>
public class SpawnAnnouncementBanner : MonoBehaviour
{
    [Header("=== UI 연결 ===")]
    [Tooltip("슬라이드할 배너 패널 (Image 포함)")]
    [SerializeField] private RectTransform bannerPanel;

    [Tooltip("캐릭터 이름 표시용 텍스트")]
    [SerializeField] private TMP_Text announcementText;

    [Header("=== 타이밍 ===")]
    [Tooltip("들어오는 시간 (초)")]
    [SerializeField] private float slideInDuration = 0.6f;

    [Tooltip("표시 유지 시간 (초)")]
    [SerializeField] private float displayDuration = 2.5f;

    [Tooltip("나가는 시간 (초)")]
    [SerializeField] private float slideOutDuration = 0.4f;

    [Header("=== 네온사인 효과 ===")]
    [Tooltip("글자 하나당 등장 간격 (초)")]
    [SerializeField] private float charRevealInterval = 0.06f;

    [Tooltip("글자 등장 시 번쩍 지속 시간 (초)")]
    [SerializeField] private float glowFadeDuration = 0.3f;

    [Tooltip("네온 발광 색상")]
    [SerializeField] private Color neonGlowColor = new Color(0.6f, 1f, 1f, 1f);

    private Coroutine _currentAnim;
    private int _revealedCount;
    private float[] _charRevealTime;
    private bool _neonAnimating;
    private Color _originalColor = Color.white;

    void Start()
    {
        if (bannerPanel != null)
            SetOffScreenRight();
    }

    void Update()
    {
        if (_neonAnimating && announcementText != null)
            UpdateNeonGlow();
    }

    /// <summary>
    /// 배너 표시 (외부에서 호출)
    /// </summary>
    public void Show(string characterName)
    {
        if (bannerPanel == null) return;

        if (_currentAnim != null)
            StopCoroutine(_currentAnim);

        _neonAnimating = false;
        _currentAnim = StartCoroutine(BannerSequence(characterName));
    }

    IEnumerator BannerSequence(string characterName)
    {
        float canvasW = GetCanvasWidth();
        float y = bannerPanel.anchoredPosition.y;
        float currentX = bannerPanel.anchoredPosition.x;

        // 현재 화면에 보이면 먼저 왼쪽으로 빠르게 퇴장
        if (currentX > -canvasW * 0.9f && currentX < canvasW * 0.9f)
        {
            yield return SlideX(currentX, -canvasW, 0.2f, EaseInCubic);
        }

        // 텍스트 설정
        if (announcementText != null)
        {
            string particle = HasBatchim(characterName) ? "이" : "가";
            announcementText.text = $"{characterName}{particle} 나타날 준비를 하고 있어요~!";
            _originalColor = announcementText.color;

            // 글자 0개부터 시작 (타이핑 효과)
            announcementText.ForceMeshUpdate();
            announcementText.maxVisibleCharacters = 0;
            _revealedCount = 0;

            int totalChars = announcementText.textInfo.characterCount;
            _charRevealTime = new float[totalChars];
        }

        // 오른쪽 밖에서 시작
        bannerPanel.anchoredPosition = new Vector2(canvasW, y);

        // 슈-웅 들어오기
        yield return SlideX(canvasW, 0f, slideInDuration, EaseOutCubic);

        // 네온사인: 한 글자씩 톡톡
        if (announcementText != null)
        {
            _neonAnimating = true;
            int totalChars = announcementText.textInfo.characterCount;

            for (int i = 0; i < totalChars; i++)
            {
                _revealedCount = i + 1;
                announcementText.maxVisibleCharacters = _revealedCount;
                _charRevealTime[i] = Time.time;

                yield return new WaitForSeconds(charRevealInterval);
            }
        }

        // 전부 다 나온 후 네온 잔광 마무리 대기
        yield return new WaitForSeconds(glowFadeDuration);
        _neonAnimating = false;

        // 텍스트 원래 색으로 복구
        if (announcementText != null)
        {
            announcementText.ForceMeshUpdate();
            announcementText.maxVisibleCharacters = 99999;
        }

        // 표시 유지
        yield return new WaitForSeconds(displayDuration);

        // 왼쪽으로 퇴장
        yield return SlideX(0f, -canvasW, slideOutDuration, EaseInCubic);

        // 오른쪽 밖으로 리셋 (다음 표시 대기)
        bannerPanel.anchoredPosition = new Vector2(canvasW, y);
        _currentAnim = null;
    }

    /// <summary>
    /// 네온 발광: 방금 나타난 글자는 밝게, 시간 지나면 원래 색으로
    /// </summary>
    void UpdateNeonGlow()
    {
        announcementText.ForceMeshUpdate();
        TMP_TextInfo textInfo = announcementText.textInfo;
        if (textInfo == null || textInfo.characterCount == 0) return;

        for (int i = 0; i < _revealedCount && i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            int matIdx = charInfo.materialReferenceIndex;
            int vertIdx = charInfo.vertexIndex;
            Color32[] colors = textInfo.meshInfo[matIdx].colors32;

            // 등장 후 경과 시간
            float elapsed = Time.time - _charRevealTime[i];
            float t = Mathf.Clamp01(elapsed / glowFadeDuration);

            // 발광 → 원래 색 (EaseOut)
            Color c = Color.Lerp(neonGlowColor, _originalColor, t * t);

            Color32 c32 = c;
            for (int v = 0; v < 4; v++)
            {
                colors[vertIdx + v] = c32;
            }
        }

        // 메시 업데이트
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            textInfo.meshInfo[i].mesh.colors32 = textInfo.meshInfo[i].colors32;
            announcementText.UpdateGeometry(textInfo.meshInfo[i].mesh, i);
        }
    }

    IEnumerator SlideX(float fromX, float toX, float duration, System.Func<float, float> ease)
    {
        float elapsed = 0f;
        float y = bannerPanel.anchoredPosition.y;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ease(Mathf.Clamp01(elapsed / duration));
            bannerPanel.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, t), y);
            yield return null;
        }

        bannerPanel.anchoredPosition = new Vector2(toX, y);
    }

    void SetOffScreenRight()
    {
        float y = bannerPanel.anchoredPosition.y;
        bannerPanel.anchoredPosition = new Vector2(GetCanvasWidth(), y);
    }

    float GetCanvasWidth()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            return ((RectTransform)canvas.transform).rect.width;
        return Screen.width;
    }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    static float EaseInCubic(float t) => t * t * t;

    static bool HasBatchim(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        char last = text[text.Length - 1];
        if (last < 0xAC00 || last > 0xD7A3) return false;
        return (last - 0xAC00) % 28 != 0;
    }
}
