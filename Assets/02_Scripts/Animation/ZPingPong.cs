using UnityEngine;

/// <summary>
/// Z축 회전 핑퐁
/// Inspector에서 각도 범위와 속도를 설정하면 Z축 기준으로 자동 왕복 회전
/// </summary>
public class ZPingPong : MonoBehaviour
{
    [Header("Z축 회전 범위 (도)")]
    [Tooltip("최소 각도")]
    [SerializeField] private float angleMin = -15f;

    [Tooltip("최대 각도")]
    [SerializeField] private float angleMax = 15f;

    [Header("회전 설정")]
    [Tooltip("왕복 속도 (클수록 빠름)")]
    [SerializeField] private float speed = 1f;

    [Tooltip("시작 시 랜덤 오프셋 (여러 오브젝트가 동시에 안 움직이게)")]
    [SerializeField] private bool randomStartOffset = true;

    private float _time;

    void Start()
    {
        if (randomStartOffset)
            _time = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        _time += Time.deltaTime * speed;

        float t = (Mathf.Sin(_time) + 1f) * 0.5f; // 0~1
        float angle = Mathf.Lerp(angleMin, angleMax, t);

        var rot = transform.localEulerAngles;
        rot.z = angle;
        transform.localEulerAngles = rot;
    }
}
