# Shaders noir et blanc

| Fichier | Rôle |
|---|---|
| `OutlineFullscreen.shader` + `Outline.mat` | Contours sur les arêtes de tous les modèles 3D, sauf la route. Passe aussi l'image en noir et blanc (`_Desaturate`). |
| `RoadLines.shader` + `RoadLines.mat` | Asphalte sombre + bandes blanches émissives (bords pleins, centre en pointillés), calées sur les UV du générateur. |
| `DotShadow.shader` + `DotShadow.mat` + `CarDotShadow.cs` | Ombre de la voiture en points blancs. |

## À faire dans l'éditeur

1. **Contours** : sur `Settings/Mobile_Renderer` **et** `Settings/PC_Renderer`, *Add Renderer Feature > Full Screen Pass Renderer Feature*.
   - Pass Material : `Outline`
   - Injection Point : `Before Rendering Post Processing`
   - Requirements : `Depth` + `Normal` (cocher les deux)
   - Fetch Color Buffer : coché
2. **Lignes émissives qui brillent** : dans le Volume de la scène, ajouter *Bloom* (Threshold ~1, Intensity ~1) et cocher *Post Processing* sur la caméra. Le HDR est déjà activé dans les RP Assets.
3. **Route** : `RoadLines.mat` est déjà assigné dans `Paul/RoadGenerator.prefab` et dans la scène `PL`. Si tu changes `roadWidth` ou `uvTileLength` du générateur, mets les mêmes valeurs dans `_RoadWidth` / `_UVTileLength` du material.
   `Pointillés par longueur d'UV` doit rester un entier (sinon les pointillés sautent au rebase du TrackRoot).
4. **Ombre en points** : ajouter `CarDotShadow` sur la voiture, y mettre `DotShadow.mat`, régler `Size` à l'empreinte de la voiture. Mettre *Cast Shadows = Off* sur les MeshRenderer de la voiture pour ne garder que les points.
5. **Autres shaders maison** : un objet n'a de contour que si son shader a une passe `DepthNormals` (URP/Lit l'a déjà, elle a été ajoutée à `WorldGrid`).

## À vérifier
- Pas de fausses lignes sur le sol vu de biais : sinon monter `Tolérance sols rasants` ou `Seuil profondeur`.
- Pas de contour sur la route, mais la voiture et les glissières sont bien détourées.
- Les pointillés restent continus en roulant longtemps (plus de 500 m, rebase du TrackRoot).
