# Snyk source batch สำหรับเครื่อง company

วันที่ 2026-09-26

ชุดนี้รวมการแก้ source ครบตำแหน่งที่แจ้งเดิม 31 findings เพื่อ transfer ครั้งเดียว **ยังไม่ใช่ผลยืนยันว่า Snyk เป็นศูนย์** ต้องสแกนหลังนำเข้า การแก้ JavaScript เดิมยังไม่เคยลองบน company; ภาพ AI fix เป็น reference เท่านั้น

## สิ่งที่ส่ง

ZIP มี source/test files 27 ไฟล์ และเอกสาร 2 ไฟล์ พร้อม `FILES.sha256` สำหรับเทียบไฟล์หลังแตก ZIP โครงสร้างเริ่มที่ `RoleValidation.Web`, `RoleValidation.Web.Tests` ฯลฯ ตรงกับโฟลเดอร์ที่มี `RoleValidation.slnx` ไม่มี bin/obj, SDK, credentials หรือการตั้งค่า policy ในชุดส่ง

- C# production 4 ไฟล์: เปลี่ยนเฉพาะชื่อ private/local parameter 5 จุด ไม่เปลี่ยน business logic หรือ username behavior
- JavaScript production 1 ไฟล์: navigation guard ของ 2 Open Redirect findings ที่ทำไว้ก่อนหน้า
- C# tests 21 ไฟล์: parameter name, reflection allowlist, test-only marker และ self-contained identity fixtures คง assertions/test cases เดิม
- JavaScript behavior tests 1 ไฟล์: รวมกรณี URL ภายนอก, query/filter, hash และ PathBase

ไม่มี dependency ใหม่ ไม่ใช้ env สำหรับ fixtures ไม่ตั้ง Ignore/Not vulnerable ไม่ exclude tests และไม่มีการเปลี่ยน database/API contract

## ผลตรวจ local ล่าสุด

Release build ผ่าน 0 warnings / 0 errors; JavaScript ผ่าน 41 tests

.NET: Core, Application และ Infrastructure modules ผ่าน รวมทั้ง solution ผ่าน 1,401 จาก 1,847 tests แต่ Web มี 446 errors ในรอบที่ Windows Application Control บล็อก `RoleValidation.Web.dll` (`0x800711C7`) จึงยังไม่ยืนยัน Web tests และไม่ถือว่า solution ผ่านครบ ไม่มีการข้าม policy

Independent source review ไม่พบ material findings ใน diff รอบนี้ ผลทดสอบเก่าก่อนเปลี่ยน C# ไม่ใช้แทนผลของ source ชุดนี้

## ขั้นตอน transfer ครั้งเดียว

1. แตก ZIP ลงโฟลเดอร์ชั่วคราว ตรวจรายชื่อไฟล์ใน `FILES.sha256` และเทียบกับ source บน company ก่อนแทนที่ ไฟล์เป็น full source จาก workspace ที่ผู้ใช้ยืนยันว่าตรงกัน ไม่ใช่ patch เทียบ Git HEAD
2. นำทุกไฟล์ตามโครงสร้างไปไว้ใต้ `C:\IT_CodeRepo\Developments\Celestica\RoleValidation` ซึ่งเป็น solution root เก็บการเปลี่ยนแปลงอื่นบนเครื่อง company ไว้ตามกระบวนการของทีม
3. Build และ test จาก solution root:

```powershell
dotnet build RoleValidation.slnx --configuration Release
dotnet test --solution RoleValidation.slnx --configuration Release --no-build --no-restore
```

ถ้ามี Node.js ใน environment ทดสอบ ให้รัน behavior tests ด้วย:

```powershell
node --test "RoleValidation.Web.Tests/JavaScript/*.test.js"
```

4. สแกน Code Security ผ่าน Snyk extension เดิมใน Visual Studio ใช้ organization `cth-custom-apps` และ scope/settings เดิม ไม่จำเป็นต้องติดตั้ง standalone Snyk CLI เพื่อขั้นตอนนี้
5. เก็บจำนวน findings ใหม่ และ Fix analysis/Data flow ของรายการที่ยังเหลือ ใช้ [ตาราง 31 ตำแหน่งเดิม](2026-09-26-snyk-code-review.md) เทียบ เพราะเลขบรรทัดเปลี่ยนหลังแก้

หาก Application Control บล็อกไฟล์ที่จำเป็นต่อ build/test/scan ให้ใช้ขั้นตอน Request Access ของบริษัท ไม่ย้าย executable หรือแก้ policy เพื่อข้ามการบล็อก

Snyk Open Source dependency scan ยังมีเรื่อง `PackageOverrides.txt` และ ThreatLocker บล็อก `parse.exe` ที่ต้องจัดการแยก ผล Code Security และการแก้ source ชุดนี้ไม่ยืนยันสถานะ Open Source scan

## ข้อสังเกตเรื่อง fixtures

ชื่อผู้ใช้ที่เป็นข้อมูลประกอบถูกสร้างภายใน test และใช้ค่าเดียวกันทั้ง input/expected คง domain, รูปแบบเลข, ชื่อร่วม/ต่าง, uppercase/whitespace และ fixed-shape trap InlineData ไว้ ไม่ใช้ข้อมูลหรือ secret ของเครื่อง

ค่า default ของ private helpers ใน Deduplicator และ OpenXml exporter สร้างครั้งเดียวต่อ class โดย null หมายถึงใช้ default; ไม่มี explicit-null caller เดิม Redaction marker มี prefix จึงไม่ว่างและยังเป็น invalid integer ในกรณีทดสอบ binding
