# Stable NuGet migration (beta, 2026-10-23)

## Decisions and breaking changes

- Microsoft/System packages: 10.0.13. Microsoft.Data.Sqlite now restores
  SQLitePCLRaw 2.1.13 instead of vulnerable 2.1.11; obsolete NU1903 suppression
  removed. A regression test checks the loaded native SQLite is >=3.50.2,
  WAL mode and data persistence after reopen, not just package metadata.
- SharpCompress: 0.50.7. Compressed TAR is no longer exposed through the
  random-access archive API. Use content detection and streaming TAR readers,
  with a separate totals pass; preserve ZIP/7z paths, overwrite and cancellation.
- protobuf-net: 3.4.40. Existing Steam account protobuf field numbers and legacy
  wire bytes are covered by read/write tests; tests never access real credentials.
- Avalonia: 12.1.4; WebView 12.1.1; FluentIcons 2.1.344;
  Xaml.Behaviors.Avalonia 12.0.7. Replace deprecated Avalonia.Diagnostics with
  AvaloniaUI.DiagnosticsSupport 2.2.3 and deprecated Avalonia.ReactiveUI with
  ReactiveUI.Avalonia.Reactive 12.1.6 (System.Reactive interoperability flavor).
  ReactiveUI 26 splits reactive/binding namespaces, replaces RxApp with
  RxSchedulers and requires a builder configure delegate in UseReactiveUI.
  Global usings preserve the existing Unit/command/observable MVVM code without
  rewriting it to the lean Primitives flavor. No XAML redesign.
- Velopack library and local CLI: 1.2.161. The library replaced NuGet.Versioning
  with its own SemanticVersion and removed WaitExitThenApplyUpdatesAsync.
  Keep the application's async entry point, then call the synchronous launcher
  after initialization. Preserve legacy/native feeds, SHA256 and SHA1 fallback,
  release/beta versions, and mark lateral moves/downgrades correctly.
  Bound the full manifest read (including the body), forward cancellation during
  channel switches, and apply a resettable idle timeout to package downloads.
  Regression tests cover stalled bodies with both timeout and caller cancellation.
- Packaging: `.config/dotnet-tools.json` pins vpk. All packaging entry points use
  `Invoke-Velopack.ps1`, which checks UI/Core/CLI version agreement before restore
  or packaging, disables CLI update checks and never changes the global tool installation. `--delta` takes
  a mode, not a package filename. Velopack 1.2.158+ removed old bsdiff fallback.
- Test tooling: Test SDK 18.11.0, runner 4.0.1, Coverlet collector 10.1.0,
  Moq 4.21.0. Use xunit.v3.mtp-off 4.0.2 with executable test projects to retain
  VSTest/TRX/XPlat Code Coverage. The default xunit.v3 package enables MTP v2,
  which is not compatible with the existing Coverlet collector workflow.
  Replace the removed assembly parallelization setting; retain serial execution
  because tests share static application state.

## Dependency policy

All direct dependencies target current stable, listed releases. Do not force
independent major upgrades of SkiaSharp/HarfBuzzSharp/SQLitePCLRaw beyond the
versions selected by their supported parent packages. The transitive outdated
report is therefore intentionally nonempty. No application locale strings or
PL/EN resources are changed.

Final audit: 106 unique project dependency packages; zero outdated direct
references, zero prerelease packages, zero vulnerability/deprecation findings.
17 unique transitive packages have newer releases: the SkiaSharp/HarfBuzzSharp
families, SQLitePCLRaw family, MicroCom.Runtime, Tmds.DBus.Protocol and
Microsoft.Bcl.AsyncInterfaces. Keep parent-selected versions rather than adding
unsupported direct overrides.

## Verification and release gates

Recorded checks (2026-10-23):

| Check | Result |
|---|---|
| Entire solution build, Debug + Release | Passed |
| Core tests, Debug + Release | 315 passed in each configuration; unsafe global-ToU class excluded |
| Read-only catalog E2E, Debug + Release | 6 passed in each configuration; isolated temporary root |
| VSTest TRX and XPlat Code Coverage | Generated successfully |
| Pinned toolchain offline tests, PowerShell 5.1 + 7 | Passed |
| Existing release text encoding checks, PowerShell 5.1 + 7 | Passed, no network calls |
| NuGet vulnerability/deprecation scan, including transitive packages | No findings after restore |
| Self-contained Windows x64 Release publish | Passed; unpacked output; Debug diagnostics DLL absent |
| Local Velopack portable/full package smoke test | Passed with pinned CLI; no installer execution/signing/publication; packaged Update.exe is x64 (PE 0x8664) |

Migration regression suites: SharpCompressMigrationTests, ProtobufMigrationTests,
SqliteDependencyMigrationTests and VelopackMigrationTests. Offline packaging
wrapper tests run under both Windows PowerShell 5.1 and PowerShell 7.

Do not run ModInstanceTouConfigServiceTests on a developer profile: the existing
test writes and deletes the real global ToU settings. Core test verification
excludes that class. E2E verification selects the read-only catalog suite and
uses a unique temporary root, not real game installs/download/launch suites.

Before public release: manually test an existing 0.0.1298-based installation
updating to this beta, full/delta compatibility where supported, release/beta
switching (including downgrade), restart, ReactiveUI activation/commands,
Windows WebView and visual UI behavior. A successful build/package is not proof
that the installed update flow works. Publication requires separate user approval.
The user subsequently requested release 3.0.14-beta, explicitly unsigned
(`signingMode=none`, no SignPath). Automated checks above do not imply that the
manual UI/installed-update gates have been completed.

Read-only reviews found no migration-specific blocker after the updater timeout
fixes. Existing async-void double-click command in MainWindowViewModel (confirmed
also in HEAD), old extractor progress localization, and solid-reader write-retry
semantics are pre-existing follow-up work, not changes made in this migration.
Existing xUnit1051 cancellation-token advisories remain in older tests; new
migration tests observe the test cancellation token.

## Sources used

MCP RAG/documentation resources were unavailable; used local source and official:

- NuGet registration/nuspec metadata: https://api.nuget.org/v3/index.json
- https://learn.microsoft.com/en-us/dotnet/core/tools/local-tools-how-to-use
- https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list
- https://github.com/advisories/GHSA-2m69-gcr7-jv3q
- https://github.com/adamhathcock/sharpcompress/releases
- https://github.com/protobuf-net/protobuf-net/releases
- https://github.com/AvaloniaUI/Avalonia/releases
- https://github.com/reactiveui/ReactiveUI.Avalonia/releases
- https://github.com/velopack/velopack/releases
- Velopack 1.2.161 source, revision 92d6a1c91716729d449034df5c50307dcce39493
- https://xunit.net/docs/getting-started/v3/migration
- https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/VSTestIntegration.md
