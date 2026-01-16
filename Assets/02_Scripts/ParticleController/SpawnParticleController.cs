using UnityEngine;
using System.Collections;

/// <summary>
/// 모델 스폰 시 파티클 효과
/// 파티클 재생 → 타이머 완료 → 파티클 사라짐 → 모델 등장
/// </summary>
public class SpawnParticleController : MonoBehaviour
{
    [Header("=== 스폰 파티클 ===")]
    [Tooltip("모델 등장 전 재생할 파티클 프리팹")]
    [SerializeField] private GameObject spawnParticlePrefab;

    [Tooltip("파티클 재생 시간 (초) - 이 시간 후 모델 등장")]
    [Range(0.5f, 10f)]
    [SerializeField] private float spawnDelay = 2f;

    [Header("=== 등장 파티클 (선택) ===")]
    [Tooltip("모델 등장 시 추가 파티클 (없으면 비워두기)")]
    [SerializeField] private GameObject appearParticlePrefab;

    [Tooltip("등장 파티클 재생 시간")]
    [Range(0.5f, 5f)]
    [SerializeField] private float appearDuration = 1f;

    [Header("=== 위치 설정 ===")]
    [Tooltip("파티클 위치 오프셋")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Header("=== 연결 ===")]
    [Tooltip("AnimalModelManager 연결")]
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

        // 파티클 프리팹 없으면 바로 모델 표시
        if (spawnParticlePrefab == null)
        {
            model.SetActive(true);
            return;
        }

        Vector3 spawnPos = model.transform.position + positionOffset;

        // 모델 숨기기
        model.SetActive(false);

        // 파티클 시퀀스 시작
        StartCoroutine(PlaySpawnSequence(spawnPos, model));
    }

    private IEnumerator PlaySpawnSequence(Vector3 position, GameObject model)
    {
        // 1. 스폰 파티클 재생
        GameObject spawnParticle = SpawnParticle(spawnParticlePrefab, position);

        // 2. 대기
        yield return new WaitForSeconds(spawnDelay);

        // 3. 스폰 파티클 삭제
        if (spawnParticle != null)
            Destroy(spawnParticle);

        // 4. 모델 등장!
        if (model != null)
            model.SetActive(true);

        // 5. 등장 파티클 (선택)
        if (appearParticlePrefab != null)
        {
            GameObject appearParticle = SpawnParticle(appearParticlePrefab, position);

            yield return new WaitForSeconds(appearDuration);

            if (appearParticle != null)
                Destroy(appearParticle);
        }
    }

    private GameObject SpawnParticle(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return null;

        GameObject instance = Instantiate(prefab, position, Quaternion.identity);

        // ParticleSystem 자동 재생
        var ps = instance.GetComponent<ParticleSystem>();
        if (ps != null && !ps.isPlaying)
            ps.Play();

        return instance;
    }
}
