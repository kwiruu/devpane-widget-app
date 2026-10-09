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
| Jira | Issues assigned to you, your current sprint, reported, or watched, with counts by status; one card per view or project if you like | In progress |

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

**After changing how a card looks**, redraw the previews the **+** dialog shows, and commit the images:

```powershell
powershell -ExecutionPolicy Bypass -File tools\render-previews.ps1
```

They're drawn from the cards' own code with made-up sample data (`src/DevPane.Widgets/Previews`), using the Widgets Board's own renderer and styles, so they always match the real cards and never show a real account.

**Optional:** to give the System stats card Task Manager's icon on your PC, run `dotnet run tools/extract-taskmanager-icon.cs` before deploying. The icon is Microsoft's artwork, so it's saved to the gitignored `Assets/Local` folder and never committed or shipped; builds without it use Dev Pane's own icon.

### Publishing to the Microsoft Store

```powershell
powershell -ExecutionPolicy Bypass -File tools\pack-store.ps1
```

This builds the unsigned x64 + ARM64 `.msixupload` that Partner Center takes. It needs the app identity from
Partner Center in `Package.appxmanifest` first, and stops with instructions until that's filled in.
[docs/microsoft-store.md](docs/microsoft-store.md) covers the whole path: developer account, reserving the
name, where each identity value comes from, what to put on every submission page, and what certification looks
at.

### Troubleshooting

- **Dev Pane isn't in the + list:** run the script again with `-RestartWidgets`.
- **The build fails on `resources.pri`:** the Widgets Board still has an old copy open. Run again with `-RestartWidgets`.
- **A card is blank or stuck:** check the provider log at
  `%LOCALAPPDATA%\Packages\DevPane_<id>\LocalState\devpane-widgets.log`.

## Privacy

Dev Pane runs entirely on your PC: no account, no server, no analytics. Tokens live in Windows Credential
Locker, and the only network calls go straight from your PC to GitHub, Vercel or Jira when you pin those cards.
[PRIVACY.md](PRIVACY.md) has the details, including exactly what the GitHub sign-in grants. To report a
security problem, see [SECURITY.md](SECURITY.md).

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

The **GitHub mark** on the GitHub cards is GitHub's, used unmodified under
[GitHub's logo guidelines](https://github.com/logos), and isn't covered by the license.

Every other card icon is Dev Pane's own drawing, in `tools/generate-widget-icons.cs`. The Vercel card shows a
globe, the Claude Code usage card a progress ring, and the Jira card a plain tick — none of them is that
company's logomark. Where a card borrows a brand's colour, that's all it borrows: a colour isn't a trademark,
but the shape is, which is why the shapes here are ours.

Octicons and other third-party code are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first: all contributors sign a [Contributor License Agreement](CLA.md).
