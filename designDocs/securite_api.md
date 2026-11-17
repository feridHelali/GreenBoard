# Revue de sécurité — API GreenBoard Cloud (Sprint 2, S2-5)

**Cible :** OWASP API Top 10 2023  
**Statut :** Revue effectuée — 16/11/2026  
**Auteur :** Ferid HELALI + Claude Code — Alfa Computers Apps

---

## 1. Broken Object Level Authorization (BOLA) — API1:2023

**Risque :** Un utilisateur pourrait accéder aux documents d'un autre utilisateur en devinant ou forçant l'identifiant de document dans l'URL.

**Mesure prise :** Toutes les routes documentaires vérifient la propriété du document avant de répondre :
- `GET /api/v1/documents/{id}` — vérifie `userId` propriétaire
- `PUT /api/v1/documents/{id}` — vérifie `userId` + ETag
- `PATCH /api/v1/documents/{id}` — vérifie `userId`
- `DELETE /api/v1/documents/{id}` — vérifie `userId`

**Vérification :** Les tests unitaires doivent inclure un scénario où l'utilisateur A tente d'accéder au document de l'utilisateur B (doit retourner 404, pas 403, pour ne pas révéler l'existence du document).

---

## 2. Broken Authentication — API2:2023

**Risque :** Vol de session, brute-force sur les mots de passe, tokens JWT non expirés.

**Mesures prises :**
- Mot de passe haché avec ASP.NET Core Identity (PBKDF2), jamais stocké en clair
- Jeton d'accès (access token) : durée de vie 15 minutes
- Jeton de rafraîchissement (refresh token) : durée de vie 30 jours, rotatif (chaque utilisation génère un nouveau token et révoque l'ancien), révocable
- Verrouillage du compte après 5 tentatives échouées (15 minutes)
- Messages d'erreur génériques (« Identifiants incorrects ») sans indiquer si l'e-mail existe

**Recommandation :** Ajouter une authentification à deux facteurs (2FA) dans un sprint ultérieur.

---

## 3. Broken Object Property Level Authorization — API3:2023

**Risque :** Exposition de champs sensibles via les réponses API.

**Mesures prises :**
- Les réponses utilisent des records dédiés (`UserInfo`, `MeResponse`, `DocumentInfo`) qui n'exposent que les champs nécessaires
- Le hash de mot de passe n'est jamais retourné
- Les tokens ne sont jamais inclus dans les réponses GET (seulement lors de login/refresh)

---

## 4. Unrestricted Resource Consumption — API4:2023

**Risque :** Déni de service par envois de fichiers volumineux ou requêtes massives.

**Mesures prises :**
- Limite de taille d'envoi : 50 Mo maximum par document `.gboard`
- Rate limiting global : 100 requêtes par minute par utilisateur/IP
- Validation du fichier `.gboard` (ZIP valide, `board.json` présent) avant acceptation

**Recommandation :** Ajouter un quota par utilisateur (déjà prévu : 500 Mo gratuit, renvoyé par `/me`).

---

## 5. Broken Function Level Authorization — API5:2023

**Risque :** Accès à des fonctions administratives sans droits.

**Mesures prises :**
- Toutes les routes sensibles nécessitent une authentification JWT (`[Authorize]` / `.RequireAuthorization()`)
- `/api/v1/me` et `DELETE /api/v1/me` opèrent sur l'utilisateur connecté uniquement (pas d'accès par ID arbitraire)
- Pas de rôle administrateur exposé dans ce sprint (tout utilisateur gère uniquement ses propres données)

---

## 6. Unrestricted Access to Sensitive Business Flows — API6:2023

**Risque :** Automatisation de flux sensibles (ex. création de comptes en masse).

**Mesures prises :**
- Rate limiting sur `/auth/register` et `/auth/login` (inclus dans le limiteur global 100 req/min)
- Verrouillage de compte après échecs répétés

---

## 7. Server Side Request Forgery (SSRF) — API7:2023

**Risque :** L'application effectue des requêtes vers des ressources internes sur demande d'un utilisateur.

**Mesures prises :** L'API n'effectue aucune requête HTTP vers des URLs fournies par l'utilisateur. Les envois d'e-mails utilisent un service SMTP configuré côté serveur, pas d'URL dynamique.

---

## 8. Security Misconfiguration — API8:2023

**Risque :** Configuration par défaut non sécurisée, messages d'erreur détaillés en production.

**Mesures prises :**
- `UseExceptionHandler()` et `UseStatusCodePages()` pour masquer les détails d'erreur en production
- CORS fermé (pas d'origine wildcard)
- Secrets (Jwt__Key, ConnectionStrings) dans les variables d'environnement Render, jamais dans le code
- `ProblemDetails` pour les réponses d'erreur standardisées

**Recommandation :** Activer HTTPS uniquement en production (Render gère le TLS automatiquement).

---

## 9. Improper Inventory Management — API9:2023

**Risque :** Versions d'API non documentées, endpoints oubliés.

**Mesures prises :**
- Swagger/OpenAPI activé uniquement en développement (à conditionner à l'environnement)
- Version `v1` explicite dans toutes les routes
- Documentation des routes dans `SPRINT-02.md`

**Recommandation :** Désactiver Swagger en production via `builder.Environment.IsDevelopment()`.

---

## 10. Unsafe Consumption of APIs — API10:2023

**Risque :** Intégration d'API tierces non sécurisée.

**Mesures prises :** Aucune API tierce consommée dans ce sprint. Le stockage S3 (Cloudflare R2 / B2 / AWS S3) utilise des clés de configuration côté serveur, jamais exposées au client.

---

## Checklist de sécurité — Sprint 2

| # | Vérification | Statut |
|---|---|---|
| S1 | Mots de passe hachés (Identity PBKDF2) | ✅ |
| S2 | JWT access token 15 min | ✅ |
| S3 | Refresh token rotatif 30 j | ✅ |
| S4 | Rate limiting (100 req/min) | ✅ |
| S5 | Limite d'envoi 50 Mo | ✅ |
| S6 | Vérification propriété document | ✅ |
| S7 | Messages d'erreur sans fuite | ✅ |
| S8 | Secrets en variables d'environnement | ✅ |
| S9 | Suppression de compte + documents (RGPD) | ✅ |
| S10 | Swagger désactivé en production | ⚠️ À conditionner à `IsDevelopment()` |

---

## Limitations connues

- Pas d'authentification à deux facteurs (2FA) dans ce sprint
- Pas de connexion OAuth (Google/Microsoft) — réservé aux sprints suivants
- Le refresh token est stocké côté client (WPF) chiffré avec DPAPI ; côté serveur il est stocké en base (hash recommandé pour un sprint ultérieur)
