# การเปลี่ยนแปลงสะสม

ฐานเปรียบเทียบคือ HVP-20260913-service-source ก่อนเปลี่ยนเป็น standalone ชุด final นี้รวม standalone เดิมครบ ไม่ใช่ patch เฉพาะตัวทดสอบ

## Production project

| ไฟล์/รายการ | การเปลี่ยนแปลง |
|---|---|
| HvpService.cs | เลิกสืบทอด BaseService และเลิกใช้ SessionFactory/Pulllist types อ่าน connectionStrings/Material แล้วใช้ OracleConnection + Dapper.Execute คง SQL และ method signature |
| Program.cs | เพิ่ม --background สำหรับ Task Scheduler โดยไม่อ่าน stdin คง manual EXE และ Windows Service entry เดิม พร้อม exit code/cleanup เมื่อเกิด fatal host error |
| App.config | ใช้ Material Oracle SID connection string แบบ placeholders เก็บ HVP settings เดิม และให้ build สร้าง binding redirects |
| Z02JHVPService.csproj | เพิ่ม project แยก net48 ไม่มี Pulllist ProjectReference/assembly reference ใช้ PackageReference |
| packages.lock.json | เพิ่ม version/hash ของ NuGet packages ที่ตรวจแล้ว |
| packages.config | ลบออกจาก project HVP เดิม ไม่ใช้ร่วมกับ project ใหม่ |
| HvpFileProcessor.cs, HvpLog.cs, HvpStateStore.cs, HvpWinService.cs | คงเดิมเทียบกับชุด 20260913 ทั้ง 4 ไฟล์ |
| เอกสารและ Verify-Hvp.sql | คู่มือ standalone, reference inventory, deploy และ SELECT สำหรับตรวจ QA โดยไม่สร้างตาราง |

กติกาที่ยังคงเดิม: สแกนทันทีเมื่อเริ่มแล้วทุก 5 นาที, เรียงไฟล์ตาม CreationTime, ทำไฟล์ใหม่/ที่ metadata เปลี่ยนพร้อม replay ตามลำดับ, ไม่ย้าย/archive/delete input, Material-only UPDATE ทุกแถวที่ match, HVP หรือ blank, retry สูงสุด 3 ครั้งรวมครั้งแรกและปรับ config ได้, State/cache/compaction/log retention เดิม

## Tests และ final-review fix

Tests ที่ส่งมาด้วยรวม DB/file/State regression 56 กรณี, host 14 กรณี, actual DLL smoke 6 กรณี และ isolation regression ใหม่ 2 กรณี ทั้งหมดเป็น optional สำหรับเครื่อง development

P3 ที่แก้ใน HvpRuntimeSmoke/Run.ps1 คือการใช้ bin เดิมทำให้ DLL ค้างช่วยให้ชุด build ที่ไม่ครบผ่านได้ เปลี่ยนเป็น bin/<run-id> ต่อ invocation และเพิ่ม Verify-Isolation.ps1 ให้รัน build ครบก่อน แล้วทดสอบ build ที่จงใจขาด System.Text.Json.dll โดยชุดหลังต้องล้มเหลวจาก missing reference

หลังแก้ P3 ไม่มี production source delta จาก HVP-20260914-standalone-source.zip และไม่เพิ่ม package/reference ใหม่
