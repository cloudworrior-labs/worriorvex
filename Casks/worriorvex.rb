cask "worriorvex" do
  arch arm: "arm64", intel: "x64"

  version "0.1.0"
  sha256 arm:   "50abfd1d4298bef68f9457708555b77d006245b1aa5831b6bffde6cdd61b4dda",
         intel: "bf5f98414762752eea975125fa09194df8ec23b53b6e988dabb89eb60b60fa6e"

  url "https://github.com/cloudworrior-labs/worriorvex/releases/download/v#{version}/WorriorVex-#{version}-macos-#{arch}.dmg"
  name "WorriorVex"
  desc "Local-first note-taking app and personal knowledge workspace"
  homepage "https://github.com/cloudworrior-labs/worriorvex"

  depends_on macos: ">= :monterey"

  app "WorriorVex.app"

  # Notes live in ~/Library/Application Support/WorriorVex. Uninstalling never removes them.

  caveats <<~EOS
    WorriorVex is not yet signed with an Apple Developer ID. If macOS refuses to open it, run:

      xattr -dr com.apple.quarantine /Applications/WorriorVex.app
  EOS
end
