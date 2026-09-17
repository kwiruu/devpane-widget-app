# Dev Pane

Developer cards for the Windows 11 Widgets Board. Press **Win + W**, click **+**, and pin the Dev Pane cards you want next to Weather.

> **Status:** early development (milestone M1). You can build and run it yourself; there's no release yet.

## Cards

| Card | Shows | Status |
|---|---|---|
| System stats | CPU, RAM, GPU, disk, and network | In progress (M1) |
| Local dev | Dev servers on your ports, dev tool memory, WSL/Docker memory | In progress (M1) |
| GitHub | Reviews waiting for you, your PRs with CI status, assigned issues, workflow runs; one card per repo if you like | In progress (M2) |
| GitHub contributions | Your contribution calendar, current and longest streak | In progress (M2) |
| Claude Code usage | Current session and weekly usage with reset times, last 7 days at API prices | In progress |
| Vercel | Latest deployments and production status per project; one card per team or project if you like | In progress |

## Build and run

### Requirements

- Windows 11 with the Widgets Board turned on
- Visual Studio 2026 with the **WinUI application development** workload
- .NET 10 SDK
- **Developer Mode** turned on: Settings → System → Advanced → Developer Mode

### Steps

1. From the repository folder, run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\deploy-dev.ps1
   ```

   The script builds Dev Pane, copies it to `.deploy`, and installs it for your user.
2. Press **Win + W**, click **+**, find **Dev Pane**, and pin **System stats** or **Local dev**.

Add `-RestartWidgets` after changing cards in `Package.appxmanifest`, so the Widgets Board picks up the change.

**Optional:** to give the System stats card Task Manager's icon on your PC, run `dotnet run tools/extract-taskmanager-icon.cs` before deploying. The icon is Microsoft's artwork, so it's saved to the gitignored `Assets/Local` folder and never committed or shipped; builds without it use Dev Pane's own icon.

### Troubleshooting

- **Dev Pane isn't in the + list:** run the script again with `-RestartWidgets`.
- **The build fails on `resources.pri`:** the Widgets Board still has an old copy open. Run again with `-RestartWidgets`.
- **A card is blank or stuck:** check the provider log at
  `%LOCALAPPDATA%\Packages\DevPane_<id>\LocalState\devpane-widgets.log`.

## License

Dev Pane is **source-available**, not open source. It's licensed under the [PolyForm Shield License 1.0.0](LICENSE.md).

In plain terms:

- You can use Dev Pane for free, including at work.
- You can read, study, and change the code, and contribute back.
- You **can't** sell Dev Pane or use its code to build a product that competes with it.

The license text is the authority; this summary isn't legal advice.

### Name and logo

The name "Dev Pane" and its logo aren't covered by the license. Forks must use a different name and logo.

### Third-party logos

Some cards show other companies' logos, which belong to them and aren't covered by the license:

- **GitHub mark** on the GitHub cards, used under [GitHub's logo guidelines](https://github.com/logos).
- **Claude Spark** on the Claude Code usage card, from [Anthropic's press kit](https://www.anthropic.com/news). Anthropic doesn't publish terms for using it in other apps, so get Anthropic's permission before a public release, or replace it with Dev Pane's own icon.
- **Vercel logomark** on the Vercel card. [Vercel's brand guidelines](https://vercel.com/geist/brands) require Vercel's written permission to use it in other apps, so get permission before a public release, or replace it with Dev Pane's own icon.

Octicons and other third-party code are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first: all contributors sign a [Contributor License Agreement](CLA.md).
