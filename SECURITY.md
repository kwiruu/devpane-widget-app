# Security

## Reporting a vulnerability

Please don't open a public issue for a security problem. Report it privately instead, through
[GitHub's private vulnerability reporting](https://github.com/kwiruu/devpane-widget-app/security/advisories/new) for
this repository. Only the maintainer sees the report until a fix is out.

Say what an attacker could do, which card or file it involves, and how to reproduce it. Dev Pane is maintained by one
person, so a reply can take a few days. You'll hear back either way, and you'll be credited in the advisory unless
you'd rather not be.

Only the latest release is supported. Fixes ship as a new release through the Microsoft Store.

## In scope

- The Dev Pane app: the widget provider, its cards, and how it stores and sends your tokens.
- The Dev Pane website, [devpane.vercel.app](https://devpane.vercel.app).
- The scripts in `tools`.

Problems in GitHub, Vercel, Atlassian, Anthropic or Windows themselves belong with those companies.

## How Dev Pane protects you

- **Nothing to breach on our side.** There's no Dev Pane server or account. Your data goes straight from your PC to
  the service it belongs to, over HTTPS.
- **Tokens stay sealed.** GitHub, Vercel and Jira tokens are kept in Windows Credential Manager, encrypted by Windows
  for your user account. Each is sent only to the service that issued it, and never written to the log.
- **Card text can't turn into links.** Titles, commit messages, build logs and other text written by other people
  are escaped before a card is drawn, so they show as plain text and can't add links or load images.
- **Cards only open web pages.** A link that isn't http or https is dropped rather than handed to Windows to open.
- **The website runs nothing it didn't ship.** It sends a strict content security policy, sets no cookies and loads
  nothing from other sites.

[PRIVACY.md](PRIVACY.md) lists exactly what Dev Pane reads, sends and keeps.
