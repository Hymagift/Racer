# Cônes de chantier : modèle, matériau et effet d'impact

## Ce qui est déjà fait dans le repo

| Fichier | Rôle |
|---|---|
| `Assets/_Racer/Prefabs/Cone.prefab` | Utilise maintenant `Models/cone.fbx` (enfant `Modele`, échelle 2.5 : ~0.9 m de haut, ~0.95 m de base). BoxCollider recalé sur le modèle |
| `Assets/_Racer/Shaders/TrafficCone.shader` | Shader URP simple : corps orange, 2 bandes blanches légèrement brillantes, socle sombre. Passes ForwardLit, ShadowCaster, DepthOnly, DepthNormals |
| `Assets/_Racer/Materials/TrafficCone.mat` | Le matériau. Le script `Cone` l'applique au modèle (champ **Body Material** du prefab) |
| `Assets/_Racer/Scripts/Road/ConeHitFX.cs` | Effet d'impact orange→jaune en Particle System, créé par code |
| `ConeSpawner.cs` | Case **Hit Particles** + matériau : crée l'effet sous le TrackRoot (les particules défilent avec la route) |

## 0. Vérifier le cône (30 s)

Ouvre `Cone.prefab` :

- Le cône doit être **debout**, socle en bas, posé à la base du collider vert. S'il est couché ou trop
  petit/grand, ajuste seulement l'objet `Modele` (rotation, échelle, Y).
- Si l'aperçu est gris, c'est normal dans l'éditeur (le matériau est posé au lancement). Pour le voir
  tout de suite : glisse `TrafficCone.mat` sur l'objet `cone`.
- **Si les bandes sont verticales au lieu d'horizontales** : dans `TrafficCone.mat`, mets
  **Axe vertical du mesh** à `(0, 1, 0)` (au lieu de `(0, 0, 1)`).
- Si les bandes sont décalées (après un changement d'échelle de `Modele`), règle **Haut du socle** et
  **Pointe** : ce sont des mètres depuis le pivot du mesh (valeurs actuelles pour l'échelle 2.5 :
  -0.16 et 0.66). Les bandes se règlent avec **Bande basse / Bande haute** (début, fin entre 0 et 1).

## 1. Effet d'impact (Particle System)

Plus de VFX Graph : `ConeHitFX` crée tout seul, au lancement, deux Particle Systems sous `TrackRoot`
(bouffées rondes + étincelles étirées, teintes aléatoires de l'orange au jaune), avec le matériau `ConeHitParticle.mat`
(shader `Racer/ParticleSoft`, un disque doux sans texture).

- Activer/désactiver : case **Hit Particles** du ConeSpawner (scène `PL`).
- Réglages (couleur, nombre, taille, vitesse, durée, inclinaison du jet) : sur l'objet `ConeHitFX`
  sous `TrackRoot` en Play. Ils sont lus à la création : pour les garder, recopie-les dans les valeurs
  par défaut de `ConeHitFX.cs`.
- `Shaders/ConeHIt.vfx` n'est plus utilisé, tu peux le supprimer.
