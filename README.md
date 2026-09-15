# Dev Pane

Developer cards for the Windows 11 Widgets Board. Press **Win + W**, click **+**, and pin the Dev Pane cards you want next to Weather.

> **Status:** early development (milestone M0, the proof of concept). Nothing is installable yet.

## Planned cards

| Card | Shows | Milestone |
|---|---|---|
| System stats | CPU, RAM, GPU | M1 |
| GitHub | Pull requests waiting on you, CI status | M2 |
| Claude usage | Spend and tokens (Claude Code and the Admin API) | v2 |
| Vercel | Latest deployment per project | v2 |

## Build and run

### Requirements

- Windows 11 with the Widgets Board turned on
- Visual Studio 2026 with the **WinUI application development** workload
- .NET 10 SDK
- **Developer Mode** turned on: Settings → System → Advanced → Developer Mode

### Steps

1. Open `DevPane.slnx` in Visual Studio.
2. Set **DevPane.Widgets** as the startup project and pick the **x64** platform.
3. Press **F5**. Visual Studio installs the package and starts the widget provider.
4. Press **Win + W**, click **+**, find **Dev Pane**, and pin the **Dev Pane test card**.

### Troubleshooting

- **Dev Pane isn't in the + list:** close the Widgets Board, wait a few seconds, and open it again.
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

## Contributing

Contributions are welcome. Read [CONTRIBUTING.md](CONTRIBUTING.md) first: all contributors sign a [Contributor License Agreement](CLA.md).
