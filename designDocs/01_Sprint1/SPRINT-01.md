# Sprint 1 — « Release 1.0.0 : À propos, Réglages, Aide »

**Objectif :** livrer GreenBoard 1.0.0 à des utilisateurs externes (amis, enseignants) avec une identité
(auteur, éditeur), des réglages persistants et une aide intégrée, puis publier la version sur GitHub.

**Éditeur :** Alfa Computers Apps · **Auteur :** Ferid HELALI + Claude Code · **Version :** 1.0.0

## Récits utilisateur

### S1-1 · À propos (identité de l'application)
En tant qu'utilisateur, je veux savoir qui a créé GreenBoard et quelle version j'utilise.
- [x] Fenêtre « À propos » : icône, « GreenBoard », version (lue depuis l'assembly), « Alfa Computers Apps »
- [x] Auteur : Ferid HELALI + Claude Code, avec la photo de l'auteur et le logo Alfa Computers
- [x] Accessible depuis l'aide et le menu Réglages
- [x] Métadonnées : `Company`/`Authors` du csproj, `AppPublisher` de l'installateur = Alfa Computers Apps

### S1-2 · Réglages (roue dentée en bas à gauche)
En tant qu'enseignant, je veux adapter l'outil à ma manière de travailler, et que mes choix soient conservés.
- [x] Bouton roue dentée flottant en bas à gauche de la zone de dessin (ne dessine pas, ne déplace pas la vue)
- [x] Épaisseur du stylet et des formes (1 à 20)
- [x] Lissage du tracé (courbes de Bézier) et sensibilité à la pression (stylet graphique)
- [x] Arrière-plan par défaut des nouveaux tableaux
- [x] Sensibilité du zoom à la molette
- [x] Confirmation avant « Tout effacer »
- [x] Sauvegarde automatique (désactivée, 1, 2, 5 ou 10 min) pour un tableau déjà enregistré
- [x] Réglages appliqués immédiatement, enregistrés dans `%APPDATA%\GreenBoard\reglages.json`,
      bouton « Valeurs par défaut » ; un fichier corrompu ou absent redonne les valeurs par défaut
- [x] Tests unitaires : aller-retour JSON, bornes, fichier corrompu

### S1-3 · Aide (fonctionnalités et raccourcis)
En tant que nouvel utilisateur, je veux découvrir les fonctionnalités et les raccourcis clavier.
- [x] Bouton « ? » à côté de la roue dentée, et touche F1
- [x] Onglet Fonctionnalités (outils, pages, images, export, partage, IA/MCP)
- [x] Onglet Raccourcis clavier (outils, fichier, édition, pages, vue)
- [x] Onglet À propos

### S1-4 · Connecter un assistant IA (ajouté en cours de sprint)
En tant qu'enseignant, je veux brancher mon assistant IA sur GreenBoard sans lire de documentation technique.
- [x] Onglet « Assistant IA » de l'aide (et bouton « Connecter un assistant… » dans les Réglages)
- [x] Détection du serveur MCP installé ; sinon, explication (réinstaller en mode « Complète »)
- [x] Blocs prêts à copier, avec le vrai chemin : Claude Desktop, Claude Code, **OpenCode (gratuit et libre)**,
      et tout autre client MCP stdio (Cursor, VS Code, Continue…)
- [x] 10 demandes types, une par matière : physique, mathématiques, électronique, informatique, géographie,
      SVT, chimie, histoire, français, économie — bouton « Copier » sur chacune
- [x] Le mode d'emploi `LISEZ-MOI-MCP.txt` de l'installateur mentionne aussi OpenCode

### S1-5 · Écran de démarrage et plein écran (ajouté en cours de sprint)
- [x] Écran de démarrage (icône, nom, version, auteurs, logo de l'éditeur), au moins 1,5 s, fondu à l'ouverture
- [x] Image générée par `installer\make-icon.ps1` (version lue dans le csproj), régénérée par `build-installer.ps1`
- [x] La fenêtre principale s'ouvre agrandie (plein écran de travail)

### S1-6 · Outil Texte (ajouté en cours de sprint)
- [x] Outil Texte (X) : clic → saisie multiligne, Échap / Ctrl+Entrée / clic ailleurs pour terminer
- [x] Clic sur un texte (ou double-clic avec la Sélection) pour le modifier ; couleur au choix pendant la saisie
- [x] Déplacement, redimensionnement, suppression, annuler/rétablir comme les images
- [x] Contours vectoriels : rendu identique en PNG, SVG et PDF ; format `.gboard` version 4
- [x] Taille par défaut dans les Réglages ; aide et raccourcis à jour ; 5 tests

### S1-7 · Guide du développeur
- [x] `DEVELOPER.md` : outils, compilation, architecture, recettes, tests, installateur, publication, dépannage

### S1-8 · Publication
- [x] Installateur reconstruit (tests verts) avec l'éditeur Alfa Computers Apps
- [x] Dépôt GitHub public `feridHelali/GreenBoard`, branche poussée
- [x] Release `v1.0.0` avec `GreenBoard-Setup-1.0.0.exe` et son empreinte SHA-256

## Définition de « terminé »
Build sans avertissement nouveau, tous les tests verts, installateur reconstruit et testé, textes en français,
AGENTS.md à jour, commit sur `feature/greenboard-v1`.
