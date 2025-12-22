using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// QR 코드 텍스트에 따라 동물 모델을 스폰합니다.
/// 같은 QR이 다시 스캔되면 추가로 스폰됩니다.
/// </summary>
public class AnimalModelManager : MonoBehaviour
{
    [Serializable]
    public class AnimalEntry
    {
        [Tooltip("QR 코드 텍스트 (예: 뜸부기, 수리부엉이)")]
        public string qrText;

        [Tooltip("해당 동물 프리팹")]
        public GameObject prefab;

        [Tooltip("프리팹 내 Renderer 경로 (비워두면 자동 검색)")]
        public string rendererPath = "";
    }

    [Header("=== 11개 동물 프리팹 등록 ===")]
    [SerializeField]
    private List<AnimalEntry> animalPrefabs = new List<AnimalEntry>()
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

    [Header("스폰 위치 (랜덤 범위)")]
    [SerializeField] private Vector3 spawnRangeMin = new Vector3(-5, 0, -5);
    [SerializeField] private Vector3 spawnRangeMax = new Vector3(5, 0, 5);
    [SerializeField] private Vector3 spawnScale = Vector3.one;

    [Header("매칭 설정")]
    [SerializeField] private bool ignoreCase = true;
    [SerializeField] private bool allowPartialMatch = true;

    // 스폰된 모든 모델 관리
    private List<SpawnedModel> _spawnedModels = new List<SpawnedModel>();

    // 가장 최근 스폰된 모델
    public GameObject CurrentModel { get; private set; }
    public Renderer CurrentRenderer { get; private set; }
    public string CurrentQRText { get; private set; }

    /// <summary>
    /// 스폰된 모델 정보
    /// </summary>
    public class SpawnedModel
    {
        public GameObject instance;
        public Renderer renderer;
        public string qrText;
    }

    /// <summary>
    /// 모델 스폰 이벤트
    /// </summary>
    public event Action<GameObject, Renderer> OnModelSpawned;

    /// <summary>
    /// QR 텍스트로 모델 스폰 (중복 허용)
    /// </summary>
    public bool SpawnModelByQR(string qrText)
    {
        if (string.IsNullOrEmpty(qrText))
        {
            Debug.LogWarning("[AnimalModelManager] QR 텍스트가 비어있습니다.");
            return false;
        }

        var entry = FindEntry(qrText);
        if (entry == null)
        {
            Debug.LogWarning($"[AnimalModelManager] '{qrText}'에 해당하는 프리팹 없음");
            return false;
        }

        if (entry.prefab == null)
        {
            Debug.LogError($"[AnimalModelManager] '{entry.qrText}' 프리팹이 null");
            return false;
        }

        // 랜덤 위치에 스폰 (기존 모델 유지!)
        Vector3 pos = GetRandomSpawnPosition();

        var instance = Instantiate(entry.prefab, pos, Quaternion.identity);
        instance.transform.localScale = spawnScale;
        instance.name = $"{entry.qrText}_Model_{_spawnedModels.Count}";

        var renderer = FindRenderer(instance, entry.rendererPath);

        // 스폰 목록에 추가
        var spawnedModel = new SpawnedModel
        {
            instance = instance,
            renderer = renderer,
            qrText = entry.qrText
        };
        _spawnedModels.Add(spawnedModel);

        // 현재 모델 업데이트 (가장 최근 것)
        CurrentModel = instance;
        CurrentRenderer = renderer;
        CurrentQRText = entry.qrText;

        OnModelSpawned?.Invoke(instance, renderer);

        Debug.Log($"[AnimalModelManager] '{entry.qrText}' 스폰 완료 (총 {_spawnedModels.Count}마리)");
        return true;
    }

    /// <summary>
    /// 가장 최근 모델의 Renderer
    /// </summary>
    public Renderer GetCurrentRenderer() => CurrentRenderer;

    /// <summary>
    /// 스폰된 모델 개수
    /// </summary>
    public int SpawnedCount => _spawnedModels.Count;

    /// <summary>
    /// 모든 모델 삭제
    /// </summary>
    public void DestroyAllModels()
    {
        foreach (var model in _spawnedModels)
        {
            if (model.instance != null)
                Destroy(model.instance);
        }
        _spawnedModels.Clear();

        CurrentModel = null;
        CurrentRenderer = null;
        CurrentQRText = null;

        Debug.Log("[AnimalModelManager] 모든 모델 삭제됨");
    }

    /// <summary>
    /// 가장 최근 모델만 삭제
    /// </summary>
    public void DestroyLastModel()
    {
        if (_spawnedModels.Count == 0) return;

        var last = _spawnedModels[_spawnedModels.Count - 1];
        if (last.instance != null)
            Destroy(last.instance);

        _spawnedModels.RemoveAt(_spawnedModels.Count - 1);

        // 현재 모델 업데이트
        if (_spawnedModels.Count > 0)
        {
            var newLast = _spawnedModels[_spawnedModels.Count - 1];
            CurrentModel = newLast.instance;
            CurrentRenderer = newLast.renderer;
            CurrentQRText = newLast.qrText;
        }
        else
        {
            CurrentModel = null;
            CurrentRenderer = null;
            CurrentQRText = null;
        }
    }

    private AnimalEntry FindEntry(string qrText)
    {
        string search = ignoreCase ? qrText.ToLower() : qrText;

        foreach (var entry in animalPrefabs)
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

    private Renderer FindRenderer(GameObject model, string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            var child = model.transform.Find(path);
            if (child != null)
            {
                var r = child.GetComponent<Renderer>();
                if (r != null) return r;
            }
        }

        var skinned = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinned != null) return skinned;

        var mesh = model.GetComponentInChildren<MeshRenderer>();
        if (mesh != null) return mesh;

        return model.GetComponentInChildren<Renderer>();
    }

    private Vector3 GetRandomSpawnPosition()
    {
        return new Vector3(
            UnityEngine.Random.Range(spawnRangeMin.x, spawnRangeMax.x),
            UnityEngine.Random.Range(spawnRangeMin.y, spawnRangeMax.y),
            UnityEngine.Random.Range(spawnRangeMin.z, spawnRangeMax.z)
        );
    }

    public int EntryCount => animalPrefabs.Count;
}
