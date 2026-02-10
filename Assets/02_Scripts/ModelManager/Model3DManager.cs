using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 3D 모델 스폰 및 텍스처 매핑 관리
/// - QR 코드에 해당하는 3D 모델 스폰
/// - 스캔된 텍스처를 모델의 Material에 적용
/// </summary>
public class Model3DManager : MonoBehaviour
{
    [Serializable]
    public class Model3DEntry
    {
        [Tooltip("QR 코드 텍스트 (예: 뜸부기, 수리부엉이)")]
        public string qrText;

        [Tooltip("3D 모델 프리팹 (애니메이션 포함)")]
        public GameObject modelPrefab;

        [Tooltip("텍스처를 적용할 Material 인덱스 (보통 0)")]
        public int materialIndex = 0;

        [Tooltip("텍스처 프로퍼티 이름 (URP: _BaseMap, Standard: _MainTex)")]
        public string texturePropertyName = "_BaseMap";

        [Header("=== UV 조정 ===")]
        [Tooltip("좌우 반전")]
        public bool flipX = false;

        [Tooltip("상하 반전")]
        public bool flipY = false;

        [Tooltip("X 오프셋 (+ = 오른쪽 이동)")]
        [Range(-0.5f, 0.5f)]
        public float offsetX = 0f;

        [Tooltip("Y 오프셋 (+ = 위로 이동)")]
        [Range(-0.5f, 0.5f)]
        public float offsetY = 0f;

        [Tooltip("X 스케일 (1=원본, 2=2배 크게, 0.5=절반 작게)")]
        [Range(0.05f, 20f)]
        public float scaleX = 1f;

        [Tooltip("Y 스케일 (1=원본, 2=2배 크게, 0.5=절반 작게)")]
        [Range(0.05f, 20f)]
        public float scaleY = 1f;

        [Tooltip("회전 (0=없음, 90, 180, 270)")]
        public int rotation = 0;

        [Header("=== 투영 방식 ===")]
        [Tooltip("UV: 기존 UV 사용 / Front: 정면 투영 (왜곡 없음, 측면 페이드)")]
        public ProjectionType projectionType = ProjectionType.UV;

        [Tooltip("BakedFront 투영 축 (모델 정면 방향에 따라 선택)")]
        public BakeAxis bakeAxis = BakeAxis.Z_FrontBack;

        [Header("=== 배회 설정 ===")]
        [Tooltip("X 최소 범위")]
        public float moveMinX = -10f;
        [Tooltip("X 최대 범위")]
        public float moveMaxX = 10f;
        [Tooltip("Y 최소 범위")]
        public float moveMinY = -7f;
        [Tooltip("Y 최대 범위")]
        public float moveMaxY = -4f;

        [Tooltip("최소 이동 속도")]
        [Range(0f, 10f)]
        public float moveSpeedMin = 1f;

        [Tooltip("최대 이동 속도")]
        [Range(0f, 10f)]
        public float moveSpeedMax = 3f;

        [Tooltip("도착 후 대기 시간 최소 (초)")]
        [Range(0f, 5f)]
        public float waitMin = 0.5f;

        [Tooltip("도착 후 대기 시간 최대 (초)")]
        [Range(0f, 5f)]
        public float waitMax = 2f;

        [Header("=== 개별 스케일/회전 ===")]
        [Tooltip("스폰 스케일 (캐릭터별 크기)")]
        public Vector3 spawnScale = Vector3.one;

        [Tooltip("스폰 회전 (Euler, 좌우이동 시 Y축 ±180 자동 적용)")]
        public Vector3 spawnRotation = Vector3.zero;

        [Header("=== 스폰 이펙트 (개별) ===")]
        [Tooltip("파티클 이펙트 색상")]
        public Color effectColor = new Color(0.3f, 0.85f, 1f, 1f);

        [Tooltip("응축 단계 머티리얼 (비워두면 기본 Additive 파티클)")]
        public Material effectMaterial;

        [Tooltip("폭발(팡!) 단계 머티리얼 (비워두면 응축 머티리얼과 동일)")]
        public Material burstEffectMaterial;

        [Tooltip("파티클 퍼짐 범위 배율 (1=기본, 2=2배 넓게)")]
        [Range(0.3f, 5f)]
        public float effectRange = 1f;

        [Header("=== 스폰 음성 ===")]
        [Tooltip("스폰 시 재생할 음성 클립 (예: '꾸구리가 나타났어요!')")]
        public AudioClip spawnAudioClip;
    }

    public enum ProjectionType
    {
        UV,         // 기존 UV 매핑
        Front,      // 정면 투영 (평면처럼 보임, 애니메이션 미추적)
        BakedFront  // 정면 투영 UV를 메시에 구움 (왜곡 없음 + 애니메이션 추적)
    }

    public enum BakeAxis
    {
        Z_FrontBack,   // XY 투영 (모델이 Z축 바라봄 = 기본)
        X_LeftRight,   // ZY 투영 (모델이 X축 바라봄)
        NegZ_BackFront,// XY 투영 (모델이 -Z축 바라봄, U 반전)
        NegX_RightLeft // ZY 투영 (모델이 -X축 바라봄, U 반전)
    }

    [Header("=== 3D 모델 등록 ===")]
    [SerializeField]
    private List<Model3DEntry> modelEntries = new List<Model3DEntry>()
    {
        new Model3DEntry { qrText = "수리부엉이" },
        new Model3DEntry { qrText = "뜸부기" },
        new Model3DEntry { qrText = "금개구리" },
        new Model3DEntry { qrText = "맹꽁이" },
        new Model3DEntry { qrText = "도롱뇽" },
        new Model3DEntry { qrText = "꾸구리" },
        new Model3DEntry { qrText = "어름치" },
        new Model3DEntry { qrText = "대모잠자리" },
        new Model3DEntry { qrText = "늦반딧불이" },
        new Model3DEntry { qrText = "말똥게" },
        new Model3DEntry { qrText = "파랑이" },
    };

    [Header("매칭 설정")]
    [SerializeField] private bool ignoreCase = true;
    [SerializeField] private bool allowPartialMatch = true;

    [Header("=== 스폰 설정 ===")]
    [Tooltip("스폰 스케일")]
    [SerializeField] private Vector3 spawnScale = Vector3.one;

    [Tooltip("스폰 회전 (Euler)")]
    [SerializeField] private Vector3 spawnRotation = Vector3.zero;

    [Header("=== 스폰 이펙트 ===")]
    [Tooltip("스폰 시 에너지 응축 → 폭발 이펙트")]
    [SerializeField] private bool enableSpawnEffect = true;

    [Tooltip("에너지 응축 시간 (초)")]
    [Range(0.3f, 5f)]
    [SerializeField] private float spawnEffectGatherDuration = 2f;

    [Tooltip("모델 등장 스케일업 시간 (초)")]
    [Range(0.1f, 1.5f)]
    [SerializeField] private float spawnEffectScaleDuration = 0.5f;

    [Tooltip("스폰 안내 텍스트 (인스펙터에서 연결, 비워두면 텍스트 없음)")]
    [SerializeField] private TMP_Text spawnAnnouncementText;

    [Tooltip("스폰 음성 재생용 AudioSource (비워두면 자동 생성)")]
    [SerializeField] private AudioSource spawnAudioSource;

    [Header("=== 머티리얼 설정 ===")]
    [Tooltip("기존 Material 무시하고 항상 새로 생성")]
    [SerializeField] private bool forceNewMaterial = true;

    [Tooltip("자동 생성 시 사용할 쉐이더 (비워두면 URP Unlit)")]
    [SerializeField] private Shader defaultShader;

    [Header("=== 카메라 설정 ===")]
    [Tooltip("3D용 카메라 (없으면 Main Camera 사용)")]
    [SerializeField] private Camera targetCamera;

    [Header("=== 모델 개수 제한 ===")]
    [Tooltip("최대 모델 개수 (0 = 무제한)")]
    [SerializeField] private int maxModelCount = 3;

    [Tooltip("퇴장 위치 (ExitZone 오브젝트 연결, 비워두면 즉시 삭제)")]
    [SerializeField] private Transform exitZone;

    [Tooltip("퇴장 이동 속도")]
    [Range(1f, 20f)]
    [SerializeField] private float exitSpeed = 5f;

    [Header("=== 모델 간 회피 ===")]
    [Tooltip("모델 간 최소 거리 (이보다 가까우면 밀어냄)")]
    [Range(0f, 5f)]
    [SerializeField] private float separationDistance = 2f;

    [Tooltip("밀어내는 힘 (클수록 빠르게 회피)")]
    [Range(0f, 10f)]
    [SerializeField] private float separationForce = 3f;

    [Header("=== 런타임 조정 ===")]
    [Tooltip("Inspector에서 값 변경 시 실시간 반영")]
    [SerializeField] private bool liveAdjust = true;

    [Header("=== 메모리 관리 ===")]
    [SerializeField] private bool enablePeriodicGC = true;
    [SerializeField] private float gcInterval = 30f;
    private float _lastGCTime;

    // 런타임 조정용 캐시
    private Texture2D _originalTexture;
    private float _lastFlipX, _lastFlipY, _lastOffsetX, _lastOffsetY, _lastScaleX, _lastScaleY;
    private int _lastRotation;
    private int _lastBakeAxis;

    // 스폰된 모델 관리
    private List<SpawnedModel> _spawnedModels = new List<SpawnedModel>();

    // 현재 모델
    public GameObject CurrentModel { get; private set; }
    public Renderer CurrentRenderer { get; private set; }
    public Material CurrentMaterial { get; private set; }
    public string CurrentQRText { get; private set; }

    public class SpawnedModel
    {
        public GameObject instance;
        public Renderer renderer;
        public Material material;
        public string qrText;
        public Texture2D appliedTexture;
        public float moveSpeed;
        public Vector3 wanderTarget;
        public Vector3 dampVelocity;
        public bool facingRight = true;
        public float flipCooldown;
        public float waitTimer;
        public Vector3 baseRotation;
        public bool isExiting;
        public Mesh originalMesh; // BakedFront 재구움용 원본 메시 캐시
    }

    public event Action<GameObject, Renderer> OnModelSpawned;

    void Start()
    {
        // 카메라 설정
        if (targetCamera == null)
            targetCamera = Camera.main;

        // 3D용 카메라 설정 확인
        if (targetCamera != null && targetCamera.orthographic)
        {
            Debug.LogWarning("[Model3DManager] 카메라가 Orthographic 모드입니다. 3D 모델은 Perspective 카메라 권장");
        }
    }

    void Update()
    {
        // 모든 스폰된 모델 이동 처리
        UpdateModelMovement();

        // 런타임 조정 모드
        if (!liveAdjust || CurrentMaterial == null || _originalTexture == null) return;

        var entry = FindEntry(CurrentQRText);
        if (entry == null) return;

        // 변경 감지
        bool flipChanged = (_lastFlipX != (entry.flipX ? 1 : 0)) || (_lastFlipY != (entry.flipY ? 1 : 0));
        bool uvChanged = _lastOffsetX != entry.offsetX || _lastOffsetY != entry.offsetY ||
                         _lastScaleX != entry.scaleX || _lastScaleY != entry.scaleY;
        bool rotationChanged = _lastRotation != entry.rotation;
        bool bakeAxisChanged = _lastBakeAxis != (int)entry.bakeAxis;

        if (flipChanged || uvChanged || rotationChanged || bakeAxisChanged)
        {
            // 캐시 업데이트
            _lastFlipX = entry.flipX ? 1 : 0;
            _lastFlipY = entry.flipY ? 1 : 0;
            _lastOffsetX = entry.offsetX;
            _lastOffsetY = entry.offsetY;
            _lastScaleX = entry.scaleX;
            _lastScaleY = entry.scaleY;
            _lastRotation = entry.rotation;
            _lastBakeAxis = (int)entry.bakeAxis;

            // BakedFront: UV를 다시 구움 (Inspector에서 실시간 조정 가능)
            if (entry.projectionType == ProjectionType.BakedFront && CurrentRenderer != null)
            {
                BakeFrontProjectionUVs(CurrentRenderer, entry);
                Debug.Log($"[Model3DManager] BakedFront 재구움: flip=({entry.flipX},{entry.flipY}), scale=({entry.scaleX},{entry.scaleY}), offset=({entry.offsetX},{entry.offsetY})");
                return;
            }

            // 정면 투영 모드 체크
            bool isFrontMode = entry.projectionType == ProjectionType.Front && CurrentMaterial.HasProperty("_FlipX");

            if (isFrontMode)
            {
                // 정면 투영 쉐이더: 쉐이더 파라미터로 직접 설정
                CurrentMaterial.SetFloat("_FlipX", entry.flipX ? 1f : 0f);
                CurrentMaterial.SetFloat("_FlipY", entry.flipY ? 1f : 0f);
                CurrentMaterial.SetFloat("_ScaleX", entry.scaleX);
                CurrentMaterial.SetFloat("_ScaleY", entry.scaleY);
                CurrentMaterial.SetFloat("_OffsetX", entry.offsetX);
                CurrentMaterial.SetFloat("_OffsetY", entry.offsetY);

                // 회전 적용 (0, 90, 180, 270)
                CurrentMaterial.SetFloat("_Rotation", entry.rotation);

                Debug.Log($"[Model3DManager] 런타임 정면 투영: flip=({entry.flipX},{entry.flipY}), scale=({entry.scaleX},{entry.scaleY}), offset=({entry.offsetX},{entry.offsetY}), rotation={entry.rotation}°");
            }
            else
            {
                // 기존 UV 방식
                // 플립 변경 시 텍스처 재생성
                if (flipChanged)
                {
                    Texture2D newTex = FlipTexture(_originalTexture, entry.flipX, entry.flipY);
                    CurrentMaterial.mainTexture = newTex;

                    string[] textureProps = { "_BaseMap", "_MainTex" };
                    foreach (var prop in textureProps)
                    {
                        if (CurrentMaterial.HasProperty(prop))
                            CurrentMaterial.SetTexture(prop, newTex);
                    }
                }

                // UV 스케일/오프셋 업데이트
                // Unity tiling은 역방향: tiling=2면 텍스처가 2번 반복 (작아짐)
                // 직관적으로: scale=2면 텍스처가 2배 크게 보이도록 1/scale 사용
                float tilingX = 1f / Mathf.Max(0.1f, entry.scaleX);
                float tilingY = 1f / Mathf.Max(0.1f, entry.scaleY);
                Vector2 tiling = new Vector2(tilingX, tilingY);

                // 오프셋도 스케일에 맞게 조정 (텍스처 중심 기준)
                // 스케일 변경 시 중심이 유지되도록 보정
                float offsetX = entry.offsetX + (1f - tilingX) * 0.5f;
                float offsetY = entry.offsetY + (1f - tilingY) * 0.5f;
                Vector2 offset = new Vector2(offsetX, offsetY);

                CurrentMaterial.mainTextureScale = tiling;
                CurrentMaterial.mainTextureOffset = offset;

                if (CurrentMaterial.HasProperty("_BaseMap_ST"))
                {
                    CurrentMaterial.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
                }

                Debug.Log($"[Model3DManager] 런타임 UV 조정: flip=({entry.flipX},{entry.flipY}), scale=({entry.scaleX},{entry.scaleY}), tiling=({tilingX:F2},{tilingY:F2}), offset=({offsetX:F2},{offsetY:F2})");
            }
        }
    }

    /// <summary>
    /// 모든 스폰된 모델의 자유 배회 처리 (SmoothDamp 기반)
    /// </summary>
    private void UpdateModelMovement()
    {
        for (int i = 0; i < _spawnedModels.Count; i++)
        {
            var model = _spawnedModels[i];
            if (model.instance == null)
            {
                _spawnedModels.RemoveAt(i);
                i--;
                continue;
            }

            // 퇴장 중인 모델: ExitZone으로 이동
            if (model.isExiting)
            {
                Vector3 exitPos = model.instance.transform.position;
                Vector3 exitTarget = exitZone != null ? exitZone.position : exitPos;
                exitTarget.z = exitPos.z;

                // ExitZone 방향으로 일정 속도 이동
                Vector3 exitNewPos = Vector3.MoveTowards(exitPos, exitTarget, exitSpeed * Time.deltaTime);
                model.instance.transform.position = exitNewPos;

                // 퇴장 방향으로 회전
                float exitDx = exitTarget.x - exitPos.x;
                if (Mathf.Abs(exitDx) > 0.1f)
                {
                    bool shouldFaceRight = exitDx > 0;
                    if (model.facingRight != shouldFaceRight)
                    {
                        model.facingRight = shouldFaceRight;
                        Vector3 rot = model.baseRotation;
                        rot.y += shouldFaceRight ? 0f : 180f;
                        model.instance.transform.rotation = Quaternion.Euler(rot);
                    }
                }

                // 도착하면 삭제
                if (Vector3.Distance(exitNewPos, exitTarget) < 0.3f)
                {
                    SafeDestroyModel(model);
                    _spawnedModels.RemoveAt(i);
                    i--;
                    Debug.Log($"[Model3DManager] '{model.qrText}' 퇴장 완료 (남은: {_spawnedModels.Count})");
                }
                continue;
            }

            var entry = FindEntry(model.qrText);
            if (entry == null || model.moveSpeed <= 0f) continue;

            // 대기 중이면 타이머 감소
            if (model.waitTimer > 0f)
            {
                model.waitTimer -= Time.deltaTime;
                continue;
            }

            Vector3 pos = model.instance.transform.position;
            Vector3 target = model.wanderTarget;

            // SmoothDamp로 부드럽게 이동 (가속/감속 자연스러움)
            float smoothTime = 1f / Mathf.Max(0.1f, model.moveSpeed);
            Vector3 newPos = Vector3.SmoothDamp(pos, target, ref model.dampVelocity, smoothTime);
            newPos.z = 0;

            // 소프트 회피: 다른 모델과 가까우면 밀어냄 + 반대 방향으로 목표 변경
            if (separationDistance > 0f)
            {
                Vector3 separation = Vector3.zero;
                for (int j = 0; j < _spawnedModels.Count; j++)
                {
                    if (i == j) continue;
                    var other = _spawnedModels[j];
                    if (other.instance == null) continue;

                    Vector3 otherPos = other.instance.transform.position;
                    Vector3 diff = newPos - otherPos;
                    diff.z = 0;
                    float dist = diff.magnitude;

                    if (dist < separationDistance)
                    {
                        // 완전히 겹치면 랜덤 방향으로 밀기
                        Vector3 pushDir;
                        if (dist < 0.01f)
                            pushDir = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), 0).normalized;
                        else
                            pushDir = diff.normalized;

                        // 가까울수록 강하게 (제곱 = 겹칠수록 훨씬 강함)
                        float t = 1f - dist / separationDistance;
                        separation += pushDir * (t * t);

                        // 목표점이 상대방 근처면 → 반대 방향으로 목표 변경
                        Vector3 targetDiff = model.wanderTarget - otherPos;
                        targetDiff.z = 0;
                        if (targetDiff.magnitude < separationDistance)
                        {
                            float awayX = Mathf.Clamp(pos.x + pushDir.x * separationDistance * 3f, entry.moveMinX, entry.moveMaxX);
                            float awayY = Mathf.Clamp(pos.y + pushDir.y * separationDistance * 3f, entry.moveMinY, entry.moveMaxY);
                            model.wanderTarget = new Vector3(awayX, awayY, 0);
                        }
                    }
                }

                if (separation.sqrMagnitude > 0f)
                {
                    newPos += separation * separationForce * Time.deltaTime;
                    newPos.z = 0;
                }
            }

            // 범위 체크 - 벗어나면 새 목표로 리셋
            if (newPos.x < entry.moveMinX || newPos.x > entry.moveMaxX ||
                newPos.y < entry.moveMinY || newPos.y > entry.moveMaxY)
            {
                model.wanderTarget = GetRandomTarget(entry);
                model.dampVelocity = Vector3.zero;
                continue;
            }

            // 좌우 방향 전환 (쿨다운)
            model.flipCooldown -= Time.deltaTime;
            if (model.flipCooldown <= 0f)
            {
                float dx = model.dampVelocity.x;
                if (Mathf.Abs(dx) > 0.02f)
                {
                    bool shouldFaceRight = dx > 0;
                    if (model.facingRight != shouldFaceRight)
                    {
                        model.facingRight = shouldFaceRight;
                        Vector3 rot = model.baseRotation;
                        rot.y += shouldFaceRight ? 0f : 180f;
                        model.instance.transform.rotation = Quaternion.Euler(rot);
                        model.flipCooldown = 0.5f;
                    }
                }
            }

            model.instance.transform.position = newPos;

            // 목표 근처 도착하면 대기 후 새 목표
            if (Vector3.Distance(pos, target) < 0.3f)
            {
                model.wanderTarget = GetRandomTarget(entry);
                model.waitTimer = UnityEngine.Random.Range(entry.waitMin, entry.waitMax);
            }
        }
    }

    /// <summary>
    /// Entry의 범위 안에서 랜덤 목표 위치 반환
    /// </summary>
    private Vector3 GetRandomTarget(Model3DEntry entry)
    {
        float x = UnityEngine.Random.Range(entry.moveMinX, entry.moveMaxX);
        float y = UnityEngine.Random.Range(entry.moveMinY, entry.moveMaxY);
        return new Vector3(x, y, 0);
    }

    /// <summary>
    /// 기존 모델과 겹치지 않는 스폰 위치 찾기
    /// separationDistance 이내에 다른 모델이 있으면 다른 위치 시도
    /// maxAttempts번 시도 후에도 못 찾으면 가장 멀리 떨어진 위치 반환
    /// </summary>
    private Vector3 FindNonOverlappingPosition(Model3DEntry entry, int maxAttempts = 30)
    {
        if (_spawnedModels.Count == 0 || separationDistance <= 0f)
            return GetRandomTarget(entry);

        float minDist = separationDistance;
        Vector3 bestPos = Vector3.zero;
        float bestMinDist = -1f;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector3 candidate = GetRandomTarget(entry);
            float closestDist = float.MaxValue;

            foreach (var model in _spawnedModels)
            {
                if (model.instance == null || model.isExiting) continue;
                Vector3 diff = candidate - model.instance.transform.position;
                diff.z = 0;
                float dist = diff.magnitude;
                if (dist < closestDist)
                    closestDist = dist;
            }

            // 충분히 떨어져 있으면 바로 사용
            if (closestDist >= minDist)
                return candidate;

            // 가장 멀리 떨어진 후보 기록
            if (closestDist > bestMinDist)
            {
                bestMinDist = closestDist;
                bestPos = candidate;
            }
        }

        Debug.Log($"[Model3DManager] 겹치지 않는 위치 못 찾음 → 가장 먼 위치 사용 (최소거리: {bestMinDist:F2})");
        return bestPos;
    }

    /// <summary>
    /// QR 텍스트로 3D 모델 스폰 (동기 버전)
    /// </summary>
    public bool SpawnModelByQR(string qrText)
    {
        var entry = ValidateAndFindEntry(qrText);
        if (entry == null) return false;

        var instance = Instantiate(entry.modelPrefab);
        return SetupSpawnedModel(instance, entry);
    }

    /// <summary>
    /// QR 텍스트로 3D 모델 스폰 (비동기 - 렉 방지)
    /// InstantiateAsync로 인스턴스화를 여러 프레임에 분산
    /// </summary>
    public IEnumerator SpawnModelByQRAsync(string qrText, Action<bool> onComplete = null)
    {
        var entry = ValidateAndFindEntry(qrText);
        if (entry == null)
        {
            onComplete?.Invoke(false);
            yield break;
        }

        // 비동기 인스턴스화 (여러 프레임에 걸쳐 처리 → 렉 방지)
        var asyncOp = InstantiateAsync(entry.modelPrefab, 1);
        yield return asyncOp;

        if (asyncOp.Result == null || asyncOp.Result.Length == 0)
        {
            Debug.LogError($"[Model3DManager] '{entry.qrText}' 비동기 인스턴스화 실패");
            onComplete?.Invoke(false);
            yield break;
        }

        var instance = asyncOp.Result[0] as GameObject;
        bool result = SetupSpawnedModel(instance, entry);
        onComplete?.Invoke(result);
    }

    /// <summary>
    /// Entry 유효성 검사 (SpawnModelByQR / SpawnModelByQRAsync 공용)
    /// </summary>
    private Model3DEntry ValidateAndFindEntry(string qrText)
    {
        if (string.IsNullOrEmpty(qrText))
        {
            Debug.LogWarning("[Model3DManager] QR 텍스트가 비어있습니다.");
            return null;
        }

        var entry = FindEntry(qrText);
        if (entry == null)
        {
            Debug.LogWarning($"[Model3DManager] '{qrText}'에 해당하는 모델 없음");
            return null;
        }

        if (entry.modelPrefab == null)
        {
            Debug.LogError($"[Model3DManager] '{entry.qrText}' 모델 프리팹이 null");
            return null;
        }

        return entry;
    }

    /// <summary>
    /// 인스턴스화된 모델 셋업 (동기/비동기 공용)
    /// </summary>
    private bool SetupSpawnedModel(GameObject instance, Model3DEntry entry)
    {
        instance.name = $"{entry.qrText}_3DModel_{_spawnedModels.Count}";
        Vector3 randomPos = FindNonOverlappingPosition(entry);
        instance.transform.position = randomPos;
        instance.transform.localScale = entry.spawnScale;
        instance.transform.rotation = Quaternion.Euler(entry.spawnRotation);

        // Renderer 찾기
        var renderer = instance.GetComponentInChildren<Renderer>();
        if (renderer == null)
        {
            Debug.LogError($"[Model3DManager] '{entry.qrText}' 모델에 Renderer가 없음");
            Destroy(instance);
            return false;
        }

        // BakedFront: 바인드 포즈 기준 정면 투영 UV를 메시에 구움
        if (entry.projectionType == ProjectionType.BakedFront)
        {
            BakeFrontProjectionUVs(renderer, entry);
        }

        // Material 가져오기 또는 생성
        Material mat = GetOrCreateMaterial(renderer, entry);
        if (mat == null)
        {
            Debug.LogError($"[Model3DManager] '{entry.qrText}' 모델에 Material 생성 실패");
            Destroy(instance);
            return false;
        }

        Debug.Log($"[Model3DManager] Renderer: {renderer.GetType().Name}, Material: {mat.shader.name}");

        // 스폰 목록에 추가
        var spawned = new SpawnedModel
        {
            instance = instance,
            renderer = renderer,
            material = mat,
            qrText = entry.qrText,
            appliedTexture = null,
            moveSpeed = UnityEngine.Random.Range(entry.moveSpeedMin, entry.moveSpeedMax),
            wanderTarget = GetRandomTarget(entry),
            dampVelocity = Vector3.zero,
            facingRight = true,
            flipCooldown = 0f,
            waitTimer = enableSpawnEffect ? spawnEffectGatherDuration + spawnEffectScaleDuration : 0f,
            baseRotation = entry.spawnRotation
        };
        _spawnedModels.Add(spawned);

        // 현재 모델 업데이트
        CurrentModel = instance;
        CurrentRenderer = renderer;
        CurrentMaterial = mat;
        CurrentQRText = entry.qrText;

        // 스폰 이펙트 실행
        if (enableSpawnEffect)
        {
            var effect = instance.AddComponent<SpawnEffect>();
            effect.Play(entry.spawnScale, spawnEffectGatherDuration, spawnEffectScaleDuration, entry.effectColor, entry.qrText, spawnAnnouncementText, entry.effectMaterial, entry.burstEffectMaterial, entry.effectRange);
        }

        // 스폰 음성 재생
        if (entry.spawnAudioClip != null)
        {
            if (spawnAudioSource == null)
                spawnAudioSource = gameObject.AddComponent<AudioSource>();
            spawnAudioSource.PlayOneShot(entry.spawnAudioClip);
        }

        OnModelSpawned?.Invoke(instance, renderer);

        Debug.Log($"[Model3DManager] '{entry.qrText}' 3D 모델 스폰 완료 (총 {_spawnedModels.Count}개)");

        // 모델 개수 제한 (퇴장 중인 모델 제외)
        if (maxModelCount > 0)
        {
            int activeCount = 0;
            foreach (var m in _spawnedModels)
                if (!m.isExiting) activeCount++;

            if (activeCount > maxModelCount)
                RemoveOldestModel();
        }

        return true;
    }

    /// <summary>
    /// 현재 모델에 텍스처 적용
    /// </summary>
    public bool ApplyTextureToCurrentModel(Texture2D texture)
    {
        if (CurrentMaterial == null)
        {
            Debug.LogWarning("[Model3DManager] 적용할 모델이 없습니다.");
            return false;
        }

        if (texture == null)
        {
            Debug.LogWarning("[Model3DManager] 텍스처가 null입니다.");
            return false;
        }

        var entry = FindEntry(CurrentQRText);

        // 텍스처를 직접 사용 (ScanProcessor3D가 매번 새 텍스처를 생성하므로 복제 불필요)
        // 텍스처 수명은 SpawnedModel.appliedTexture + SafeDestroyModel이 관리
        _originalTexture = texture;

        // 초기 캐시값 설정
        if (entry != null)
        {
            _lastFlipX = entry.flipX ? 1 : 0;
            _lastFlipY = entry.flipY ? 1 : 0;
            _lastOffsetX = entry.offsetX;
            _lastOffsetY = entry.offsetY;
            _lastScaleX = entry.scaleX;
            _lastScaleY = entry.scaleY;
            _lastRotation = entry.rotation;
            _lastBakeAxis = (int)entry.bakeAxis;
        }

        // 투영 모드 체크
        bool isFrontMode = entry != null && entry.projectionType == ProjectionType.Front && CurrentMaterial.HasProperty("_FlipX");
        bool isBakedFront = entry != null && entry.projectionType == ProjectionType.BakedFront;

        // UV 조정이 필요하면 텍스처 변환
        // BakedFront: flip/scale/offset은 메시 UV에 이미 구워져 있으므로 텍스처 변환 불필요
        // Front: 쉐이더에서 처리하므로 텍스처 변환 불필요
        Texture2D finalTexture = texture;
        if (!isFrontMode && !isBakedFront && entry != null && (entry.flipX || entry.flipY))
        {
            finalTexture = FlipTexture(texture, entry.flipX, entry.flipY);
            Debug.Log($"[Model3DManager] 텍스처 플립: flipX={entry.flipX}, flipY={entry.flipY}");
        }

        // 모든 가능한 텍스처 프로퍼티에 적용
        string[] textureProps = { "_BaseMap", "_MainTex", "_BaseColorMap" };
        bool anyApplied = false;

        foreach (var prop in textureProps)
        {
            if (CurrentMaterial.HasProperty(prop))
            {
                CurrentMaterial.SetTexture(prop, finalTexture);
                Debug.Log($"[Model3DManager] 텍스처 적용: {prop}");
                anyApplied = true;
            }
        }

        // mainTexture도 직접 설정 (구버전 호환)
        CurrentMaterial.mainTexture = finalTexture;

        // UV/쉐이더 파라미터 적용
        if (entry != null)
        {
            if (isBakedFront)
            {
                // BakedFront: flip/scale/offset은 메시 UV에 구워져 있음 → material은 기본값
                CurrentMaterial.mainTextureScale = Vector2.one;
                CurrentMaterial.mainTextureOffset = Vector2.zero;
                if (CurrentMaterial.HasProperty("_BaseMap_ST"))
                    CurrentMaterial.SetVector("_BaseMap_ST", new Vector4(1, 1, 0, 0));

                Debug.Log($"[Model3DManager] BakedFront: 메시 UV에 구움 완료, material tiling/offset 리셋");
            }
            else if (isFrontMode)
            {
                // 정면 투영 쉐이더: 쉐이더 파라미터로 직접 설정
                CurrentMaterial.SetFloat("_FlipX", entry.flipX ? 1f : 0f);
                CurrentMaterial.SetFloat("_FlipY", entry.flipY ? 1f : 0f);
                CurrentMaterial.SetFloat("_ScaleX", entry.scaleX);
                CurrentMaterial.SetFloat("_ScaleY", entry.scaleY);
                CurrentMaterial.SetFloat("_OffsetX", entry.offsetX);
                CurrentMaterial.SetFloat("_OffsetY", entry.offsetY);

                // 회전 적용 (0, 90, 180, 270)
                CurrentMaterial.SetFloat("_Rotation", entry.rotation);

                Debug.Log($"[Model3DManager] 정면 투영: flip=({entry.flipX},{entry.flipY}), scale=({entry.scaleX},{entry.scaleY}), offset=({entry.offsetX},{entry.offsetY}), rotation={entry.rotation}°");
            }
            else
            {
                // 기존 UV 방식: tiling/offset
                // Unity tiling은 역방향: tiling=2면 텍스처가 2번 반복 (작아짐)
                // 직관적으로: scale=2면 텍스처가 2배 크게 보이도록 1/scale 사용
                float tilingX = 1f / Mathf.Max(0.1f, entry.scaleX);
                float tilingY = 1f / Mathf.Max(0.1f, entry.scaleY);
                Vector2 tiling = new Vector2(tilingX, tilingY);

                // 오프셋도 스케일에 맞게 조정 (텍스처 중심 기준)
                float offsetX = entry.offsetX + (1f - tilingX) * 0.5f;
                float offsetY = entry.offsetY + (1f - tilingY) * 0.5f;
                Vector2 offset = new Vector2(offsetX, offsetY);

                CurrentMaterial.mainTextureScale = tiling;
                CurrentMaterial.mainTextureOffset = offset;

                // URP용 프로퍼티도 설정
                if (CurrentMaterial.HasProperty("_BaseMap_ST"))
                {
                    CurrentMaterial.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, offset.x, offset.y));
                }

                Debug.Log($"[Model3DManager] UV 조정: scale=({entry.scaleX}, {entry.scaleY}) → tiling=({tilingX:F2}, {tilingY:F2}), offset=({offsetX:F2}, {offsetY:F2})");
            }
        }

        if (!anyApplied)
        {
            Debug.LogWarning($"[Model3DManager] 알려진 텍스처 프로퍼티 없음, mainTexture만 설정됨");
        }

        // 스폰 목록에 텍스처 기록
        var current = _spawnedModels.Find(m => m.instance == CurrentModel);
        if (current != null)
        {
            if (current.appliedTexture != null && current.appliedTexture != texture)
            {
                Destroy(current.appliedTexture);
            }
            current.appliedTexture = finalTexture;
        }

        Debug.Log($"[Model3DManager] 텍스처 적용 완료: {finalTexture.width}x{finalTexture.height}, Shader: {CurrentMaterial.shader.name}");
        return true;
    }

    /// <summary>
    /// 텍스처 플립 (좌우/상하)
    /// </summary>
    private Texture2D FlipTexture(Texture2D source, bool flipX, bool flipY)
    {
        int w = source.width;
        int h = source.height;
        var original = source.GetPixels32();
        var flipped = new Color32[original.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int srcX = flipX ? (w - 1 - x) : x;
                int srcY = flipY ? (h - 1 - y) : y;
                int srcIdx = srcY * w + srcX;
                int dstIdx = y * w + x;
                flipped[dstIdx] = original[srcIdx];
            }
        }

        var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        result.SetPixels32(flipped);
        result.Apply();
        result.filterMode = source.filterMode;
        result.wrapMode = source.wrapMode;
        return result;
    }

    /// <summary>
    /// 바인드 포즈(T-포즈) 기준 정면 투영 UV를 메시에 구움
    /// - 원본 메시의 XY 좌표를 0~1 UV로 변환
    /// - 메시 인스턴스를 생성하여 UV 채널에 기록
    /// - 애니메이션 시 UV가 변하지 않으므로 텍스처가 따라감
    /// - 볼록면 왜곡 없음 (정면 투영이므로 평면처럼 보임)
    /// </summary>
    private void BakeFrontProjectionUVs(Renderer renderer, Model3DEntry entry)
    {
        bool isSkinned = renderer is SkinnedMeshRenderer;

        // 캐시된 원본 메시 찾기 (재구움 시 이전 bake 결과가 아닌 원본 사용)
        var spawned = _spawnedModels.Find(m => m.renderer == renderer);
        Mesh originalMesh = spawned?.originalMesh;

        // 원본 캐시가 없으면 현재 메시를 원본으로 저장
        if (originalMesh == null)
        {
            if (isSkinned)
                originalMesh = ((SkinnedMeshRenderer)renderer).sharedMesh;
            else
            {
                var mf = renderer.GetComponent<MeshFilter>();
                if (mf != null) originalMesh = mf.sharedMesh;
            }

            if (spawned != null)
                spawned.originalMesh = originalMesh;
        }

        if (originalMesh == null)
        {
            Debug.LogWarning("[Model3DManager] BakedFront: 메시를 찾을 수 없음");
            return;
        }

        if (!originalMesh.isReadable)
        {
            Debug.LogError($"[Model3DManager] BakedFront 실패: '{originalMesh.name}' 메시의 Read/Write가 꺼져있음!\n" +
                           "→ FBX 파일 선택 → Inspector → Model 탭 → Read/Write Enabled 체크 → Apply\n" +
                           "→ UV 모드로 대체합니다.");
            return;
        }

        // 원본을 복제 (원본 sharedMesh를 수정하면 안 됨)
        Mesh bakedMesh = Instantiate(originalMesh);
        bakedMesh.name = originalMesh.name + "_BakedFrontUV";

        // 바인드 포즈 정점 좌표 (T-포즈 기준)
        Vector3[] vertices = bakedMesh.vertices;
        Vector2[] newUVs = new Vector2[vertices.Length];

        // 메시 바운드에서 XY 범위 구하기
        Bounds bounds = bakedMesh.bounds;
        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minY = bounds.min.y;
        float maxY = bounds.max.y;
        float rangeX = maxX - minX;
        float rangeY = maxY - minY;

        // 0으로 나누기 방지
        if (rangeX < 0.001f) rangeX = 1f;
        if (rangeY < 0.001f) rangeY = 1f;

        // 투영 축에 따라 사용할 좌표 결정
        bool useZForU = (entry.bakeAxis == BakeAxis.X_LeftRight || entry.bakeAxis == BakeAxis.NegX_RightLeft);
        bool flipU = (entry.bakeAxis == BakeAxis.NegZ_BackFront || entry.bakeAxis == BakeAxis.NegX_RightLeft);

        // 투영 축에 맞는 바운드 재계산
        if (useZForU)
        {
            minX = bounds.min.z;
            maxX = bounds.max.z;
            rangeX = maxX - minX;
            if (rangeX < 0.001f) rangeX = 1f;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            // 투영 축에 따라 U 좌표 선택 (V는 항상 Y)
            float rawU = useZForU ? vertices[i].z : vertices[i].x;
            float u = (rawU - minX) / rangeX;
            float v = (vertices[i].y - minY) / rangeY;

            // 축 방향 반전 (뒷면/반대편)
            if (flipU) u = 1f - u;

            // 회전 적용 (UV 중심 0.5,0.5 기준)
            if (entry.rotation != 0)
            {
                float cu = u - 0.5f;
                float cv = v - 0.5f;
                float rad = -entry.rotation * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                u = cu * cos - cv * sin + 0.5f;
                v = cu * sin + cv * cos + 0.5f;
            }

            // 플립
            if (entry.flipX) u = 1f - u;
            if (entry.flipY) v = 1f - v;

            // 스케일 (중심 기준)
            u = (u - 0.5f) / Mathf.Max(0.05f, entry.scaleX) + 0.5f;
            v = (v - 0.5f) / Mathf.Max(0.05f, entry.scaleY) + 0.5f;

            // 오프셋
            u += entry.offsetX;
            v += entry.offsetY;

            newUVs[i] = new Vector2(u, v);
        }

        bakedMesh.uv = newUVs;

        // 메시 적용
        if (isSkinned)
        {
            ((SkinnedMeshRenderer)renderer).sharedMesh = bakedMesh;
        }
        else
        {
            var mf = renderer.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = bakedMesh;
        }

        Debug.Log($"[Model3DManager] BakedFront UV 적용: 축={entry.bakeAxis}, 정점 {vertices.Length}개, U범위[{minX:F2}~{maxX:F2}] V범위[{minY:F2}~{maxY:F2}]");
    }

    /// <summary>
    /// 메시 바운드 계산 (로컬 좌표 기준)
    /// </summary>
    private Bounds CalculateMeshBounds(Renderer renderer)
    {
        if (renderer == null)
            return new Bounds(Vector3.zero, Vector3.one);

        // SkinnedMeshRenderer는 현재 포즈의 바운드 사용
        if (renderer is SkinnedMeshRenderer skinnedRenderer)
        {
            // 로컬 바운드 반환 (sharedMesh 기준)
            if (skinnedRenderer.sharedMesh != null)
            {
                return skinnedRenderer.sharedMesh.bounds;
            }
            // sharedMesh 없으면 localBounds 사용
            return skinnedRenderer.localBounds;
        }

        // MeshRenderer는 MeshFilter에서 가져옴
        var meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return meshFilter.sharedMesh.bounds;
        }

        // 기본값
        return new Bounds(Vector3.zero, Vector3.one);
    }

    /// <summary>
    /// 현재 모델의 Renderer 반환
    /// </summary>
    public Renderer GetCurrentRenderer() => CurrentRenderer;

    /// <summary>
    /// 현재 모델의 Material 반환
    /// </summary>
    public Material GetCurrentMaterial() => CurrentMaterial;

    /// <summary>
    /// 현재 모델의 Entry 반환
    /// </summary>
    public Model3DEntry GetCurrentEntry()
    {
        if (string.IsNullOrEmpty(CurrentQRText)) return null;
        return FindEntry(CurrentQRText);
    }

    public int SpawnedCount => _spawnedModels.Count;

    /// <summary>
    /// 모든 모델 삭제
    /// </summary>
    public void DestroyAllModels()
    {
        foreach (var model in _spawnedModels)
        {
            if (model.instance != null)
                SafeDestroyModel(model);
        }
        _spawnedModels.Clear();

        CurrentModel = null;
        CurrentRenderer = null;
        CurrentMaterial = null;
        CurrentQRText = null;

        Debug.Log("[Model3DManager] 모든 모델 삭제됨");
    }

    private void RemoveOldestModel()
    {
        if (_spawnedModels.Count == 0) return;

        // 가장 오래된 비퇴장 모델 찾기
        for (int i = 0; i < _spawnedModels.Count; i++)
        {
            if (!_spawnedModels[i].isExiting)
            {
                var model = _spawnedModels[i];

                if (exitZone != null)
                {
                    // ExitZone으로 걸어서 퇴장
                    model.isExiting = true;
                    model.waitTimer = 0f;
                    model.dampVelocity = Vector3.zero;
                    Debug.Log($"[Model3DManager] '{model.qrText}' 퇴장 시작 → ExitZone");
                }
                else
                {
                    // ExitZone 없으면 즉시 삭제
                    _spawnedModels.RemoveAt(i);
                    SafeDestroyModel(model);
                    Debug.Log($"[Model3DManager] '{model.qrText}' 즉시 삭제 (남은: {_spawnedModels.Count})");
                }
                return;
            }
        }

        // 전부 퇴장 중이면 가장 오래된 것 강제 삭제
        var oldest = _spawnedModels[0];
        _spawnedModels.RemoveAt(0);
        SafeDestroyModel(oldest);
    }

    private void SafeDestroyModel(SpawnedModel model)
    {
        if (model.instance == null) return;

        // 적용된 텍스처 정리
        if (model.appliedTexture != null)
        {
            Destroy(model.appliedTexture);
            model.appliedTexture = null;
        }

        // Material 정리 (new Material로 생성된 인스턴스)
        if (model.material != null)
        {
            Destroy(model.material);
            model.material = null;
        }

        Destroy(model.instance);
        model.instance = null;

        // 주기적 GC
        if (enablePeriodicGC && Time.time - _lastGCTime > gcInterval)
        {
            _lastGCTime = Time.time;
            System.GC.Collect();
            Resources.UnloadUnusedAssets();
            Debug.Log("[Model3DManager] GC 실행");
        }
    }

    /// <summary>
    /// Material 가져오기 또는 자동 생성
    /// </summary>
    private Material GetOrCreateMaterial(Renderer renderer, Model3DEntry entry)
    {
        Material mat = null;

        // 강제 새 Material 생성 모드
        if (forceNewMaterial)
        {
            mat = CreateDefaultMaterial(entry.qrText, entry);
            if (mat != null)
            {
                renderer.material = mat;
                Debug.Log($"[Model3DManager] 새 Material 강제 생성: {mat.shader.name}");
            }
            return mat;
        }

        // 기존 Material 확인
        if (renderer.sharedMaterials != null && renderer.sharedMaterials.Length > entry.materialIndex)
        {
            var sharedMat = renderer.sharedMaterials[entry.materialIndex];
            if (sharedMat != null)
            {
                // 인스턴스화된 Material 사용
                mat = renderer.materials[entry.materialIndex];
                Debug.Log($"[Model3DManager] 기존 Material 사용: {mat.name}");
                return mat;
            }
        }

        // Material이 없으면 자동 생성
        mat = CreateDefaultMaterial(entry.qrText, entry);
        if (mat != null)
        {
            renderer.material = mat;
            Debug.Log($"[Model3DManager] Material 자동 생성: {mat.shader.name}");
        }

        return mat;
    }

    /// <summary>
    /// 기본 Material 생성 (텍스처 매핑용)
    /// </summary>
    private Material CreateDefaultMaterial(string name, Model3DEntry entry = null)
    {
        Shader shader = defaultShader;

        // 정면 투영 모드면 전용 쉐이더 사용
        if (entry != null && entry.projectionType == ProjectionType.Front)
        {
            shader = Shader.Find("LiveSketch/FrontProjectionUnlit");
            if (shader != null)
            {
                var frontMat = new Material(shader);
                frontMat.name = $"{name}_FrontMaterial";
                Debug.Log($"[Model3DManager] 정면 투영 쉐이더 생성");
                return frontMat;
            }
            else
            {
                Debug.LogWarning("[Model3DManager] FrontProjectionUnlit 쉐이더를 찾을 수 없음 → 기본 쉐이더 사용");
            }
        }

        // BakedFront 모드면 전용 쉐이더 사용 (UV 기반 + 측면 페이드)
        if (entry != null && entry.projectionType == ProjectionType.BakedFront)
        {
            shader = Shader.Find("LiveSketch/BakedFrontUnlit");
            if (shader != null)
            {
                var bakedMat = new Material(shader);
                bakedMat.name = $"{name}_BakedFrontMaterial";
                Debug.Log($"[Model3DManager] BakedFront 쉐이더 생성 (측면 페이드 포함)");
                return bakedMat;
            }
            else
            {
                Debug.LogWarning("[Model3DManager] BakedFrontUnlit 쉐이더를 찾을 수 없음 → 기본 쉐이더 사용");
            }
        }

        // 쉐이더 미지정 시 URP 또는 Standard 사용
        if (shader == null)
        {
            // URP Unlit 시도
            shader = Shader.Find("Universal Render Pipeline/Unlit");

            // URP 없으면 Standard
            if (shader == null)
                shader = Shader.Find("Standard");

            // 그래도 없으면 Unlit/Texture
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
        }

        if (shader == null)
        {
            Debug.LogError("[Model3DManager] 사용 가능한 쉐이더를 찾을 수 없음");
            return null;
        }

        var mat = new Material(shader);
        mat.name = $"{name}_AutoMaterial";

        // URP Unlit은 _BaseMap, Standard는 _MainTex
        // 나중에 ApplyTexture에서 적절한 프로퍼티에 적용됨

        return mat;
    }

    private Model3DEntry FindEntry(string qrText)
    {
        string search = ignoreCase ? qrText.ToLower() : qrText;

        foreach (var entry in modelEntries)
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

    public int EntryCount => modelEntries.Count;
}
