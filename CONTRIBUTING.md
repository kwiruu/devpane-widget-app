# Contributing to Dev Pane

Thanks for helping build Dev Pane.

## Before your first pull request

**Sign the Contributor License Agreement.** Every contributor signs the [CLA](CLA.md) once. When you open your first pull request, a bot asks you to sign by leaving a comment. Pull requests can't be merged until the CLA is signed.

Why: Dev Pane is source-available under the PolyForm Shield License, and the maintainer may offer paid features later. The CLA lets your contribution be used in every edition of Dev Pane, free or paid. You keep the copyright to your work.

## Project layout

| Folder | What's in it |
|---|---|
| `src/DevPane.Widgets` | The widget provider: talks to the Widgets Board, owns card templates |
| `src/DevPane.Integrations` | Data sources (system stats, GitHub, ...), with no UI code |

Keep data fetching in `DevPane.Integrations` and presentation in `DevPane.Widgets`, so every card can reuse the same data.

## Building

See [Build and run](README.md#build-and-run) in the README.

## Guidelines

- Match the style of the surrounding code.
- Never commit tokens, API keys, or signing certificates.
- Keep the Widgets Board fast: cards only refresh while the board is open.
