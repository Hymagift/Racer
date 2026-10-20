using UnityEngine;

/// <summary>
/// Habille le modèle 3D du cône avec le shader Racer/TrafficCone (corps noir, contour et bandes orange fluo).
/// À mettre sur la racine du prefab Cone, le modèle (cone.fbx) étant un enfant.
/// Donne au shader l'axe et la hauteur du cône, calculés à partir des bounds du mesh :
/// les bandes restent au bon endroit quelle que soit l'orientation ou l'échelle d'export du FBX.
/// </summary>
public class ConeLook : MonoBehaviour
{
    [Tooltip("Material qui utilise le shader Racer/TrafficCone")]
    public Material coneMaterial;

    static readonly int HeightAxisId = Shader.PropertyToID("_HeightAxis");
    static readonly int HeightRangeId = Shader.PropertyToID("_HeightRange");

    void Awake()
    {
        if (coneMaterial == null) return;

        var block = new MaterialPropertyBlock();
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>(true))
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null || mf.sharedMesh == null) continue;

            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = coneMaterial;
            r.sharedMaterials = mats;

            // Le "haut" du prefab exprimé dans le repère du mesh (le modèle ne bouge pas par rapport à la racine)
            Vector3 axis = mf.transform.InverseTransformDirection(transform.up).normalized;
            Bounds b = mf.sharedMesh.bounds;
            float center = Vector3.Dot(b.center, axis);
            Vector3 absAxis = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            float extent = Vector3.Dot(b.extents, absAxis);

            r.GetPropertyBlock(block);
            block.SetVector(HeightAxisId, axis);
            block.SetVector(HeightRangeId, new Vector4(center - extent, center + extent, 0f, 0f));
            r.SetPropertyBlock(block);
        }
    }
}
