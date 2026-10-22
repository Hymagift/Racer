# Performances mobile (objectif 60 fps)

Audit fait le 22/10/2026 sur le dépôt seul : **rien n'a pu être lancé ni profilé dans Unity**. Les estimations ci-dessous viennent de la lecture du code et des réglages. À confirmer avec le Profiler sur un vrai téléphone (build Development + « Autoconnect Profiler »).

## Bilan rapide

Le projet part sur de bonnes bases. Beaucoup de pièges classiques sont déjà évités :

- 60 fps forcés au lancement (`PerformanceSettings` : `targetFrameRate = 60`, `vSyncCount = 0`), Frame Pacing (Swappy) activé, GC incrémental, IL2CPP ARM64, rendu multithread.
- URP « Mobile » léger : pas de HDR, pas de MSAA, render scale 0.8, ombres temps réel coupées (ombre « blob » à la place), pas de lumières additionnelles, pas de depth/opaque texture, pas de post-process, SRP Batcher actif.
- Caméra : far clip à 180 m, pas de post-process.
- Scripts : route, terrain et décor générés au plus un morceau par image, décor et cônes recyclés (pool), raycast sans allocation, OnGUI retiré des builds, textes de l'UI mis à jour seulement quand ils changent, couches d'effets du ComboHUD dans des Canvas séparés.
- Modèles très légers (FBX de 12 à 125 Ko), aucune texture ni son importé (tout est généré par code).

Rien d'alarmant, donc. Ce qui risque de coûter le plus sur un téléphone moyen est listé ci-dessous, du plus gros impact au plus petit.

## Problèmes classés par impact

| # | Impact | Problème | Statut |
|---|--------|----------|--------|
| 1 | Fort (iPhone) | Qualité par défaut iPhone/tvOS = niveau 2, qui n'existe pas (il n'y a que 0 = Mobile et 1 = PC). Risque de tourner avec le réglage PC (HDR, MSAA, ombres douces, render scale 1). | **Corrigé** |
| 2 | Moyen | Fumée de drift : jusqu'à 2 × 80 boules de 320 triangles (~50 000 triangles), assemblées par le CPU à chaque image (pas d'instancing GPU), shader en alpha-test (`clip`) : coûteux sur les GPU mobiles. | À proposer |
| 3 | Moyen | Décor : quelques centaines d'objets séparés (sapins, rochers, palmiers), un draw call par objet et par matériau, matériaux URP/Lit complets importés des FBX, aucun LOD. Le décor est aussi généré jusqu'à 220 m alors que la caméra s'arrête à 180 m. | À proposer |
| 4 | Faible à moyen | Chaque sapin, rocher, palmier et cône avait un composant **Animator** inutile (rig « Generic » à l'import). | **Corrigé** |
| 5 | Faible | Petites saccades possibles au moment où un nouveau morceau de route arrive : mesh + collider calculés sur le thread principal (avec des allocations). | À surveiller |
| 6 | Faible | ComboHUD : le cercle suit la voiture à chaque image dans le Canvas principal (il re-batch le score avec) ; chaque particule d'UI est une Image (jusqu'à ~60 en même temps aux gros paliers). | À proposer |
| 7 | Faible | Cônes : `GetComponentsInChildren` à chaque réutilisation et `FindFirstObjectByType` au premier choc. | **Corrigé** |
| 8 | Très faible | Boost autopilote : texte du chrono refabriqué à chaque image. | **Corrigé** |
| 9 | Info | Voiture en `ContinuousDynamic` (plus cher que `ContinuousSpeculative`) ; physique à 50 Hz pour 60 fps d'affichage (OK : route et voiture sont interpolées). | À tester |
| 10 | Info | La scène de test « Pilou Controler test » est dans le build. Adaptive Performance est installé mais pas démarré (chargement automatique désactivé). | À décider |

## Correctifs appliqués (sans changement visible)

1. **`ProjectSettings/QualitySettings.asset`** : iPhone et tvOS utilisent le niveau « Mobile » (0), comme Android.
2. **Import des FBX du décor et du cône** (`pinetree`, `palmier_1`, `palmier_2`, `pierre_haute`, `pierre_large`, `pierre_ronde`, `cone`) : Rig = None et Import Animation décoché. Ces modèles n'ont ni os ni animation ; cela supprime un Animator par objet posé. Les voitures ne sont pas touchées (elles ont des os de roues).
3. **`Cone.cs`** : liste des Renderers mise en cache dans `Awake`, ScoreManager pris via `ScoreManager.Instance`.
4. **`AutopilotBoost.cs`** : le texte « N s » n'est refait que quand la seconde change.

Unity réimportera les 7 FBX à l'ouverture du projet (quelques secondes).

## Propositions (changement visible léger, à valider en jeu)

1. **Fumée de drift**, par ordre de simplicité :
   - baisser `maxParticles` (80 → 40) et/ou `Smoke Max Rate` (60 → 35) sur la voiture ;
   - boules à 1 subdivision au lieu de 2 (80 triangles au lieu de 320 : un peu plus facettées, très low poly) ;
   - activer l'instancing GPU des particules dans `StylizedSmoke.shader` (aucun changement visible, mais demande des tests).
2. **Décor** :
   - limiter la génération à la distance de la caméra (180 m) et baisser un peu la densité de la « Forêt en contrebas » (70 → 50) ;
   - passer les matériaux des FBX en URP **Simple Lit** (ou un shader dégradé comme celui du terrain) ;
   - éventuellement activer le GPU Resident Drawer (Unity 6, demande le rendu Forward+ et Vulkan).
3. **ComboHUD** : mettre le cercle qui suit la voiture dans son propre Canvas (comme les couches d'effets) ; plafonner les particules d'UI.
4. **Physique** : essayer `ContinuousSpeculative` sur la voiture.
5. **Build** : retirer la scène de test de la liste ; décider si on branche Adaptive Performance (baisse automatique du render scale quand le téléphone chauffe).

## À vérifier

- Dans `PL.unity`, le DecorSpawner n'a aucune liste de prefabs enregistrée : elles sont remplies automatiquement dans l'éditeur. Vérifier que le décor apparaît bien dans un build (sinon ouvrir la scène et la sauvegarder), car sans décor le téléphone tourne plus vite qu'en vrai et les mesures seraient faussées.

## Comment mesurer sur téléphone

1. Build Development + Autoconnect Profiler, téléphone branché en USB.
2. Regarder la frame time CPU (Main Thread et Render Thread) et GPU. Viser moins de 16,6 ms, idéalement 12 à 13 ms pour garder de la marge contre la chauffe.
3. Frame Debugger pour compter les draw calls en pleine course.
4. Tester sur un téléphone d'entrée de gamme et faire une partie de 5 minutes pour voir l'effet de la chauffe.
