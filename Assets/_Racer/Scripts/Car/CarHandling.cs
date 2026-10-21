using System;
using UnityEngine;

/// <summary>
/// Conduite propre à une voiture du Garage (champ "handling" de chaque voiture dans CarCatalog).
///
/// Ce sont des MULTIPLICATEURS des réglages du DriftCar de la scène : 1 = exactement le réglage de la scène,
/// qui est celui de la voiture de drift de référence (Ryujin GT-X, tout à 1). Exemple : steering = 0.8 braque 20 % de moins.
/// Ainsi, retoucher le DriftCar de la scène fait évoluer toutes les voitures ensemble, et chaque profil garde son caractère.
/// Appliqué par CarModelSwap au démarrage et quand on équipe une voiture dans le Garage.
/// </summary>
[Serializable]
public class CarHandling
{
    const float Min = 0.3f, Max = 2f;

    [Header("Moteur")]
    [Range(Min, Max), Tooltip("Vitesse de départ et vitesse max (startSpeed, maxSpeed)")]
    public float speed = 1f;
    [Range(Min, Max), Tooltip("Accélération du moteur (engineAccel)")]
    public float acceleration = 1f;

    [Header("Direction")]
    [Range(Min, Max), Tooltip("Angle de braquage max (maxSteerLow, maxSteerHigh)")]
    public float steering = 1f;
    [Range(Min, Max), Tooltip("Vitesse à laquelle les roues suivent le doigt et reviennent au centre (steerFollowSpeed, steerMaxRate, steerReturnSpeed)")]
    public float steerResponse = 1f;
    [Range(Min, Max), Tooltip("Inertie en rotation (yawInertia) : plus haut = voiture lourde, lente à pivoter et à se redresser")]
    public float rotationInertia = 1f;
    [Range(Min, Max), Tooltip("Contre-braquage automatique en drift (countersteerAssist)")]
    public float countersteer = 1f;

    [Header("Adhérence / drift")]
    [Range(Min, Max), Tooltip("Adhérence normale avant et arrière (frontGrip, rearGrip)")]
    public float grip = 1f;
    [Range(Min, Max), Tooltip("Adhérence arrière en drift (rearDriftGrip) : plus haut = glisse moins")]
    public float driftGrip = 1f;
    [Range(Min, Max), Tooltip("Angle de drift max (maxDriftAngle)")]
    public float driftAngle = 1f;
    [Range(Min, Max), Tooltip("Braquage à partir duquel l'arrière décroche (driftStartSteer) : plus haut = il faut braquer plus fort")]
    public float driftThreshold = 1f;
    [Range(Min, Max), Tooltip("Vitesse à laquelle l'arrière décroche (driftEngageRate)")]
    public float driftEngage = 1f;
    [Range(Min, Max), Tooltip("Vitesse à laquelle l'arrière se raccroche au relâché (driftReleaseRate)")]
    public float driftRecover = 1f;
    [Range(Min, Max), Tooltip("Vitesse gardée en glisse (driftSpeedKeep, plafonné à 1, et driftBoost)")]
    public float driftSpeedKeep = 1f;

    [Header("Visuel")]
    [Range(0f, 3f), Tooltip("Inclinaison de la caisse en virage (bodyRollMax)")]
    public float bodyRoll = 1f;

    /// <summary>Profil neutre : les réglages de la scène tels quels.</summary>
    public static readonly CarHandling Reference = new CarHandling();
}
