# Contributing to Kerbal Space Directorate

Thanks for helping. Bug reports, playtest notes and pull requests are all welcome.

## Reporting a bug

Open an issue with the **Bug report** template. The most useful things to include:

- What you did, what you expected, and what happened.
- Your `KSP.log` (in the KSP folder). Every line from this mod starts with `[KSA]`.
- Your KSP version and mod list (CKAN: File > Export modlist).
- Whether AI features were on, and which model (never paste your API key).

## Pull requests

1. Fork the repo and branch from `main`.
2. Follow the layout in the README's "Building from source" section. The code is split in two:
   - `KSA.Core` is plain C# with **no** KSP or Unity references. Logic and rules go here.
   - `KSA.KSP` is the adapter that talks to the game.
3. Run `dotnet test KSA.Core.Tests` and make sure everything passes. Add a test for new Core logic.
4. Keep content data-driven: medals, goals, careers and balance numbers belong in `GameData/KSA/Configs/*.cfg`, not in code.
5. Don't include anything from KSP itself (Squad/Take-Two code, textures, models) or from other mods unless their license allows it and you say so in the PR.
6. Never commit API keys. `ksa_settings.cfg` is ignored for that reason; only the example file is tracked.
7. The mod must never require the Making History or Breaking Ground DLC.

Pull requests are squash-merged, so one PR becomes one commit.

## Sign-off and licensing of contributions

Every commit must be signed off, which certifies the [Developer Certificate of Origin](https://developercertificate.org/): that you wrote the change or otherwise have the right to submit it. Add the line with `git commit -s`:

```
Signed-off-by: Your Name <you@example.com>
```

By submitting a contribution you agree that:

- It is licensed under the project's [MIT License](LICENSE).
- **Blackbox Entertainment may also use, modify and relicense it**, including under other terms, in this mod and in future products built from `KSA.Core`. You keep the copyright on your own work.

If you can't agree to that, please say so in the PR before it is merged. We may still be able to take the change outside `KSA.Core`.

## Questions

Use GitHub Discussions for questions and ideas, and Issues for bugs.
