# SB-Mac — Service Bus Explorer for macOS

[![CI](https://github.com/chanakya-net/ServiceBusExplorer-Mac/actions/workflows/ci.yml/badge.svg)](https://github.com/chanakya-net/ServiceBusExplorer-Mac/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A native macOS app for browsing and operating Azure Service Bus namespaces: queues,
topics, subscriptions, rules, messages and dead-letter queues.

Runs on Apple Silicon and Intel. The packaged `.app` is self-contained — no .NET runtime
required to run it.

---

## Install

Grab the `.dmg` for your Mac from the
[latest release](https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/latest):

| Mac | File |
|---|---|
| Apple Silicon (M1/M2/M3/M4) | `SB-Mac-arm64.dmg` |
| Intel | `SB-Mac-x64.dmg` |

Not sure which you have? Run `uname -m` — `arm64` means Apple Silicon.

Open the `.dmg` and drag **SB-Mac** to Applications.

### First launch: clearing the quarantine flag

Release builds are **ad-hoc signed**, not signed with a paid Apple Developer ID. macOS
quarantines anything downloaded from the internet, so the first launch fails with
*"SB-Mac is damaged and can't be opened"* until you clear that flag:

```bash
xattr -dr com.apple.quarantine /Applications/SB-Mac.app
```

Then open it normally. Once per install.

Nothing is wrong with the download — this is what macOS does to any app distributed
without a $99/year Apple Developer account. Every release ships `.sha256` files if you
want to verify the bytes.

**Prefer to avoid it entirely?** A locally built bundle is never quarantined:

```bash
git clone https://github.com/chanakya-net/ServiceBusExplorer-Mac.git
cd ServiceBusExplorer-Mac
./build/make-app.sh
open artifacts/SB-Mac.app
```

That needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

---

## Why this is a rewrite, not a port

This project exists because the
[Windows Service Bus Explorer](https://github.com/paolosalvatori/ServiceBusExplorer)
cannot be made to run on macOS. Two independent blockers, either of which alone would be
fatal:

| Layer | Size | Blocker |
|---|---|---|
| `ServiceBusExplorer` (UI) | ~92k lines, 56 WinForms designers | WinForms is Windows-only. It does not exist in any .NET on macOS. |
| `Common` | ~18.8k lines, 56 of 92 files affected | Built on `WindowsAzure.ServiceBus` 7.0.1 — the legacy WCF `Microsoft.ServiceBus` SDK. .NET Framework only; never ported to .NET Core. Also depends on `System.ServiceModel`, `System.Web`, `System.IdentityModel`. |

So SB-Mac reimplements both layers on cross-platform foundations:

- **Service layer** → `Azure.Messaging.ServiceBus` 7.20.2 + `Azure.Identity` 1.21.0
- **UI** → Avalonia 11.3.13, targeting .NET 10

The original repository's `netstandard2.0` projects (`Utilities`, `ServiceBus`,
`EventHubs`, `Relay`, `EventGridExplorerLibrary`) were already portable and already on
the modern SDKs. SB-Mac takes the same approach rather than depending on them, so this
repository is self-contained — no source from the Windows project is vendored in.

---

## What it does

**Entities**
- Browse queues, topics, subscriptions and rules in a namespace tree with live message counts
- Create, edit and delete queues, topics and subscriptions
- Enable / disable any entity
- Settings that Azure fixes at creation time (partitioning, sessions, duplicate
  detection) are shown but locked once an entity exists — the service rejects updates to
  them, so the UI does too

**Messages**
- **Peek** — read without removing or locking. Safe against production.
- **Receive and delete** — destructive, behind a confirmation
- **Send** — compose a body with content type, label, correlation ID, session ID,
  partition key and custom application properties; send N copies; schedule for later
- **Edit and resend** — open the compose dialog pre-filled from an existing message
- Body viewer detects JSON, XML, plain text and binary, and unwraps the legacy WCF
  DataContract framing that the Windows tool used to write (see below)

**Dead-letter**
- Toggle any queue or subscription view to its dead-letter sub-queue
- Dead-letter reason and error description are grid columns, not buried in a detail pane
- **Resubmit** selected messages back to their entity, keeping or deleting the
  dead-letter copies
- Delete selected messages by sequence number
- **Purge** an entity or its dead-letter queue, with a running count and a Cancel button

**Import / export**
- Export a whole namespace's entity definitions to XML or JSON
- Import into another namespace, choosing whether existing entities are skipped, updated,
  or reported as conflicts
- Per-entity result log, so a partial import tells you exactly what happened

---

## Authentication

Both modes the question asked for, selectable per namespace.

**Connection string** — paste a SAS connection string from the portal.

**Entra ID** — no secret stored anywhere. Pick a credential flow:

| Flow | Use when |
|---|---|
| Default | You want the standard `DefaultAzureCredential` chain |
| Azure CLI | You already ran `az login` |
| Azure PowerShell | You already ran `Connect-AzAccount` |
| Azure Developer CLI | You already ran `azd auth login` |
| Interactive browser | You want to sign in now, in a browser |
| Device code | You're on SSH and can't open a browser |
| App registration | Service principal with a client secret |

Entra ID needs the **Azure Service Bus Data Owner** role on the namespace to browse
entities and messages.

Both modes support AMQP over WebSockets (port 443) for networks that block 5671.

### Where secrets go

Connection strings and client secrets are written to the **macOS login keychain** via
`/usr/bin/security`, keyed per namespace. They are never written to the config file.

```
~/Library/Application Support/SB-Mac/
  connections.json     namespace list — no secrets
  settings.json        preferences
```

A test asserts this directly: `SecretsAreKeptOutOfTheConnectionsFile`.

---

## The interface

Light and dark both follow the system appearance — there is no in-app theme switch and
no second palette to maintain.

Every colour in the app resolves through a token in `src/SbMac.App/Styles/Theme.axaml`,
which defines a light and a dark set behind the same names (`SurfaceBrush`,
`TextSecondaryBrush`, `AccentBrush`, `DangerBrush`, and so on). `AppStyles.axaml` contains
no literal colours at all. Retuning the palette means editing one file.

Theme.axaml also overrides `SystemAccentColor` and its light/dark ramp. FluentTheme derives
checkbox ticks, focus rings and the tab underline from those, so the built-in control
templates pick up the palette without being restyled one by one.

A few deliberate choices:

- **Counts are badges, not text.** A queue row shows active messages in an accent pill and
  dead-lettered ones in a red pill. Zero is omitted entirely — a badge reading "0" on every
  row is noise. The dead-letter count is the thing people scan a namespace for, so it gets
  colour rather than being concatenated into a detail string.
- **Icons are stroked paths on a 24×24 grid** (`Styles/Icons.axaml`), not Unicode symbols.
  The first version used characters like `▤` and `◫`, which render at whatever weight the
  system font happens to have. Stroked geometry stays crisp and consistent at the 15px the
  UI draws them at.
- **Dead-letter is a mode, not a checkbox.** The toolbar toggle fills with the danger tint
  when active, because acting on the wrong sub-queue is a costly mistake.
- **Destructive actions are separated.** Purge and Delete sit apart from the read actions
  and carry the danger colour.
- **The message list gets the larger share** of the vertical split by default: scanning is
  the common task, reading one body is the occasional one.
- **Message properties are a real two-column list**, grouped into Identity / Delivery /
  Content / Dead-letter / Application properties, with selectable values — not
  space-padded monospace.

### Seeing the UI

macOS needs screen-recording permission to capture a running app, which is awkward in
automation and impossible over SSH. So the repo ships a harness that renders the real
windows — same XAML, same styles, same compiled bindings — straight to PNG through
Avalonia's headless platform with Skia drawing:

```bash
dotnet run --project tools/SbMac.Preview ./artifacts/screenshots
```

It writes both themes for the main window, the properties pane, and the connection, send
and queue dialogs, populated with representative sample data.

`tools/SbMac.Preview` is **not** in `SB-Mac.sln`, on purpose. It fabricates sample entities
through the Service Bus SDK's internal constructors, so an SDK upgrade can break it —
keeping it out of the solution means that breaks the screenshot tool rather than the build.

Note that it opens the real main window, so it loads whatever namespaces you have saved in
`~/Library/Application Support/SB-Mac/connections.json` and they will appear in the
rendered sidebar alongside the sample data.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build          # build everything
dotnet test           # 96 tests
dotnet run --project src/SbMac.App    # run without packaging
```

Package a distributable `.app`, and optionally a `.dmg`:

```bash
./build/make-app.sh              # this machine's architecture
./build/make-app.sh osx-x64      # Intel
./build/make-app.sh osx-arm64    # Apple Silicon

./build/make-dmg.sh artifacts/SB-Mac.app dist/SB-Mac.dmg
```

The output is self-contained (~112 MB) and ad-hoc code-signed.

Two things worth knowing about that signature, because they're easy to conflate:

- **It is required for the app to run at all.** Apple Silicon refuses to execute a Mach-O
  with no signature, and copying the published host into the bundle invalidates the one
  the SDK applied. Skip the `codesign` step and the app won't launch, even locally.
- **It does nothing for Gatekeeper.** An ad-hoc signature has no Developer ID behind it,
  so `spctl` reports `no usable signature` and a downloaded copy is rejected. Clearing the
  quarantine flag is what makes a downloaded build run, which is why the install
  instructions above exist.

`codesign --verify` on the bundle reports broken subcomponents, and that is expected
rather than a fault: .NET puts its managed assemblies and JSON config next to the host in
`Contents/MacOS`, and `codesign` treats everything in that directory as nested code, so
the unsigned `.dll` and `.json` files are flagged. The app launches regardless. Both
`make-app.sh` and CI therefore verify what actually determines whether it runs — that the
host is Mach-O, carries a signature, and that the packaged app starts and stays running.

### Project layout

```
SB-Mac/
  src/SbMac.Core/          service layer — no UI dependency
    Connections/           auth, keychain, saved namespaces
    Entities/              queue/topic/subscription/rule CRUD
    Messaging/             peek, receive, send, purge, body decoding
    ImportExport/          definition DTOs and import/export
  src/SbMac.App/           Avalonia UI
    ViewModels/            including Tree/ and Dialogs/
    Views/                 windows and dialogs
    Styles/                Theme.axaml (tokens), Icons.axaml, AppStyles.axaml
    Converters/            icon-name to geometry lookup
  tests/SbMac.Tests/       96 tests, including headless UI tests
  tools/SbMac.Preview/     dev-only screenshot harness (not in the solution)
  build/                   Info.plist, icon generator, bundling script
```

`SbMac.Core` has no reference to Avalonia, so the service layer can be driven from a CLI
or tests without a UI.

---

## Testing

```bash
dotnet test
```

96 tests, all passing, no Azure connection required. They cover message body decoding,
connection string parsing, secret storage, import/export round-trips, and view model
logic.

The UI tests are not superficial: they instantiate every real window against Avalonia's
headless platform, which is what catches a bad binding path, a missing style resource or
a broken template — none of which the compiler sees.

These tests found four genuine bugs in the service layer, all fixed:

1. **`EntityStatus` is a struct, not an enum.** `Enum.TryParse<EntityStatus>` throws
   `ArgumentException` at runtime. It compiled fine. Every create, update and import
   would have failed. Now parsed against the known status set.
2. **`UserMetadata` rejects null.** Importing any definition without user metadata threw
   `ArgumentNullException`. Now cleared with an empty string.
3. **Resubmitting a session message threw.** A session message reports the same value for
   `PartitionKey` and `SessionId`; assigning both back is rejected by the SDK. Every
   dead-letter resubmit on a session-enabled entity would have failed.
4. **Cancel meant "yes".** The resubmit prompt was a two-way confirmation, so cancelling
   still resubmitted — just without deleting the originals. Now a three-way dialog where
   Cancel aborts.

Rendering the UI (rather than only compiling it) caught two more:

5. **The tree never expanded.** A node's `IsExpanded` was set on the view model but never
   reached its `TreeViewItem`, so the namespace stayed collapsed after connecting. The fix
   is a style setter on the container. Selection is deliberately *not* bound the same way:
   `TreeView.SelectedItem` already owns it, and a second two-way binding fights it as
   containers realise, clearing the selection.
6. **Renamed style classes left the dialogs unstyled.** The redesign renamed
   `sectionHeader` → `caption`, `muted` → `tertiary` and `panel` → `card`. Avalonia
   resolves a missing style class to nothing and does not warn, so six dialogs silently
   lost their headings, hint text and panel borders while still compiling cleanly.

---

## Implementation notes

Things that are non-obvious, and why they are the way they are.

**Legacy WCF message bodies.** Messages written by the old `WindowsAzure.ServiceBus` SDK —
which is what the Windows Service Bus Explorer used — are not raw UTF-8. They carry
binary-XML `DataContractSerializer` framing: `0x40 0x06 "string"` then a length-prefixed
payload. Messages sitting in long-lived queues are still in that shape. Without unwrapping
it, the viewer shows a hex dump of the user's own JSON. `MessageBodyDecoder` strips the
framing, then classifies the payload on its own merits.

**Deleting specific messages.** Service Bus has no delete-by-sequence-number operation.
`DeleteMessagesAsync` receives under lock, completes the targets and abandons the rest.
Abandoning increments the delivery count on untargeted messages, which matters on an
entity already close to its `MaxDeliveryCount`. The confirmation dialog says so.

**Purge termination.** Service Bus can return an empty batch while messages remain, so
draining stops only after three consecutive empty batches rather than the first one.

**Prefetch is off.** Prefetching would pull messages the user never asked for and, under
PeekLock, start their lock timers running.

**Durations.** Definition files store ISO 8601 (`PT30S`) because it round-trips through
both XML and JSON unambiguously. The UI shows and accepts `hh:mm:ss`, `d.hh:mm:ss` or
ISO 8601. Blank means "leave the service default alone" and round-trips to null — not to
zero, which would be sent as a real value.

**`TrueRuleFilter` and `FalseRuleFilter` derive from `SqlRuleFilter`.** A type switch that
matches `SqlRuleFilter` first silently reports them as plain SQL filters, changing rules
on an export/import round-trip. They are matched first; a test pins this.

**No X11 or Win32 backend.** The project references `Avalonia.Native` and `Avalonia.Skia`
directly rather than the `Avalonia.Desktop` metapackage. That metapackage also pulls in
the X11 backend, which drags in a `Tmds.DBus.Protocol` version with a published advisory
(GHSA-xrw6-gwf8-vvr9) that can never execute on macOS. Referencing the macOS backend
directly keeps the dependency graph to what actually runs. This is why `Program.cs` names
the backend explicitly instead of calling `UsePlatformDetect()`.

---

## Not carried over

The Windows tool covers more than Service Bus. These were out of scope and are **not**
in SB-Mac:

- Event Hubs, Notification Hubs, Relay, Event Grid explorers
- Message inspector / generator plugin model (`IBrokeredMessageInspector` and friends)
- Event processor checkpoint management
- SDK-side chunking and the deflate/zip inspectors

`SbMac.Core` is where they'd go — the tree and service layers are shaped to take
additional entity kinds without restructuring.

---

## Releasing

CI builds, tests and packages on every push and pull request, so a broken bundle is
caught before it can be tagged.

To publish a release, push a tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

That builds and tests both architectures, produces a `.dmg`, a `.zip` and a `.sha256` for
each, and opens a GitHub Release with install instructions. `.zip` archives are made with
`ditto` rather than `zip`, which preserves the bundle's symlinks and extended attributes —
plain `zip` mangles them and invalidates the code signature.

### Adding real code signing later

The workflow ad-hoc signs. To ship without the quarantine step, you'd need an Apple
Developer ID ($99/year) and would add, after `make-app.sh` in `release.yml`:

1. Import the Developer ID certificate into a temporary keychain
2. `codesign --deep --options runtime --sign "Developer ID Application: …"`
3. `xcrun notarytool submit --wait` and then `xcrun stapler staple`

Everything else in the pipeline stays as it is.

---

## Licence

MIT — see [LICENSE](LICENSE).

This is an independent application, not a fork: it contains no source code from the
Windows Service Bus Explorer. It follows that project's naming and overall shape so
people already familiar with it can find their way around. See [NOTICE](NOTICE) for
attribution and third-party licences.
