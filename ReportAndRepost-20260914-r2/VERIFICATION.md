# ผลตรวจ Report and Repost 2026-09-14 r2

รันทดสอบซ้ำหลังแก้ paging สำหรับชุด r2 ตรวจโค้ดที่ส่งมอบจริงใน workspace โดยไม่เรียก SAP/SMART/Oracle ของบริษัท

| การตรวจ | ผล |
|---|---|
| C# status classification | 123 checks ผ่าน |
| DAO workflow ด้วย DB boundary จำลอง | 29 checks ผ่าน |
| DAO query/parameter/page/cart | 28 checks ผ่าน |
| Controller action gates + HTTP contract ผ่าน loopback TCP จริง | 50 checks ผ่าน |
| URL-building block บน .NET Framework CLR 4.0.30319.42000 | 12 checks ผ่าน |
| JavaScript syntax, selection/batch, columns/renderers | ผ่านทั้งสามชุด |
| JavaScript regression: Fail 26 → 25 หลัง Success และหลัง browser ไม่ได้รับคำตอบ | 2 tests ผ่าน |

รวม C# harness 230 checks และ .NET Framework query 12 checks ไม่มี failure

## Regression ที่พิสูจน์แล้ว

รอบ r2 เพิ่มสอง tests ใน ui.test.js โดยรัน runBatch จริง ก่อนแก้ทั้งสองกรณีล้มด้วย offset 25 แทน 0 หลังเปลี่ยน reload เป็น reset paging ทั้งสองจุด ผ่านทั้งสองกรณี ตรวจว่าเห็น 25 แถวที่เหลือ, filter เดิมยังอยู่, refresh ครั้งเดียว, ไม่มี request ส่ง SMART เพิ่ม และรายการที่ไม่รู้ผลยังถูกกันเลือกซ้ำ ขอบเขต DataTables/paging/network ใช้ test doubles ไม่ได้ทดสอบ library หรือ browser จริง

ผล regression เดิมที่ยังคงอยู่ในชุดทดสอบ:

ก่อนแก้ตัวจำแนกสถานะ test เพิ่มสำหรับ Timeout และ HTTP error ของ service ทำให้ 22 assertions ล้ม หลังแก้ผ่าน สถานะ Success/NULL/Unknown และ SAP_DOC1 guard เดิมยังผ่าน

ก่อนแก้ encoding บน .NET Framework มี 2 failures: CTN และ user ภาษาไทย decode กลับเป็นข้อความ `%uXXXX` หลังใช้ UTF-8 UrlEncode ทีละค่า ผ่านทั้ง 12 checks รวมการเก็บ repeated endpoint parameters, bare parameter และการแทน ctn/pl_no/user เดิม

HTTP tests ใช้ Controller/SendToSmart จริงกับ HttpClient และ TcpListener บน loopback ตรวจ POST, query encoding, empty UTF8 text/plain, ไม่มี Authorization/X-Requested-With/SAP headers, business fail ผ่าน HTTP 200, body ผิดรูปแบบ/ว่าง, HTTP 204, HTTP 503 ที่ body ดูเป็น Success และ connection refused

Action tests ใช้ MVC/config/service boundary จำลอง แต่รัน Repost action จริง ตรวจว่า CONFIRM_CTN ที่ถูกต้องเข้าถึง service ได้โดยไม่มี CONFIRM_CTN_AUTHORIZATION, endpoint ผิด/ว่างถูกปฏิเสธ, session/permission และ rowId ไม่ผ่านจะไม่เข้าถึง service ไม่ใช่การทดสอบ anti-forgery pipeline ของ MVC

Workflow tests ใช้ DAO snippet จริงโดยแทน DB boundary ตรวจ row lock ขณะส่ง, user จาก DB, ไม่ส่งแถวที่ผิดเงื่อนไข, ไม่เขียน Success เมื่อ SMART fail/ผลไม่แน่นอน, write/commit ล้ม, rollback ล้ม และ canonical Success หลัง commit

## รันทดสอบ

จาก root workspace ใช้ PowerShell, .NET 10 SDK, Node และ Windows PowerShell/.NET Framework ที่มีอยู่:

```powershell
./05-Tests/ReportAndRepost/Run.ps1
./05-Tests/ReportAndRepost/framework-query.test.ps1
node --check ./02-Enhancement-Working/ReportAndRepost/PulllistMaterial/Scripts/Views/ReportAndRepost.js
node ./05-Tests/ReportAndRepost/ui.test.js
node ./05-Tests/ReportAndRepost/ui-render.test.js
```

Run.ps1 สร้าง DAO wrapper จาก snippet ใหม่ทุกครั้ง และ test project link Controller/Models/Status ตัวส่งมอบโดยตรง ไม่ใช่สำเนาแยก

framework-query.test.ps1 ดึงเฉพาะ block สร้าง URL ของ Controller พร้อมตรวจตำแหน่งต้น/ท้าย แล้ว compile/run บน .NET Framework ใช้เพื่อจับพฤติกรรม System.Web ที่ต่างจาก .NET ใหม่ ไม่ได้ build Controller/MVC ทั้งไฟล์บน net48

การรัน C# และ Windows PowerShell ใช้ execution ที่ผ่าน approval นอก sandbox หลังระบบปฏิเสธโหลด test assembly ใน sandbox ไม่ได้เปลี่ยน security settings ผลที่สรุปด้านบนมาจาก run ที่จบครบด้วย exit code 0

## ขอบเขตที่ยังต้องตรวจตอนนำไปวาง

Harness net10 มี warning SYSLIB0014 จาก ServicePointManager ซึ่งยังใช้กับเป้าหมาย net48 ของเว็บ ไม่ใช่ผล build solution บริษัท

ยังไม่ได้ build MVC5/Razor กับ DLL บริษัท, render Layout/DataTables จริง, execute Oracle SQL/lock/transaction, ทดสอบ TLS/timeout ครบ 30 วินาทีบนเว็บบริษัท หรือทดสอบ SMART รับคำขอซ้ำ ผลนี้ยืนยัน implementation ในขอบเขตทดสอบข้างต้น ให้ทำ TEST-UAT.md เมื่อนำไฟล์ไปวาง
