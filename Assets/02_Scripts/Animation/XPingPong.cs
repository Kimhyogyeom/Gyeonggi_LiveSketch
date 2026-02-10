using UnityEngine;

/// <summary>
/// X축 위치 핑퐁 (스폰 위치 기준 상대 이동)
/// Inspector에서 이동 거리와 속도를 설정하면 스폰된 위치에서 자동 왕복
/// </summary>
public class XPingPong : MonoBehaviour
{
    [Header("X축 이동 범위 (스폰 위치 기준)")]
    [Tooltip("왼쪽 오프셋 (음수)")]
    [SerializeField] private float offsetMin = -3f;

    [Tooltip("오른쪽 오프셋 (양수)")]
    [SerializeField] private float offsetMax = 3f;

    [Header("이동 설정")]
    [Tooltip("왕복 속도 (클수록 빠름)")]
    [SerializeField] private float speed = 1f;

    [Tooltip("시작 시 랜덤 오프셋 (여러 오브젝트가 동시에 안 움직이게)")]
    [SerializeField] private bool randomStartOffset = true;

    private float _time;
    private float _startX;

    void Start()
    {
        _startX = transform.position.x;

        if (randomStartOffset)
            _time = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        _time += Time.deltaTime * speed;

        float t = (Mathf.Sin(_time) + 1f) * 0.5f; // 0~1
        float offset = Mathf.Lerp(offsetMin, offsetMax, t);

        var pos = transform.position;
        pos.x = _startX + offset;
        transform.position = pos;
    }
}
