# Github Trend

Github Trend is a desktop app built with [Avalonia UI](https://avaloniaui.net/) to browse GitHub trending repositories with a richer, card-based interface.

## Features

- Browse GitHub trending repositories by time range:
  - Daily
  - Weekly
  - Monthly
  - All time
- Filter the trending list by programming language
- Search available languages quickly
- Open any repository in your default browser
- Display a visual repository banner
- Show the repository title and description
- Show language, license, stars, forks, and last updated information
- Display contributor avatars and a `+X others` badge
- Show repository topics as badges

## Screenshots

![The first screenshot](Screenshots/1.png)
![The second screenshot](Screenshots/2.png)

## Requirements

- .NET 10 SDK
- A desktop environment supported by Avalonia

## Running the app

From the project root:

```bash
dotnet restore
dotnet run
```

## Build

```bash
dotnet build
```

## Project structure

- `MainWindow.axaml` - main UI layout
- `MainWindow.axaml.cs` - window behavior and repository opening action
- `ViewModels/` - view models and commands
- `Models/` - repository and contributor data models
- `Services/` - GitHub and colors data fetching services

## Data sources

- Trending repositories are fetched from:
  - `https://githubtrending.lessx.xyz/trending`
- Language colors are fetched from:
  - `https://raw.githubusercontent.com/ozh/github-colors/master/colors.json`
- Repository details, contributors, topics, and licenses are fetched from the GitHub API.

## Notes

- Repository banners are loaded from GitHub OpenGraph images.
- Contributor avatars are loaded on demand from the GitHub API data.
- The app is designed to combine a mobile-style visual layout with web-style repository details.

