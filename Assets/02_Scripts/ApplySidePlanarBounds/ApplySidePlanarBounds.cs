using UnityEngine;

/// <summary>
/// 메시 바운드를 셰이더 파라미터로 자동 설정합니다.
/// SidePlanarUnlitURP 셰이더용.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ApplySidePlanarBounds : MonoBehaviour
{
    [Header("셰이더 파라미터 이름")]
    [SerializeField] private string minYParam = "_MinY";
    [SerializeField] private string maxYParam = "_MaxY";
    [SerializeField] private string minZParam = "_MinZ";
    [SerializeField] private string maxZParam = "_MaxZ";

    void Start()
    {
        var renderer = GetComponent<Renderer>();
        Bounds bounds = GetMeshBounds(renderer);

        var mat = renderer.material;
        mat.SetFloat(minYParam, bounds.min.y);
        mat.SetFloat(maxYParam, bounds.max.y);
        mat.SetFloat(minZParam, bounds.min.z);
        mat.SetFloat(maxZParam, bounds.max.z);

        Debug.Log($"[ApplySidePlanarBounds] Y: {bounds.min.y}~{bounds.max.y}, Z: {bounds.min.z}~{bounds.max.z}");
    }

    private Bounds GetMeshBounds(Renderer renderer)
    {
        // SkinnedMeshRenderer
        var smr = renderer as SkinnedMeshRenderer;
        if (smr != null && smr.sharedMesh != null)
            return smr.sharedMesh.bounds;

        // MeshFilter
        var mf = GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            return mf.sharedMesh.bounds;

        // 기본값
        return new Bounds(Vector3.zero, Vector3.one);
    }
}
