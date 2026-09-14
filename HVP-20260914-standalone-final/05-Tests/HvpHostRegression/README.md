# HVP host regression

รันจาก workspace root บน Windows ที่มี .NET Framework 4.x:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\05-Tests\HvpHostRegression\Run.ps1
```

ใช้ `-SourceDir <folder>` เพื่อทดสอบ `Program.cs` จาก source package อื่น และ `-Compiler <csc.exe>` เพื่อเปลี่ยน compiler ได้ ไม่ต้อง restore NuGet package

ชุดทดสอบ compile `Program.cs` โดยไม่แก้หรือสร้างสำเนาโค้ดที่ดัดแปลง แล้วรันใน child process จริง ใช้ boundary stubs แทน `HvpWinService`, `HvpLog`, `ServiceBase`, ค่า `Environment.UserInteractive` และการตรวจว่า stdin ถูก redirect หรือไม่ ส่วน process, timer และ stdin/stdout/stderr เป็น .NET Framework/OS APIs จริง

ครอบคลุม 14 กรณี: `--background` ทำงานต่อหลัง Enter/EOF ในทั้งสอง environment, ไม่อ่าน stdin, manual Enter เรียก `StopService`, manual ที่ stdin ถูก redirect ไม่หยุดเพราะ EOF, no-argument noninteractive เข้า `ServiceBase.Run`, argument ผิดไม่เริ่ม service และคืน nonzero, startup ที่ล้มเหลวเรียก `StopService`, fatal startup/stop ไม่เผย exception message และมี diagnostic แม้ logger ล้มเหลว

ก่อนแก้ production code ได้ผล 3 passed / 11 failed ใน `red-result.txt` หลังแก้ได้ 14 passed / 0 failed ใน `green-result.txt` ไฟล์ EXE และ trace ที่เกิดจากการรันอยู่ใน `bin` และไม่ใช่ production deliverable

ชุดนี้ตรวจการเลือก branch ของ host และ lifecycle ที่ขอบเขต service ไม่ได้ติดตั้ง Windows Service, รัน Task Scheduler, ต่อ Oracle หรืออ่าน SAP จริง การส่ง Ctrl+C จาก Windows console ยังต้องทดสอบด้วยมือ การหยุด child process ที่ใช้พิสูจน์ว่า background ยังทำงานอยู่เป็นการ kill เพื่อเก็บกวาด test จึงไม่ได้ใช้ยืนยัน graceful shutdown การกด End ใน Task Scheduler หรือ kill process อาจไม่เรียก `StopService`

ตรวจ manual Ctrl+C เพิ่มเติมบน TEST/UAT โดยเปิด EXE ไม่มี argument รอให้เริ่มทำงาน แล้วกด Ctrl+C ต้องปิดหลังงานที่กำลังทำเสร็จตาม `StopService` เดิม และคืน exit code 0 เมื่อ shutdown สำเร็จ
