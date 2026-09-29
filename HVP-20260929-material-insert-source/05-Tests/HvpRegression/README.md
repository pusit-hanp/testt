# HVP regression harness

Compiles the actual HvpFileProcessor/HvpService/HvpStateStore/HvpLog source with test-only configuration, Oracle and Dapper boundaries. No Pulllist substitute or reference remains. No network or SAP/DB connection. Uses temporary files only, retained for inspection.

2026-09-29 material insert result: 101 PASS, Failures: 0. Added configurable filename suffix, INSERT mapping for ROH/HALB/FERT, Material-only update/insert choice, NULL/default STR9 values, required insert fields, duplicate Material behavior, transaction rollback, same-PK insert race and safe validation diagnostics. Location tests include a missing first Issue St Loc while the later BOM column still exists. Evidence: create_image_temp/hvp-20260929-regression-final.log; review regressions failed before the fixes in hvp-20260929-review-red.log. SQL and transaction tests use boundaries, not a real Oracle server; the QA checks in DEPLOY.md remain required.

2026-09-14 standalone result: 56 PASS, Failures: 0. The previous 48 file/state regressions remain; 8 additional cases cover direct Material connection configuration, empty work, opening/disposal, command failures, parameter values and persistent retry/skip after Oracle open failures. Evidence: create_image_temp/hvp-standalone-regression-final.log. Separate net48 host tests and actual-DLL offline smoke tests live in ../HvpHostRegression and ../HvpRuntimeSmoke. The dated results below are historical.

Run on a machine that permits local test assemblies:

```powershell
dotnet run --project 05-Tests/HvpRegression/HvpRegression.csproj
```

For earlier baseline sources, use the regression harness shipped in the archived final package. The longrun suite directly tests HvpStateStore, which does not exist in those sources, so changing SourceDir alone will not compile this suite against them.

Requires .NET10 SDK for this isolated harness only; production remains .NET Framework4.8. NuGet.Config clears external package sources; no test package dependency. The longrun suite links HvpStateStore.cs and adds LogRetentionTests.cs/StatePerformanceTests.cs; its direct store tests require the longrun source. Use the archived final package's harness when testing earlier source.

21 cases cover missed files, ordering, checkpoint across restart, changed file versions, header normalization, truncated rows, retry blocking, folder visibility, null connection, replay of later files after an older revision, exclusive job lock, material-only SQL contract, persistent skip after the attempt limit, changed-version retry reset, configurable limits/fallback, legacy four-column checkpoints and changes to a queued file's CreationTime in both directions. The latter must defer without consuming an attempt, retain pending state and apply the new order after processor restart. SQL-contract checks do not prove Oracle affected rows or commit behavior. HvpWinService/SCM behavior must be checked separately in TEST.

2026-09-12 latest run: 21 PASS, Failures: 0. Before the ordering fix, the two new cases failed while the original 19 passed. Evidence: create_image_temp/hvp-final-review/order-red.log and order-green.log (also included under results in the latest ZIP). The standalone reproducer also now passes, leaving the expected HVP flag. The earlier retry-limit change separately produced 6 failures before its fix; retry-red.log/retry-green.log retain that history. This isolated harness does not build the production .NET Framework 4.8 solution.

2026-09-12 longrun update supersedes the preceding result: 48 PASS, Failures: 0. Added retention boundaries/defaults/disable/culture/permission failures/reparse directories, cache hits and invalidation, append-only state updates, truncated final record recovery, full-row corruption, retry preservation during compaction, append failure, compaction failure before and during File.Replace, partial pending-tail recovery and scan metrics. RED for missing longrun behavior is in create_image_temp/hvp-longrun-red.log; GREEN is in create_image_temp/hvp-longrun-green.log. Tests use real temporary files and Windows sharing/junction behavior; DB remains a stub. File symlinks could not be created without administrator privilege, so directory junction fixtures cover reparse paths and file ReparsePoint checks were reviewed in code.
