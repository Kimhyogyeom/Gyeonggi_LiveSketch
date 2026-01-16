using System;
using System.Collections;
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

        [Header("=== UV 설정 (프리팹별) ===")]
        [Tooltip("UV Offset X")]
        [Range(-1f, 1f)]
        public float uvOffsetX = 0f;

        [Tooltip("UV Offset Y")]
        [Range(-1f, 1f)]
        public float uvOffsetY = 0f;

        [Tooltip("UV Scale X")]
        [Range(0.1f, 5f)]
        public float uvScaleX = 1f;

        [Tooltip("UV Scale Y")]
        [Range(0.1f, 5f)]
        public float uvScaleY = 1f;

        // 색상은 스캔 이미지에서 자동 추출됨
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

    [Header("=== 스폰 설정 ===")]
    [Tooltip("카메라 (비워두면 Main Camera)")]
    [SerializeField] private Camera spawnCamera;

    [Tooltip("카메라 앞 최소 거리 (Z)")]
    [SerializeField] private float spawnDistanceZMin = 3f;

    [Tooltip("카메라 앞 최대 거리 (Z)")]
    [SerializeField] private float spawnDistanceZMax = 10f;

    [Header("=== 사다리꼴 X 범위 (Near = Z Min, Far = Z Max) ===")]
    [Tooltip("가까운 쪽 좌측 한계 (Z Min에서)")]
    [SerializeField] private float spawnRangeXMinNear = -3f;

    [Tooltip("가까운 쪽 우측 한계 (Z Min에서)")]
    [SerializeField] private float spawnRangeXMaxNear = 3f;

    [Tooltip("먼 쪽 좌측 한계 (Z Max에서)")]
    [SerializeField] private float spawnRangeXMinFar = -8f;

    [Tooltip("먼 쪽 우측 한계 (Z Max에서)")]
    [SerializeField] private float spawnRangeXMaxFar = 8f;

    [Header("=== 하위 호환용 (사용 안함) ===")]
    [HideInInspector]
    [SerializeField] private float spawnRangeXMin = -5f;
    [HideInInspector]
    [SerializeField] private float spawnRangeXMax = 5f;

    [Header("=== 배회 설정 ===")]
    [Tooltip("스폰 후 자동 배회")]
    [SerializeField] private bool enableWander = true;

    [Tooltip("배회 이동 속도")]
    [SerializeField] private float wanderMoveSpeed = 2f;

    [Tooltip("배회 회전 속도")]
    [SerializeField] private float wanderRotationSpeed = 120f;

    [Header("=== 스케일 ===")]
    [Tooltip("체크하면 프리팹 원본 스케일 유지")]
    [SerializeField] private bool keepPrefabScale = true;
    [Tooltip("keepPrefabScale이 false일 때 적용할 스케일")]
    [SerializeField] private Vector3 spawnScale = Vector3.one;

    [Header("매칭 설정")]
    [SerializeField] private bool ignoreCase = true;
    [SerializeField] private bool allowPartialMatch = true;

    [Header("=== 모델 개수 제한 ===")]
    [Tooltip("최대 모델 개수 (0 = 무제한)")]
    [SerializeField] private int maxModelCount = 10;

    [Tooltip("퇴장 지점 (Transform)")]
    [SerializeField] private Transform exitPoint;

    [Tooltip("퇴장 이동 속도")]
    [SerializeField] private float exitMoveSpeed = 5f;

    [Tooltip("퇴장 타임아웃 (초)")]
    [SerializeField] private float exitTimeout = 10f;

    [Header("=== 메모리 관리 ===")]
    [Tooltip("주기적 GC 호출")]
    [SerializeField] private bool enablePeriodicGC = true;

    [Tooltip("GC 호출 간격 (초)")]
    [SerializeField] private float gcInterval = 30f;

    private float _lastGCTime;

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

        // 스폰 위치 결정
        Vector3 pos = GetSpawnPosition();

        var instance = Instantiate(entry.prefab, pos, Quaternion.identity);
        if (!keepPrefabScale)
            instance.transform.localScale = spawnScale;
        instance.name = $"{entry.qrText}_Model_{_spawnedModels.Count}";

        // 배회 컴포넌트 추가
        if (enableWander)
        {
            var wander = instance.AddComponent<AnimalWander>();
            // AnimalWander가 Rigidbody를 자동 추가함 (RequireComponent)
        }

        var renderer = FindRenderer(instance, entry.rendererPath);

        // 프리팹별 UV/색상 설정 적용
        if (renderer != null)
        {
            ApplyEntrySettings(renderer, entry);
        }

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

        // 모델 개수 제한 체크
        if (maxModelCount > 0 && _spawnedModels.Count > maxModelCount)
        {
            RemoveOldestModel();
        }

        return true;
    }

    /// <summary>
    /// 가장 최근 모델의 Renderer
    /// </summary>
    public Renderer GetCurrentRenderer() => CurrentRenderer;

    /// <summary>
    /// 가장 최근 스폰된 모델의 AnimalEntry 반환
    /// </summary>
    public AnimalEntry GetCurrentEntry()
    {
        if (string.IsNullOrEmpty(CurrentQRText)) return null;
        return FindEntry(CurrentQRText);
    }

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
            SafeDestroyModel(last);

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

    /// <summary>
    /// 가장 오래된 모델 퇴장시키기
    /// </summary>
    private void RemoveOldestModel()
    {
        if (_spawnedModels.Count == 0) return;

        var oldest = _spawnedModels[0];
        _spawnedModels.RemoveAt(0);

        if (oldest.instance != null)
        {
            // 퇴장 지점이 있으면 퇴장 애니메이션
            if (exitPoint != null)
            {
                StartCoroutine(ExitAndDestroy(oldest));
            }
            else
            {
                SafeDestroyModel(oldest);
            }
        }

        Debug.Log($"[AnimalModelManager] 오래된 모델 퇴장 (남은 개수: {_spawnedModels.Count})");
    }

    /// <summary>
    /// 퇴장 지점으로 이동 후 삭제
    /// </summary>
    private IEnumerator ExitAndDestroy(SpawnedModel model)
    {
        if (model.instance == null) yield break;

        // AnimalWander 비활성화
        var wander = model.instance.GetComponent<AnimalWander>();
        if (wander != null)
            wander.enabled = false;

        Vector3 targetPos = exitPoint.position;
        float elapsed = 0f;

        while (model.instance != null && elapsed < exitTimeout)
        {
            Vector3 direction = targetPos - model.instance.transform.position;
            direction.y = 0;

            if (direction.magnitude < 1f)
                break;

            // 회전
            Quaternion targetRot = Quaternion.LookRotation(direction);
            model.instance.transform.rotation = Quaternion.RotateTowards(
                model.instance.transform.rotation,
                targetRot,
                wanderRotationSpeed * Time.deltaTime
            );

            // 이동
            model.instance.transform.position += model.instance.transform.forward * exitMoveSpeed * Time.deltaTime;

            elapsed += Time.deltaTime;
            yield return null;
        }

        SafeDestroyModel(model);
    }

    /// <summary>
    /// 안전한 모델 삭제 (머티리얼, 텍스처 정리)
    /// </summary>
    private void SafeDestroyModel(SpawnedModel model)
    {
        if (model.instance == null) return;

        // 머티리얼과 텍스처 정리
        if (model.renderer != null)
        {
            var mat = model.renderer.material;
            if (mat != null)
            {
                var tex = mat.mainTexture;
                if (tex != null)
                    Destroy(tex);
                Destroy(mat);
            }
        }

        Destroy(model.instance);

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

    /// <summary>
    /// 프리팹별 UV 설정을 머티리얼에 적용 (색상은 ScanProcessor에서 자동 추출)
    /// </summary>
    private void ApplyEntrySettings(Renderer renderer, AnimalEntry entry)
    {
        if (renderer == null) return;

        var mat = renderer.material;
        if (mat == null) return;

        // UV 설정만 적용 (색상은 스캔 시 자동 추출됨)
        if (mat.HasProperty("_OffsetX"))
            mat.SetFloat("_OffsetX", entry.uvOffsetX);
        if (mat.HasProperty("_OffsetY"))
            mat.SetFloat("_OffsetY", entry.uvOffsetY);
        if (mat.HasProperty("_ScaleX"))
            mat.SetFloat("_ScaleX", entry.uvScaleX);
        if (mat.HasProperty("_ScaleY"))
            mat.SetFloat("_ScaleY", entry.uvScaleY);

        Debug.Log($"[AnimalModelManager] '{entry.qrText}' UV 설정 적용됨");
    }

    private Vector3 GetSpawnPosition()
    {
        Camera cam = spawnCamera != null ? spawnCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[AnimalModelManager] 카메라 없음!");
            return Vector3.zero;
        }

        // Z 거리 먼저 결정
        float randomZ = UnityEngine.Random.Range(spawnDistanceZMin, spawnDistanceZMax);

        // Z 비율에 따라 X 범위 보간 (사다리꼴)
        float zRatio = (randomZ - spawnDistanceZMin) / (spawnDistanceZMax - spawnDistanceZMin);
        float xMin = Mathf.Lerp(spawnRangeXMinNear, spawnRangeXMinFar, zRatio);
        float xMax = Mathf.Lerp(spawnRangeXMaxNear, spawnRangeXMaxFar, zRatio);

        float randomX = UnityEngine.Random.Range(xMin, xMax);

        Vector3 spawnPos = cam.transform.position
            + cam.transform.forward * randomZ
            + cam.transform.right * randomX;

        // Y축: Raycast로 지면 높이 찾기 (Terrain Collider 필요)
        if (Physics.Raycast(spawnPos + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f))
        {
            spawnPos.y = hit.point.y;
        }

        Debug.Log($"[AnimalModelManager] 스폰 위치: {spawnPos}");
        return spawnPos;
    }

    public int EntryCount => animalPrefabs.Count;

    // 외부에서 범위 접근용
    public Camera GetSpawnCamera() => spawnCamera != null ? spawnCamera : Camera.main;
    public float GetSpawnDistanceZMin() => spawnDistanceZMin;
    public float GetSpawnDistanceZMax() => spawnDistanceZMax;

    // 사다리꼴 범위
    public float GetSpawnRangeXMinNear() => spawnRangeXMinNear;
    public float GetSpawnRangeXMaxNear() => spawnRangeXMaxNear;
    public float GetSpawnRangeXMinFar() => spawnRangeXMinFar;
    public float GetSpawnRangeXMaxFar() => spawnRangeXMaxFar;

    // 하위 호환용
    public float GetSpawnRangeXMin() => spawnRangeXMin;
    public float GetSpawnRangeXMax() => spawnRangeXMax;

    // 배회 속도
    public float GetWanderMoveSpeed() => wanderMoveSpeed;
    public float GetWanderRotationSpeed() => wanderRotationSpeed;

#if UNITY_EDITOR
    [Header("=== Gizmo 설정 ===")]
    [Tooltip("Gizmo 높이 (고정값, 위에서 2D로 보기 편하게)")]
    [SerializeField] private float gizmoHeight = 5f;

    private void OnDrawGizmos()
    {
        Camera cam = spawnCamera != null ? spawnCamera : Camera.main;
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        Vector3 forward = cam.transform.forward;
        Vector3 right = cam.transform.right;

        // Y를 0으로 만들어서 수평으로만 (위에서 2D로 보기 편하게)
        forward.y = 0;
        forward.Normalize();
        right.y = 0;
        right.Normalize();

        float baseY = gizmoHeight;

        // 4개 코너 (사다리꼴 - Near는 좁고, Far는 넓음)
        // 가까운 쪽 (Z Min) - 좁은 범위
        Vector3 nearLeft = camPos + forward * spawnDistanceZMin + right * spawnRangeXMinNear;
        Vector3 nearRight = camPos + forward * spawnDistanceZMin + right * spawnRangeXMaxNear;
        // 먼 쪽 (Z Max) - 넓은 범위
        Vector3 farLeft = camPos + forward * spawnDistanceZMax + right * spawnRangeXMinFar;
        Vector3 farRight = camPos + forward * spawnDistanceZMax + right * spawnRangeXMaxFar;

        // 높이 고정
        nearLeft.y = baseY;
        nearRight.y = baseY;
        farLeft.y = baseY;
        farRight.y = baseY;

        // 사다리꼴 테두리 (초록색)
        Gizmos.color = Color.green;
        Gizmos.DrawLine(nearLeft, nearRight);   // 가까운 쪽 (좁음)
        Gizmos.DrawLine(farLeft, farRight);     // 먼 쪽 (넓음)
        Gizmos.DrawLine(nearLeft, farLeft);     // 왼쪽 사선
        Gizmos.DrawLine(nearRight, farRight);   // 오른쪽 사선

        // 코너 표시
        Gizmos.DrawSphere(nearLeft, 0.3f);
        Gizmos.DrawSphere(nearRight, 0.3f);
        Gizmos.DrawSphere(farLeft, 0.3f);
        Gizmos.DrawSphere(farRight, 0.3f);

        // 카메라 위치 표시 (빨간색)
        Gizmos.color = Color.red;
        Vector3 camGizmoPos = new Vector3(camPos.x, baseY, camPos.z);
        Gizmos.DrawWireSphere(camGizmoPos, 0.5f);

        // 카메라 앞 방향 화살표 (노란색)
        Gizmos.color = Color.yellow;
        Vector3 arrowEnd = camGizmoPos + forward * 3f;
        Gizmos.DrawLine(camGizmoPos, arrowEnd);
        // 화살촉
        Gizmos.DrawLine(arrowEnd, arrowEnd - forward * 0.5f + right * 0.3f);
        Gizmos.DrawLine(arrowEnd, arrowEnd - forward * 0.5f - right * 0.3f);

        // 퇴장 지점 표시 (파란색)
        if (exitPoint != null)
        {
            Gizmos.color = Color.blue;
            Vector3 exitGizmoPos = new Vector3(exitPoint.position.x, baseY, exitPoint.position.z);
            Gizmos.DrawWireSphere(exitGizmoPos, 0.8f);
            Gizmos.DrawLine(camGizmoPos, exitGizmoPos);
        }
    }
#endif
}
