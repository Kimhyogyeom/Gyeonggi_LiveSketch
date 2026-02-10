using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// QR 코드 텍스트에 따라 2D 캐릭터 스프라이트를 스폰합니다.
/// 같은 QR이 다시 스캔되면 추가로 스폰됩니다.
/// </summary>
public class AnimalModelManager : MonoBehaviour
{
    [Serializable]
    public class AnimalEntry
    {
        [Tooltip("QR 코드 텍스트 (예: 뜸부기, 수리부엉이)")]
        public string qrText;

        [Tooltip("베이스 캐릭터 스프라이트 (깨끗한 외곽선 이미지)")]
        public Sprite baseSprite;
    }

    [Header("=== 동물 캐릭터 등록 ===")]
    [SerializeField]
    private List<AnimalEntry> animalEntries = new List<AnimalEntry>()
    {
        new AnimalEntry { qrText = "수리부엉이" },
        new AnimalEntry { qrText = "뜸부기" },
        new AnimalEntry { qrText = "금개구리" },
        new AnimalEntry { qrText = "맹꽁이" },
        new AnimalEntry { qrText = "도롱뇽" },
        new AnimalEntry { qrText = "꾸구리" },
        new AnimalEntry { qrText = "어름치" },
        new AnimalEntry { qrText = "대모잠자리" },
        new AnimalEntry { qrText = "늦반딧불이" },
        new AnimalEntry { qrText = "말똥게" },
        new AnimalEntry { qrText = "파랑이" },
    };

    [Header("매칭 설정")]
    [SerializeField] private bool ignoreCase = true;
    [SerializeField] private bool allowPartialMatch = true;

    [Header("=== 스폰 설정 ===")]
    [Tooltip("스폰 스케일")]
    [SerializeField] private Vector3 spawnScale = new Vector3(10f, 10f, 10f);

    [Header("=== 모델 개수 제한 ===")]
    [Tooltip("최대 모델 개수 (0 = 무제한)")]
    [SerializeField] private int maxModelCount = 10;

    [Header("=== 메모리 관리 ===")]
    [Tooltip("주기적 GC 호출")]
    [SerializeField] private bool enablePeriodicGC = true;

    [Tooltip("GC 호출 간격 (초)")]
    [SerializeField] private float gcInterval = 30f;

    private float _lastGCTime;

    // 스폰된 모든 캐릭터 관리
    private List<SpawnedCharacter> _spawnedCharacters = new List<SpawnedCharacter>();

    // 가장 최근 스폰된 캐릭터
    public GameObject CurrentModel { get; private set; }
    public SpriteRenderer CurrentSpriteRenderer { get; private set; }
    public string CurrentQRText { get; private set; }

    public class SpawnedCharacter
    {
        public GameObject instance;
        public SpriteRenderer spriteRenderer;
        public string qrText;
    }

    /// <summary>
    /// 모델 스폰 이벤트
    /// </summary>
    public event Action<GameObject, SpriteRenderer> OnCharacterSpawned;

    /// <summary>
    /// QR 텍스트로 2D 캐릭터 스폰
    /// </summary>
    public bool SpawnSpriteByQR(string qrText)
    {
        if (string.IsNullOrEmpty(qrText))
        {
            Debug.LogWarning("[AnimalModelManager] QR 텍스트가 비어있습니다.");
            return false;
        }

        var entry = FindEntry(qrText);
        if (entry == null)
        {
            Debug.LogWarning($"[AnimalModelManager] '{qrText}'에 해당하는 캐릭터 없음");
            return false;
        }

        if (entry.baseSprite == null)
        {
            Debug.LogError($"[AnimalModelManager] '{entry.qrText}' 베이스 스프라이트가 null");
            return false;
        }

        // 화면 중앙에 생성
        var instance = new GameObject($"{entry.qrText}_Character_{_spawnedCharacters.Count}");
        instance.transform.position = Vector3.zero;
        instance.transform.localScale = spawnScale;

        var spriteRenderer = instance.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = entry.baseSprite;

        // 스폰 목록에 추가
        var spawned = new SpawnedCharacter
        {
            instance = instance,
            spriteRenderer = spriteRenderer,
            qrText = entry.qrText
        };
        _spawnedCharacters.Add(spawned);

        // 현재 캐릭터 업데이트
        CurrentModel = instance;
        CurrentSpriteRenderer = spriteRenderer;
        CurrentQRText = entry.qrText;

        OnCharacterSpawned?.Invoke(instance, spriteRenderer);

        Debug.Log($"[AnimalModelManager] '{entry.qrText}' 2D 캐릭터 스폰 완료 (총 {_spawnedCharacters.Count}마리)");

        // 모델 개수 제한 체크
        if (maxModelCount > 0 && _spawnedCharacters.Count > maxModelCount)
        {
            RemoveOldestCharacter();
        }

        return true;
    }

    /// <summary>
    /// 가장 최근 캐릭터의 SpriteRenderer
    /// </summary>
    public SpriteRenderer GetCurrentSpriteRenderer() => CurrentSpriteRenderer;

    /// <summary>
    /// 가장 최근 스폰된 캐릭터의 AnimalEntry 반환
    /// </summary>
    public AnimalEntry GetCurrentEntry()
    {
        if (string.IsNullOrEmpty(CurrentQRText)) return null;
        return FindEntry(CurrentQRText);
    }

    /// <summary>
    /// 스폰된 캐릭터 개수
    /// </summary>
    public int SpawnedCount => _spawnedCharacters.Count;

    /// <summary>
    /// 모든 캐릭터 삭제
    /// </summary>
    public void DestroyAllCharacters()
    {
        foreach (var character in _spawnedCharacters)
        {
            if (character.instance != null)
                SafeDestroyCharacter(character);
        }
        _spawnedCharacters.Clear();

        CurrentModel = null;
        CurrentSpriteRenderer = null;
        CurrentQRText = null;

        Debug.Log("[AnimalModelManager] 모든 캐릭터 삭제됨");
    }

    /// <summary>
    /// 가장 오래된 캐릭터 삭제
    /// </summary>
    private void RemoveOldestCharacter()
    {
        if (_spawnedCharacters.Count == 0) return;

        var oldest = _spawnedCharacters[0];
        _spawnedCharacters.RemoveAt(0);

        if (oldest.instance != null)
            SafeDestroyCharacter(oldest);

        Debug.Log($"[AnimalModelManager] 오래된 캐릭터 삭제 (남은 개수: {_spawnedCharacters.Count})");
    }

    /// <summary>
    /// 안전한 캐릭터 삭제 (텍스처 정리)
    /// </summary>
    private void SafeDestroyCharacter(SpawnedCharacter character)
    {
        if (character.instance == null) return;

        // 스프라이트 텍스처 정리
        if (character.spriteRenderer != null && character.spriteRenderer.sprite != null)
        {
            var sprite = character.spriteRenderer.sprite;
            var tex = sprite.texture;
            Destroy(sprite);
            if (tex != null)
                Destroy(tex);
        }

        Destroy(character.instance);

        // 주기적 GC
        if (enablePeriodicGC && Time.time - _lastGCTime > gcInterval)
        {
            _lastGCTime = Time.time;
            System.GC.Collect();
            Resources.UnloadUnusedAssets();
            Debug.Log("[AnimalModelManager] GC 실행");
        }
    }

    private AnimalEntry FindEntry(string qrText)
    {
        string search = ignoreCase ? qrText.ToLower() : qrText;

        foreach (var entry in animalEntries)
        {
            if (string.IsNullOrEmpty(entry.qrText)) continue;

            string entryText = ignoreCase ? entry.qrText.ToLower() : entry.qrText;

            if (allowPartialMatch)
            {
                if (search.Contains(entryText) || entryText.Contains(search))
                    return entry;
            }
            else
            {
                if (search == entryText)
                    return entry;
            }
        }

        return null;
    }

    public int EntryCount => animalEntries.Count;
}
