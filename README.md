# GitHub Trend

Browse GitHub's trending repositories in style with a rich, card-based desktop interface. Stay on top of what's hot in
the developer world, right from your desktop.

## ✨ What you can do

- **Browse trending repositories** across different time ranges (daily, weekly, monthly, all-time)
- **Filter by programming language** and quickly search through languages
- **Star and watch repositories** directly from the app using your GitHub account
- **View rich repository details** including:
    - Repository description and topics
    - Contributor previews with avatars
    - Stars, forks, license, and language info
    - Last update date and repository banners
- **Open repositories in your browser** with a single click
- **Sign in securely** through GitHub OAuth without managing tokens manually

## 📥 Download and Installation

### Quick Start: Pre-built Packages

Pre-built packages are available for your platform:

- **Windows** (x64, arm64)
- **Linux** (x64, arm64)

| OS        | Architectures | Status                                 |
|-----------|---------------|----------------------------------------|
| ✅ Windows | x64, arm64    | Supported (Auto-extracting installer)  |
| ✅ Linux   | x64, arm64    | Supported (Binary, DEB, RPM, AppImage) |
| ⏳ macOS   | x64, arm64    | Waiting for a contributor/tester       |

1. Download the latest release from the [releases page](https://github.com/YoannDev90/Github-Trend/releases)
2. Extract the package for your platform

#### Windows

```
1. Download the _setup.exe file
2. Run the installer and follow the instructions
```

#### Linux

```bash
tar -xzf Github-Trend-linux-x64.tar.gz
cd Github-Trend-linux-x64
./Github-Trend
```

## 🔐 Authentication

The app uses GitHub's OAuth device flow for secure authentication:

- No need to generate or manage personal access tokens
- Your authentication token is stored locally and protected
- Simply sign in through your GitHub account using the app

**Having trouble with GitHub actions?**

- If you get an authorization error, try signing out and signing back in to refresh your credentials.

## 🎨 Interface Features

- **Dark-themed cards** for easy browsing
- **Information-dense layout** that's still easy to scan
- **Language color indicators** for quick visual recognition
- **Contributor avatars** to see who's behind the code
- **Fast language filtering** to find what you're interested in

## 📷 Screenshots

![Screenshot](Screenshots/1.png)
![Screenshot](Screenshots/2.png)

## 📋 Requirements

- Windows, macOS, or Linux desktop environment
- Internet connection to fetch trending data

## ❓ FAQ

**Q: Is my GitHub token secure?**  
A: Yes. Your authentication token is stored locally on your machine and is cryptographically protected before being
saved to disk.

**Q: Do I need to install .NET?**  
A: No, the pre-built packages include everything you need. Only required if building from source.

**Q: Why do some actions require re-authentication?**  
A: Some GitHub actions require specific permissions. If you see an authorization error, signing out and back in will
refresh your permissions.

## 🤖 Transparency

This project uses AI to assist with code implementation and documentation writing. However, all AI-generated content is
carefully reviewed and refined by the project maintainer to ensure accuracy and quality.

If you find any issues, inaccuracies, or areas for improvement in the documentation or code, please feel free to open an
issue or submit a pull request : contributions from all skill levels are welcome!

