# Admin pages HTTP boundary review

Run: 2026-11-08 18:44:07 +07:00
Target: `http://localhost:58761`
Result: **100/100 passed; 0 failed**.

The suite sends actual HTTP requests with Invoke-WebRequest, cookie sessions, URL-encoded form bodies and anti-forgery values extracted from rendered Razor views. It exercises IISExpress, .NET Framework 4.8, System.Web, MVC 5.2.3, Razor, Dapper and the delivered controllers/services/DAOs. It does not invoke actions directly or stub HTTP results.

Company BaseDAO/BaseService/SessionFactory and database execution use CompanyBoundaries.cs. Fixture/Stats verifies that denied or invalid requests never reach this DB boundary. Fixture/Reset restores F5 / SERVICE / Y between cases. The suite finishes by restoring that seed.

This is not Oracle integration evidence. Oracle SQL syntax, schema compatibility, Dapper/provider OUT binds, transactions, concurrency, NLS configuration, production authentication and the company build remain TEST/UAT checks. The TP/WIP cases verify serialization and grouping of fixture projections; they do not prove Oracle joins or fallback selection. Host TCODEs are CR01/SM01. Permission XX is denied under that config; the configured-XX fail-closed branch belongs to the separate authorization unit tests.

Anti-forgery failures return the real MVC HttpAntiForgeryException (HTTP 500 in this debug test host). Tests require the exception signature plus zero DB reads/writes, so an arbitrary server error cannot count as a pass. Production error presentation is not represented by this debug host.

Requires PowerShell 7.5+ (ConvertFrom-Json -DateKind String preserves exact wire dates). Run after building and starting the isolated Framework host:

```powershell
pwsh -File 05-Tests/AdminPages/Framework/http-tests.ps1
```

| Case | Result | Detail |
| --- | --- | --- |
| Razor GET /CostRollup/Index | PASS |  |
| Razor GET /ServiceMonitoring/Index | PASS |  |
| Anonymous access /CostRollup/Index | PASS |  |
| Anonymous access /ServiceMonitoring/Index | PASS |  |
| Anonymous access /CostRollup/Search | PASS |  |
| Anonymous access /ServiceMonitoring/GetSummary | PASS |  |
| Anonymous access /CostRollup/Add | PASS |  |
| Anonymous access /CostRollup/Update | PASS |  |
| Anonymous access /CostRollup/Delete | PASS |  |
| Anonymous access /ServiceMonitoring/TrackBatch | PASS |  |
| Permission <WRONG> denied /CostRollup/Index | PASS |  |
| Permission <WRONG> denied /ServiceMonitoring/Index | PASS |  |
| Permission <WRONG> denied /CostRollup/Search | PASS |  |
| Permission <WRONG> denied /ServiceMonitoring/GetSummary | PASS |  |
| Permission <WRONG> denied /CostRollup/Add | PASS |  |
| Permission <WRONG> denied /CostRollup/Update | PASS |  |
| Permission <WRONG> denied /CostRollup/Delete | PASS |  |
| Permission <WRONG> denied /ServiceMonitoring/TrackBatch | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /CostRollup/Index | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /ServiceMonitoring/Index | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /CostRollup/Search | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /ServiceMonitoring/GetSummary | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /CostRollup/Add | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /CostRollup/Update | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /CostRollup/Delete | PASS |  |
| Permission <XCR01,CR010,XSM01,SM010> denied /ServiceMonitoring/TrackBatch | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /CostRollup/Index | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /ServiceMonitoring/Index | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /CostRollup/Search | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /ServiceMonitoring/GetSummary | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /CostRollup/Add | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /CostRollup/Update | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /CostRollup/Delete | PASS |  |
| Permission <CR01.extra,SM01/extra> denied /ServiceMonitoring/TrackBatch | PASS |  |
| Permission <XX> denied /CostRollup/Index | PASS |  |
| Permission <XX> denied /ServiceMonitoring/Index | PASS |  |
| Permission <XX> denied /CostRollup/Search | PASS |  |
| Permission <XX> denied /ServiceMonitoring/GetSummary | PASS |  |
| Permission <XX> denied /CostRollup/Add | PASS |  |
| Permission <XX> denied /CostRollup/Update | PASS |  |
| Permission <XX> denied /CostRollup/Delete | PASS |  |
| Permission <XX> denied /ServiceMonitoring/TrackBatch | PASS |  |
| Permission <> denied /CostRollup/Index | PASS |  |
| Permission <> denied /ServiceMonitoring/Index | PASS |  |
| Permission <> denied /CostRollup/Search | PASS |  |
| Permission <> denied /ServiceMonitoring/GetSummary | PASS |  |
| Permission <> denied /CostRollup/Add | PASS |  |
| Permission <> denied /CostRollup/Update | PASS |  |
| Permission <> denied /CostRollup/Delete | PASS |  |
| Permission <> denied /ServiceMonitoring/TrackBatch | PASS |  |
| Invalid session actor <SERVICE> denied | PASS |  |
| Invalid session actor <service> denied | PASS |  |
| Invalid session actor <EMP 123> denied | PASS |  |
| Page permissions stay separate | PASS |  |
| Delimited exact permission tokens are accepted | PASS |  |
| Anti-forgery missing /CostRollup/Add | PASS |  |
| Anti-forgery mismatched /CostRollup/Add | PASS |  |
| Anti-forgery malformed /CostRollup/Add | PASS |  |
| Anti-forgery missing-cookie /CostRollup/Add | PASS |  |
| GET cannot invoke POST action /CostRollup/Add | PASS |  |
| Anti-forgery missing /CostRollup/Update | PASS |  |
| Anti-forgery mismatched /CostRollup/Update | PASS |  |
| Anti-forgery malformed /CostRollup/Update | PASS |  |
| Anti-forgery missing-cookie /CostRollup/Update | PASS |  |
| GET cannot invoke POST action /CostRollup/Update | PASS |  |
| Anti-forgery missing /CostRollup/Delete | PASS |  |
| Anti-forgery mismatched /CostRollup/Delete | PASS |  |
| Anti-forgery malformed /CostRollup/Delete | PASS |  |
| Anti-forgery missing-cookie /CostRollup/Delete | PASS |  |
| GET cannot invoke POST action /CostRollup/Delete | PASS |  |
| Anti-forgery missing /ServiceMonitoring/TrackBatch | PASS |  |
| Anti-forgery mismatched /ServiceMonitoring/TrackBatch | PASS |  |
| Anti-forgery malformed /ServiceMonitoring/TrackBatch | PASS |  |
| Anti-forgery missing-cookie /ServiceMonitoring/TrackBatch | PASS |  |
| GET cannot invoke POST action /ServiceMonitoring/TrackBatch | PASS |  |
| Cost Search JSON preserves Oracle wall time and is read-only | PASS |  |
| Invalid Cost Search customer=ABC | PASS |  |
| Invalid Cost Search customer=F5%27%20OR%201%3D1-- | PASS |  |
| Invalid Cost Search status=X | PASS |  |
| Invalid Add invalid customer | PASS |  |
| Invalid Add new N | PASS |  |
| Invalid Add impossible date | PASS |  |
| Invalid Add timezone suffix | PASS |  |
| Invalid Add one date | PASS |  |
| Invalid Add reversed range | PASS |  |
| Add uses session actor, preserves leading zero and exact dates | PASS |  |
| Duplicate Add returns 409 without replacing the existing row | PASS |  |
| Update binds Original snapshot and ignores forged actor/reference | PASS |  |
| Update cannot change customer key | PASS |  |
| Delete rejects stale snapshot, accepts current snapshot, then returns missing | PASS |  |
| Summary is one read with all nine movement rows and PK311 N/A | PASS |  |
| Tracking preserves order, deduplicates and serializes Oracle wall time | PASS |  |
| Invalid Tracking no IDs | PASS |  |
| Invalid Tracking 51 IDs | PASS |  |
| Invalid Tracking 51 duplicate IDs | PASS |  |
| Invalid Tracking SQL injection | PASS |  |
| Invalid Tracking wildcard | PASS |  |
| Invalid Tracking 51 characters | PASS |  |
| Invalid Tracking control character | PASS |  |
| Tracking accepts exactly 50 slips in one query | PASS |  |
