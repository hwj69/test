# Analyse de l'Anonymiseur World of Numeric

- **Outil analysé** : https://worldofnumeric.fr/outils/anonymiseur/app/
- **Éditeur** : World of Numeric SASU (Paris, SIRET 798 353 470 00023), d'après https://worldofnumeric.fr/mentions-legales.html
- **Date de l'analyse** : 16/11/2026. Elle porte sur la version servie ce jour-là, dont les empreintes sont dans `resultats/empreintes-sha256-assets.txt`. Le code peut changer sans préavis.
- **Méthode** : lecture des en-têtes HTTP et du code JavaScript livré au navigateur ; tests dynamiques dans Chromium headless (Playwright) avec journalisation de toutes les requêtes réseau ; jeux de test **100 % fictifs** (FEC, courrier texte, courrier PDF).

## Synthèse

| Promesse affichée | Verdict | Niveau de preuve |
|---|---|---|
| « Tout se passe dans votre navigateur, aucun fichier n'est envoyé » | **Vérifiée** | CSP `connect-src 'none'` sur la page et les workers + aucun appel réseau dans le code + 0 requête sortante observée pendant les tests |
| « Sans rétention : aucun serveur, aucune base de données » | **Vérifiée côté client** | Pas de localStorage/IndexedDB/cookie pour les données ; seuls le thème (localStorage) et l'acceptation de l'avertissement (sessionStorage) sont stockés. Les journaux serveur Apache/OVH (IP, URL) restent hors du périmètre vérifiable. |
| Pas de mesure d'audience sur l'outil | **Vérifiée** | La CSP de la page n'autorise pas Google Analytics/GTM (alors que le reste du site les autorise) ; aucune requête observée |
| Détection des noms, e-mails, téléphones, IBAN, NIR, SIREN/SIRET, adresses | **Globalement vérifiée, avec des trous** | Voir « Limites constatées » |
| PDF : « les pages deviennent des images, rien ne reste caché dessous » | **Vérifiée** | PDF exporté = 1 image JPEG par page, 0 caractère extractible (`pdftotext`), pas de métadonnées |
| FEC : format et soldes conservés | **Vérifiée** | 18 colonnes conservées, CompteNum/Débit/Crédit inchangés |
| Word, PowerPoint, images, PDF scannés | **Vérifiée** | En‑têtes, pieds de page, notes, EXIF et métadonnées traités (§4) |
| Excel | **Partiellement vérifiée** | Onglets masqués et noms d'onglets non traités (§4, L6‑L7) |
| Ré‑identification et table chiffrée | **Vérifiées** | Aller‑retour identique ; AES‑256‑GCM et PBKDF2 en 600 000 itérations, déchiffrement contrôlé de façon indépendante (§4) |
| Nom « Anonymiseur » | **Trompeur sur le plan juridique** | L'outil fait de la **pseudonymisation** (table de correspondance réversible), ce que l'éditeur reconnaît lui‑même |

**Conclusion** : l'outil fait ce qu'il annonce sur la confidentialité (aucune donnée ne quitte le poste, et c'est verrouillé techniquement par la CSP, pas seulement promis). En revanche, la détection n'est pas exhaustive et le résultat reste une donnée personnelle au sens du RGPD. Une relecture humaine reste obligatoire.

## Constats détaillés

### 1. Confidentialité : aucune donnée ne sort

- **En-tête CSP de la page de l'outil** : `connect-src 'none'; form-action 'none'; frame-src 'none'; img-src 'self' data: blob:`. Le navigateur interdit donc tout `fetch`, XHR, WebSocket, beacon et formulaire vers quelque serveur que ce soit, y compris celui de l'éditeur (`resultats/en-tetes-http-app.txt`).
- **Workers** (PDF.js, Tesseract OCR) : servis avec leur propre CSP `default-src 'none'; connect-src 'none'`. Le modèle de langue OCR français est embarqué dans le JS en base64 : rien n'est téléchargé depuis un CDN tiers.
- **Analyse statique** du bundle principal et des modules chargés : 0 occurrence de `fetch(`, `XMLHttpRequest`, `sendBeacon`, `WebSocket`, `EventSource`, `indexedDB`, `serviceWorker`, `document.cookie`, ni de marqueur analytics.
- **Test dynamique** : après le chargement de la page, les seules requêtes sont des `GET` de fichiers statiques (`/assets/*.js`, polices) sans paramètre. Aucun `POST`, aucune requête avec données.

Canaux résiduels non bloqués par la CSP : chargement d'images vers le même domaine (`img-src 'self'`) et navigation de page. Le code n'en fait pas usage pour transmettre des données : il n'y a aucune affectation de `src` vers une URL distante ni de redirection.

### 2. Qualité de la détection (tests sur données fictives)

**FEC** (`jeux-de-test/FEC_test.txt` → `resultats/OUT_FEC_pseudonymise.txt`) : noms des clients, fournisseurs et salariés (y compris dans `EcritureLib`), e-mail, IBAN, téléphone, SIREN et NIR (à clé valide) ont été remplacés. `PieceRef` est également recodé.

**Texte** (`courrier.txt`) : noms, adresse, e-mail, téléphone, IBAN, NIR, SIRET et raison sociale ont été masqués.

### 3. Limites constatées

| # | Constat | Gravité |
|---|---|---|
| L1 | **PDF : IBAN coupé par un retour à la ligne non détecté**. Il reste lisible en clair dans le PDF exporté (`resultats/pdfpage-1.png`) et dans la version Word. Le même IBAN sur une seule ligne est détecté dans le .txt. | **Élevée** |
| L2 | Date de naissance (« Né le 12/05/1985 »), code postal et ville laissés visibles. « Lyon » est seulement **proposé** au masquage (« À masquer aussi ? »). Ce sont des identifiants indirects. | Moyenne |
| L3 | FEC : `CompAuxNum` conserve les 3 premiers caractères du code d'origine (`CDUPONT` → `CDU_CLT_AUX_0001`, `FMARTIN` → `FMA_FRS_AUX_0001`), ce qui fuit des initiales. | Faible à moyenne |
| L4 | Un NIR à clé invalide n'est pas détecté. Ce choix limite les faux positifs, mais il laisse passer un NIR mal saisi. | Faible |
| L5 | Montants, dates et contexte métier sont conservés par construction : la ré‑identification par recoupement reste possible. L'éditeur le signale lui‑même. | Structurelle |

### 4. Tests complémentaires (lot 2) : Office, images, ré‑identification, table chiffrée

Jeux de test fictifs dans `jeux-de-test/lot2/`, résultats dans `resultats/lot2/`. Les résidus ont été recherchés dans **tout le XML** des fichiers produits, métadonnées comprises (`scripts/residus.py`). Aucune requête sortante n'a été observée sur l'ensemble des tests.

| Format / fonction | Résultat | Détail |
|---|---|---|
| **Word** (.docx) | ✅ Très bon | Corps, tableau, **en‑tête et pied de page**, nom découpé sur plusieurs « runs » de mise en forme : tout est masqué. Auteur supprimé, titre pseudonymisé. Restent visibles : code postal et ville. |
| **PowerPoint** (.pptx) | ✅ Très bon | Diapositive et **notes de l'orateur** masquées, auteur supprimé. Restent visibles : code postal et ville. |
| **Excel** (.xlsx) | ⚠️ Partiel | Cellules et commentaires masqués ; nombres, dates et formules conservés ; auteur supprimé. **Mais un onglet masqué n'est pas traité** : `Marie Curie` et son e‑mail restent en clair, sans alerte. **Le nom d'onglet n'est pas traité** non plus (`Salariés Durand`, repris aussi dans `docProps/app.xml`). |
| **Image** (PNG, JPEG) | ✅ Bon | L'OCR local a détecté les 10 informations du texte (légèrement flouté et incliné). Les métadonnées **EXIF** (auteur, description, GPS) sont supprimées. |
| **PDF scanné** | ✅ Bon | Même détection qu'en image. Sortie : 1 image par page, aucun texte extractible. |
| **Ré‑identification** | ✅ Fiable | Aller‑retour sur le Word : texte restauré **strictement identique** à l'original (corps, tableau, en‑tête, pied de page). Sur une « analyse IA » simulée : codes avec ou sans crochets restaurés, code inconnu (`NOM_0099`) signalé. Un code en **minuscules** (`[nom_0001]`) n'est **pas** restauré, et le titre du document reste pseudonymisé. |
| **Table protégée par mot de passe** | ✅ Robuste | AES‑256‑GCM, clé dérivée par PBKDF2‑SHA‑256 en **600 000 itérations**, sel aléatoire de 16 octets et IV de 12 octets, mot de passe de 8 caractères minimum. **Déchiffrement indépendant réussi en Python** (`scripts/decrypt.py`), échec sur mauvais mot de passe (tag GCM invalide) ; l'application affiche « Mot de passe incorrect ». |

Points secondaires :
- L'interface annonce **310 000 itérations**, alors que le code et le fichier produit en utilisent **600 000**. L'écart est sans risque (le réglage réel est plus fort), mais l'affichage est inexact. 600 000 est le seuil OWASP pour PBKDF2‑HMAC‑SHA256 (OWASP Password Storage Cheat Sheet).
- La **table non protégée** (.json ou .xlsx) contient toutes les données d'origine en clair. Elle doit donc être traitée comme le fichier source.
- Excel : un commentaire « collègue de Paul Bernard » a été masqué en bloc comme un seul nom (sur‑masquage). C'est sans risque pour la confidentialité, et la ré‑identification le restaure.

**Limites ajoutées au tableau §3**

| # | Constat | Gravité |
|---|---|---|
| L6 | Excel : **onglets masqués non traités**, données laissées en clair sans avertissement | **Élevée** |
| L7 | Excel : **noms d'onglets non traités** (également présents dans les propriétés du fichier) | Moyenne |
| L8 | Ré‑identification : codes en minuscules non reconnus (cas fréquent si une IA reformule) | Faible |

**Toujours non testé** : gros volumes et performance, .xls/.ods/.odt/.odp/.rtf, HEIC/TIFF multipage, objets incorporés (graphiques, SmartArt, zones de texte Word, champs de formulaire), suivi des modifications et commentaires Word, mode « anonymisation » des paramètres, restauration d'un FEC complet.

## Lecture RGPD / charte IA (pour l'échange avec l'IT et le DPO)

- **Qualification** : le résultat est une donnée **pseudonymisée**, donc toujours une donnée personnelle (RGPD, art. 4(5) et considérant 26 ; CNIL, « L'anonymisation de données personnelles », https://www.cnil.fr/fr/technologies/lanonymisation-de-donnees-personnelles). Envoyer un fichier pseudonymisé à un outil d'IA reste donc un traitement de données personnelles. Il faut un outil approuvé par la charte, une base légale et, le cas échéant, un encadrement des transferts.
- **Rôle de l'éditeur** : aucun traitement côté World of Numeric sur les fichiers (traitement local). L'éditeur n'a pas la qualité de sous‑traitant pour ce contenu, et aucun DPA n'est nécessaire pour cet usage. Ce point est à confirmer par le DPO.
- **Intérêt** : c'est une mesure de minimisation utile en amont d'un usage IA, à condition d'exiger une relecture humaine (L1 montre qu'un oubli est possible).
- **Prérequis avant diffusion interne** :
  1. validation DPO et RSSI ;
  2. figer une version, car le code servi peut changer : à défaut, prévoir un contrôle périodique ou une auto‑hébergement si la licence le permet (à vérifier) ;
  3. consigne de relecture systématique, en particulier pour les PDF et les identifiants coupés ; **pour Excel, afficher et vérifier tous les onglets masqués et renommer les onglets nominatifs avant traitement** ;
  4. conserver la table de correspondance dans un espace sécurisé du cabinet, **en version protégée par mot de passe** ;
  5. rester interdit pour les données de niveau 1 et les catégories particulières, même pseudonymisées, sauf décision contraire de la charte.

## Reproduire l'analyse

```bash
node scripts/run.js  <dossier>                         # charge un FEC, lance, journalise les requêtes
node scripts/run2.js <dossier> FEC_test.txt 'FEC (.txt)' # export du FEC pseudonymisé
node scripts/run3.js <dossier>                         # courrier texte
node scripts/run4.js <dossier>                         # courrier PDF → export « PDF seul »
```

Lot 2 :

```bash
node scripts/gen.js <dossier> <fichier> [mot_de_passe]       # traite un fichier, exporte le résultat et la table (chiffrée si mot de passe)
python3 scripts/residus.py <fichiers Office produits>        # recherche les données résiduelles dans tout le XML
node scripts/restore.js <dossier> <table> <analyse> [mdp]    # ré-identification
python3 scripts/decrypt.py <table_protegee.json> <mdp>       # déchiffrement indépendant (module cryptography)
```

Prérequis : Node.js et Playwright avec Chromium. `pdftotext`, `pdfimages` et `pdfinfo` (poppler-utils) servent aux contrôles du PDF.

> Analyse technique produite avec assistance IA : relecture et validation humaines requises avant toute diffusion à un tiers.
