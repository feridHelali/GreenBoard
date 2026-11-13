# GreenBoard — guide du développeur

Tout ce qu'il faut pour **installer l'environnement, comprendre, modifier, améliorer et publier** GreenBoard.
Pour l'utilisateur final, voir [README.md](README.md). Les règles détaillées pour les agents de code (Claude Code,
OpenCode…) sont dans [AGENTS.md](AGENTS.md) : lisez-les aussi, elles évitent bien des pièges.

---

## 1. Outils nécessaires

| Outil | Version | Installation (PowerShell) | Rôle |
|---|---|---|---|
| **Windows** | 10 (1809) ou 11, x64 | — | WPF ne compile et ne s'exécute que sous Windows |
| **SDK .NET** | 9.0 | `winget install Microsoft.DotNet.SDK.9` | Compilation, tests, publication |
| **Git** | récent | `winget install Git.Git` | Sources |
| **PowerShell** | 7+ (`pwsh`) | `winget install Microsoft.PowerShell` | Scripts de l'installateur et de l'icône |
| **Inno Setup** | 6 | `winget install JRSoftware.InnoSetup` | Construction de l'installateur `.exe` |
| **GitHub CLI** | récent | `winget install GitHub.cli` puis `gh auth login` | Publication des versions (facultatif) |

Éditeur au choix :
- **Visual Studio 2022** (17.12+), charge de travail « Développement .NET Desktop » — concepteur XAML, débogueur ;
- **JetBrains Rider** ;
- **VS Code** + extension *C# Dev Kit* (pas de concepteur XAML, mais suffisant).

Pour tester l'IA (facultatif) : [Claude Desktop](https://claude.ai/download), Claude Code, ou
[OpenCode](https://opencode.ai) (gratuit). Une tablette graphique (XP-Pen, Wacom…) est utile pour la pression.

Vérification :

```powershell
dotnet --list-sdks        # doit lister une version 9.0.x
pwsh --version
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /?   # doit afficher l'aide d'Inno Setup
```

## 2. Récupérer, compiler, lancer

```powershell
git clone https://github.com/feridHelali/GreenBoard.git
cd GreenBoard
dotnet build InfiniteCanvas.sln
dotnet test  InfiniteCanvas.sln
dotnet run   --project src\InfiniteCanvas.App\InfiniteCanvas.App.csproj    # ouvre la fenêtre
```

- L'exécutable de développement est `src\InfiniteCanvas.App\bin\Debug\net9.0-windows\GreenBoard.exe`.
- **Fermez GreenBoard avant de recompiler** : un `GreenBoard.exe` ouvert verrouille ses fichiers
  (idem pour `InfiniteCanvas.Mcp.exe` si un assistant IA l'utilise).
- `GreenBoard.exe "chemin\tableau.gboard"` ouvre directement un tableau.

## 3. Organisation du dépôt

```
InfiniteCanvas.sln
├─ src/
│  ├─ InfiniteCanvas.App/            Application WPF (net9.0-windows) → GreenBoard.exe
│  │  ├─ App.xaml(.cs)               Ressources globales (icônes, styles) + écran de démarrage
│  │  ├─ MainWindow.xaml(.cs)        Toute l'interface du tableau et ses événements
│  │  ├─ Views/                      Fenêtres : Partage, Réglages, Aide (fonctionnalités, IA, raccourcis, à propos)
│  │  ├─ Board*.cs                   Fonds, historique, fichier .gboard, export, texte, canal MCP
│  │  ├─ AppSettings.cs / AppInfo.cs Réglages utilisateur / identité et version
│  │  ├─ MailShare.cs                Partage par e-mail (MAPI)
│  │  └─ Assets/                     Icône, logo, écran de démarrage, photo de l'auteur
│  ├─ InfiniteCanvas.Core/           Domaine (net9.0) : protocole du canal MCP
│  ├─ InfiniteCanvas.Infrastructure/ Persistance (prévue : SQLite, synchronisation)
│  └─ InfiniteCanvas.Mcp/            Serveur MCP (stdio) pour les assistants IA → InfiniteCanvas.Mcp.exe
├─ tests/                            xUnit (les vrais tests sont dans InfiniteCanvas.App.Tests)
├─ installer/                        Inno Setup (.iss), scripts de construction et d'icône
└─ designDocs/                       Cahier des charges (.docx) et plans de sprint
```

Dépendances : `App → Infrastructure → Core`, `Mcp → Core`.

## 4. Comment ça marche (l'essentiel)

- **Feuille infinie** : un `Canvas` de 20 000 × 20 000 transformé par `ScaleTransform` puis `TranslateTransform`
  (`écran = monde × échelle + translation`). Le fond est un `DrawingBrush` en mosaïque qui suit la vue.
- **Encre** : `InkCanvas` natif (pression, lissage). Les **formes sont de vrais traits** (`Stroke`), donc effaçables,
  sélectionnables et enregistrées avec l'encre.
- **Images et textes** sont des enfants de l'`InkCanvas` (`Image` / `Border` contenant un `Path`) avec un `Tag`
  qui porte leurs données. Le **texte** est converti en contours vectoriels (`BoardText`) : rendu identique à
  l'écran et à l'export, sans police à embarquer.
- **Pages** : une seule `InkCanvas` sert toutes les pages ; changer d'onglet échange traits, éléments, fond, vue
  et historique (`ActivatePage` / `StoreActivePage`).
- **Annuler / Rétablir** : `BoardHistory`, une étape par geste (bouton gauche enfoncé → relâché).
- **Fichier `.gboard`** : archive ZIP (version 4) — `pages/<n>/ink.isf`, `pages/<n>/items/*`, `board.json`.
- **Export** : PNG/JPEG (rendu bitmap 2×), SVG et PDF vectoriels écrits à la main (`BoardExporter`).
- **IA** : l'assistant appelle `InfiniteCanvas.Mcp.exe` (stdio) → SVG rendu en PNG (Svg.Skia) → envoyé à
  GreenBoard par le canal nommé `GreenBoard.Board.v1` → posé sur la page active.
- **Réglages** : `%APPDATA%\GreenBoard\reglages.json` (`AppSettings`, valeurs bornées par `Normalized()`).

## 5. Recettes : modifier et améliorer

**Ajouter un outil** (comme l'outil Texte) :
1. une géométrie d'icône `Icon…` dans `App.xaml` (grille 24 × 24, traits seulement) ;
2. une valeur dans l'énumération `Tool` et un `RadioButton` dans `MainWindow.xaml` (`Tag` = nom de la valeur) ;
3. l'enregistrer dans `_toolButtons`, régler le mode et le curseur dans `ApplyToolCore` ;
4. une touche dans `Window_PreviewKeyDown` **et** dans l'onglet Raccourcis de `Views/HelpWindow.xaml.cs` ;
5. si l'outil crée des éléments : les ajouter à `Snapshot` (sauvegarde), `LoadDocument`, `BuildExportPages`,
   `DuplicatePage`, et au format `.gboard` (incrémenter `CurrentVersion` dans `BoardFile`).

**Ajouter un réglage** : propriété dans `AppSettings` (+ borne dans `Normalized()`), contrôle dans
`Views/SettingsWindow.xaml`, lecture/affichage dans `Show()` / `Read()`, application dans `MainWindow.ApplySettings`,
et un test dans `AppSettingsTests`.

**Ajouter un fond** : valeur dans l'énumération `BoardBackground`, libellé et motif dans `BoardBackgrounds.cs`
(le motif est réutilisé tel quel par l'export SVG/PDF).

**Ajouter un outil pour l'IA** : méthode `[McpServerTool]` dans `src/InfiniteCanvas.Mcp/BoardTools.cs` ; si elle
doit agir sur le tableau, une commande dans `Core/Models/BoardProtocol.cs` et son traitement dans
`MainWindow.HandleBoardRequest`. Ne jamais écrire sur la sortie standard du serveur MCP (c'est le canal du protocole).

**Changer l'icône ou l'écran de démarrage** : modifier `installer\make-icon.ps1`, puis
`pwsh -STA -File installer\make-icon.ps1 -Preview $env:TEMP` pour voir le résultat.

**Pièges WPF fréquents** : l'erreur `MC3000`/`MC3072` cache la vraie cause (lisez le message intérieur) ;
`StackPanel` n'a pas de `Padding` ; un `StaticResource` inexistant fait échouer la compilation ;
`InkCanvas.SetLeft` (et non `Canvas.SetLeft`) pour placer un élément dans l'`InkCanvas`.

## 6. Conventions

- **Français** pour les commentaires, les textes de l'interface et la documentation.
- `Nullable` et `ImplicitUsings` activés ; pas de `using` redondant (`System`, `System.Linq`…).
- Ne jamais ajouter d'appel `InitializeComponent()` dans `App` (le `Main` généré le fait déjà).
- Les nombres écrits dans les fichiers (SVG, PDF, JSON) utilisent toujours `CultureInfo.InvariantCulture`.
- Tests xUnit sans `using Xunit;` ; tout ce qui touche WPF (encre, rendu) passe par le `Sta(...)` des tests.

## 7. Tests

```powershell
dotnet test InfiniteCanvas.sln                                   # tout
dotnet test tests\InfiniteCanvas.App.Tests --filter BoardTextTests  # une classe
```

Couverts : enregistrement/ouverture multipage et rétrocompatibilité, export PNG/JPEG/SVG/PDF, textes,
destinataires et `mailto`, historique, réglages.

## 8. Construire l'installateur

```powershell
pwsh -File installer\build-installer.ps1              # ou -SkipTests
```

Étapes : icône + écran de démarrage → tests → `dotnet publish` autonome win-x64 de l'application et du serveur MCP
(dans `installer\publish\`) → Inno Setup. Résultat : **`dist\GreenBoard-Setup-<version>.exe`** (≈ 87 Mo, .NET inclus)
et son empreinte SHA-256.

- L'installateur (`installer\GreenBoard.iss`) installe par utilisateur, sans droits administrateur, en français
  ou en anglais, avec raccourcis, association `.gboard` et le serveur MCP en option (type « Complète »).
- **Ne changez jamais `AppId`** : les mises à jour et la désinstallation en dépendent.
- Test sans toucher au système : lancer `installer\publish\app\GreenBoard.exe`. Test complet :
  `dist\GreenBoard-Setup-<version>.exe /VERYSILENT /CURRENTUSER /LOG="setup.log"`.
- L'installateur n'est pas signé (avertissement SmartScreen). Pour le supprimer : certificat de signature de code
  et `signtool sign` sur `GreenBoard.exe` puis sur l'installateur.

## 9. Publier une nouvelle version

1. Incrémenter `<Version>` dans `src\InfiniteCanvas.App\InfiniteCanvas.App.csproj` (ex. `1.1.0`).
2. Commit, puis `pwsh -File installer\build-installer.ps1` (l'écran de démarrage prend la nouvelle version).
3. Publier sur GitHub :

```powershell
git tag v1.1.0; git push origin master --tags
gh release create v1.1.0 dist\GreenBoard-Setup-1.1.0.exe --title "GreenBoard 1.1.0" --notes "…"
```

Le lien « dernière version » du README pointe automatiquement vers la nouvelle publication.

## 10. Dépannage

| Symptôme | Cause / solution |
|---|---|
| `MSB3027` / fichier verrouillé à la compilation | GreenBoard (ou le serveur MCP) est ouvert : le fermer |
| `MC3000 … unexpected token` dans un `.xaml` | Attribut collé au précédent (espace manquant) ou balise mal fermée |
| Le serveur MCP n'est pas vu par l'assistant | Chemin de l'exe dans la configuration, redémarrer l'assistant, GreenBoard ouvert |
| `git push` bloqué sans message | Clé SSH protégée par une phrase de passe : utiliser HTTPS (`gh auth setup-git`) |
| Windows « a protégé votre ordinateur » | Installateur non signé : *Informations complémentaires → Exécuter quand même* |

## 11. Feuille de route

- [Sprint 1](designDocs/01_Sprint1/SPRINT-01.md) — version 1.0.0 (livrée).
- [Sprint 2](designDocs/02_Sprint2/SPRINT-02.md) — compte optionnel et espace de documents en ligne
  (ASP.NET Core sur **Render.com**, PostgreSQL, stockage S3).
- Cahier des charges complet : `designDocs/00_Inception/GreenboardInfinitCanvas.docx`.
