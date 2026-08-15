# SB-Mac — Service Bus Explorer for macOS

[![CI](https://github.com/chanakya-net/ServiceBusExplorer-Mac/actions/workflows/ci.yml/badge.svg)](https://github.com/chanakya-net/ServiceBusExplorer-Mac/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A native macOS app for browsing and operating Azure Service Bus and Azure Event Hubs
namespaces: queues, topics, subscriptions, rules, messages and dead-letter queues on one
side; event hubs, partitions and events on the other.

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

SB-Mac checks for a newer stable release when it opens, at most once every 24
hours. If one is available, **View Release** opens its GitHub page; **Later**
defers the same notification for another day. The app never downloads or
installs an update itself.

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

## Built on the current Azure stack

SB-Mac targets **.NET 10** and uses the current, actively maintained Azure SDKs — the ones
Microsoft recommends for new work. No legacy compatibility shims, and nothing held back:

| Dependency | Version | Latest | Role |
|---|---|---|---|
| [`Azure.Messaging.ServiceBus`](https://www.nuget.org/packages/Azure.Messaging.ServiceBus) | 7.20.2 | 7.20.2 ✅ | Messaging and entity management |
| [`Azure.Messaging.EventHubs`](https://www.nuget.org/packages/Azure.Messaging.EventHubs) | 5.12.2 | 5.12.2 ✅ | Reading and publishing events |
| [`Azure.Identity`](https://www.nuget.org/packages/Azure.Identity) | 1.21.0 | 1.21.0 ✅ | Entra ID authentication |
| [`Azure.ResourceManager.ServiceBus`](https://www.nuget.org/packages/Azure.ResourceManager.ServiceBus) | 1.2.0 | 1.2.0 ✅ | Namespace-level resource operations |
| [`Azure.ResourceManager.EventHubs`](https://www.nuget.org/packages/Azure.ResourceManager.EventHubs) | 1.3.0 | 1.3.0 ✅ | Listing event hubs and consumer groups |
| [`CommunityToolkit.Mvvm`](https://www.nuget.org/packages/CommunityToolkit.Mvvm) | 8.4.2 | 8.4.2 ✅ | View model plumbing |

`dotnet list package --outdated` reports nothing to update for `SbMac.Core`, which is the
entire Azure surface.

The UI is [Avalonia](https://avaloniaui.net) **12.1**, also current.
`Avalonia.Controls.DataGrid` ships slightly ahead of the core packages at 12.1.2, which is
fine — it requires core `>= 12.1.0`. Keep all `Avalonia.*` references on the same version;
mixing across a release line is not supported.

Two consequences of being on 12 rather than 11 are worth knowing:

- **DevTools is gone.** `Avalonia.Diagnostics` has no 12.x release and no first-party
  replacement, so the in-app visual tree inspector is not available in Debug builds. The
  screenshot harness below covers most of what it was used for here.
- **Tests run on xunit v3.** Not a choice — `Avalonia.Headless.XUnit` 12 depends on
  `xunit.v3.extensibility.core`, and referencing v2 alongside it makes every `[Fact]`
  ambiguous. xunit v3 test projects are self-executing, hence `<OutputType>Exe</OutputType>`
  in the test project.

### Why it's a rewrite rather than a port

The [Windows Service Bus Explorer](https://github.com/paolosalvatori/ServiceBusExplorer)
is an excellent tool, but its code cannot be carried across to macOS. Two independent
blockers, either fatal on its own:

| Layer | Size | Blocker |
|---|---|---|
| `ServiceBusExplorer` (UI) | ~92k lines, 56 WinForms designers | WinForms is a Windows-only technology. It has no implementation on macOS in any version of .NET. |
| `Common` | ~18.8k lines, 56 of 92 files affected | Built on the `Microsoft.ServiceBus` API from the `WindowsAzure.ServiceBus` package, which is WCF-based and ships a single `net462` target. |

On that second point, this is Microsoft's own guidance rather than an opinion — the
[`WindowsAzure.ServiceBus`](https://www.nuget.org/packages/WindowsAzure.ServiceBus)
package description reads:

> Please note, for Azure Service Bus, Azure Event Hubs and Azure Relay, newer packages
> Azure.Messaging.ServiceBus, Azure.Messaging.EventHubs and Microsoft.Azure.Relay are
> available as of November 2020, February 2020 and March 2017 respectively. While
> WindowsAzure.ServiceBus will continue to receive critical bug fixes, we strongly
> encourage you to upgrade.

Its successor is exactly what SB-Mac uses. Upgrading the Windows app's `Common` layer in
place would mean rewriting it against a different API surface anyway — the old
`NamespaceManager` / `BrokeredMessage` model has no direct equivalent — and its UI would
still be stuck on WinForms. So both layers were rebuilt here on the current stack.

The Windows repository's `netstandard2.0` projects (`Utilities`, `ServiceBus`,
`EventHubs`, `Relay`, `EventGridExplorerLibrary`) were already portable and already on the
modern SDKs. SB-Mac takes the same approach rather than depending on them, so this
repository is self-contained — no source from the Windows project is vendored in.

### Keeping it current

```bash
dotnet list package --outdated
```

Bump versions in `src/*/*.csproj` and `tests/*/*.csproj`. CI builds, tests and packages on
every push, so a bad upgrade surfaces before it can be tagged. Keep all `Avalonia.*`
packages on the same version — see the note above.

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
- **Purge** an entity or its dead-letter queue, drained by several receivers at once, with
  a running count, a progress bar sized from the entity's message count, and a Cancel
  button

**Import / export**
- Export a whole namespace's entity definitions to XML or JSON
- Import into another namespace, choosing whether existing entities are skipped, updated,
  or reported as conflicts
- Per-entity result log, so a partial import tells you exactly what happened

**Event Hubs**

A namespace is added as either Service Bus or Event Hubs, and the tree, toolbar and menus
follow that choice.

- Browse event hubs and their partitions, with retained event counts and each partition's
  live sequence range
- **Peek** the most recent events, from one partition or fanned out across all of them.
  Reads are non-destructive by construction — Event Hubs is an append-only log with
  time-based retention, and no consumer operation removes an event
- **Send** — the same compose dialog, routing by partition key or straight to the selected
  partition
- The body viewer, JSON/XML detection and message grid are shared with Service Bus, so an
  event reads the same way a message does — with partition and offset in place of the
  broker fields Event Hubs does not have
- Receive-and-delete, purge, dead-letter and message deletion are disabled for Event Hubs
  rather than approximated, because the service has no equivalent of any of them

Listing the hubs in a namespace is a management-plane call, so it happens over ARM when
the signed-in identity can read the namespace resource. It can't be done with a SAS key at
all, so hub names can also be typed into the connection dialog — either way, everything
below that point runs over the data plane.

---

## Authentication

Two modes, selectable per namespace.

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

For Event Hubs, it needs **Azure Event Hubs Data Receiver** to read events and **Azure
Event Hubs Data Sender** to publish them. Listing the hubs in the namespace additionally
needs **Reader** on the namespace resource, which the data-plane roles do not include; a
connection without it still works, with the hub names typed in.

Both modes support AMQP over WebSockets (port 443) for networks that block 5671.

### Where secrets go

Connection strings and client secrets are written to the **macOS login keychain**, keyed
per namespace. They are never written to the config file.

```
~/Library/Application Support/SB-Mac/
  connections.json     namespace list — no secrets
  settings.json        preferences
```

Keychain access goes through the Security framework (`SecItemAdd` and friends) rather than
the `security` command-line tool. That is not a stylistic preference — the CLI cannot do
this job safely or, as it turns out, correctly:

- `security add-generic-password -w` with no value **prompts on the terminal** instead of
  reading stdin. From a GUI app it stores an *empty* password and still exits 0, so every
  saved connection string silently disappeared. This shipped in v1.0.0 and v1.1.0; see
  `KeychainSecretStoreTests` for the regression tests.
- The forms that do work — `-w <value>` and `-X <hex>` — put the secret in the process
  arguments, where anything running as the same user can read it out of `ps`.

Two tests pin the behaviour end to end: `SecretsAreKeptOutOfTheConnectionsFile` and
`ConnectionStringSurvivesAnAppRestart`, the latter saving through one `ConnectionStore`
and reading back through a fresh one, which is what an app restart does.

**Upgrading from v1.0.0 or v1.1.0?** Namespaces saved by those builds have an empty
keychain entry. SB-Mac now flags them on startup — select the namespace, choose **Edit
Namespace**, and paste the connection string again. It will stick this time.

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
- **Operations run side by side, and each one has its own bar.** A namespace enumerating
  its topics no longer blocks peeking a queue that has already loaded. The status bar
  carries one small bar per operation, coloured by what it is — connect, refresh, read,
  receive, send, purge, delete, manage, transfer — so several at once stay tellable apart;
  clicking it opens the full list, with per-operation progress, outcome and Cancel. The
  last few finished operations stay in the list, dimmed, so it is visible what just
  completed and what failed.
- **The message list gets the larger share** of the vertical split by default: scanning is
  the common task, reading one body is the occasional one.
- **Message properties are a real two-column list**, grouped into Identity / Delivery /
  Content / Dead-letter / Application properties, with selectable values — not
  space-padded monospace. An event swaps the Delivery group for Position (partition and
  offset): showing locks, expiry and redelivery at their defaults would read as fact
  rather than as absence.

### Seeing the UI

macOS needs screen-recording permission to capture a running app, which is awkward in
automation and impossible over SSH. So the repo ships a harness that renders the real
windows — same XAML, same styles, same compiled bindings — straight to PNG through
Avalonia's headless platform with Skia drawing:

```bash
dotnet run --project tools/SbMac.Preview ./artifacts/screenshots
```

It writes both themes for the main window, the expanded activity panel, the properties
pane, the Event Hubs tree and its event properties, and the connection, send and queue
dialogs — including the Event Hubs variant of the connection dialog — populated with
representative sample data.

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
dotnet test           # 172 tests
dotnet run --project src/SbMac.App    # run without packaging
```

Package a distributable `.app`, and optionally a `.dmg`:

```bash
./build/make-app.sh                     # this machine's architecture
./build/make-app.sh osx-x64             # Intel
./build/make-app.sh osx-arm64           # Apple Silicon
./build/make-app.sh osx-arm64 v1.2.0    # stamped as version 1.2.0

./build/make-dmg.sh artifacts/SB-Mac.app dist/SB-Mac.dmg
```

The output is self-contained (~112 MB) and ad-hoc code-signed.

**Version stamping.** `build/Info.plist` holds `0.0.0` as a placeholder; `make-app.sh`
overwrites both version keys from the release version it is given — as the second argument,
from `SBMAC_VERSION`, or from the tag the checkout sits exactly on — and fails the build if
the stamp didn't take. The release workflow passes the prepared version explicitly, because
`actions/checkout` fetches shallow and without tags, so `git describe` inside the script
would find nothing. A bundle reporting `0.0.0` was built outside the release workflow.
The stamp is applied before `codesign`, since editing `Info.plist` afterwards would
invalidate the signature.

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
    EventHubs/             event hubs, partitions, event reading and publishing
    Messaging/             peek, receive, send, purge, body decoding
    ImportExport/          definition DTOs and import/export
  src/SbMac.App/           Avalonia UI
    ViewModels/            including Tree/ and Dialogs/
    Views/                 windows and dialogs
    Styles/                Theme.axaml (tokens), Icons.axaml, AppStyles.axaml
    Converters/            icon-name to geometry lookup
  tests/SbMac.Tests/       172 tests, including headless UI and real-keychain tests
  tools/SbMac.Preview/     dev-only screenshot harness (not in the solution)
  build/                   Info.plist, icon generator, packaging and signing scripts
```

`SbMac.Core` has no reference to Avalonia, so the service layer can be driven from a CLI
or tests without a UI.

---

## Testing

```bash
dotnet test
```

172 tests, all passing, no Azure connection required. They cover message body decoding,
connection string parsing, secret storage, import/export round-trips, the Event Hubs
session, mappers and read-window arithmetic, and view model logic.

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
draining stops only after three consecutive empty batches rather than the first one. Four
receivers drain in parallel — a single link spends a purge waiting on round trips rather
than saturating anything — and each applies the empty-batch rule for itself.

**Late reads are discarded, not displayed.** Reads now run alongside each other, so a slow
peek can return after the user has selected something else. The result is dropped with a
line in the activity log rather than shown under another entity's name.

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

The Windows tool covers more than Service Bus and Event Hubs. These were out of scope and
are **not** in SB-Mac:

- Notification Hubs, Relay and Event Grid explorers
- Creating, editing or deleting event hubs and consumer groups — those are ARM operations,
  and SB-Mac browses Event Hubs over the data plane
- Message inspector / generator plugin model (`IBrokeredMessageInspector` and friends)
- Event processor checkpoint management
- SDK-side chunking and the deflate/zip inspectors

`SbMac.Core` is where they'd go — the tree and service layers are shaped to take
additional entity kinds without restructuring.

---

## Releasing

CI builds, tests and packages on every push and pull request, so a broken bundle is
caught before it reaches `main`.

Every push to `main` starts a queued release (up to GitHub Actions' 100-run queue limit). The
workflow finds the latest stable tag, increments its patch number (for example `v1.3.0` →
`v1.3.1`), builds the exact triggering commit for both architectures, and creates the tag and
GitHub Release only after every test and build succeeds.

For an intentional major or minor bump, create the desired tag and run the Release workflow
manually with that tag:

```bash
git tag v2.0.0
git push origin v2.0.0
gh workflow run Release -f tag=v2.0.0
```

That builds and tests both architectures, produces a `.dmg`, a `.zip` and a `.sha256` for
each, and opens a GitHub Release with install instructions. `.zip` archives are made with
`ditto` rather than `zip`, which preserves the bundle's symlinks and extended attributes —
plain `zip` mangles them and invalidates the code signature.

### Turning on Developer ID signing and notarization

The pipeline already does this — it just needs credentials. With no secrets configured it
ad-hoc signs and writes the quarantine workaround into the release notes. Add the five
secrets below and the same workflow produces a notarized release that opens on a double-click,
with the workaround removed from the notes automatically.

You need an [Apple Developer Program](https://developer.apple.com/programs/) membership
($99/year) for a **Developer ID Application** certificate.

Export the certificate and key from Keychain Access as a `.p12`, then base64 it:

```bash
base64 -i DeveloperID.p12 | pbcopy
```

Add these under **Settings → Secrets and variables → Actions**:

| Secret | What it is |
|---|---|
| `APPLE_CERTIFICATE_P12` | The base64 blob from above |
| `APPLE_CERTIFICATE_PASSWORD` | Password you set when exporting the `.p12` |
| `APPLE_SIGNING_IDENTITY` | e.g. `Developer ID Application: Jane Doe (ABCDE12345)` |
| `APPLE_ID` | Apple ID email used for notarization |
| `APPLE_TEAM_ID` | Ten-character team identifier |
| `APPLE_APP_PASSWORD` | An [app-specific password](https://support.apple.com/en-us/102654) — **not** your account password |

`APPLE_CERTIFICATE_P12` is the switch: the workflow resolves its presence into a step
output and skips every signing step when it's empty, because GitHub doesn't allow secrets
in `if:` expressions directly.

What the signing path does, in `build/sign-and-notarize.sh`:

1. Imports the certificate into a throwaway keychain that is deleted in an `always()` step,
   so the key never outlives the job even if the build fails.
2. Signs nested `.dylib`s **before** the bundle. Order matters — signing the bundle seals
   its contents, so anything signed afterwards invalidates the outer signature.
3. Signs with `--options runtime` and `build/entitlements.plist`. Notarization requires the
   hardened runtime, and .NET needs specific exemptions from it: the CoreCLR JIT writes
   executable memory at runtime, and a self-contained publish loads its own native
   libraries. Without `allow-jit`, `allow-unsigned-executable-memory` and
   `disable-library-validation`, a hardened .NET app crashes on launch.
4. Submits with `notarytool --wait`, then **staples** the ticket. Stapling is what makes
   first launch work offline; without it a user with no network sees the same rejection.
5. Asserts the result with `spctl --assess --type execute` — the check macOS actually runs
   on a quarantined download.

The `.dmg` is signed, notarized and stapled separately, because a disk image is its own
artifact and gets its own assessment when mounted.

Nothing in the ad-hoc path changes, so there's no risk in adding the secrets later.

---

## Licence

MIT — see [LICENSE](LICENSE).

This is an independent application, not a fork: it contains no source code from the
Windows Service Bus Explorer. It follows that project's naming and overall shape so
people already familiar with it can find their way around. See [NOTICE](NOTICE) for
attribution and third-party licences.
