# Update Notification Design

## Context

SB-Mac publishes a stable GitHub release for every merge to `main`. The release
workflow stamps the release tag into `CFBundleShortVersionString` inside the
packaged application, but the running app does not currently check whether a
newer release exists.

The feature will inform users about newer releases without downloading or
installing anything. A user who chooses to defer the update may be reminded
again after 24 hours.

## Goals

- Check for a newer stable GitHub release after the main window opens.
- Compare the release tag with the version stamped into the running `.app`.
- Show the installed and available versions in a native in-app dialog.
- Offer `View Release` and `Later` actions.
- Open the official GitHub release page when requested.
- Limit both network attempts and reminders to once per 24 hours.
- Keep application startup usable when every part of the update check fails.

## Non-goals

- Downloading, mounting, installing, or relaunching an update.
- Background polling while the application remains open.
- Prerelease or draft release notifications.
- A user preference for disabling checks.
- Authentication with GitHub.
- Release notes rendered inside the application.

## Considered Approaches

### Built-in GitHub release checker (selected)

Call GitHub's latest-release API, parse its small JSON response, and compare the
tag with the local bundle version. This requires no new package and fits the
notification-only scope.

### Follow the `/releases/latest` redirect

The final redirect URL contains the release tag, but treating redirect behavior
as structured release metadata is more brittle and harder to test clearly.

### Sparkle

Sparkle provides a mature macOS update pipeline, but it introduces an appcast,
native-framework packaging, signing considerations, and installation behavior
that this notification-only feature does not need.

## Architecture

### Update checker

An `IUpdateChecker` abstraction and production `UpdateChecker` implementation
will live under `SbMac.App/Services`. `CheckAsync` returns an `UpdateInfo` only
when a newer release is available; all other outcomes return no update.

The implementation has four replaceable inputs so its behavior can be tested
without the network, clock, real bundle, or user configuration directory:

- `HttpClient` for the GitHub request.
- An application-version provider.
- An update-check state store.
- `TimeProvider` for the 24-hour boundary.

The production composition remains manual. `App` creates the production checker
and passes it to `MainWindowViewModel`. The view model's existing parameterless
construction remains network-free for tests and previews; only the production
composition root opts into update checks.

### Local version

The version provider reads `CFBundleShortVersionString` from the running app's
`Contents/Info.plist`. This is the canonical installed version because
`build/make-app.sh` already stamps it from the release tag and verifies it during
packaging.

If the process is not running from a valid app bundle, the version cannot be
parsed, or the bundle version is `0.0.0`, checking is skipped. `0.0.0` explicitly
means an unversioned local build in the current packaging workflow and must not
be presented as an outdated public release.

### Release source and version rules

The checker requests:

`https://api.github.com/repos/chanakya-net/ServiceBusExplorer-Mac/releases/latest`

It sends a product-specific `User-Agent` and uses a five-second timeout. GitHub's
latest-release endpoint excludes draft and prerelease releases. The response
fields used are `tag_name` and `html_url`.

Both local and remote versions must be three non-negative integer components.
The remote tag may have the repository's leading `v`. A notification appears
only when the remote version is strictly greater than the local version.

Before opening `html_url`, the app verifies that it is an absolute HTTPS URL on
`github.com`. An invalid URL makes the release response unusable.

### Reminder state

The checker stores `lastUpdateCheckUtc` in the existing
`~/Library/Application Support/SB-Mac/settings.json` object. It updates only that
property and preserves unrelated or unknown settings fields.

The timestamp is written when an eligible startup begins its network attempt,
not only after a successful response. This prevents offline launches or GitHub
failures from causing a request on every app start. Another attempt becomes
eligible 24 hours later. Consequently, choosing `Later` also permits the same
release to be shown again after 24 hours.

Invalid or missing settings are treated as having no prior check. Directory or
file write failures do not block the current in-memory check.

### User interaction

After the existing namespace initialization finishes, the main window asks the
checker for an update. Because the call starts from the window's loaded event,
the interface is already visible while the asynchronous request runs.

`IUiServices` gains an update prompt operation. The existing lightweight message
dialog is extended to support a caller-supplied dismiss label, producing:

- `Later`, which closes the dialog.
- `View Release`, which is the default action and opens the validated release URL
  in the user's default browser.

The dialog states both versions, for example: `Version 1.4.0 is available. You
are using 1.3.2.` No download begins inside SB-Mac.

## Data Flow

1. `MainWindow.OnLoaded` runs the view model's existing initialization.
2. The view model asks its injected update checker for an available update.
3. The checker reads the installed bundle version.
4. If the build is eligible, the state store evaluates the 24-hour interval.
5. When due, the checker records the attempt and requests GitHub's latest stable
   release.
6. The checker validates and compares the response.
7. A newer version is returned to the view model as `UpdateInfo`.
8. The UI service shows the update dialog.
9. `Later` ends the flow; `View Release` opens the validated GitHub URL.

## Failure Handling

The feature is best-effort and must never prevent normal application use.
Network failures, timeouts, non-success HTTP responses, rate limits, malformed
JSON, invalid versions, invalid URLs, unreadable bundle metadata, invalid local
state, state-write failures, and browser-launch failures are contained by the
update flow.

Failures do not show an error dialog or enter the operation log. Update checking
is ancillary, and surfacing offline or GitHub errors would turn a convenience
feature into startup noise.

## Testing

Tests will be written before implementation and will cover:

- Newer, equal, and older semantic versions.
- Leading-`v` tags and malformed local or remote versions.
- Valid newer-release JSON and validated release URLs.
- Non-success responses, timeouts, and malformed JSON.
- Skipping unbundled and `0.0.0` builds.
- Skipping attempts inside 24 hours and allowing them at the boundary.
- Recording an attempt before a failed request.
- Preserving unknown fields in `settings.json`.
- Read and write failures in the settings store.
- `View Release` and `Later` behavior in the headless update dialog.
- Production composition without allowing tests or previews to make real network
  requests.

Verification will include the complete test suite, a Release build, and the
existing `.app` packaging verification.

## Expected Repository Changes

- Add the update-checking service, bundle-version provider, and state-store code
  under `src/SbMac.App/Services`.
- Extend `IUiServices`, `MainWindow`, and `MessageDialog` for the update prompt and
  browser action.
- Inject the production checker from `App` and invoke it during
  `MainWindowViewModel.InitializeAsync`.
- Add focused checker, state-store, and headless dialog tests under
  `tests/SbMac.Tests`.
- Document the startup notification behavior in `README.md`.
