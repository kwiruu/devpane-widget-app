# Publishing Dev Pane to the Microsoft Store

Everything needed to get Dev Pane from this repository into the Store, in order. Steps 1–3 are done once; steps
4–6 are repeated for every release.

## 1. Get a developer account

Dev Pane needs its own entry in [Partner Center](https://partner.microsoft.com/dashboard), which needs a
Microsoft account enrolled in the Windows & Xbox developer program. Enrolment asks for a one-time registration
fee and, for a company account, verification of the company, which takes a few days. An individual account is
enough to publish Dev Pane; the publisher display name customers see is the one registered here, so pick the
name Dev Pane should be published under.

Start at [Register as an app developer](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account).

## 2. Reserve the name

In Partner Center: **Apps and games → New product → MSIX or PWA app**, and reserve **Dev Pane**.

Pick that product type, not **EXE or MSI app**: it's the one that takes the `.msixupload` built in step 4, and the
only one where the Store signs and hosts the package for you. The two game types go to a different
certification track. A product's type can't be changed afterwards.

Reserving is free and instant, and it holds the name for three months even if nothing is submitted. If the name
is taken, the reserved name has to change, along with `DisplayName` in `Package.appxmanifest` and the name in
the Store listing. Reserve it early.

## 3. Copy the app identity into the manifest — done

Dev Pane's identity is already in `Package.appxmanifest`, and it doesn't change again:

| | |
| --- | --- |
| Identity name | `KeiruCabili.DevPane` |
| Publisher | `CN=D3E591D2-F191-488D-A347-256754C0415A` |
| Publisher display name | `Keiru Cabili` |
| Store ID | `9NTM1R4TD2TT` |
| Listing URL | <https://apps.microsoft.com/detail/9NTM1R4TD2TT> |

The rest of this section is how those values were found, for a fork or a second product.

This is the step that decides whether Partner Center accepts an upload at all. The Store does **not** rewrite a
package's identity: the identity inside the package has to match the reserved app exactly, or the upload is
rejected. A published product's identity can never be changed.

In Partner Center, open the app, then **Product management → Product identity**. It lists, among others:

| Partner Center shows | Goes into `src/DevPane.Widgets/Package.appxmanifest` |
| --- | --- |
| `Package/Identity/Name` (looks like `41234Yourname.DevPane`) | `<Identity Name="...">` |
| `Package/Identity/Publisher` (looks like `CN=1B2C3D4E-5F60-...`) | `<Identity Publisher="...">` |
| `Package/Properties/PublisherDisplayName` | `<PublisherDisplayName>` |

The three values are case-sensitive, and spaces and punctuation have to match too.

Leave `Package Family Name` and `Package SID` alone: Partner Center derives them from the values above, and
they don't belong in the manifest.

> The page only appears once the app has a Store-assigned identity. If **Product identity** isn't in the menu
> yet, finish reserving the name and reload.

Changing the identity gives the local dev package a new package family name, so Windows treats it as a
different app: cards pinned under the old identity are orphaned, and keep showing their last frame with no
title and no size options until they're removed. Uninstall whatever is left over, then pin the cards again:

```powershell
Get-AppxPackage -Name '*DevPane*' | Where-Object { $_.Name -ne 'KeiruCabili.DevPane' } | Remove-AppxPackage
```

## 4. Build the package

```powershell
powershell -ExecutionPolicy Bypass -File tools\pack-store.ps1
```

This produces `AppPackages\DevPane.Widgets_<version>_x64_arm64_bundle.msixupload`, holding x64 and ARM64 builds
so one submission covers both Intel/AMD and Arm PCs. It needs Visual Studio with the **WinUI application
development** workload; the .NET SDK alone can't build MSIX bundles.

The package is deliberately **unsigned**. The Store re-signs it with a Microsoft certificate after it passes
certification, so no code signing certificate has to be bought, and signing it first would only cause a
publisher mismatch. The side effect is that the `.msixupload` can't be installed by double-clicking it — keep
using `tools\deploy-dev.ps1` to test on this PC.

To raise the version for a later release:

```powershell
powershell -ExecutionPolicy Bypass -File tools\pack-store.ps1 -Version 1.0.1.0
```

Every submission needs a version higher than the last published one. The fourth part belongs to the Store and
has to stay `0`.

If the script warns that `mspdbcmf.exe` is missing, the package still builds, but without the symbol file that
gives Partner Center crash reports. Add **Desktop development with C++** in the Visual Studio Installer to get it.

### Run the certification kit first

The Windows App Certification Kit runs most of the tests certification runs, on this PC, in about ten minutes.
Finding a problem here instead of after a submission saves a review cycle.

Point it at the **package file** that `tools\pack-store.ps1` builds, not at the copy `tools\deploy-dev.ps1`
installs. The dev script registers a folder of loose files in development mode, and the kit can't read one of
those: it reports "The manifest file for this app package could not be found" and then fails every test it
never ran, which looks alarming and means nothing.

Build the package first, then run both lines in a PowerShell started with **Run as administrator** — the kit
won't start without it, and the first line clears any earlier run:

```powershell
powershell -ExecutionPolicy Bypass -File tools\pack-store.ps1
```

```powershell
& "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe" reset
```

```powershell
& "${env:ProgramFiles(x86)}\Windows Kits\10\App Certification Kit\appcert.exe" test -appxpackagepath (Get-ChildItem "$HOME\Documents\DevPane\AppPackages" -Recurse -Filter *.msixbundle).FullName -reportoutputpath "$env:TEMP\devpane-wack.xml"
```

The path is spelled out because an administrator PowerShell starts in `C:\WINDOWS\system32`, not in the
repository; change it if the repository lives somewhere else.

It takes about ten minutes and leaves the screen alone while it works. When it finishes, read the report:

```powershell
([xml](Get-Content "$env:TEMP\devpane-wack.xml")).REPORT.OVERALL_RESULT
```

`PASS` means certification's automated tests should pass too. For anything else, list what actually failed
rather than reading the XML by hand:

```powershell
([xml](Get-Content "$env:TEMP\devpane-wack.xml")).SelectNodes('//TEST') | Where-Object { $_.RESULT.InnerText -ne 'PASS' } | ForEach-Object { '{0,-8} {1}' -f $_.RESULT.InnerText, $_.NAME }
```

Check the report's `APP_NAME` before believing a long list of failures. If it's empty, the kit never opened the
package and every failure below it is that one problem:

```powershell
([xml](Get-Content "$env:TEMP\devpane-wack.xml")).REPORT.APP_NAME
```

The package `pack-store.ps1` builds is unsigned, because that's what the Store wants. If the kit refuses to
open it for that reason, the choice is to sign a copy locally just for testing, or to skip the kit and let
Store certification be the check — it runs the same tests.

## 5. Fill in the submission

In Partner Center, open Dev Pane and start a submission. Each page, with what Dev Pane needs:

**Pricing and availability.** Free. Dev Pane's cards are useful worldwide, so all markets is fine. Choose
whether it's visible in the Store or only reachable by link — a hidden first submission is a cheap way to see
the whole flow end to end.

**Properties.**

- Category: **Developer tools**.
- Privacy policy URL: **required**, because Dev Pane signs in to GitHub, Vercel and Jira accounts. Publish
  [PRIVACY.md](../PRIVACY.md) somewhere public and link it. The raw GitHub URL works, but a GitHub Pages URL
  reads better to a reviewer.
- Support contact info: an email address or issues URL that a person actually reads.

**Age ratings.** A questionnaire. Dev Pane has no ads, no user-generated content, and no data sharing, which
makes it the lowest rating in every market. Answer honestly — a wrong answer here is grounds for removal later.

**Packages.** Upload the `.msixupload` from step 4. Partner Center validates the identity as soon as it's
uploaded, so an identity mistake from step 3 shows up here immediately.

**Store listing.** Needs a description and **at least one screenshot, 1366×768 or 3840×2160**. The card
screenshots in `Assets/Widgets` are 300×304 and are what the Widgets Board shows in its own picker — they are
not Store listing screenshots and Partner Center will reject them. Take new ones of the Widgets Board with Dev
Pane cards pinned, at one of the two sizes above.

Worth saying explicitly in the description, because it isn't obvious: Dev Pane adds cards to the Windows
Widgets Board and has no window of its own. Press **Win + W**, click **+**, and pin them. Reviewers and
customers both open an app expecting a window, and this is the one thing about Dev Pane that surprises them.

**Submission options.** Nothing Dev Pane needs. Notes for certification is a free-text box read by a human
reviewer — see the next section.

## 6. What certification will look at

Certification is usually a few hours to a few days. Three things about Dev Pane are worth knowing in advance.

**It has no window.** `AppListEntry="none"` in the manifest means Dev Pane never appears in the Start menu:
there is nothing to show, because the provider is a background process the Widgets Board starts on demand.
A reviewer who launches it and sees nothing happen may read that as a broken app. Use **Notes for
certification** to say so directly, with the steps to see it working:

> Dev Pane is a Windows Widgets Board provider and has no window of its own. To see it: press Win + W to open
> the Widgets Board, click +, find "Dev Pane", and pin "System stats" or "Local dev". Those two cards need no
> sign-in. The GitHub, Vercel and Jira cards ask for an account on the card itself.

**It declares `runFullTrust`.** Normal and expected for a packaged desktop app, and not something that needs
justifying. Dev Pane needs it to read performance counters, local TCP listeners and process memory.

**It shows one third-party logo.** The GitHub cards carry GitHub's mark, unmodified, under
[GitHub's logo guidelines](https://github.com/logos), which permit referring to GitHub. That's the only
borrowed mark left.

The Vercel, Claude and Jira cards used to carry their companies' marks, which was the likeliest reason for a
rejection under the Store policy on using content you don't have the rights to. Those are now Dev Pane's own
drawings — a globe, a progress ring and a plain tick — produced by `tools\generate-widget-icons.cs`. The cards
still use each brand's colour, which is fine: trademark protects the mark, not the hex value, and a card that
reads your Vercel deployments is entitled to say so by name.

## 7. Releasing an update

1. Make the change and test it with `tools\deploy-dev.ps1`.
2. `tools\pack-store.ps1 -Version <higher version>`.
3. In Partner Center, open Dev Pane → **Update**, upload the new `.msixupload`, and fill in **What's new**.

The identity never changes again after the first submission — it can't be, for a published product. Only the
version moves.
