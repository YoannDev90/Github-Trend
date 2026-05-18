# Development Guide

This guide covers everything you need to know to develop, build, and contribute to Github Trend.

## 🛠️ Requirements

- **.NET 10 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git** - for version control
- **A desktop environment** supported by Avalonia (Windows, macOS, Linux)

## 🚀 Getting Started

### Clone and Setup

```bash
git clone https://github.com/yoannflorent/Github-Trend.git
cd Github-Trend
dotnet restore
```

### Run the Application

```bash
dotnet run
```

### Build the Application

```bash
dotnet build
```

## 📦 Technology Stack

- **Framework**: [Avalonia UI](https://avaloniaui.net/) - Cross-platform desktop framework for .NET
- **Language**: C# (.NET 10)
- **Architecture**: MVVM (Model-View-ViewModel) pattern
- **Authentication**: GitHub OAuth Device Flow

## 📁 Project Structure

### Core Application Files

- **`App.axaml` / `App.axaml.cs`**
  - Application bootstrap and entry point
  - Global styling and resource definitions
  - Application-level event handlers

- **`MainWindow.axaml` / `MainWindow.axaml.cs`**
  - Main UI layout and UI logic
  - Parent window for the entire application
  - Data binding setup

- **`Program.cs`**
  - Entry point for the application
  - Window creation and initialization

- **`Constants.cs`**
  - Centralized configuration repository
  - GitHub API endpoints
  - OAuth configuration (client ID, client secret placeholder)
  - Data source URLs

### Architecture Directories

#### ViewModels/
MVVM view models that manage application state and commands:

- **`MainWindowViewModel.cs`** - Primary view model for the main window
  - Manages trending repository list
  - Handles filtering and language selection
  - Orchestrates data loading
  
- **`LanguageOptionViewModel.cs`** - Represents selectable programming languages
- **`TimeRangeOptionViewModel.cs`** - Represents time range filters
- **`RelayCommand.cs`** - ICommand implementation for binding user interactions to view model methods

#### Models/
Data models representing domain entities:

- **`GithubTrendingRepository.cs`** - Trending repository from the trending feed
- **`GitHubUserProfile.cs`** - User profile data from GitHub API
- **`GithubTrendingAuthor.cs`** - Contributor information
- **`GithubContributorPreview.cs`** - Lightweight contributor data for display
- **`GithubColorEntry.cs`** - Programming language color mapping
- **`GithubColorsCatalog.cs`** - Collection of language colors
- **`GitHubAuthSession.cs`** - Active authentication session
- **`GitHubAuthTokenRecord.cs`** - Stored token record

#### Services/
Service layer providing business logic and external integrations:

- **`GitHubApiClient.cs`** - Direct GitHub REST API interactions
  - Repository details fetching
  - Star/watch operations
  - User profile queries

- **`GithubTrendingService.cs`** - Fetches trending repository data
  - Queries the trending endpoint
  - Maps raw data to domain models

- **`GithubColorsService.cs`** - Language color information
  - Fetches language-to-color mappings
  - Caches color data locally

- **`GitHubAuthenticationService.cs`** - Primary OAuth flow orchestration
  - Manages authentication state
  - Delegates to specialized auth services

- **`GitHubDeviceFlowAuthService.cs`** - Device flow OAuth implementation
  - Initiates device flow
  - Polls for completion
  - Exchanges code for token

- **`GitHubLoopbackAuthServer.cs`** - Local loopback server for legacy flows
  - Handles OAuth callbacks

- **`GitHubAuthTokenStore.cs`** - Token persistence layer
  - Loads and saves tokens locally
  - Works with crypto service for protection

- **`GitHubTokenProtector.cs`** - Cryptographic token protection
  - Encrypts tokens before storage
  - Decrypts tokens on load

- **`GitHubTokenRefreshService.cs`** - Token refresh logic
  - Handles expired token scenarios
  - Requests new tokens when needed

- **`SelectedLanguagesStore.cs`** - User language filter preferences
  - Persists selected language filters

- **`GithubRepositoryDetailsService.cs`** - Repository enrichment
  - Combines data from trending feed with GitHub API metadata
  - Fetches contributors
  - Handles license and topic information

#### Controls/
Custom Avalonia controls:

- **`SvgImage.cs`** - Custom control for rendering SVG images
  - Used for icons (delete, github, open, refresh, save, star, watch)

#### Localization/
Internationalization and localization:

- **`LocalizationService.cs`** - Service for managing localized strings
- **`Resources.resx`** - English resource strings
- **`Resources.fr.resx`** - French resource strings

### Configuration Files

- **`Github-Trend.csproj`** - Project file with NuGet package references
- **`Github-Trend.sln`** - Solution file
- **`app.manifest`** - Application manifest for Windows

### Assets

- **`Assets/Icons/`** - SVG icon files used throughout the UI
  - `delete.svg`, `github.svg`, `open.svg`, `refresh.svg`, `save.svg`, `star.svg`, `watch.svg`

## 🔌 External Data Sources

### Trending Repositories
- **Endpoint**: `https://githubtrending.lessx.xyz/trending`
- **Purpose**: Provides list of trending repositories
- **Scope**: Used to populate the main trending feed

### Language Colors
- **Endpoint**: `https://raw.githubusercontent.com/ozh/github-colors/master/colors.json`
- **Purpose**: Maps programming languages to their canonical colors
- **Scope**: Visual differentiation in the UI

### GitHub REST API
- **Base URL**: `https://api.github.com`
- **Purpose**: Repository details, contributors, licensing, starring, watching
- **Authentication**: OAuth user access token
- **Scopes Required**:
  - `public_repo` - for querying public repository data
  - `notifications` - for watch functionality

## 🔐 Authentication Architecture

### OAuth Device Flow

The app implements GitHub's Device Authorization Grant flow:

```
1. App initiates device flow → receives device_code, user_code, verification_uri
2. User sees user_code and visits verification_uri on any device
3. User approves the app
4. App polls the authorization endpoint until approval
5. Server returns access_token
6. Token is encrypted and stored locally
```

### Token Storage

- Tokens are stored in the user's profile directory
- Encrypted using DPAPI (Data Protection API)
- Loaded on application startup for offline-first experience

## 🚦 MVVM Pattern

The application follows the MVVM pattern:

### Model
- Data models in `Models/` directory
- Represent domain entities and API responses

### ViewModel
- Located in `ViewModels/` directory
- Exposes Collections and Commands to the View
- Manages application state
- Handles business logic coordination

### View
- XAML files (`*.axaml`)
- Defines the UI layout
- Binds to ViewModel properties via DataContext

### Command Pattern
- `RelayCommand` implements `ICommand` interface
- Allows binding UI events to ViewModel methods
- Supports parameter passing and can-execute checks

## 🛠️ Building and Publishing

### Local Build

```bash
dotnet build
```

### Publish Packages

Run the provided script to publish to multiple platforms:

```bash
./scripts/publish-packages.sh
```

This creates:
- **FDD (Framework-Dependent Deployment)** - Requires .NET runtime
- **SCD (Self-Contained Deployment)** - Includes runtime, larger size
- **Packages** - Compressed archives for distribution

Output directories:
- `publish/fdd/` - Framework-dependent builds
- `publish/scd/` - Self-contained builds
- `publish/packages/` - Distribution packages

### Supported Platforms

- Windows (x64, arm64)
- Linux (x64, arm64)
- macOS (x64, arm64)

## 🔄 Data Flow

### Loading Trending Repositories

```
1. MainWindowViewModel.OnInitialized()
2. GithubTrendingService.GetTrendingRepositories()
3. Trend endpoint returns repository list
4. For each repository:
   a. GithubRepositoryDetailsService enriches with GitHub API data
   b. GithubApiClient fetches repository details and contributors
   c. GithubColorsService provides language color
5. UI binds to the enriched repository collection
```

### Starring a Repository

```
1. User clicks star button
2. RelayCommand in ViewModel executes
3. GitHubApiClient.StarRepository() called
4. Requires valid OAuth token with public_repo scope
5. Updates UI star state on success
6. Shows error message if authorization fails
```

## 🧪 Key Implementation Details

### Language Filtering

- Dynamically populated from trending data
- Filtered case-insensitively via search box
- Stored in `SelectedLanguagesStore` for persistence

### Time Range Selection

- Available options: Daily, Weekly, Monthly, All-time
- Updates trending data when changed
- Bound to `TimeRangeOptionViewModel`

### Error Handling

- GitHub API errors show user-friendly messages
- Token expiration triggers re-authentication prompts
- Network errors are handled gracefully

### Performance Considerations

- Contributor avatars loaded on demand
- Repository banners fetched asynchronously
- Language colors cached after first load

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/your-feature`
3. Make your changes following the MVVM pattern
4. Test locally: `dotnet run`
5. Commit with clear messages
6. Push to your fork
7. Create a Pull Request

## 🤖 Transparency & AI Usage

This project uses AI as a development aid for code implementation and documentation. However, **all AI-generated content is carefully reviewed by the project maintainer** to ensure quality, accuracy, and adherence to project standards.

### Contributing with AI

If you use AI tools to assist with your contributions (code, documentation, tests, etc.), please:

1. **Clearly review** the AI-generated content for correctness and quality
2. **Test thoroughly** any code before submitting
3. **Cite or mention** in your PR if significant portions were AI-assisted
4. **Be prepared** to refine and iterate based on feedback

We welcome contributions from developers of all skill levels, and using AI as a tool is perfectly acceptable—we just ask that you take responsibility for reviewing and understanding what you're submitting.

**Quality over perfection**: It's okay if your contribution isn't perfect; what matters is that you've put thought into it and are willing to improve it through the review process.

## 📝 Notes for Developers

- **Configuration**: All GitHub endpoints and credentials are in `Constants.cs`
- **Styling**: Global styles defined in `App.axaml`
- **Localization**: Add strings to `.resx` files and use `LocalizationService`
- **Token Security**: Never log or expose authentication tokens
- **API Rate Limiting**: Be aware of GitHub API rate limits (60 requests/min unauthenticated, 5000/hour authenticated)

## 🐛 Debugging

### Enable Debug Output

Set environment variable (varies by platform):
- Windows: `set AVALONIA_LOG_LEVEL=Debug`
- Linux/macOS: `export AVALONIA_LOG_LEVEL=Debug`

Then run:
```bash
dotnet run
```

### Using Rider or Visual Studio

- Set breakpoints in the IDE
- Press F5 to run with debugger attached
- Use the Variables and Locals windows to inspect state

## 📚 Resources

- [Avalonia Documentation](https://docs.avaloniaui.net/)
- [GitHub OAuth Documentation](https://docs.github.com/en/developers/apps/building-oauth-apps/authorizing-oauth-apps)
- [C# Documentation](https://docs.microsoft.com/en-us/dotnet/csharp/)
- [MVVM Pattern Guide](https://docs.microsoft.com/en-us/archive/msdn-magazine/2009/february/patterns-wpf-apps-with-the-model-view-viewmodel-design-pattern)


