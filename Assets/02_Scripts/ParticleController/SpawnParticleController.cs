using UnityEngine;
using System.Collections;

/// <summary>
/// 모델 스폰 시 파티클 순차 재생
/// 파티클 A (5초) → 파티클 B (2초)
/// </summary>
public class SpawnParticleController : MonoBehaviour
{
    [Header("파티클 프리팹 (3~4개 등록)")]
    [SerializeField] private GameObject[] particleA_Prefabs;
    [SerializeField] private GameObject[] particleB_Prefabs;

    [Header("재생 시간")]
    [SerializeField] private float particleA_Duration = 5f;
    [SerializeField] private float particleB_Duration = 2f;

    [Header("위치 설정")]
    [SerializeField] private bool useModelPosition = true;
    [SerializeField] private Vector3 fixedPosition = Vector3.zero;
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Header("모델 매니저 연결")]
    [SerializeField] private AnimalModelManager modelManager;

    private void OnEnable()
    {
        if (modelManager != null)
            modelManager.OnModelSpawned += HandleModelSpawned;
    }

    private void OnDisable()
    {
        if (modelManager != null)
            modelManager.OnModelSpawned -= HandleModelSpawned;
    }

    private void HandleModelSpawned(GameObject model, Renderer renderer)
    {
        if (model == null) return;

        Vector3 spawnPos;
        if (useModelPosition)
            spawnPos = model.transform.position + positionOffset;
        else
            spawnPos = fixedPosition + positionOffset;

        // 모델 숨기기 (파티클 A 동안)
        model.SetActive(false);

        StartCoroutine(PlayParticleSequence(spawnPos, model));
    }

    private IEnumerator PlayParticleSequence(Vector3 position, GameObject model)
    {
        // 파티클 A 재생 (랜덤 1개)
        GameObject activeA = SpawnRandomParticle(particleA_Prefabs, position);

        yield return new WaitForSeconds(particleA_Duration);

        // 파티클 A 정지 및 삭제
        if (activeA != null)
            Destroy(activeA);

        // 모델 나타나기 + 파티클 B 재생
        if (model != null)
            model.SetActive(true);

        GameObject activeB = SpawnRandomParticle(particleB_Prefabs, position);

        yield return new WaitForSeconds(particleB_Duration);

        // 파티클 B 정지 및 삭제
        if (activeB != null)
            Destroy(activeB);
    }

    private GameObject SpawnRandomParticle(GameObject[] prefabs, Vector3 position)
    {
        if (prefabs == null || prefabs.Length == 0) return null;

        // 랜덤 인덱스 선택
        int randomIndex = Random.Range(0, prefabs.Length);

        if (prefabs[randomIndex] == null) return null;

        GameObject instance = Instantiate(prefabs[randomIndex], position, Quaternion.identity);

        // ParticleSystem 자동 재생
        var ps = instance.GetComponent<ParticleSystem>();
        if (ps != null && !ps.isPlaying)
            ps.Play();

        return instance;
    }
}
