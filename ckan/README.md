# CKAN listing

`KerbalSpaceDirectorate.netkan` is the metadata CKAN's bot uses to index each GitHub release.

To list the mod (once, by pull request): copy the file to `NetKAN/KerbalSpaceDirectorate.netkan` in a fork of
https://github.com/KSP-CKAN/NetKAN and open a pull request. After it is merged the bot checks this repo's GitHub
releases every few hours; every new release (zip from `tools\package.ps1` attached) appears in CKAN on its own.

- Version comes from `GameData/KSA/KSA.version` inside the zip (`$vref: ksp-avc`), so bump it with each release.
- `release_status: testing` while the mod is pre-1.0; change to `stable` at the M7 release.
- The release asset must be the zip from `tools\package.ps1` (it holds `GameData/KSA`).
