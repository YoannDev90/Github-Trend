# GitHub Trend

Desktop app that shows trending GitHub repos. Built it because I got tired of opening the browser every time I wanted to see what's trending.

## What it does

- Shows trending repos from GitHub (daily, weekly, monthly, all time)
- Filter by language (there are a lot of languages, there's a search box)
- Lets you star/watch repos if you log in with GitHub
- Shows repo details: description, topics, contributors, stars, forks, license, language, when it was last updated
- Click a repo to open it in your browser
- Login uses GitHub's OAuth device flow so you don't have to mess with tokens

## How to get it

Grab the latest release from the [releases page](https://github.com/YoannDev90/Github-Trend/releases).

| OS      | Architectures  | Status                              |
|---------|----------------|-------------------------------------|
| Windows | x64, arm64     | Works (installer)                   |
| Linux   | x64, arm64     | Works (binary, DEB, RPM, AppImage)  |
| macOS   | x64, arm64     | Haven't tested, need someone to try |

### Windows

Download the `.exe` and run it.

### Linux

```bash
tar -xzf Github-Trend-linux-x64.tar.gz
cd Github-Trend-linux-x64
./Github-Trend
```

## Screenshots

![Screenshot](Assets/screenshots/1.png)
![Screenshot](Assets/screenshots/2.png)

