# ผลตรวจ Report and Repost 2026-09-26

ทดสอบ source ชุดส่งมอบใน workspace โดยไม่เรียก SAP, SMART หรือ Oracle ของบริษัท

| การตรวจ | ผล |
|---|---|
| C# status classification | 123 checks ผ่าน |
| DAO workflow ด้วย DB boundary จำลอง | 29 checks ผ่าน |
| DAO query/parameter/page/cart | 28 checks ผ่าน |
| Controller action gates + HTTP contract ผ่าน loopback TCP จริง | 108 checks ผ่าน |
| URL-building block บน .NET Framework CLR 4.0.30319.42000 | 12 checks ผ่าน |
| JavaScript syntax | ผ่าน |
| Node UI selection/batch/render regression | 9 tests ผ่าน รวม assert checks เดิม |
| Chrome headless: confirmation DOM/CSS ที่ 1280×900 และ 390×844 | ผ่านตามขอบเขตด้านล่าง |

รวม C# harness 288 checks และ .NET Framework query 12 checks เป็น 300 checks ไม่มี failure ส่วน Node 9 tests ไม่มี failure

## สิ่งที่ยืนยันในรอบนี้

เพิ่ม regression สำหรับ Fail ล่าสุดก่อนแก้ production code พบ 19 assertions ล้ม หลังแก้รันชุดเต็มผ่าน ยืนยันเส้นทาง Controller `SendToSmart` → DAO `RepostReportCtn` โดยใช้ HttpClient/TcpListener จริงที่ loopback และจำลองเฉพาะ DB boundary:

- HTTP 200 + `result=Fail` / `CTN not found in smart system.` บันทึก response JSON ดิบลง `STATUS_TRANFER` หลัง update ได้หนึ่งแถวและ commit ผล Repost ยังเป็น `Success=false`, `Outcome=FAILED`
- Fail ถัดไปแทนข้อความเก่า เก็บ Unicode และ whitespace ของ response ไม่เติม `HTTP 200:` หน้า JSON ที่บันทึก
- HTTP 503 แม้ body ดูเป็น Success ยังบันทึก `Error 503: <reason>` และรายงาน Fail ตามรูปแบบของ service
- malformed HTTP 200 และ empty HTTP 204 ยังเป็นผลไม่แน่นอน ไม่เขียนสถานะหรือส่งซ้ำอัตโนมัติ
- Fail ตอบกลับแล้วแต่ DB write/commit ล้ม รายงาน `SMART_FAIL_DB_FAILED` พร้อมคำตอบ SMART และข้อผิดพลาด DB ไม่กล่าวว่า save สำเร็จ
- ส่งหนึ่งครั้งต่อรายการ ใช้ row lock และเงื่อนไข ROWID/CTN/Slip/สถานะเดิมก่อน update ยังคง guard เดิม

UI tests รัน JavaScript จริงโดยจำลองขอบเขต jQuery/DataTables/SweetAlert/AJAX ตรวจ header checkbox ทั้ง unchecked/mixed/checked, เฉพาะ eligible rows หน้าปัจจุบัน, หน้าไม่มีแถวเลือกได้, ล้างและล็อกแถวเดิมระหว่าง search, ล็อกช่วง confirmation/batch, Cancel คืน selection และ focus, Confirm ส่งเฉพาะชุดที่แสดง และ Fail ที่ DB save ไม่ยืนยันยังเลือกได้หลัง refresh

Regression เดิมยังผ่าน: filter Fail เหลือ 26 → 25 แถวหลัง Success หรือ browser ไม่ได้รับคำตอบ ต้อง reload หน้าแรกโดยคง filter และกันเลือกแถวที่ไม่รู้ผลซ้ำ

ตรวจ browser ด้วย DOM ของ confirmation ที่สร้างจาก JavaScript จริงและ CSS จาก `Index.cshtml` บน local modal shell ตรวจ 25 รายการ, escape identifier ที่มี HTML, ตารางเลื่อนภายใน popup และความสูงแต่ละแถว 33 px ทั้ง desktop/narrow ไม่ตัด Slip/CTN เป็นหลายบรรทัด ภาพตรวจอยู่ใน `create_image_temp/ReportAndRepost-20260926/confirmation-desktop.png` และ `confirmation-narrow.png` ของ workspace

## คำสั่งรัน

จาก root workspace:

```powershell
./05-Tests/ReportAndRepost/Run.ps1
& "$env:WINDIR/System32/WindowsPowerShell/v1.0/powershell.exe" -NoProfile -ExecutionPolicy Bypass -File ./05-Tests/ReportAndRepost/framework-query.test.ps1
node --check ./02-Enhancement-Working/ReportAndRepost/PulllistMaterial/Scripts/Views/ReportAndRepost.js
node --test ./05-Tests/ReportAndRepost/ui.test.js ./05-Tests/ReportAndRepost/ui-render.test.js
node ./create_image_temp/ReportAndRepost-20260926/ui-layout-check.js
```

`Run.ps1` สร้าง DAO wrapper จาก snippet ใหม่ทุกครั้งและ link Controller/Models/Status โดยตรง ใช้ .NET 10 SDK ส่วน Framework query test ดึงเฉพาะ block สร้าง URL ของ Controller มา compile/run บน CLR เดิม `ExecutionPolicy Bypass` ใช้เฉพาะ child process ไม่เปลี่ยน policy เครื่อง

Browser script ใช้ bundled Playwright และ Chrome ที่ติดตั้งในเครื่องนี้ ภาพและ script เป็นหลักฐาน local ไม่ใช่ไฟล์ที่ต้องวางใน web project

## ขอบเขตที่ยังต้องตรวจบนเครื่อง company

Harness net10 มี warning SYSLIB0014 จาก `ServicePointManager` ซึ่งยังใช้กับเป้าหมาย legacy net48 ไม่ใช่ผล build solution บริษัท

ยังไม่ได้ build MVC5/Razor ทั้ง solution กับ DLL บริษัท, ใช้ Layout/DataTables/SweetAlert ของบริษัทจริง หรือทดสอบ Oracle SQL/lock/transaction และขนาด `STATUS_TRANFER` กับ response จริง Browser check ใช้ local modal shell จึงไม่ใช่การรับรองหน้าจอเต็มของบริษัท

ยังไม่ได้ทดสอบ TLS/timeout ครบ 30 วินาทีบนเว็บบริษัท หรือพฤติกรรม SMART เมื่อรับคำขอซ้ำ ให้ทำ `TEST-UAT.md` ใน TEST/UAT ก่อนนำขึ้น PROD
