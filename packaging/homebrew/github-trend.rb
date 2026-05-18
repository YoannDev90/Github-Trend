cask "github-trend" do
  version "@VERSION@"
  
  # Choisir l'architecture
  if Hardware::CPU.intel?
    sha256 "@MAC_INTEL_SHA@"
    url "https://github.com/YoannDev90/Github-Trend/releases/download/v#{version}/github-trend-#{version}-osx-x64.dmg"
  else
    sha256 "@MAC_ARM_SHA@"
    url "https://github.com/YoannDev90/Github-Trend/releases/download/v#{version}/github-trend-#{version}-osx-arm64.dmg"
  end

  name "Github Trend"
  desc "GitHub Trend Tracker based on Avalonia"
  homepage "https://github.com/YoannDev90/Github-Trend"

  app "Github-Trend.app" # Remplace ça par le nom exact du .app dans le .dmg 

  zap trash: [
    "~/Library/Application Support/Github-Trend",
    "~/Library/Preferences/org.yoanndev.githubtrend.plist",
    "~/Library/Saved Application State/org.yoanndev.githubtrend.savedState",
  ]
end
