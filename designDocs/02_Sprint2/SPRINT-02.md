# Sprint 2 — « GreenBoard Cloud : compte optionnel et espace de documents »

**Objectif :** permettre à un utilisateur qui le souhaite de créer un compte et de disposer d'un espace en ligne
où ranger ses tableaux `.gboard`, accessible depuis n'importe quel PC. **Sans compte, GreenBoard reste
100 % local et fonctionne comme aujourd'hui** (offline-first, cahier des charges).

**Hébergement :** [Render.com](https://render.com) · **Durée :** 2 semaines · **Version visée :** 1.1.0

## Architecture

```
GreenBoard.exe (WPF)                         Render.com
┌──────────────────────┐   HTTPS + JWT   ┌───────────────────────────────┐
│ CloudClient (HttpClient)│ ───────────▶ │ Web Service : InfiniteCanvas.Api │
│ jeton dans le Coffre    │              │ ASP.NET Core 9 (Docker)          │
│ Windows (DPAPI)         │ ◀─────────── │  /auth  /me  /documents          │
└──────────────────────┘                 └──────┬───────────────┬────────┘
                                                │               │
                                    Render PostgreSQL     Stockage objet S3
                                    (comptes, métadonnées) (fichiers .gboard)
```

- **Nouveau projet** `src/InfiniteCanvas.Api` (ASP.NET Core 9 Minimal API), références `Core`. Respecte la
  direction `Api → Infrastructure → Core`.
- **Base de données :** Render PostgreSQL (EF Core 9 + Npgsql, migrations dans `Infrastructure/Database`).
- **Fichiers :** les `.gboard` ne vont **pas** dans PostgreSQL. Le disque de Render est éphémère (sauf disque
  persistant payant, non partagé entre instances) → stockage objet compatible S3 (Cloudflare R2 : 10 Go gratuits,
  sans frais de sortie ; ou Backblaze B2 / AWS S3) via `AWSSDK.S3`. Interface `IDocumentStore` pour pouvoir changer
  de fournisseur, implémentation « disque local » pour le développement.
- **Déploiement :** `render.yaml` (Blueprint) à la racine : un Web Service Docker (`Dockerfile` multi-étapes,
  image `mcr.microsoft.com/dotnet/aspnet:9.0`), une base PostgreSQL, variables d'environnement
  (`ConnectionStrings__Db`, `Jwt__Key`, `Storage__*`) déclarées `sync: false` ou générées (`generateValue: true`).
  Port lu dans `PORT` (fourni par Render), `healthCheckPath: /health`, migrations appliquées au démarrage.
- **Coût :** le plan gratuit de Render met le service en veille après 15 min d'inactivité (≈ 1 min de réveil) et
  la base PostgreSQL gratuite expire après 30 jours → plan *Starter* (service + base) recommandé dès les premiers
  vrais utilisateurs. Le client affiche « Connexion au serveur… » pendant un réveil.

## API (v1)

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/api/v1/auth/register` | Créer un compte (e-mail, mot de passe, nom affiché) |
| POST | `/api/v1/auth/login` | Jeton d'accès (JWT 15 min) + jeton de rafraîchissement (30 j, rotatif) |
| POST | `/api/v1/auth/refresh` · `/logout` | Renouveler / révoquer |
| GET | `/api/v1/me` | Profil + quota (utilisé / total) |
| DELETE | `/api/v1/me` | Supprimer le compte **et tous ses documents** (RGPD) |
| GET | `/api/v1/documents` | Liste : id, titre, taille, nb de pages, miniature, modifié le |
| POST | `/api/v1/documents` | Envoyer un `.gboard` (multipart, ≤ 50 Mo) |
| GET | `/api/v1/documents/{id}` | Télécharger |
| PUT | `/api/v1/documents/{id}` | Remplacer (en-tête `If-Match` = version → 409 si conflit) |
| PATCH / DELETE | `/api/v1/documents/{id}` | Renommer / supprimer (corbeille 30 jours) |
| GET | `/health` | Sonde de Render |

## Récits utilisateur

### S2-1 · Socle de l'API et déploiement sur Render — 5 pts
- [ ] Projet `InfiniteCanvas.Api`, `/health`, journalisation, ProblemDetails, CORS fermé, limite de débit
- [ ] `Dockerfile` + `render.yaml` ; déploiement automatique à chaque push sur `main`
- [ ] Domaine `api.greenboard…` (ou `*.onrender.com`) en HTTPS

### S2-2 · Inscription et connexion (compte optionnel) — 8 pts
- [ ] Comptes : e-mail unique, mot de passe haché (ASP.NET Core Identity, PBKDF2) — jamais stocké en clair
- [ ] JWT signé (clé dans les variables d'environnement de Render), jeton de rafraîchissement rotatif révocable
- [ ] Vérification de l'adresse e-mail et « mot de passe oublié » (service d'envoi : Resend / Brevo)
- [ ] Limite de tentatives de connexion, messages d'erreur sans fuite (« identifiants incorrects »)

### S2-3 · Espace de documents par utilisateur — 8 pts
- [ ] Chaque document appartient à un utilisateur ; toute requête vérifie la propriété (pas d'accès par simple id)
- [ ] Clé de stockage : `users/{userId}/documents/{docId}/{version}.gboard`
- [ ] Validation à l'envoi : archive ZIP valide, `board.json` présent, taille ≤ 50 Mo, nombre de pages
- [ ] Quota par compte (gratuit : 500 Mo), renvoyé par `/me`
- [ ] Gestion de version optimiste (ETag) : pas d'écrasement silencieux entre deux PC

### S2-4 · Intégration dans GreenBoard — 8 pts
- [ ] Réglages › nouvelle section « Compte GreenBoard » : Créer un compte / Se connecter / Se déconnecter
- [ ] Jeton de rafraîchissement chiffré avec DPAPI (`ProtectedData`, portée utilisateur) — jamais en clair
- [ ] Fenêtre « Mes tableaux en ligne » : liste avec miniatures, ouvrir, renommer, supprimer
- [ ] « Enregistrer dans mon espace » (Ctrl+Maj+U) ; la barre de titre indique « ☁ en ligne »
- [ ] Hors connexion : message clair, le travail local n'est jamais bloqué ; conflit → « Garder les deux »
- [ ] Aucun envoi sans action de l'utilisateur (pas de synchronisation automatique dans ce sprint)

### S2-5 · Qualité, sécurité, confidentialité — 5 pts
- [ ] Tests d'intégration de l'API (`WebApplicationFactory` + Testcontainers PostgreSQL)
- [ ] Tests du client (`HttpMessageHandler` simulé) : connexion, rafraîchissement, 409, hors ligne
- [ ] Revue de sécurité : OWASP API Top 10 (contrôle d'accès par objet, débit, taille des envois)
- [ ] Page « Confidentialité » (données stockées, durée, suppression du compte), mentionnée dans l'aide

## Hors périmètre (sprints suivants)
- Synchronisation automatique en arrière-plan et édition hors ligne fusionnée (outbox événementielle du cahier des charges)
- Partage d'un tableau par lien, collaboration en temps réel (SignalR)
- Connexion Google / Microsoft (OAuth), offre payante (open-core)

## Définition de « terminé »
API déployée sur Render et joignable en HTTPS, tous les tests verts (local + CI GitHub Actions), un compte créé
depuis GreenBoard peut enregistrer, lister, rouvrir et supprimer un tableau depuis deux PC différents, et un
utilisateur sans compte ne voit aucune différence. AGENTS.md à jour.

**Capacité estimée :** 34 points.
