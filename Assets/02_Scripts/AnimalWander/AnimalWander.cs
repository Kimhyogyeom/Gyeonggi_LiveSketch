using UnityEngine;

/// <summary>
/// 동물 모델이 카메라 시야 범위 내에서 배회합니다.
/// X 범위를 벗어나지 않고 앞뒤로 자유롭게 이동합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class AnimalWander : MonoBehaviour
{
    [Header("=== 이동 설정 ===")]
    [Tooltip("이동 속도 (Manager에서 자동 설정)")]
    [SerializeField] private float moveSpeed = 2f;

    [Tooltip("회전 속도 (Manager에서 자동 설정)")]
    [SerializeField] private float rotationSpeed = 120f;

    [Header("=== 범위 설정 (자동) ===")]
    [Tooltip("AnimalModelManager 참조 (자동 설정)")]
    [SerializeField] private AnimalModelManager modelManager;

    private Rigidbody _rb;
    private Vector3 _targetPosition;
    private Camera _camera;

    // 사다리꼴 범위 (Near = 카메라 가까이, Far = 카메라 멀리)
    private float _xMinNear, _xMaxNear, _xMinFar, _xMaxFar;
    private float _zMin, _zMax;

    private bool _initialized = false;

    private void Awake()
    {
        InitializeRigidbody();
    }

    private void InitializeRigidbody()
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.isKinematic = false;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
    }

    private void Start()
    {
        Initialize();
    }

    private void OnEnable()
    {
        // Rigidbody 확인
        InitializeRigidbody();

        // SetActive(true)로 다시 활성화될 때 재초기화
        if (!_initialized)
            Initialize();
        else
            SetNewRandomTarget();
    }

    private void Initialize()
    {
        // Rigidbody 다시 확인
        if (_rb == null)
            InitializeRigidbody();

        // AnimalModelManager 찾기
        if (modelManager == null)
            modelManager = FindFirstObjectByType<AnimalModelManager>();

        if (modelManager != null)
        {
            _camera = modelManager.GetSpawnCamera();

            // 사다리꼴 범위 가져오기
            _xMinNear = modelManager.GetSpawnRangeXMinNear();
            _xMaxNear = modelManager.GetSpawnRangeXMaxNear();
            _xMinFar = modelManager.GetSpawnRangeXMinFar();
            _xMaxFar = modelManager.GetSpawnRangeXMaxFar();

            _zMin = modelManager.GetSpawnDistanceZMin();
            _zMax = modelManager.GetSpawnDistanceZMax();

            // 배회 속도 가져오기
            moveSpeed = modelManager.GetWanderMoveSpeed();
            rotationSpeed = modelManager.GetWanderRotationSpeed();
        }
        else
        {
            _camera = Camera.main;
            _xMinNear = -3f;
            _xMaxNear = 3f;
            _xMinFar = -8f;
            _xMaxFar = 8f;
            _zMin = 3f;
            _zMax = 10f;
        }

        _initialized = true;
        SetNewRandomTarget();

        Debug.Log($"[AnimalWander] 초기화 완료 - Camera: {(_camera != null ? _camera.name : "null")}, Rigidbody: {(_rb != null ? "OK" : "NULL")}, Target: {_targetPosition}");
    }

    private void Update()
    {
        if (_camera == null)
        {
            Debug.LogWarning("[AnimalWander] Update: _camera가 null!");
            return;
        }

        // 범위 벗어났는지 체크
        if (IsOutOfBounds())
        {
            // 범위 안쪽으로 새 목표 설정
            SetNewRandomTarget();
        }

        // 목표에 도달하면 새 목표
        float distanceToTarget = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(_targetPosition.x, 0, _targetPosition.z)
        );

        if (distanceToTarget < 1f)
        {
            SetNewRandomTarget();
        }

        // 이동
        MoveTowardsTarget();
        AdjustHeightToGround();
    }

    private bool IsOutOfBounds()
    {
        if (_camera == null) return false;

        // 카메라 로컬 좌표로 변환
        Vector3 localPos = _camera.transform.InverseTransformPoint(transform.position);

        // Z 범위 체크
        if (localPos.z < _zMin || localPos.z > _zMax)
            return true;

        // Z 비율에 따른 X 범위 계산 (사다리꼴)
        float zRatio = Mathf.Clamp01((localPos.z - _zMin) / (_zMax - _zMin));
        float xMin = Mathf.Lerp(_xMinNear, _xMinFar, zRatio);
        float xMax = Mathf.Lerp(_xMaxNear, _xMaxFar, zRatio);

        // X 범위 체크
        if (localPos.x < xMin || localPos.x > xMax)
            return true;

        return false;
    }

    private float _debugTimer = 0f;

    private void MoveTowardsTarget()
    {
        Vector3 direction = _targetPosition - transform.position;
        direction.y = 0;

        // 1초마다 디버그 로그
        _debugTimer += Time.deltaTime;
        if (_debugTimer > 1f)
        {
            _debugTimer = 0f;
            Debug.Log($"[AnimalWander] 이동 중 - 현재: {transform.position}, 목표: {_targetPosition}, 거리: {direction.magnitude:F2}");
        }

        if (direction.magnitude > 0.1f)
        {
            // 회전 (목표 방향으로)
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            // 이동 (Transform 직접 이동)
            Vector3 move = transform.forward * moveSpeed * Time.deltaTime;
            transform.position += move;
        }
    }

    private void AdjustHeightToGround()
    {
        Vector3 pos = transform.position;

        // Raycast로 지면 높이 찾기
        if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
        {
            pos.y = hit.point.y;
            _rb.MovePosition(pos);
        }
    }

    private void SetNewRandomTarget()
    {
        if (_camera == null) return;

        // Z 먼저 결정
        float randomZ = Random.Range(_zMin + 0.5f, _zMax - 0.5f);

        // Z 비율에 따른 X 범위 계산 (사다리꼴)
        float zRatio = (randomZ - _zMin) / (_zMax - _zMin);
        float xMin = Mathf.Lerp(_xMinNear, _xMinFar, zRatio);
        float xMax = Mathf.Lerp(_xMaxNear, _xMaxFar, zRatio);

        float randomX = Random.Range(xMin + 0.5f, xMax - 0.5f);

        _targetPosition = _camera.transform.position
            + _camera.transform.forward * randomZ
            + _camera.transform.right * randomX;

        // 지면 높이 맞추기
        if (Physics.Raycast(_targetPosition + Vector3.up * 500f, Vector3.down, out RaycastHit hit, 1000f))
        {
            _targetPosition.y = hit.point.y;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // 목표 위치 표시
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(_targetPosition, 0.5f);
        Gizmos.DrawLine(transform.position, _targetPosition);
    }
#endif
}
