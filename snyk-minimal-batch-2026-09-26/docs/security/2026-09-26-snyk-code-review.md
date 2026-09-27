# Snyk Code: ชุดแก้ 31 ตำแหน่งสำหรับเครื่อง company

วันที่ 2026-09-26

อ้างอิง Snyk Code เดิม 31 findings (2 Medium, 29 Low) ผู้ใช้ยืนยันว่า local source ตรงกับเครื่อง company การแก้ครอบคลุมตำแหน่งทั้ง 31 แล้ว แต่ **ยังไม่มีผล Snyk scan หลังแก้** จึงยังไม่ยืนยันว่า findings เป็นศูนย์ ภาพ AI fix ที่ส่งเพิ่มเป็นข้อมูลอ้างอิง ไม่ใช่ผลการลอง application-users.js ที่แก้แล้ว

## ขอบเขตการแก้

- Production: เปลี่ยนชื่อ private/local parameter 5 จุดใน Controllers และ EmailOptionsValidator ให้ตรงหน้าที่ คงชื่อ method, TempData/configuration strings, parsing และ validation logic เดิม
- Production JavaScript: คง navigation guard ของ Open Redirect 2 จุดที่ทำไว้ก่อนหน้า ตรวจ origin และ HTTP/HTTPS ก่อน navigation; ไม่แทนที่ด้วย AI fix ในภาพ
- Tests: เปลี่ยน key ของ scenario 1 จุด, จำกัด reflection method names 3 จุด, สร้าง redaction marker ใน test 1 จุด, ตัด optional username ที่ไม่เกี่ยวกับ owner-scope test 1 จุด และปรับ username fixtures อีก 18 จุด
- ไม่เพิ่ม dependency, environment variable, scanner exclusion หรือ Ignore และไม่เปลี่ยน production username/authentication behavior
- รวมส่ง source และ tests ครั้งเดียวตาม [คู่มือ transfer](2026-09-26-snyk-company-transfer.md)

## การรักษาพฤติกรรมของ tests

Username ที่ใช้เป็นข้อมูลสมมติสร้างภายใน test ด้วยค่าที่ไม่ว่าง แล้วใช้ค่าเดียวกันทั้ง input และ expected ค่าที่ต้องเป็นคนเดียวกันใช้ร่วมกันตลอด test/class; ชื่อที่ต้องต่างใช้ suffix จึงต่างแน่นอน ไม่สร้าง default ใหม่ทุกครั้งที่เรียก helper ของ deduplication

คงการทดสอบ uppercase/whitespace, domain prefix `asia\\`, รูปแบบ `th` ตามด้วยเลข 8 หลัก, ความสัมพันธ์ username/EmployeeNo และ InlineData ที่ห้าม derive employee number จาก username ไว้ คง source-role labels เช่น CUSCI แยกจาก username ทุก assertion และ test case เดิมยังอยู่

Private helpers ของ Deduplicator และ OpenXml exporter ใช้ `null` เป็น “ใช้ default fixture” แทน default literal เดิม ตรวจแล้วไม่มี caller เดิมส่ง explicit null ค่า default ถูกสร้างครั้งเดียวต่อ class ส่วนฟังก์ชัน production ไม่เปลี่ยน

Redaction marker ใช้ `test-only-` ตามด้วย GUID และใช้ field เดียวกันทั้ง setup/assertion จึงไม่ว่างและยังแปลงเป็น integer ไม่ได้สำหรับกรณี invalid TimeoutSeconds ไม่มีการอ่าน secret จริงจากเครื่อง

การเปลี่ยน application ใน JavaScript ยังล้าง query เก่า; sorting ยังกลับ page 1 และเก็บ repeated filters, hash และ PathBase; HTTP localhost/custom port ยังใช้ได้ ถ้า URL ไม่ผ่าน guard จะไม่ navigate และไม่เปิด loading mask

## ตารางเทียบกับผลสแกนเดิม

เลขบรรทัดอ้างอิง screenshot ก่อนแก้ ไม่ใช่เลขบรรทัดปัจจุบัน สถานะทุกแถวรอ rescan ที่ company

| # | Finding เดิม | ไฟล์และบรรทัดเดิม | การแก้ในชุดนี้ | สถานะ Snyk |
|---|---|---|---|---|
| 1 | Low / Hardcoded Credentials | [RoleValidation.Application.Tests/Authentication/AuthenticationAccessEvaluatorTests.cs](../../RoleValidation.Application.Tests/Authentication/AuthenticationAccessEvaluatorTests.cs) : 197 | สร้าง TestUserName/SharedUserName ใน test; ใช้ร่วมระหว่าง employee กับ ExternalIdentity และคง uppercase/whitespace | รอสแกน company |
| 2 | Low / Hardcoded Credentials | [RoleValidation.Application.Tests/Email/PrepareEmailDeliveryArtifactHandlerTests.cs](../../RoleValidation.Application.Tests/Email/PrepareEmailDeliveryArtifactHandlerTests.cs) : 539 | ตัด optional username ออกจาก helper User; owner/role และ assertions ที่เลือก scope คงเดิม | รอสแกน company |
| 3 | Low / Hardcoded Credentials | [RoleValidation.Application.Tests/Employees/EmployeeIdentityResolverTests.cs](../../RoleValidation.Application.Tests/Employees/EmployeeIdentityResolverTests.cs) : 170 | สร้าง identity ใน test และคงชื่อร่วม, null และ InlineData ที่ห้ามแปลง username เป็นเลขพนักงาน | รอสแกน company |
| 4 | Low / Hardcoded Credentials | [RoleValidation.Application.Tests/Exports/ApplicationUserExportDeduplicatorTests.cs](../../RoleValidation.Application.Tests/Exports/ApplicationUserExportDeduplicatorTests.cs) : 173 | ใช้ default identity ค่าเดียวต่อ class; ชื่อที่ต้องต่างใช้ suffix .old เพื่อคงเงื่อนไข deduplication | รอสแกน company |
| 5 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/EvaApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/EvaApplicationUserProviderTests.cs) : 43 | สร้างชื่อสมมติใน test พร้อม prefix asia\\; ใช้ค่าเดียวกันใน row และ assertion | รอสแกน company |
| 6 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/IcProgrammingApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/IcProgrammingApplicationUserProviderTests.cs) : 47 | สร้างชื่อสมมติรูปแบบ th + เลข 8 หลัก; input และ expected ใช้ค่าเดียวกัน | รอสแกน company |
| 7 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/IcProgrammingApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/IcProgrammingApplicationUserProviderTests.cs) : 67 | สร้างชื่อสมมติรูปแบบ th + เลข 8 หลัก; คงการตรวจ fallback display name | รอสแกน company |
| 8 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/IdmApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/IdmApplicationUserProviderTests.cs) : 45 | สร้างชื่อสมมติใน test พร้อม prefix asia\\; ใช้ค่าเดียวกันใน row และ assertion | รอสแกน company |
| 9 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/MfmApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/MfmApplicationUserProviderTests.cs) : 40 | สร้างชื่อสมมติใน test พร้อม prefix asia\\; ใช้ค่าเดียวกันใน row และ assertion | รอสแกน company |
| 10 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/OpenMarketApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/OpenMarketApplicationUserProviderTests.cs) : 45 | สร้างชื่อสมมติใน test พร้อม prefix asia\\; ใช้ค่าเดียวกันใน row และ assertion | รอสแกน company |
| 11 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/PullListApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/PullListApplicationUserProviderTests.cs) : 46 | สร้างเลขพนักงานสมมติใน test แล้วใช้กับ EmployeeNo และ username ที่มี prefix th | รอสแกน company |
| 12 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/ApplicationUsers/PullListApplicationUserProviderTests.cs](../../RoleValidation.Infrastructure.Tests/ApplicationUsers/PullListApplicationUserProviderTests.cs) : 67 | สร้างเลขพนักงานสมมติใน test แล้วใช้กับ EmployeeNo และ username ที่มี prefix th; คง unknown-role assertion | รอสแกน company |
| 13 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/Email/DevelopmentEmailExecutionStoreTests.cs](../../RoleValidation.Infrastructure.Tests/Email/DevelopmentEmailExecutionStoreTests.cs) : 1323 | เปลี่ยน private/local parameter key เป็น scenarioName; dictionary key string และ scenario เดิม | รอสแกน company |
| 14 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/Email/ErsnOwnerScopeBuilderTests.cs](../../RoleValidation.Infrastructure.Tests/Email/ErsnOwnerScopeBuilderTests.cs) : 379 | แยก label ของ row ออกจาก username ที่สร้างด้วย TestRunId; role fields/labels เดิม | รอสแกน company |
| 15 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/Email/ErsnOwnerScopeBuilderTests.cs](../../RoleValidation.Infrastructure.Tests/Email/ErsnOwnerScopeBuilderTests.cs) : 397 | คง unresolved row และ label เดิม; username ใช้ TestRunId ภายใน test | รอสแกน company |
| 16 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/Email/ErsnOwnerWorkbookExporterTests.cs](../../RoleValidation.Infrastructure.Tests/Email/ErsnOwnerWorkbookExporterTests.cs) : 287 | คง source-role labels รวม CUSCI; สร้างเฉพาะ username ภายใน test | รอสแกน company |
| 17 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/Employees/OracleEmployeeReaderTests.cs](../../RoleValidation.Infrastructure.Tests/Employees/OracleEmployeeReaderTests.cs) : 17 | ใช้ชื่อสมมติเดียวกันใน Oracle row และ mapped-value assertion; ไม่เปิด connection | รอสแกน company |
| 18 | Low / Hardcoded Credentials | [RoleValidation.Infrastructure.Tests/Exports/OpenXmlApplicationUserWorkbookExporterTests.cs](../../RoleValidation.Infrastructure.Tests/Exports/OpenXmlApplicationUserWorkbookExporterTests.cs) : 275 | ใช้ default username ค่าเดียวต่อ classและ expected workbook; ชื่ออื่นใช้ suffix ที่ต่างแน่นอน | รอสแกน company |
| 19 | Low / Code Injection | [RoleValidation.Web.Tests/Applications/ApplicationsControllerTests.cs](../../RoleValidation.Web.Tests/Applications/ApplicationsControllerTests.cs) : 87 | เพิ่ม nameof allowlist ก่อน GetMethod; Theory actions และ security assertions เดิม | รอสแกน company |
| 20 | Low / Hardcoded Credentials | [RoleValidation.Web.Tests/Authentication/AuthenticationControllerTests.cs](../../RoleValidation.Web.Tests/Authentication/AuthenticationControllerTests.cs) : 355 | ใช้ identity ค่าเดียวกันใน employee, encrypted callback payload และ NameIdentifier assertion | รอสแกน company |
| 21 | Low / Hardcoded Credentials | [RoleValidation.Web.Tests/Authentication/AuthenticationFlowServiceTests.cs](../../RoleValidation.Web.Tests/Authentication/AuthenticationFlowServiceTests.cs) : 143 | ใช้ identity ค่าเดียวกันใน employee และ encrypted callback payload | รอสแกน company |
| 22 | Low / Hardcoded Credentials | [RoleValidation.Web.Tests/Email/EmailConfigurationHttpTests.cs](../../RoleValidation.Web.Tests/Email/EmailConfigurationHttpTests.cs) : 27 | เปลี่ยน sentinel เป็น static readonly test-only GUID marker; assertions เดิมและ invalid TimeoutSeconds ยังทำงาน | รอสแกน company |
| 23 | Low / Code Injection | [RoleValidation.Web.Tests/RoleOwners/RoleOwnersControllerTests.cs](../../RoleValidation.Web.Tests/RoleOwners/RoleOwnersControllerTests.cs) : 358 | เพิ่ม nameof allowlist สำหรับ Assign/Reassign/Deactivate; assertions เดิม | รอสแกน company |
| 24 | Low / Code Injection | [RoleValidation.Web.Tests/ValidationRoles/ValidationRolesControllerTests.cs](../../RoleValidation.Web.Tests/ValidationRoles/ValidationRolesControllerTests.cs) : 130 | เพิ่ม nameof allowlist สำหรับ Save/Deactivate; assertions เดิม | รอสแกน company |
| 25 | Low / Hardcoded Credentials | [RoleValidation.Web/Controllers/ApplicationsController.cs](../../RoleValidation.Web/Controllers/ApplicationsController.cs) : 169 | เปลี่ยน key เป็น tempDataName; method, lookup strings และ integer parsing เดิม | รอสแกน company |
| 26 | Low / Hardcoded Credentials | [RoleValidation.Web/Controllers/SourceRoleMappingsController.cs](../../RoleValidation.Web/Controllers/SourceRoleMappingsController.cs) : 344 | เปลี่ยน key เป็น tempDataName; lookup strings และ integer/null behavior เดิม | รอสแกน company |
| 27 | Low / Hardcoded Credentials | [RoleValidation.Web/Controllers/ValidationRolesController.cs](../../RoleValidation.Web/Controllers/ValidationRolesController.cs) : 242 | เปลี่ยน key เป็น tempDataName; integer parsing เดิม | รอสแกน company |
| 28 | Low / Hardcoded Credentials | [RoleValidation.Web/Controllers/ValidationRolesController.cs](../../RoleValidation.Web/Controllers/ValidationRolesController.cs) : 253 | เปลี่ยน key เป็น tempDataName; boolean parsing เดิม | รอสแกน company |
| 29 | Low / Hardcoded Credentials | [RoleValidation.Web/Email/EmailOptionsValidator.cs](../../RoleValidation.Web/Email/EmailOptionsValidator.cs) : 45 | เปลี่ยน key เป็น settingName; configuration names และ validation messages เดิม | รอสแกน company |
| 30 | Medium / Open Redirect | [RoleValidation.Web/wwwroot/js/application-users.js](../../RoleValidation.Web/wwwroot/js/application-users.js) : 20 | คง navigateWithinOrigin ที่ตรวจ same origin และ HTTP/HTTPS ก่อน navigate | รอสแกน company |
| 31 | Medium / Open Redirect | [RoleValidation.Web/wwwroot/js/application-users.js](../../RoleValidation.Web/wwwroot/js/application-users.js) : 200 | ใช้ navigation guard เดิมร่วมกับ sorting; คง repeated filters, page reset, hash และ PathBase | รอสแกน company |

## Verification ใน local

- Final Release build ผ่าน 0 warnings / 0 errors
- JavaScript suite ผ่าน 41 tests
- .NET test modules: Core, Application และ Infrastructure ผ่านในรอบล่าสุด
- Web tests ยังยืนยันไม่ได้: Windows Application Control บล็อก `RoleValidation.Web.dll` ด้วย `FileLoadException` / `0x800711C7` ระหว่างรัน มีทั้ง load errors และ theory discovery error ผลรวม 1,847 tests: ผ่าน 1,401, failed 446, skipped 0; คำสั่งจบด้วย exit code 1 จึงไม่ถือว่า solution test suite ผ่านครบ
- การบล็อกนี้เกิดบนเครื่อง local ไม่ใช่หลักฐานสถานะของเครื่อง company ไม่มีการปิดหรือข้าม policy
- Independent source review: ไม่พบ material findings ใน diff 25 C# files เทียบกับ dirty-workspace baseline; ไม่มีการลด assertions หรือ test cases ของ username fixtures

คำสั่งรันจากโฟลเดอร์ที่มี `RoleValidation.slnx`:

```powershell
dotnet build RoleValidation.slnx --configuration Release --no-restore
dotnet test --solution RoleValidation.slnx --configuration Release --no-build --no-restore
node --test "RoleValidation.Web.Tests/JavaScript/*.test.js"
```

ผลจากรอบก่อนหน้าซึ่ง Web เคยผ่านและ Infrastructure ถูกบล็อก ไม่ใช้แทนผลของ source ชุดล่าสุดนี้ ต้องรัน Web tests ใหม่ใน environment ที่ policy อนุญาต

## การยืนยันบนเครื่อง company

Transfer code ทั้งชุดครั้งเดียว จากนั้น build/test และสแกน Snyk Code โดยใช้ scope/organization เดิม เก็บผลรวมและ Data flow ของรายการที่ยังเหลือ ไม่มีการตั้ง Ignore/Not vulnerable ในขั้นตอนนี้

Snyk Open Source เป็นอีกผลหนึ่ง: ปัญหา targeting pack `PackageOverrides.txt` และ ThreatLocker บล็อก helper `parse.exe` ยังต้องจัดการบนเครื่อง company การแก้ source ชุดนี้ไม่ได้เปลี่ยน SDK หรือ policy ดังกล่าว และไม่ได้หมายความว่า Open Source scan ผ่านแล้ว
