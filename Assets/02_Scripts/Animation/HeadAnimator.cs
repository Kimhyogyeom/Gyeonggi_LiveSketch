using UnityEngine;

/// <summary>
/// 머리 부분 간단한 애니메이션
/// - 좌우 흔들기 (고개 갸웃)
/// - 통통 뛰기
/// </summary>
public class HeadAnimator : MonoBehaviour
{
    [Header("=== 회전 애니메이션 ===")]
    [Tooltip("좌우 회전 활성화")]
    [SerializeField] private bool enableRotation = true;

    [Tooltip("최대 회전 각도")]
    [Range(0f, 30f)]
    [SerializeField] private float maxRotation = 10f;

    [Tooltip("회전 속도")]
    [Range(0.1f, 5f)]
    [SerializeField] private float rotationSpeed = 1f;

    [Header("=== 상하 움직임 ===")]
    [Tooltip("상하 움직임 활성화")]
    [SerializeField] private bool enableBounce = true;

    [Tooltip("상하 움직임 거리")]
    [Range(0f, 0.5f)]
    [SerializeField] private float bounceAmount = 0.05f;

    [Tooltip("상하 움직임 속도")]
    [Range(0.1f, 5f)]
    [SerializeField] private float bounceSpeed = 2f;

    [Header("=== 스케일 애니메이션 ===")]
    [Tooltip("숨쉬기 효과 활성화")]
    [SerializeField] private bool enableBreathing = false;

    [Tooltip("스케일 변화량")]
    [Range(0f, 0.1f)]
    [SerializeField] private float breathingAmount = 0.02f;

    [Tooltip("숨쉬기 속도")]
    [Range(0.1f, 3f)]
    [SerializeField] private float breathingSpeed = 1f;

    // 초기값 저장
    private Vector3 _initialLocalPos;
    private Quaternion _initialLocalRot;
    private Vector3 _initialLocalScale;
    private float _time;

    void Start()
    {
        _initialLocalPos = transform.localPosition;
        _initialLocalRot = transform.localRotation;
        _initialLocalScale = transform.localScale;
    }

    void Update()
    {
        _time += Time.deltaTime;

        // 회전
        if (enableRotation)
        {
            float rotZ = Mathf.Sin(_time * rotationSpeed) * maxRotation;
            transform.localRotation = _initialLocalRot * Quaternion.Euler(0, 0, rotZ);
        }

        // 상하 움직임
        if (enableBounce)
        {
            float offsetY = Mathf.Sin(_time * bounceSpeed * Mathf.PI) * bounceAmount;
            transform.localPosition = _initialLocalPos + new Vector3(0, offsetY, 0);
        }

        // 숨쉬기 (스케일)
        if (enableBreathing)
        {
            float scale = 1f + Mathf.Sin(_time * breathingSpeed * Mathf.PI) * breathingAmount;
            transform.localScale = new Vector3(
                _initialLocalScale.x * scale,
                _initialLocalScale.y * scale,
                _initialLocalScale.z
            );
        }
    }

    /// <summary>
    /// 애니메이션 리셋
    /// </summary>
    public void ResetAnimation()
    {
        _time = 0;
        transform.localPosition = _initialLocalPos;
        transform.localRotation = _initialLocalRot;
        transform.localScale = _initialLocalScale;
    }

    /// <summary>
    /// 초기값 재설정 (런타임에서 위치가 바뀐 후 호출)
    /// </summary>
    public void RecaptureInitialValues()
    {
        _initialLocalPos = transform.localPosition;
        _initialLocalRot = transform.localRotation;
        _initialLocalScale = transform.localScale;
    }
}
