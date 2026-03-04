using UnityEngine;

public class JumpBounce : MonoBehaviour
{
    [Header("Jump Settings")]
    [Tooltip("점프 초기 속도 (위로 튀어오르는 힘)")]
    public float jumpForce = 5f;

    [Tooltip("중력 가속도 (클수록 빨리 떨어짐)")]
    public float gravity = 15f;

    [Tooltip("바닥 기준 Y 오프셋 (시작 위치 기준)")]
    public float groundOffset = 0f;

    [Tooltip("인스턴스별 랜덤 편차 비율 (0.1 = ±10%)")]
    [Range(0f, 0.3f)]
    public float randomVariance = 0.1f;

    private float startY;
    private float velocity;
    private float actualJumpForce;
    private float actualGravity;
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
        // 인스턴스마다 점프력/중력에 랜덤 편차 → 주기가 달라져서 동기화 방지
        float variation = 1f + Random.Range(-randomVariance, randomVariance);
        actualJumpForce = jumpForce * variation;
        actualGravity = gravity * variation;
        startY = transform.localPosition.y + groundOffset;
        velocity = actualJumpForce;
        if (animator != null) animator.SetTrigger("Jump");
    }

    void Update()
    {
        velocity -= actualGravity * Time.deltaTime;

        Vector3 pos = transform.localPosition;
        pos.y += velocity * Time.deltaTime;

        // 바닥에 닿으면 다시 점프
        if (pos.y <= startY)
        {
            pos.y = startY;
            velocity = actualJumpForce;
            if (animator != null) animator.SetTrigger("Jump");
        }

        transform.localPosition = pos;
    }
}
