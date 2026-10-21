using UnityEngine;

/// <summary>
/// Remplace le modèle 3D d'une voiture (DriftCar.body) par celui de la voiture équipée dans le Garage (CarCatalog).
/// Appelé par DriftCar au démarrage (partie ET démo du menu), et par le Garage quand on équipe une voiture.
///
/// Chaque modèle est calé automatiquement sur la boîte de collision de la voiture : même longueur, posé au sol
/// (le bas du modèle = le bas de la boîte), centré. Peu importe l'échelle ou l'origine du modèle exporté.
/// Si un modèle est à l'envers ou trop petit / grand, corrige "modelYaw" / "modelScale" dans le catalogue.
/// Les roues du nouveau modèle sont retrouvées par CarWheels (os nommés "wheel"...).
/// </summary>
public static class CarModelSwap
{
    const string Prefix = "Modèle : ";

    /// <summary>Met sur la voiture le modèle de la voiture équipée.</summary>
    public static void ApplyEquipped(DriftCar car)
    {
        var entry = CarCatalog.Load().Equipped;
        if (entry != null) Apply(car, entry);
    }

    /// <summary>Met à jour toutes les voitures présentes (ex : la démo derrière le menu après "Équiper").</summary>
    public static void ApplyEquippedToAll()
    {
        foreach (var car in Object.FindObjectsByType<DriftCar>(FindObjectsSortMode.None))
            ApplyEquipped(car);
    }

    public static void Apply(DriftCar car, CarCatalog.Car entry)
    {
        if (car == null || entry == null || entry.model == null) return; // pas de modèle : on garde celui de la scène
        Transform old = car.body;
        if (old != null && old.name == Prefix + entry.id) return;      // déjà en place

        Transform root = car.transform;
        Bounds target = Footprint(car, old);

        // Support : garde la rotation propre du modèle importé (ex : -90° des exports Blender)
        var holder = new GameObject(Prefix + entry.id).transform;
        holder.SetParent(old != null && old.parent != null ? old.parent : root, false);
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one;

        var model = Object.Instantiate(entry.model, holder, false);
        model.name = entry.model.name;
        // Le modèle n'est qu'un visuel : pas de collisions ni de physique en double
        foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (var r in model.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(r);

        // Orientation : la voiture regarde vers +Z. Si le modèle est plus large que long, on le tourne d'un quart de tour.
        holder.localRotation = Quaternion.Euler(0f, entry.modelYaw, 0f);
        Bounds b = LocalBounds(root, holder);
        if (b.size.x > b.size.z * 1.15f)
        {
            holder.localRotation = Quaternion.Euler(0f, entry.modelYaw + 90f, 0f);
            b = LocalBounds(root, holder);
        }

        // Taille : même longueur que la boîte de collision (x modelScale)
        float scale = b.size.z > 0.001f ? target.size.z / b.size.z * Mathf.Max(0.05f, entry.modelScale) : 1f;
        holder.localScale = Vector3.one * scale;
        b = LocalBounds(root, holder);

        // Position : centré sur la boîte, le bas du modèle au bas de la boîte (les roues touchent la route)
        Vector3 offset = new Vector3(target.center.x - b.center.x, target.min.y - b.min.y, target.center.z - b.center.z);
        holder.localPosition += InParent(root, holder.parent, offset);

        if (old != null)
        {
            // Détaché tout de suite (Destroy n'agit qu'en fin de frame) : CarWheels ne doit plus trouver ses roues
            old.SetParent(null, false);
            old.gameObject.SetActive(false);
            Object.Destroy(old.gameObject);
        }
        car.SetBody(holder);

        var wheels = car.GetComponent<CarWheels>();
        if (wheels != null) wheels.Rebind();
    }

    // Zone que doit occuper le modèle, dans le repère de la voiture : la boîte de collision, sinon l'ancien modèle
    static Bounds Footprint(DriftCar car, Transform old)
    {
        foreach (var box in car.GetComponents<BoxCollider>())
            if (!box.isTrigger) return new Bounds(box.center, box.size);
        if (old != null)
        {
            Bounds b = LocalBounds(car.transform, old);
            if (b.size.sqrMagnitude > 0.0001f) return b;
        }
        return new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 1.2f, 4.2f));
    }

    // Boîte englobante des meshs de "part", exprimée dans le repère de "space"
    static Bounds LocalBounds(Transform space, Transform part)
    {
        bool any = false;
        var result = new Bounds();
        foreach (var r in part.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            Bounds lb;
            Matrix4x4 toSpace;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                lb = smr.sharedMesh.bounds;
                toSpace = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            }
            else if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
            {
                lb = mf.sharedMesh.bounds;
                toSpace = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
            }
            else continue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = toSpace.MultiplyPoint3x4(corner);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    // Convertit un déplacement exprimé dans le repère de la voiture en déplacement dans le repère du parent du modèle
    static Vector3 InParent(Transform carRoot, Transform parent, Vector3 offset)
    {
        if (parent == null || parent == carRoot) return offset;
        return parent.InverseTransformVector(carRoot.TransformVector(offset));
    }
}
