# EmbyGitHubPlugins

Emby Server plugin that installs and updates other Emby plugins straight from their **GitHub releases**, from the Emby dashboard.

- Add a repository (`owner/repo` or a GitHub link). The page shows its latest release and whether it is installed, up to date or has an update.
- **Install** / **Update to vX** downloads the release's `.dll`, checks GitHub's SHA-256, and puts it in Emby's plugins folder (keeping `Name.dll.bak`).
- **Remove** takes the repository off the list and deletes its DLL.
- **Restart Emby now** activates the changes.
- Scheduled task **Update GitHub plugins** (daily, 04:00) checks all repositories, and installs updates when "Install updates automatically" is on.
- Add `Riomdrion/EmbyGitHubPlugins` to the list to keep this plugin itself up to date.

> A plugin runs with all of Emby's rights. The page is admin-only; only add repositories you trust.

## Requirements for a repository

- A published GitHub release (the latest one is used) with a `.dll` attached.
- One DLL, or several where one is named `<repo>.dll`.
- Tag like `v1.2.3` or `1.2.3`; it is compared with the installed plugin's assembly version.

Without a token GitHub allows 60 API requests per hour; release lookups are cached for 5 minutes. A fine-grained token with read-only *Contents* access raises the limit and allows private repositories.

## Requirements

- Emby Server 4.9.1 or newer (built against `MediaBrowser.Server.Core` 4.9.1.90; also compiles against the 4.10.0.40 server assemblies).

## Build

```sh
dotnet build src/EmbyGitHubPlugins -c Release
dotnet test
```

## First install on a Synology (Emby package), without SSH

DSM > Control Panel > Task Scheduler > Create > Scheduled Task > User-defined script, user `root`:

```sh
DST=/volume1/@appdata/EmbyServer/plugins
curl -sSL -o "$DST/EmbyGitHubPlugins.dll" https://github.com/Riomdrion/EmbyGitHubPlugins/releases/latest/download/EmbyGitHubPlugins.dll
chown --reference="$DST" "$DST/EmbyGitHubPlugins.dll"
chmod 644 "$DST/EmbyGitHubPlugins.dll"
```

Run it once, then restart Emby (Dashboard > power icon > Restart). After that, everything goes through Dashboard > GitHub Plugins.

## Releasing

```sh
git tag v0.2.0 && git push origin v0.2.0
```

GitHub Actions runs the tests, builds the DLL with that version and attaches it to a release.
