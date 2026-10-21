# Cônes de chantier : modèle, matériau et VFX d'impact

## Ce qui est déjà fait dans le repo

| Fichier | Rôle |
|---|---|
| `Assets/_Racer/Prefabs/Cone.prefab` | Utilise maintenant `Models/cone.fbx` (enfant `Visual`, échelle 2.5 : ~0.9 m de haut, ~0.95 m de base). BoxCollider recalé sur le modèle |
| `Assets/_Racer/Shaders/TrafficCone.shader` | Shader URP simple : corps orange, 2 bandes blanches légèrement brillantes, socle sombre. Passes ForwardLit, ShadowCaster, DepthOnly, DepthNormals |
| `Assets/_Racer/Materials/TrafficCone.mat` | Le matériau. Le script `Cone` l'applique au modèle (champ **Body Material** du prefab) |
| `Assets/_Racer/Scripts/Road/ConeHitFX.cs` | Envoie l'événement `OnConeHit` au VFX Graph à chaque impact, avec la position et la vitesse |
| `ConeSpawner.cs` | Nouveau champ **Hit Effect** : y glisser `ConeHit.vfx`, il crée l'effet sous le TrackRoot (les particules défilent avec la route) |

Tant que **Hit Effect** est vide, rien ne se passe à l'impact : rien ne casse.

## 0. Vérifier le cône (30 s)

Ouvre `Cone.prefab` :

- Le cône doit être **debout**, socle en bas, posé à la base du collider vert. S'il est couché ou trop
  petit/grand, ajuste seulement l'objet `Visual` (rotation, échelle, Y).
- Si l'aperçu est gris, c'est normal dans l'éditeur (le matériau est posé au lancement). Pour le voir
  tout de suite : glisse `TrafficCone.mat` sur l'objet `cone`.
- **Si les bandes sont verticales au lieu d'horizontales** : dans `TrafficCone.mat`, mets
  **Axe vertical du mesh** à `(0, 1, 0)` (au lieu de `(0, 0, 1)`).
- Si les bandes sont décalées (après un changement d'échelle de `Visual`), règle **Haut du socle** et
  **Pointe** : ce sont des mètres depuis le pivot du mesh (valeurs actuelles pour l'échelle 2.5 :
  -0.16 et 0.66). Les bandes se règlent avec **Bande basse / Bande haute** (début, fin entre 0 et 1).

## 1. Créer le graph à partir de ta fumée

1. Dans `Assets/_Racer/Shaders`, sélectionne `Smoke.vfx` et fais **Ctrl+D**. Renomme la copie `ConeHit.vfx`.
   (On garde ton Output avec `Smoke.shadergraph` et le mesh, c'est la base des bouffées.)
2. Ouvre `ConeHit.vfx`.

## 2. Déclencher sur l'impact (au lieu d'en continu)

1. Clic droit dans le vide > **Create Node > Context > Event**. Dans son champ, écris exactement `OnConeHit`.
2. Relie la sortie de cet Event au port **Start** du contexte **Spawn**.
   Si un Event `OnPlay` est relié au Spawn, **supprime ce lien** (sinon il y a une bouffée au lancement).
3. Dans **Spawn** : supprime **Constant Spawn Rate**, ajoute **Single Burst** (Spawn > Single Burst),
   **Count** = 12 (ou Random entre 8 et 14).

## 3. Initialize : partir du point d'impact, en espace local

1. En haut à droite du contexte **Initialize**, passe l'espace de **World** à **Local**
   (le petit bouton à côté du titre). Important : l'effet est enfant de la route, en Local il défile avec elle.
2. **Capacity** : 128. **Bounds Setup Mode** : Automatic (ou bounds manuels assez grands, 20 m).
3. Ajoute ces blocks (clic droit > Create Block) dans cet ordre :
   - **Inherit Source Position** (Attribute > Inherit) : le point d'impact envoyé par le script.
   - **Inherit Source Velocity** : la vitesse de la voiture (vers l'avant), déjà réduite par le script.
   - **Add Velocity** en mode **Random (Per-component)** : A = (-2.5, 1.5, -1), B = (2.5, 4, 3).
     Ça fait gicler les bouffées vers le haut et sur les côtés.
   - **Set Lifetime Random** : 0.35 à 0.6 s (c'est un petit "pof", court).
   - **Set Size Random** : 0.2 à 0.45.
   - **Set Angle Random** (Uniform) : 0 à 360 sur les 3 axes, pour que les boules ne se ressemblent pas.

## 4. Update : retomber et ralentir

- **Gravity** : (0, -6, 0).
- **Linear Drag** : 3 (les bouffées freinent vite, effet de souffle).
- Garde ce qu'il y avait dans ta fumée si ça te plaît (turbulence, etc.).

## 5. Output : la bouffée qui gonfle puis disparaît

Dans l'Output (celui avec `Smoke.shadergraph`) :

- **Set Size over Life** (ou Multiply Size over Life) : courbe qui monte vite de 0.6 à 1 puis redescend à 0
  à la fin. C'est ça qui donne le "pop".
- Propriété **Color** du Shader Graph : blanc/gris clair pour rester dans la DA noir et blanc.
- Si tu utilises **Alphaclip** pour l'érosion : relie un **Age over Lifetime** (Operator > Attribute)
  à Alphaclip pour que la boule se ronge en fin de vie.

## 6. (Optionnel) Éclats orange du cône

1. Sélectionne le système entier (Spawn → Initialize → Update → Output), **Ctrl+C / Ctrl+V**.
2. Relie le même Event `OnConeHit` au **Start** du nouveau Spawn.
3. Dans la copie : Count 6, Lifetime 0.5 à 0.9, Size 0.06 à 0.12, Add Velocity plus fort
   (A = (-4, 3, -2), B = (4, 6, 5)), **Gravity** (0, -15, 0), **Set Angular Velocity Random** pour qu'ils tournent.
   Mesh de l'output : Cube (au lieu de la sphère). **Color** = orange (1, 0.36, 0.02).

## 7. Brancher dans la scène

1. Ouvre la scène `PL`, sélectionne l'objet qui a le **ConeSpawner**.
2. Glisse `ConeHit.vfx` dans **Hit Effect**.
3. Play, tape un cône. Les réglages fins côté script sont sur l'objet `ConeHitFX` créé sous `TrackRoot`
   au lancement (**Velocity Scale** : 0.35 par défaut, la part de la vitesse de la voiture donnée aux particules).

**Si rien n'apparaît** : vérifie le nom `OnConeHit` (majuscules), que Initialize est bien en **Local**,
et que les blocks **Inherit Source Position/Velocity** sont là (sinon tout part de l'origine de la route,
loin derrière la caméra).
