﻿using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Anime les roues de la voiture (modèle riggé ou meshs séparés) à partir de DriftCar :
/// - les roues AVANT braquent à gauche / à droite comme la direction (contre-braquage du drift compris) ;
/// - TOUTES les roues tournent sur leur axe selon la vitesse de la voiture (qui fait défiler la route).
///
/// Mise en place : ajoute ce script sur la voiture (le GameObject qui a DriftCar).
/// Laisse "Wheels" vide : les os / objets dont le nom contient "wheel", "roue", "tire" ou "pneu" sont trouvés
/// automatiquement. Si un os de roue a un autre os de roue en enfant (ex : 1stCar, wheel_dir.L > wheel_front.L),
/// le parent sert de pivot de direction et l'enfant tourne sur lui-même. Sinon, remplis la liste à la main.
///
/// Les rotations sont appliquées dans LateUpdate, PAR-DESSUS la pose d'origine de chaque os :
/// ça marche quelle que soit l'orientation des os (Blender, Maya...) et même avec un Animator sur le rig.
/// </summary>
public class CarWheels : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        [Tooltip("L'os / l'objet qui tourne sur lui-même quand on avance")]
        public Transform wheel;
        [Tooltip("Roue directrice (avant) : elle braque")]
        public bool steer;
        [Tooltip("Optionnel : l'os qui braque (parent de la roue). Vide = la roue braque elle-même")]
        public Transform steerPivot;
        [Tooltip("Inverse le sens de rotation (si la roue tourne à l'envers, ex : mesh en miroir)")]
        public bool invertSpin;

        [System.NonSerialized] public Quaternion baseRot, pivotBaseRot;
        [System.NonSerialized] public Vector3 axle;      // axe de la roue (dans son repère d'origine)
        [System.NonSerialized] public Vector3 up;        // vertical de la voiture (dans le repère du parent de l'os qui braque)
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
    [Tooltip("Noms ignorés (volant, roue de secours, os de fin de chaîne exportés par Blender...)")]
    public string[] ignoreKeywords = { "steer", "volant", "spare", "secours", "_end" };

    [Header("Rotation")]
    [Tooltip("Rayon des roues (m). 0 = estimé : hauteur du centre de la roue au-dessus du bas de la voiture")]
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

        autoFind = wheels == null || wheels.Length == 0;
        Rebind();
    }

    bool autoFind;

    /// <summary>
    /// Retrouve et prépare les roues. Appelé au démarrage, et par CarModelSwap quand le modèle de la voiture change
    /// (les roues sont alors cherchées dans le nouveau modèle).
    /// </summary>
    public void Rebind()
    {
        if (car == null) return;
        bool missing = wheels == null || System.Array.Exists(wheels, w => w == null || w.wheel == null);
        if (autoFind || missing) wheels = FindWheels();
        if (wheels.Length == 0)
            Debug.Log("[CarWheels] Aucune roue trouvée sur ce modèle : les roues ne tourneront pas.", this);

        Transform carT = car.transform;
        float bottomY = CarBottomY();
        foreach (Wheel w in wheels)
        {
            if (w.wheel == null) continue;
            if (w.steerPivot == w.wheel) w.steerPivot = null;

            w.baseRot = w.wheel.localRotation;
            // Axe de la roue = la droite de la voiture, exprimée dans le repère d'origine de la roue
            w.axle = Quaternion.Inverse(w.wheel.rotation) * carT.right;

            Transform steerT = w.steerPivot != null ? w.steerPivot : w.wheel;
            if (w.steerPivot != null) w.pivotBaseRot = w.steerPivot.localRotation;
            Transform parent = steerT.parent != null ? steerT.parent : carT;
            w.up = Quaternion.Inverse(parent.rotation) * carT.up;

            w.radius = wheelRadius > 0f ? wheelRadius : MeasureRadius(w.wheel, bottomY);
        }
    }

    // Cherche les roues par leur nom
    Wheel[] FindWheels()
    {
        var found = new List<Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t != transform && Matches(t.name)) found.Add(t);

        // Une roue qui a une autre roue en dessous d'elle = pivot de direction (ex : wheel_dir.L > wheel_front.L)
        var pivotOf = new Dictionary<Transform, Transform>();
        var tops = new List<Transform>();
        foreach (Transform t in found)
        {
            Transform p = t.parent, top = null;
            while (p != null && p != transform)
            {
                if (found.Contains(p)) top = p;
                p = p.parent;
            }
            if (top == null) tops.Add(t);
            else if (!pivotOf.ContainsKey(t) && t.parent == top) pivotOf[t] = top;
        }

        var result = new List<Wheel>();
        foreach (Transform t in tops)
        {
            // Pivot : on fait tourner son enfant direct (la vraie roue)
            bool isPivot = false;
            foreach (var kv in pivotOf)
                if (kv.Value == t) { result.Add(new Wheel { wheel = kv.Key, steer = true, steerPivot = t }); isPivot = true; }
            if (!isPivot) result.Add(new Wheel { wheel = t });
        }

        // Pas de pivot dans le rig : les roues devant le centre des roues braquent
        if (pivotOf.Count == 0 && result.Count > 0)
        {
            float avgZ = 0f;
            foreach (Wheel w in result) avgZ += transform.InverseTransformPoint(w.wheel.position).z;
            avgZ /= result.Count;
            foreach (Wheel w in result) w.steer = transform.InverseTransformPoint(w.wheel.position).z > avgZ + 0.01f;
        }

        foreach (Wheel w in result)
            Debug.Log($"[CarWheels] Roue : {w.wheel.name}" +
                      (w.steer ? $" (braque{(w.steerPivot != null ? " via " + w.steerPivot.name : "")})" : ""), w.wheel);
        return result.ToArray();
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

    // Bas de la carrosserie (les pneus touchent le sol) : sert à estimer le rayon des roues d'un rig
    float CarBottomY()
    {
        float y = float.MaxValue;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (!(r is ParticleSystemRenderer)) y = Mathf.Min(y, r.bounds.min.y);
        return y;
    }

    // Mesh propre à la roue : demi-hauteur. Os d'un rig : hauteur du centre de la roue au-dessus du bas de la voiture.
    static float MeasureRadius(Transform wheel, float bottomY)
    {
        var r = wheel.GetComponent<Renderer>();
        if (r != null && !(r is SkinnedMeshRenderer) && r.bounds.extents.y > 0.05f) return r.bounds.extents.y;

        float h = wheel.position.y - bottomY;
        return h > 0.05f && h < 3f ? h : 0.35f;
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
            Quaternion spin = w.baseRot * Quaternion.AngleAxis(w.spin, w.axle);

            if (w.steerPivot != null)
            {
                w.steerPivot.localRotation = steer * w.pivotBaseRot;
                w.wheel.localRotation = spin;
            }
            else
                w.wheel.localRotation = steer * spin;
        }
    }
}
