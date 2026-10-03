cask "worriorvex" do
  arch arm: "arm64", intel: "x64"

  version "0.2.0"
  sha256 arm:   "5ee8145307b3e313167128c4017d658b062fa95d2eb335dcae822791f5908377",
         intel: "7d3b66159099fa1489c4d0d5af69e25f0462a5975b6db9f91b47f64e2750b9a5"

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
