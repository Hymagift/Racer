# Shaders noir et blanc

| Fichier | Rôle |
|---|---|
| `OutlineFullscreen.shader` + `Outline.mat` | Contours noirs sur les arêtes de tous les modèles 3D, sauf la route et les cônes. Branché sur `Mobile_Renderer` et `PC_Renderer`. |
| `ShadedBW.shader` | Noir et blanc à nuances franches (cel shading). `MatCar` (voiture), `EnvBW` (terrain), `RailBW` (glissières). |
| `RoadLines.shader` + `RoadLines.mat` | Asphalte sombre + bandes blanches émissives calées sur les UV du générateur de route. |
| `TrafficCone.shader` + `TrafficCone.mat` + `ConeLook.cs` | Cône de chantier : corps noir, contour orange, deux bandes orange fluo émissives. |
| `DotShadow.shader` + `DotShadow.mat` + `CarDotShadow.cs` | Ombre de la voiture en points blancs. |
| `ApplyMaterial.cs` | Met un material sur tous les modèles enfants au lancement (la voiture FBX n'a pas de material). |
| `RacerPasses.hlsl` | Passes communes (ombres, normales pour le contour). |

## Déjà branché
- Renderer Feature *Full Screen Pass* (`Outline`, Before Rendering Post Processing, Depth + Normal) sur les deux Renderers.
- Bloom activé dans `Settings/DefaultVolumeProfile` (Threshold 1, Intensity 1) et Post Processing sur la Main Camera de la scène `PL`.
- Scène `PL` : fond uni gris très sombre + brouillard linéaire assorti (60 à 150 m, cache l'apparition des chunks).
- `PL/Car.prefab` : `ApplyMaterial` (MatCar) + `CarDotShadow` (ombre en points, ombre classique coupée).
- `PL/Cone.prefab` : le cube est remplacé par `cone.fbx` (enfant `Visual`, échelle 2.5, posé sur la route) + `ConeLook`.
- Terrain, route et glissières : `EnvBW`, `RoadLines`, `RailBW` dans la scène `PL` et les prefabs de Paul.

## Réglages utiles
- Plus ou moins de nuances : `Nombre de nuances` sur les materials ShadedBW.
- Fausses lignes sur le sol vu de biais : monter `Tolérance sols rasants` ou `Seuil profondeur` sur `Outline`.
- Route : `_RoadWidth` / `_UVTileLength` doivent valoir `roadWidth` / `uvTileLength` du générateur. `Pointillés par longueur d'UV` doit rester un entier.
- Cône : position/épaisseur des bandes en hauteur normalisée (0 = base, 1 = pointe), épaisseur du contour en mètres.
