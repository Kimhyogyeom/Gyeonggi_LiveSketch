using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class ApplySidePlanarBounds : MonoBehaviour
{
    [SerializeField] private string minY = "_MinY";
    [SerializeField] private string maxY = "_MaxY";
    [SerializeField] private string minZ = "_MinZ";
    [SerializeField] private string maxZ = "_MaxZ";

    void Start()
    {
        var r = GetComponent<Renderer>();
        // Mesh bounds는 로컬 기준이 필요해서 MeshFilter/SkinnedMeshRenderer에서 가져오는 게 더 정확함
        Bounds b;

        var smr = r as SkinnedMeshRenderer;
        if (smr != null && smr.sharedMesh != null)
            b = smr.sharedMesh.bounds;
        else
        {
            var mf = GetComponent<MeshFilter>();
            b = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        }

        var mat = r.material;
        mat.SetFloat(minY, b.min.y);
        mat.SetFloat(maxY, b.max.y);
        mat.SetFloat(minZ, b.min.z);
        mat.SetFloat(maxZ, b.max.z);
    }
}
