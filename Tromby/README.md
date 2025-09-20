# Tromby

## Présentation
Tromby est une application overlay Windows 11 toujours au premier plan, conçue en WinUI 3 (.NET 8) pour orchestrer des interactions multimodales avec des modèles de langage (LLM) multi-fournisseurs, des services vocaux hybrides et des actions locales sécurisées. Ce lot livre le socle technique et la structure de solution nécessaires pour poursuivre l’implémentation produit.

## Prérequis
- Windows 11 22H2 ou supérieur avec Windows App SDK 1.4+.
- Visual Studio 2022 17.8+ avec charges de travail **.NET Desktop Development** et **Développement avec C++ pour la validation MSIX**.
- Kit de développement .NET 8 SDK.
- Accès aux SDK ou API tiers (OpenAI, xAI, Google Gemini, Mistral, Edge TTS) selon les fonctionnalités activées.

## Installation
1. Cloner le dépôt dans `C:\Dev\trombone`.
2. Ouvrir `Tromby.sln` dans Visual Studio.
3. Restaurer les packages NuGet (menu **Build > Restore NuGet Packages** ou `dotnet restore`).
4. Vérifier que la machine est configurée pour le déploiement WinUI 3 (Windows App SDK installé, certificats de signature MSIX si besoin).

## Configuration
- Les fichiers de configuration se trouvent sous `config\`.
- Profils disponibles :
  - **Prod** : utiliser `config\profiles\prod\appsettings.json` (LLM distants privilégiés, quotas stricts).
  - **Démo** : utiliser `config\profiles\demo\appsettings.json` (mode dry-run, échantillons de réponses, quotas relâchés).
  - **Dev** : utiliser `config\profiles\dev\appsettings.json` (journalisation détaillée, bypass de certaines limites).
- Budgets, personas et politiques sont respectivement définis dans `config\budgets`, `config\persona`, `config\policy`.
- Les chemins sont consommés par `ConfigurationAdapter` et peuvent être surchargés via variables d’environnement `TROMBY_CONFIG_PATH` et `TROMBY_PROFILE`.

## Build & Lancement
1. Sélectionner le profil de solution **Release** ou **Debug** selon le besoin.
2. Cibler l’architecture `x64`.
3. Lancer `dotnet build` ou l’action **Build Solution** de Visual Studio pour générer les projets WinUI 3 (.NET 8).
4. Pour le packaging, utiliser le projet d’emballage MSIX (configuration dans Visual Studio : **Publish > Create App Packages**).
5. Pendant le développement, démarrer `TrombyApp` (F5) : l’overlay s’ouvre en mode CompactOverlay et peut être repositionné.

## Structure du projet
- `src/TrombyApp` : application WinUI 3, MVVM, services d’orchestration, LLM, sécurité, voix, outils.
- `config/` : paramètres d’application, budgets, personas, politiques, profils.
- `tests/Tromby.Tests` : projet de tests unitaires (xUnit) couvrant orchestrateur, scheduler et services clés.
- `build/` (à créer) : scripts d’emballage MSIX et pipelines CI/CD.

## Dépannage
- **Erreur de restauration NuGet** : vérifier la connectivité et la version du SDK .NET 8, exécuter `dotnet nuget locals all --clear`.
- **Échec de lancement WinUI 3** : s’assurer que Windows App SDK Runtime est installé et que le certificat de débogage est approuvé.
- **Problèmes MSIX** : réinstaller l’application précédente avec `Add-AppxPackage -Remove`, vérifier la signature et réinitialiser le cache déploiement (`wsreset.exe`).
- **Services LLM indisponibles** : activer le mode Démo ou Éco dans la configuration pour s’appuyer sur les providers locaux.

## Licence
Licence à définir.
