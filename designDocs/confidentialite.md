# Page « Confidentialité » — GreenBoard Cloud (Sprint 2, S2-5)

**Objet :** Informer les utilisateurs sur les données collectées, leur durée de conservation, et leurs droits (RGPD).  
**Lieu :** accessible depuis l'aide (F1) et la fenêtre « À propos ».

---

## 1. Données collectées

| Donnée | Finalité | Conservation |
|---|---|---|
| Adresse e-mail | Identifiant du compte, notifications | Tant que le compte existe |
| Mot de passe | Authentification (stocké haché, jamais en clair) | Tant que le compte existe |
| Nom affiché | Personnalisation de l'interface | Tant que le compte existe |
| Documents `.gboard` | Stockage des tableaux blancs de l'utilisateur | Tant que le compte existe ; supprimés avec le compte |
| Jeton de rafraîchissement | Maintien de la session | 30 jours, révocable à tout moment |
| Adresse IP | Sécurité (détection de connexions anormales) | Journaux de sécurité, 30 jours maximum |

---

## 2. Où sont stockées les données

- **Comptes et métadonnées :** Base de données PostgreSQL hébergée sur Render.com (région Europe)
- **Fichiers `.gboard` :** Stockage objet compatible S3 (Cloudflare R2, Backblaze B2 ou AWS S3 selon la configuration)
- **Jeton côté client (application Windows) :** Chiffré avec DPAPI (Data Protection API de Windows), portée utilisateur courant, jamais en clair sur le disque

---

## 3. Vos droits (RGPD)

| Droit | Comment l'exercer |
|---|---|
| Accès à vos données | Connectez-vous à votre compte GreenBoard ; vos données sont visibles dans l'interface |
| Rectification | Modifiez votre nom affiché ou votre e-mail depuis les réglages du compte |
| Suppression | Utilisez « Supprimer mon compte » dans les réglages : votre compte et **tous** vos documents sont supprimés définitivement |
| Export | Téléchargez chacun de vos documents `.gboard` depuis « Mes tableaux en ligne » |
| Opposition au traitement | Ne créez pas de compte : GreenBoard reste 100 % local et hors ligne |

---

## 4. Sécurité

- Les mots de passe sont hachés (jamais stockés en clair)
- Les communications entre l'application et le serveur utilisent HTTPS (TLS)
- Les jetons d'accès expirent après 15 minutes
- Les jetons de rafraîchissement expirent après 30 jours et peuvent être révoqués
- Aucun envoi de document n'a lieu sans action explicite de l'utilisateur (« Enregistrer dans mon espace »)

---

## 5. Hors ligne

Sans compte, GreenBoard fonctionne entièrement en local sur votre ordinateur. Aucun document n'est envoyé sur le serveur. Les fichiers `.gboard` sont enregistrés sur votre disque dans le dossier de votre choix.

---

## 6. Contact

Pour toute question sur la confidentialité : [contact@alfacomputers.apps](mailto:contact@alfacomputers.apps)  
Éditeur : Alfa Computers Apps — Ferid HELALI + Claude Code
