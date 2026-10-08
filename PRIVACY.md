# Dev Pane privacy policy

_Last updated: 29 September 2026_

Dev Pane runs entirely on your PC. It has no account, no server, and no analytics. Nothing it reads is sent to
Dev Pane's developer, and there is nothing for us to collect, store, or share, because none of your data
reaches us in the first place.

## What Dev Pane reads on your PC

Only to draw the cards you have pinned, and only while they're pinned:

- **System stats** — processor, memory, GPU, disk, and network counters from Windows.
- **Local dev** — programs listening on your TCP ports, and the names and memory use of running developer
  tools, including WSL and Docker.
- **Claude Code usage** — Claude Code's own session logs in `.claude\projects`, to total your usage. It reads
  the token counts and timestamps, not the contents of your conversations.

This stays on your PC.

## What Dev Pane sends, and where

Only the services you sign in to, and only directly from your PC to that service:

| Card | Talks to | For |
| --- | --- | --- |
| GitHub, GitHub contributions | `github.com`, `api.github.com` | Your pull requests, reviews, issues, workflow runs, and contribution calendar |
| Vercel | `api.vercel.com` | Your deployments and their status |
| Vercel | the domains of your own deployments | The site icon shown next to each project, fetched the way a browser does |
| Jira | `api.atlassian.com` and your Jira site | Your issues |

There is no Dev Pane server in between. If you never pin those cards, Dev Pane makes no network connections at
all.

## Your sign-in details

GitHub, Vercel and Jira access tokens are kept in **Windows Credential Locker**, encrypted by Windows for your
Windows user account. They stay on your PC and are sent only to the service that issued them.

Dev Pane only ever reads from these services. It never creates, edits or deletes anything in your
repositories, projects or issues.

The GitHub token is worth calling out, because the access you grant is wider than what Dev Pane uses. Signing
in asks GitHub for the `repo` and `read:org` permissions, so the card can show pull requests, checks and issues
from your private repositories as well as public ones. GitHub's `repo` permission also carries write access:
that is the narrowest permission GitHub offers an app like Dev Pane for reading private repositories, and there
is no read-only version of it. Dev Pane never uses the write half, but the token is capable of it, so treat it
like any other credential. You can revoke it at any time under
[GitHub → Settings → Applications](https://github.com/settings/applications).

Vercel and Jira tokens are the ones you create yourself, so their access is whatever you choose when you make
them. A read-only Vercel token and a Jira token for an account that can only browse are enough for their cards.

To remove them, unlink the account on the card, or uninstall Dev Pane — uninstalling removes everything Dev
Pane stored, including its log file.

## Children

Dev Pane is a tool for software developers and is not directed at children.

## Changes

Any change to this policy is published in this file, with the date above updated. The version in the Dev Pane
repository is always current.

## Contact

Questions about this policy: open an issue at
<https://github.com/kwiruu/devpane-widget-app/issues>.
