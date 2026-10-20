using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anime les roues de la voiture (modèle riggé ou meshs séparés) à partir de DriftCar :
/// - les roues AVANT braquent à gauche / à droite comme la direction (contre-braquage du drift compris) ;
/// - TOUTES les roues tournent sur leur axe selon la vitesse de la voiture (qui fait défiler la route).
///
/// Mise en place : ajoute ce script sur la voiture (le GameObject qui a DriftCar).
/// Laisse "Wheels" vide : les os / objets dont le nom contient "wheel", "roue", "tire" ou "pneu" sont trouvés
/// automatiquement, et ceux situés devant le centre de la voiture braquent. Sinon, remplis la liste à la main.
///
/// Les rotations sont appliquées dans LateUpdate, PAR-DESSUS la pose d'origine de chaque roue :
/// ça marche quelle que soit l'orientation des os (Blender, Maya...) et même avec un Animator sur le rig.
/// </summary>
public class CarWheels : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        public Transform wheel;
        [Tooltip("Roue directrice (avant) : elle braque")]
        public bool steer;
        [Tooltip("Inverse le sens de rotation (si la roue tourne à l'envers, ex : mesh en miroir)")]
        public bool invertSpin;

        [System.NonSerialized] public Quaternion baseRot;
        [System.NonSerialized] public Vector3 axle;      // axe de la roue (dans son repère d'origine)
        [System.NonSerialized] public Vector3 up;        // vertical de la voiture (dans le repère du parent de la roue)
        [System.NonSerialized] public float radius;
        [System.NonSerialized] public float spin;        // angle de rotation actuel (degrés)
    }

    [Header("Références")]
    [Tooltip("Trouvé automatiquement sur ce GameObject ou ses parents")]
    public DriftCar car;
    [Tooltip("Vide = recherche automatique par nom dans les enfants")]
    public Wheel[] wheels;

    [Header("Recherche automatique")]
    [Tooltip("Mots cherchés dans le nom des roues (minuscules)")]
    public string[] nameKeywords = { "wheel", "roue", "tire", "pneu" };
    [Tooltip("Noms ignorés (volant, roue de secours...)")]
    public string[] ignoreKeywords = { "steer", "volant", "spare", "secours" };

    [Header("Rotation")]
    [Tooltip("Rayon des roues (m). 0 = calculé depuis le mesh de la roue, sinon 0.35")]
    public float wheelRadius = 0f;

    [Header("Braquage")]
    [Tooltip("Multiplie l'angle de braquage de la voiture (pour l'exagérer ou l'adoucir visuellement)")]
    public float steerMultiplier = 1f;
    [Tooltip("Angle max affiché sur les roues (degrés)")]
    public float maxVisualSteer = 35f;
    [Tooltip("Vitesse à laquelle les roues suivent la direction (plus haut = plus sec)")]
    public float steerSmoothing = 15f;
    [Tooltip("Inverse le sens du braquage si les roues tournent du mauvais côté")]
    public bool invertSteer;

    float steerAngle;

    void Awake()
    {
        if (car == null) car = GetComponentInParent<DriftCar>();
        if (car == null)
        {
            Debug.LogError("[CarWheels] Aucun DriftCar trouvé sur la voiture.", this);
            enabled = false;
            return;
        }

        if (wheels == null || wheels.Length == 0) wheels = FindWheels();
        if (wheels.Length == 0)
            Debug.LogWarning("[CarWheels] Aucune roue trouvée : remplis la liste 'Wheels' à la main.", this);

        Transform carT = car.transform;
        foreach (Wheel w in wheels)
        {
            if (w.wheel == null) continue;
            w.baseRot = w.wheel.localRotation;
            // Axe de la roue = la droite de la voiture, exprimée dans le repère d'origine de la roue
            w.axle = Quaternion.Inverse(w.wheel.rotation) * carT.right;
            Transform parent = w.wheel.parent != null ? w.wheel.parent : carT;
            w.up = Quaternion.Inverse(parent.rotation) * carT.up;
            w.radius = wheelRadius > 0f ? wheelRadius : MeasureRadius(w.wheel);
        }
    }

    // Cherche les roues par leur nom ; celles devant le centre des roues sont les roues avant
    Wheel[] FindWheels()
    {
        var found = new List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t == transform || !Matches(t.name)) continue;
            // Un os "Wheel_FL" et son mesh enfant "Wheel_FL_mesh" : on ne garde que le parent (sinon double rotation)
            bool parentFound = false;
            foreach (Transform f in found)
                if (t.IsChildOf(f)) { parentFound = true; break; }
            if (!parentFound) found.Add(t);
        }

        float avgZ = 0f;
        foreach (Transform t in found) avgZ += transform.InverseTransformPoint(t.position).z;
        if (found.Count > 0) avgZ /= found.Count;

        var result = new Wheel[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            float z = transform.InverseTransformPoint(found[i].position).z;
            result[i] = new Wheel { wheel = found[i], steer = z > avgZ + 0.01f };
            Debug.Log($"[CarWheels] Roue trouvée : {found[i].name} ({(result[i].steer ? "avant, braque" : "arrière")})", found[i]);
        }
        return result;
    }

    bool Matches(string objName)
    {
        string n = objName.ToLowerInvariant();
        foreach (string k in ignoreKeywords)
            if (!string.IsNullOrEmpty(k) && n.Contains(k)) return false;
        foreach (string k in nameKeywords)
            if (!string.IsNullOrEmpty(k) && n.Contains(k)) return true;
        return false;
    }

    // Rayon = demi-hauteur du mesh de la roue (un os sans mesh n'a pas de taille : valeur par défaut)
    static float MeasureRadius(Transform wheel)
    {
        var r = wheel.GetComponentInChildren<Renderer>();
        if (r == null || r is SkinnedMeshRenderer) return 0.35f;
        float h = r.bounds.extents.y;
        return h > 0.05f ? h : 0.35f;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // Braquage lissé (la direction est calculée en FixedUpdate, on évite les à-coups à l'écran)
        float target = Mathf.Clamp(car.SteerAngle * steerMultiplier, -maxVisualSteer, maxVisualSteer);
        if (invertSteer) target = -target;
        steerAngle = Mathf.Lerp(steerAngle, target, 1f - Mathf.Exp(-steerSmoothing * dt));

        // Vitesse au sol : la voiture ne bouge pas en Z, c'est sa vitesse "réelle" qui fait défiler la route
        float speed = car.ForwardSpeed;

        foreach (Wheel w in wheels)
        {
            if (w.wheel == null) continue;

            float spinRate = speed / Mathf.Max(0.05f, w.radius) * Mathf.Rad2Deg;
            w.spin = Mathf.Repeat(w.spin + (w.invertSpin ? -spinRate : spinRate) * dt, 360f);

            Quaternion steer = w.steer ? Quaternion.AngleAxis(steerAngle, w.up) : Quaternion.identity;
            w.wheel.localRotation = steer * w.baseRot * Quaternion.AngleAxis(w.spin, w.axle);
        }
    }
}
