using UnityEngine;

/// <summary>
/// 스케일 펄스: 지정한 크기 사이를 자연스럽게 왕복
/// </summary>
public class ScalePulse : MonoBehaviour
{
    [Header("스케일 범위")]
    [Tooltip("최소 스케일")]
    [SerializeField] private float scaleMin = 1f;

    [Tooltip("최대 스케일")]
    [SerializeField] private float scaleMax = 1.1f;

    [Header("설정")]
    [Tooltip("펄스 속도 (클수록 빠름)")]
    [SerializeField] private float speed = 1f;

    [Tooltip("시작 시 랜덤 오프셋")]
    [SerializeField] private bool randomStartOffset = true;

    private float _time;
    private bool _started;

    void Start()
    {
        if (randomStartOffset)
            _time = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        // SpawnEffect가 아직 붙어있으면 대기
        if (!_started)
        {
            if (GetComponent<SpawnEffect>() != null) return;
            _started = true;
        }

        _time += Time.deltaTime * speed;

        float t = (Mathf.Sin(_time) + 1f) * 0.5f;
        float s = Mathf.Lerp(scaleMin, scaleMax, t);

        transform.localScale = new Vector3(s, s, s);
    }
}
