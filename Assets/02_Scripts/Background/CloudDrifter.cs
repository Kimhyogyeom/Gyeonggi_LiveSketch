using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 구름 스프라이트들을 화면 왼쪽→오른쪽으로 천천히 흘려보냄.
/// 화면 밖으로 나가면 반대편으로 돌아와 반복.
/// </summary>
public class CloudDrifter : MonoBehaviour
{
    [Header("=== 구름 스프라이트 ===")]
    [Tooltip("사용할 구름 스프라이트들 (여러 장 등록 가능)")]
    [SerializeField] private Sprite[] cloudSprites;

    [Header("=== 구름 개수 / 크기 ===")]
    [Tooltip("동시에 떠있는 구름 수")]
    [SerializeField] private int cloudCount = 6;

    [Tooltip("구름 최소 스케일")]
    [SerializeField] private float scaleMin = 1f;

    [Tooltip("구름 최대 스케일")]
    [SerializeField] private float scaleMax = 2.5f;

    [Header("=== 이동 속도 ===")]
    [Tooltip("구름 최소 이동 속도 (월드 유닛/초)")]
    [SerializeField] private float speedMin = 0.3f;

    [Tooltip("구름 최대 이동 속도")]
    [SerializeField] private float speedMax = 1.2f;

    [Header("=== 화면 범위 ===")]
    [Tooltip("구름이 떠다닐 Y 최소 (월드 좌표)")]
    [SerializeField] private float yMin = -12.9f;

    [Tooltip("구름이 떠다닐 Y 최대 (월드 좌표)")]
    [SerializeField] private float yMax = 12.9f;

    [Tooltip("화면 가로 반폭 + 여유 / X 간격은 xBound×2÷cloudCount로 자동 계산됨")]
    [SerializeField] private float xBound = 19f;

    [Header("=== 생성 간격 ===")]
    [Tooltip("구름 하나씩 생성될 때 최소 대기 시간 (초)")]
    [SerializeField] private float spawnIntervalMin = 3f;

    [Tooltip("구름 하나씩 생성될 때 최대 대기 시간 (초)")]
    [SerializeField] private float spawnIntervalMax = 5f;

    [Header("=== 투명도 ===")]
    [Tooltip("구름 최소 알파")]
    [SerializeField] private float alphaMin = 0.3f;

    [Tooltip("구름 최대 알파")]
    [SerializeField] private float alphaMax = 0.7f;

    [Header("=== Z 레이어 ===")]
    [Tooltip("구름 렌더링 정렬 레이어 이름")]
    [SerializeField] private string sortingLayerName = "Default";

    [Tooltip("구름 정렬 오더 (배경보다 앞, 캐릭터보다 뒤)")]
    [SerializeField] private int sortingOrder = 1;

    private class Cloud
    {
        public GameObject go;
        public SpriteRenderer sr;
        public float speed;
    }

    private readonly List<Cloud> _clouds = new List<Cloud>();

    void Start()
    {
        if (cloudSprites == null || cloudSprites.Length == 0)
        {
            Debug.LogWarning("[CloudDrifter] 구름 스프라이트가 없습니다.");
            return;
        }

        StartCoroutine(SpawnLoop());
    }

    IEnumerator SpawnLoop()
    {
        int spawned = 0;
        while (true)
        {
            // 최대 개수 미만일 때만 생성
            if (spawned < cloudCount)
            {
                SpawnCloud();
                spawned++;
            }

            yield return new WaitForSeconds(Random.Range(spawnIntervalMin, spawnIntervalMax));
        }
    }

    void Update()
    {
        foreach (var cloud in _clouds)
        {
            if (cloud.go == null) continue;

            cloud.go.transform.position += Vector3.right * cloud.speed * Time.deltaTime;

            // 화면 오른쪽 밖으로 나가면 왼쪽 바깥으로 리셋
            if (cloud.go.transform.position.x > xBound)
                ResetCloud(cloud);
        }
    }

    void SpawnCloud()
    {
        float scale = Random.Range(scaleMin, scaleMax);
        var go = new GameObject("Cloud");
        go.transform.SetParent(transform);
        go.transform.position = new Vector3(-xBound - scale, Random.Range(yMin, yMax), 0f);
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = cloudSprites[Random.Range(0, cloudSprites.Length)];
        sr.color = new Color(1f, 1f, 1f, Random.Range(alphaMin, alphaMax));
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;

        _clouds.Add(new Cloud { go = go, sr = sr, speed = Random.Range(speedMin, speedMax) });
    }

    void ResetCloud(Cloud cloud)
    {
        float scale = Random.Range(scaleMin, scaleMax);
        cloud.go.transform.localScale = Vector3.one * scale;
        cloud.go.transform.position = new Vector3(-xBound - scale, Random.Range(yMin, yMax), 0f);
        cloud.speed = Random.Range(speedMin, speedMax);
        cloud.sr.sprite = cloudSprites[Random.Range(0, cloudSprites.Length)];
        cloud.sr.color = new Color(1f, 1f, 1f, Random.Range(alphaMin, alphaMax));
    }
}
