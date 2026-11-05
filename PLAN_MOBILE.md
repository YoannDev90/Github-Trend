# Plan de migration : Desktop + Android (Avalonia)

Cible : garder le desktop tel quel et ajouter une cible Android, en réutilisant
100 % des services/ViewModels. Pas de refonte Kotlin.

## 1. Restructuration en solution multi-projet

```
Github-Trend.sln
├── Github-Trend.Core/          # class library partagée (net10.0;net10.0-android)
│   ├── App.axaml / App.axaml.cs
│   ├── Views/ ViewModels/ Services/ Database/ ...
├── Github-Trend.Desktop/       # head desktop (WinExe, net10.0)
│   ├── Program.cs (StartWithClassicDesktopLifetime)
│   └── Github-Trend.Desktop.csproj
└── Github-Trend.Android/       # head Android (net10.0-android)
    ├── MainActivity.cs
    ├── AndroidManifest.xml
    └── Resources/
```

Étapes :
1. Créer `Github-Trend.Core` comme class library, déplacer tout le code actuel dedans.
2. Créer `Github-Trend.Desktop` qui référence Core, contient `Program.cs` desktop + `.csproj` WinExe.
3. Vérifier que le build desktop reste vert avant de toucher à Android.

## 2. csproj Core

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0;net10.0-android</TargetFrameworks>
  <OutputType>Library</OutputType>
  <Nullable>enable</Nullable>
  <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Avalonia" Version="12.0.3" />
  <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.3" />
  <PackageReference Include="Avalonia.Fonts.Inter" Version="12.0.3" />
  <PackageReference Include="Svg.Controls.Avalonia" Version="12.0.0.11" />
  <PackageReference Include="Svg.Controls.Skia.Avalonia" Version="12.0.0.11" />
  <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  <PackageReference Include="Serilog" Version="4.0.1" />
  <PackageReference Include="Serilog.Sinks.Console" Version="5.0.1" />
  <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
</ItemGroup>
<!-- retirer Avalonia.Desktop du Core ; le laisser uniquement dans le head Desktop -->
<!-- retirer FrameworkReference Microsoft.AspNetCore.App -> remplacer par packages HTTP ciblés si besoin -->
```

## 3. Head Android

`Github-Trend.Android.csproj` :
```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0-android</TargetFramework>
  <SupportedOSPlatformVersion>21.0</SupportedOSPlatformVersion>
  <ApplicationId>com.githubtrend.app</ApplicationId>
  <ApplicationVersion>1</ApplicationVersion>
  <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Avalonia.Android" Version="12.0.3" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\Github-Trend.Core\Github-Trend.Core.csproj" />
</ItemGroup>
```

`MainActivity.cs` :
```csharp
using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

[Activity(Label = "Github-Trend", Theme = "@style/MyTheme.NoActionBar",
          MainLauncher = true,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App> { }
```

`MainApplication.cs` (optionnel, pour init Avalonia) + `AndroidManifest.xml` avec permissions Internet.

Prérequis dev : `dotnet workload install android`, SDK Android, JDK 17.

## 4. Adaptations du code partagé

### 4.1 Point d'entrée App
Dans `App.axaml.cs`, gérer le lifetime mobile :
```csharp
public override void OnFrameworkInitializationCompleted()
{
    _ = ThemeService.Default.InitializeAsync();
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = new MainWindow();
    }
    else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
    {
        singleView.MainView = new MainView(); // UserControl racine
    }
    base.OnFrameworkInitializationCompleted();
}
```
Créer `Views/MainView.axaml` (UserControl) qui héberge le contenu de MainWindow, pour réutiliser sur mobile.

### 4.2 Chemins de fichiers (important)
Remplacer tous les `AppContext.BaseDirectory` par un helper centralisé `AppPaths` :
- **Config / assets embarqués** (appsettings.json, graphql, icônes) : lire via `Assembly.GetManifestResourceStream` ou garder `BaseDirectory` (fonctionne sur Android pour les assets copiés).
- **Données utilisateur** (DB SQLite, tokens, logs, cache images) : utiliser `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` (déjà le cas pour DB/tokens) — OK sur Android.
- Logs Serilog : déjà `AppContext.BaseDirectory` → à migrer vers `LocalApplicationData/Github-Trend/logs`.

Fichiers concernés : `Program.cs` (ConfigureLogging), `AppConfig.cs`, `GitHubAuthOptions.cs`, `GraphQlQueryLoader.cs`, `PopularLanguagesService.cs`, `SvgImage.cs`, `SettingsWindowViewModel.cs`.

### 4.3 UI / navigation mobile
- `SettingsWindow` (Window) → transformer en `UserControl` navigué dans le `MainWindow`/`MainView` (pas de fenêtres multiples sur mobile).
- `DialogHelper` : `ShowDialog(owner)` fonctionne avec `TopLevel`; vérifier que tous les appels passent un `Window`/`TopLevel` valide. Sur mobile, préférer des dialogs in-app (ContentDialog) plutôt que des fenêtres modales.
- Responsive : ajouter `VisualStateManager` / breakpoints pour réduire la sidebar (`SidebarPanel`) en drawer sur petit écran.
- Safe areas iOS non concerné (pas d'iOS dans cette phase), mais prévoir marges Android (status bar).

### 4.4 HTTP / auth
- `Microsoft.AspNetCore.App` framework reference → retirer du Core, ajouter uniquement les packages nécessaires (`System.Net.Http.Json`) dans le head si besoin. Vérifier `GitHubDeviceFlowAuthService` (pas de dépendance ASP.NET probable).
- Vérifier `HttpClientFactory` et les timeouts réseau sur mobile (réseau instable).

### 4.5 SQLite
- `Microsoft.Data.Sqlite` fonctionne sur Android. Vérifier que le chemin DB (`LocalApplicationData`) est writable (déjà le cas). Tester la création du fichier au premier lancement.

### 4.6 Serilog fichier
- Sur Android, écrire dans `LocalApplicationData/Github-Trend/logs` (pas BaseDirectory). Adapter `ConfigureLogging`.

### 4.7 Assets / icônes
- `Assets\Icons\**` en `AvaloniaResource` → OK. Les `.svg` copiés en Content → vérifier le chemin de chargement dans `SvgImage.cs` (utilise `BaseDirectory/Assets/Icons`), à adapter si besoin.

## 5. Build & test

```bash
# Workload
dotnet workload install android

# Desktop (doit rester vert à chaque étape)
dotnet build Github-Trend.Desktop/Github-Trend.Desktop.csproj

# Android
dotnet build Github-Trend.Android/Github-Trend.Android.csproj -f net10.0-android
dotnet run -p Github-Trend.Android/Github-Trend.Android.csproj -f net10.0-android
```

Tests sur émulateur Android (API 21+) + device physique.

## 6. Ordre d'exécution recommandé

1. Extraire Core + head Desktop, valider build desktop. (½ journée)
2. Créer head Android squelette, lancer l'app (écran vide). (½ journée)
3. Brancher `App.axaml.cs` sur `ISingleViewApplicationLifetime` + créer `MainView`. (1 jour)
4. Migrer chemins (AppPaths) et logs. (½ journée)
5. Transformer SettingsWindow en vue naviguée. (1 jour)
6. Responsive (sidebar drawer, tailles). (1-2 jours)
7. Tests & correctifs (dialogs, SQLite, auth, réseau). (2-3 jours)

**Total estimé : ~1 semaine** pour un Android fonctionnel, réutilisant toute la logique existante.

## 7. Risques / points d'attention

- Dialogs modaux (`ShowDialog`) : comportement différent sur mobile; tester chaque flow.
- `ApplicationIcon` / manifest Android à fournir (icônes mipmap).
- Thème Fluent : OK sur Android.
- Performance du rendu SVG sur petit écran : surveiller.
- Pas d'iOS dans cette phase → si besoin plus tard, ajouter head iOS (même Core, même pattern).

## 8. Ce qu'on ne fait PAS dans cette phase
- Pas de Kotlin, pas de KMP.
- Pas d'iOS.
- Pas de refactor de la logique métier (services/VMs intacts).
- Pas de changement de la structure XAML desktop (on ajoute des vues mobiles en parallèle).
