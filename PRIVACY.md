# Dev Pane privacy policy

_Effective 9 October 2026._

Also published at <https://devpane.vercel.app/privacy>, with the same text.

Dev Pane runs on your PC. It has no account and no server of its own, and it collects no analytics. Nothing it
reads is sent to us. The only data that leaves your PC goes straight from your PC to a service you connected,
such as GitHub, and only while that service's card is pinned.

This policy covers the Dev Pane app and its website. Dev Pane is made and published by Keiru Cabili, called "we"
below.

## What Dev Pane reads on your PC

Only to draw the cards you've pinned, and only while they're pinned. None of it leaves your PC.

| Card | Reads |
| --- | --- |
| System stats | Processor, memory, GPU, disk and network activity, from Windows. |
| Local dev | Which programs are listening on your network ports, and the names and memory use of running developer tools, WSL and Docker. |
| Claude Code usage | Claude Code's session logs in `.claude\projects`: the model, token counts and time of each reply, and the name of each project's folder. Never the text of your conversations. It also reads your plan's usage percentages from `.claude.json`. |

## What Dev Pane sends, and where

Only to services you connect, and only directly from your PC. There's no Dev Pane server in between, and Dev
Pane never contacts Anthropic.

| Card | Talks to | For |
| --- | --- | --- |
| GitHub, GitHub contributions | `github.com`, `api.github.com` | Signing in, then your pull requests, reviews, issues, workflow runs and contribution calendar |
| Vercel | `api.vercel.com` | Your projects and deployments |
| Vercel | your projects' own domains | Each site's icon, fetched the way a browser fetches one |
| Jira | your Jira site, `api.atlassian.com` | Your issues and projects |

Every connection uses HTTPS. If you pin none of these cards, Dev Pane makes no network connections at all.

## Your sign-in details

- **GitHub** signs in with GitHub's device flow, so your password never goes into Dev Pane. It asks for the
  `repo` and `read:org` permissions, so the card can show private repositories and your organizations. GitHub's
  `repo` permission includes write access, and GitHub offers no read-only version of it. Dev Pane only ever
  reads, but the token itself could write, so treat it like any other credential. You can revoke it at any time
  in [GitHub's application settings](https://github.com/settings/applications).
- **Vercel** and **Jira** use tokens you create yourself, so their access is whatever you choose when you make
  them. A read-only Vercel token is enough for the card.

Tokens are kept in Windows Credential Manager, encrypted by Windows for your user account, and sent only to the
service that issued them. Dev Pane never writes them to its log.

## What Dev Pane keeps on your PC

| What | Where | Removed |
| --- | --- | --- |
| Tokens for GitHub, Vercel and Jira | Windows Credential Manager, named `DevPane.GitHub`, `DevPane.Vercel` and `DevPane.Jira` | When you sign out on the card. Not by uninstalling: see below. |
| Each card's settings, such as which repository it shows | Saved by Windows with the card | When you remove the card |
| Your Claude plan's usage percentages and reset times, if you turn on live limits | Dev Pane's app data | When you uninstall Dev Pane |
| A diagnostic log of what the cards did, up to 1 MB | Dev Pane's app data | When you uninstall Dev Pane |

The log never contains tokens or passwords. It can name your Jira site, the repositories and Vercel teams you
chose for a card, and error messages the services sent back.

**Uninstalling doesn't delete your tokens.** Windows keeps Credential Manager entries after the app that saved
them is removed. Sign out on each card before you uninstall, or afterwards delete the `DevPane` entries under
**Control Panel → Credential Manager → Web Credentials**. Revoking a token at the service that issued it also
makes any saved copy useless.

## What Dev Pane never does

- No analytics, telemetry, advertising or tracking.
- No selling or sharing of your data. We never receive it in the first place.
- No account with us, and no server of ours.

## Your choices

Everything Dev Pane keeps is on your PC and under your control, as listed above: sign out on a card, remove a
card, uninstall, or delete the entries in Credential Manager. Because we hold no data about you, there's nothing
for us to look up, export, correct or delete on your behalf.

## The Microsoft Store

If you install Dev Pane from the Microsoft Store, the Store itself is covered by
[Microsoft's privacy statement](https://privacy.microsoft.com/privacystatement). Microsoft shows developers
aggregate figures, such as install counts, and reports of crashes that Windows collects according to your
diagnostic data settings. We use those only to find and fix problems, and they don't identify you to us.

## The website

[devpane.vercel.app](https://devpane.vercel.app) sets no cookies and runs no analytics. If you switch between
light and dark mode, your choice is saved in your browser's local storage and never leaves your device. Its fonts
are served from the site itself, not from a font service. The site is hosted by Vercel, which handles the
requests your browser makes, as described in [Vercel's privacy policy](https://vercel.com/legal/privacy-policy).

## Children

Dev Pane is a tool for software developers. It isn't directed at children, and we don't knowingly receive data
from anyone, children included.

## Changes

If this policy changes, the new version replaces this file and the website's copy, with a new effective date at
the top. Earlier versions are kept in this file's history.

## Contact

For questions about privacy or this policy, open an issue at
<https://github.com/kwiruu/devpane-widget-app/issues>.
