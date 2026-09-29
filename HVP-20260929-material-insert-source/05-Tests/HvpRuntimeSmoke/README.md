# HVP actual-DLL smoke test

Offline integration test for the Release net48 build. No Oracle connection is opened and no SAP/DB data is accessed. The runner copies the production DLLs and generated binding redirects, replacing Material credentials with placeholders before running.

Build Z02JHVPService first, then run with Windows PowerShell and a Visual Studio C# compiler installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\05-Tests\HvpRuntimeSmoke\Run.ps1
```

Optional -BuildDir selects another built output; -Compiler selects a Roslyn csc.exe. The compiler targets the installed .NET Framework runtime, not .NET 10. Each invocation creates a new bin/<run-id> directory and prints its path. A missing build dependency cannot be supplied by an earlier run's DLLs. These local test outputs are retained for inspection, not used by the service.

Six checks cover the supplied SID descriptor, real Dapper/Oracle parameter setup and DBNull mapping, actual ASN1/JSON dependencies under net48, empty HvpService work, and absence of Pulllist assembly references. Dapper's normal Execute path supplies a cache identity; the fixture constructs that identity offline to exercise binding without connecting. This does not validate Oracle authentication, network access, server version, transactions or affected rows.

Run the regression for stale dependencies with the same optional -BuildDir and -Compiler arguments:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\05-Tests\HvpRuntimeSmoke\Verify-Isolation.ps1
```

It runs the actual runner with a complete build, then with a separate build deliberately missing System.Text.Json.dll. The second invocation must fail for the missing JSON reference even after the first succeeds. Fixtures use a copy of the unmodified runner and never delete or change production build files.

Test source may be supplied separately in a handover; test binaries and bin outputs are excluded. See the current delivery verification report for results and the distinction between these offline checks and live QA.
