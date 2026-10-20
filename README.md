# Racer

Jeu mobile (Android) de drift infini sur une route de montagne type touge. Unity 6, URP.

## Organisation

Tout le contenu du jeu est dans `Assets/_Racer/` (le `_` le garde en haut de la liste) :

| Dossier | Contenu |
|---|---|
| `Scenes/` | `Menu` (menu principal) et `PL` (partie). `Tests/` : scènes de test (`RoadTest`, `Pilou Controler test`) |
| `Scripts/Car/` | Voiture : physique du drift (`DriftCar`), pilote automatique, roues, couleur de la voiture équipée |
| `Scripts/Road/` | Route infinie (`ZigzagRoadGenerator` + tracé `TougeLayout`), terrain low poly, cônes, zones mortelles |
| `Scripts/Gameplay/` | Déroulement de la partie (`DriftGameManager`), score et multiplicateurs, cercle de combo, pilote auto en jeu |
| `Scripts/Menu/` | Menu principal (UI créée par code : `MenuStyle`, pages, effets, sons, particules) |
| `Scripts/Shop/` | Boosters et Garage (`BoosterShop`, `CarCatalog`), Boutique et achats (`PremiumShop`, `PremiumStore`) |
| `Scripts/Data/` | Sauvegarde du joueur (`PlayerData` : pièces, rubis, blueprints, pilotes auto...) |
| `Scripts/Audio/` | Musique de fond (`BackgroundMusic`, composée par code, joue pendant tout le jeu) |
| `Scripts/Editor/` | Outils de l'éditeur (menu **Drift** en haut d'Unity) |
| `Prefabs/` | Voiture, cône, GameManager, ScoreManager, route, terrain. `Tests/` : prefabs des scènes de test |
| `Models/` | Modèles 3D (`.fbx`) |
| `Materials/`, `Shaders/` | Matériaux et shaders |
| `Input/` | Actions du nouvel Input System |
| `Resources/` | (créé par le menu Drift) `CarCatalog.asset` : voitures, boosters, prix et probabilités |

Dossiers à laisser à leur place (emplacements imposés par Unity ou ses packages) :
`Assets/Settings` (URP), `Assets/Plugins` (Android), `Assets/Editor` (notifications mobiles), `Assets/Adaptive Performance`.

## Conventions

- Un script = une classe, et le fichier porte le nom de la classe (sinon Unity ne trouve pas le composant).
- On déplace ou renomme les assets **dans Unity** (ou avec leur fichier `.meta`) : le `.meta` contient l'identifiant
  qui relie les scènes et prefabs à l'asset.
- Nouveau script → le dossier de `Scripts/` qui correspond à son rôle ; nouvel asset → le dossier de son type.
