# Plan d'Implémentation & Architecture — GreenBoard ("Project InfiniteCanvas")

## 1. Vision et Objectifs Architecturaux

**GreenBoard** est une application bureautique Windows native (.NET 9 / WPF) conçue comme une alternative moderne, performante et souveraine à Microsoft Whiteboard.
Elle repose sur les piliers suivants :
1. **Ultra-faible latence & Expérience Stylet optimale** : Intégration matérielle directe (Surface Pen, XP-Pen), prise en charge fidèle de la pression, lissage vectoriel Bézier (`FitToCurve`) et rejet de paume.
2. **Espace Infini Vectoriel** : Navigation bidimensionnelle fluide (Pan & Zoom de 10 % à 1 000 %) sans pixellisation, avec grille adaptative.
3. **Architecture Propre & MVVM Strict** : Découpage clair en couches (`Core`, `Infrastructure`, `App`), injection de dépendances (`Microsoft.Extensions.DependencyInjection`), modèles réactifs via MVVM (`CommunityToolkit.Mvvm`).
4. **Offline-First & Résilience** : Stockage local ultra-rapide (SQLite en mode WAL), système de ressources déportées par hachage SHA-256, et table Outbox / Event Sourcing prête pour la synchronisation Cloud (ASP.NET Core / PostgreSQL / S3).
5. **Polyvalence des Contenus & Exports** : Encre numérique, formes vectorielles, notes repositionnables (*Sticky Notes*), images/PDF, avec exports vectoriels (SVG), matriciels (PNG/JPEG) et documents (PDF).

---

## 2. Pile Technologique Spécifiée

| Couche | Technologie / Bibliothèque | Rôle |
|---|---|---|
| **Langage & Framework** | C# 13, .NET 9 (`net9.0`, `net9.0-windows`) | Cible Windows 10/11 native |
| **Framework UI** | WPF (Windows Presentation Foundation) | Rendu vectoriel natif, composition visuelle |
| **Pattern UI** | MVVM avec `CommunityToolkit.Mvvm` | DataBinding typé, `ObservableObject`, `RelayCommand` |
| **Injection de Dépendances** | `Microsoft.Extensions.DependencyInjection` | Conteneur IoC pour Services et ViewModels |
| **Moteur d'Encre** | `System.Windows.Ink.InkCanvas` + DirectInk API | Capture brute haute fréquence, `StrokeCollection` |
| **Base Locale** | SQLite (`Microsoft.Data.Sqlite`) + WAL Mode | Persistance locale sans latence, transactionnelle |
| **Exports & Formats** | SkiaSharp / SharpVectors / PdfSharp / native | Rendu PNG/JPEG, SVG et génération PDF |
| **Tests** | xUnit 2.9.2, FluentAssertions, Coverlet | Tests unitaires isolés sans dépendance UI |

---

## 3. Structure Cible de la Solution

```
55_GreenBoard_InfiniteCanvas_Project/
│
├── designDocs/
│   └── 00_Inception/
│       ├── GreenboardInfinitCanvas.docx       # Spécification fonctionnelle et technique
│       └── database_schema.sql                # Schéma SQL initial
│
├── src/
│   ├── InfiniteCanvas.Core/                   # Couche Domaine (net9.0 - Cross-platform, Pure C#)
│   │   ├── Models/
│   │   │   ├── CanvasItem.cs                  # Entité de base abstraite (Id, X, Y, ZIndex, Transform)
│   │   │   ├── GeometricShape.cs              # Formes (Rectangle, Ellipse, Triangle)
│   │   │   ├── StickyNote.cs                  # Note autocollante (Texte, Couleur pastel)
│   │   │   ├── ImageItem.cs                   # Référence image (AssetHash, Width, Height)
│   │   │   ├── StrokeItem.cs                  # Tracé vectoriel (Points Bézier, Pression, Couleur)
│   │   │   ├── CanvasViewport.cs              # État de la caméra (X, Y, ZoomScale)
│   │   │   └── BoardMetadata.cs               # Méta-informations du tableau
│   │   ├── ValueObjects/
│   │   │   ├── CanvasPoint.cs                 # Coordonnées réelles dans l'espace infini
│   │   │   ├── CanvasRect.cs                  # Boîte englobante (Bounding Box)
│   │   │   └── ColorRgba.cs                   # Valeur de couleur indépendante de WPF
│   │   ├── Interfaces/
│   │   │   ├── IBoardRepository.cs            # Sauvegarde/chargement d'un tableau
│   │   │   ├── IAssetStorageService.cs        # Gestionnaire de fichiers binaires (SHA-256)
│   │   │   ├── IExportService.cs              # Moteur d'export (PNG, SVG, PDF)
│   │   │   ├── ISyncOutboxRepository.cs       # Journal d'événements pour le Cloud
│   │   │   └── ISpatialIndex.cs               # Abstraction pour le spatial culling (QuadTree)
│   │   └── Spatial/
│   │       └── QuadTree.cs                    # Algorithme d'indexation spatiale 2D
│   │
│   ├── InfiniteCanvas.Infrastructure/         # Couche Persistance & I/O (net9.0)
│   │   ├── Database/
│   │   │   ├── SqliteDatabaseInitializer.cs   # Création des tables, mode WAL, pragmas
│   │   │   └── SqliteConnectionFactory.cs     # Fabrique de connexions thread-safe
│   │   ├── Repositories/
│   │   │   ├── SqliteBoardRepository.cs       # Implémentation du repository de tableau
│   │   │   └── SqliteOutboxRepository.cs      # File d'attente d'événements hors-ligne
│   │   ├── Storage/
│   │   │   └── LocalFileAssetStorage.cs       # Stockage disque basé sur le hash SHA-256
│   │   └── Serialization/
│   │       ├── SvgStrokeSerializer.cs         # Sérialisation Path Data SVG des tracés
│   │       └── IsfStrokeSerializer.cs         # Sérialisation binaire Ink Serialized Format
│   │
│   └── InfiniteCanvas.App/                    # Couche Présentation WPF (net9.0-windows)
│       ├── App.xaml / App.xaml.cs             # Configuration IoC & Styles globaux
│       ├── Converters/                        # Convertisseurs XAML (ZoomToPercentage, etc.)
│       ├── Services/
│       │   ├── DialogService.cs               # Boîtes de dialogue et alertes
│       │   └── FilePickerService.cs           # Sélecteur de fichiers Windows (Open/Save)
│       ├── ViewModels/
│       │   ├── MainViewModel.cs               # ViewModel racine (Orchestration générale)
│       │   ├── CanvasViewModel.cs             # Gestion de la caméra (Pan, Zoom, Grille)
│       │   ├── ToolBarViewModel.cs            # Sélection d'outils, palettes de couleurs
│       │   └── SelectionViewModel.cs          # Manipulation des éléments sélectionnés
│       └── Views/
│           ├── MainWindow.xaml / .cs          # Fenêtre principale et raccourcis clavier
│           └── Controls/
│               ├── InfiniteCanvasControl.xaml # Contrôle personnalisé combinant Ink & Shapes
│               └── CanvasGridAdorner.cs       # Rendu accéléré de la grille adaptative
│
├── tests/
│   ├── InfiniteCanvas.Core.Tests/             # Tests unitaires du modèle et du QuadTree
│   └── InfiniteCanvas.Infrastructure.Tests/   # Tests d'intégration SQLite & sérialisation
│
└── implementation_plan.md                     # Ce document
```

---

## 4. Feuille de Route d'Implémentation par Phases

### Phase 1 : Couche Domaine `InfiniteCanvas.Core`
- [ ] **Modélisation des Objets du Canvas** :
  - Création de `CanvasItem` (classe de base abstraite avec `Id`, `X`, `Y`, `ZIndex`, `BoundingBox`).
  - Implémentation des formes vectorielles `GeometricShape` (`Rectangle`, `Ellipse`, `Triangle`).
  - Implémentation des notes autocollantes `StickyNote` (`Text`, `BackgroundColorHex`, `Width`, `Height`).
  - Modélisation de `StrokeItem` (`Points` avec pression, `ColorHex`, `Thickness`).
  - Modélisation de `ImageItem` (`AssetHash`, `SourceExtension`, `Width`, `Height`).
- [ ] **Système de Coordonnées & Primitives Géométriques** :
  - `CanvasPoint`, `CanvasRect` indépendants de `System.Windows.Point/Rect` pour assurer la portabilité et testabilité pure.
- [ ] **Indexation Spatiale (QuadTree)** :
  - Implémentation d'un `QuadTree<CanvasItem>` générique capable de retourner les objets intersectant le `ViewportRect` courant pour le *culling*.
- [ ] **Tests Unitaires Core** :
  - Validation de la géométrie, des collisions de boîtes englobantes et des insertions/recherches dans le QuadTree.

---

### Phase 2 : Persistance & Infrastructure `InfiniteCanvas.Infrastructure`
- [ ] **Gestionnaire de Base de Données SQLite** :
  - Configuration du mode `WAL` (*Write-Ahead Logging*) et des clés étrangères.
  - Initialisation conforme au schéma `database_schema.sql` (`CanvasObjects`, `HandDrawnStrokes`).
  - Ajout de la table `OutboxEvents` pour le patron de synchronisation déconnectée.
- [ ] **Sérialisation & Dé-sérialisation des Tracés** :
  - Conversion des tracés d'encre en format SVG vectoriel standard et en flux ISF binaire pour préserver la fidélité de pression XP-Pen.
- [ ] **Gestionnaire d'Assets Fichiers (Images/PDF)** :
  - Service de stockage local dédoublonné par empreinte SHA-256 dans un sous-dossier `AppData/Assets`.
- [ ] **Implémentation de `IBoardRepository` & `ISyncOutboxRepository`** :
  - Opérations CRUD asynchrones pour sauvegarder et charger l'état d'un tableau blanc.
- [ ] **Tests d'Intégration Infrastructure** :
  - Tests unitaires SQLite en mémoire ou fichier temporaire pour valider les cycles de sauvegarde/rechargement complets.

---

### Phase 3 : Architecture MVVM & Refactoring UI `InfiniteCanvas.App`
- [ ] **Mise en Place de l'Injection de Dépendances** :
  - Configuration du `ServiceProvider` dans `App.xaml.cs` (ou via une classe `Bootstrapper`).
  - Enregistrement des services du domaine, des repositories et des ViewModels.
- [ ] **Création des ViewModels (`CommunityToolkit.Mvvm`)** :
  - `MainViewModel` : gestion globale de l'état (titre, statut de sauvegarde, commandes de menu).
  - `ToolBarViewModel` : mode actif (Stylet, Gomme, Sélection, Formes, Notes), palette de couleurs (4 couleurs prédéfinies + sélecteur), épaisseur de trait.
  - `CanvasViewModel` : coordonnées caméra (`TranslateX`, `TranslateY`), niveau de zoom (`ZoomScale` bridé entre 0.1 et 10.0), déclenchement du rafraîchissement spatial.
- [ ] **Refactoring du Canvas & Contrôles XAML** :
  - Transformation du code-behind actuel de `MainWindow.xaml.cs` en bindings déclaratifs et commandes MVVM.
  - Séparation propre entre l'espace écran (Viewport souris) et l'espace monde (coordonnées absolues du canvas).
  - Préservation intégrale des propriétés d'encre basse latence : `FitToCurve = true`, `IgnorePressure = false`, mode gomme par trait `EraseByStroke`.

---

### Phase 4 : Enrichissement Fonctionnel & Manipulation d'Objets
- [ ] **Outil de Sélection & Boîte de Manipulation (Adorner)** :
  - Sélection d'éléments (formes, notes, images) avec poignées de redimensionnement et déplacement.
- [ ] **Composant Notes Autocollantes (*Sticky Notes*)** :
  - Contrôle avec zone de texte éditable, ajustement automatique de la police et choix de couleur pastel.
- [ ] **Importation d'Images & Drag-and-Drop** :
  - Prise en charge du glisser-déposer de fichiers images (PNG, JPEG, WEBP) sur le canvas.
  - Calcul du hash SHA-256 et insertion dans le stockage d'assets.
- [ ] **Grille d'Arrière-Plan Adaptative** :
  - Dessin d'une grille infinie à points ou lignes qui adapte dynamiquement son échelle selon le zoom.

---

### Phase 5 : Moteur d'Exportation
- [ ] **Export Image Haute Résolution (PNG / JPEG)** :
  - Export soit de la vue actuelle (Viewport), soit de la boîte englobante de l'ensemble des éléments (*Full Board Bounds*).
  - Option de fond transparent ou uni (vert tableau / blanc / sombre).
- [ ] **Export Vectoriel SVG** :
  - Génération d'un document SVG autonome comprenant tous les tracés vectoriels et formes sans perte de définition.
- [ ] **Export Document PDF** :
  - Exportation directe du tableau au format PDF.

---

### Phase 6 : Culling Spatial & Optimisations Haute Fréquence
- [ ] **Spatial Culling Actif** :
  - Connexion du `QuadTree` aux événements de déplacement de la caméra (Pan/Zoom) pour virtualiser les objets hors écran et préserver les 60+ FPS.
- [ ] **Virtualisation des Assets d'Images** :
  - Mipmapping et réduction en mémoire des images volumineuses lors des forts dézooms.

---

## 5. Stratégie de Qualité & Règles de Développement

1. **Typage Strict et `Nullable=enable`** :
   - Aucun `dynamic` ni cast non sécurisé.
   - Respect strict des annotations de nullabilité C#.
2. **Conventions du Projet** :
   - Langue française pour la documentation, les commentaires et les textes d'interface utilisateur (conformément à `AGENTS.md`).
   - Règles XAML strictes : pas de `Padding` sur `StackPanel`, pas de `Foreground` direct sur `Border`.
   - Respect de la structure `App.xaml` (ressources applicatives uniquement) et `App.xaml.cs` (pas d'`InitializeComponent` manuel).
3. **Tests Automatisés** :
   - Chaque fonctionnalité du domaine Core et de la persistance SQLite doit être couverte par des tests unitaires xUnit.
   - Les tests s'exécutent via `dotnet test InfiniteCanvas.sln`.
