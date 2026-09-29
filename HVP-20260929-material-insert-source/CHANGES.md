# เปลี่ยนจาก HVP-20260914-standalone-final

รุ่นนี้รวม standalone เดิมทั้งหมดพร้อม INSERT Material ใหม่ ไม่แก้โค้ดของเว็บไซต์หรือ ReportAndRepost

ไฟล์ production ที่เปลี่ยน:

- HvpService.cs: เพิ่ม mapped fields; UPDATE ทุกแถวตาม Material ก่อน แล้ว INSERT เมื่อ affected rows เป็น 0 ใช้ transaction ต่อไฟล์ พร้อม validation และจัดการ ORA-00001 เฉพาะกรณี retry UPDATE แล้วพบ Material
- HvpFileProcessor.cs: อ่าน mapping ตามชื่อ header, เลือก SLOC ตาม MTYP, กันหยิบ Issue St Loc ของ BOM เมื่อคอลัมน์ที่ต้องการหาย และอ่าน suffix จาก config
- App.config: เพิ่ม HvpFileSuffix=Z02J-HVP.txt ถ้า config เก่าไม่มี key ยังใช้ suffix เดิม แต่ค่า blank/wildcard/path เป็น error
- README.md, DEPLOY.md: อธิบาย mapping, NULL/STR9, transaction, สิทธิ์ INSERT, การเก็บ State เดิม และ QA cases
- REFERENCES.md: ปรับคำอธิบายว่า DB layer ทำ UPDATE/INSERT โดยรายการ dependency ไม่เปลี่ยน
- Verify-Hvp.sql: SELECT field ที่ INSERT เพิ่ม เพื่อใช้ตรวจ QA ไม่มี DDL/DML

Program.cs, HvpWinService.cs, HvpStateStore.cs, HvpLog.cs, csproj และ packages.lock.json ไม่เปลี่ยนจาก baseline การทำงานทุก 5 นาที, retry, ordering/replay, State cache/compaction และ log retention คงเดิม

HvpFileSuffix รองรับชื่อมี prefix และตัวพิมพ์เล็กใหญ่บน Windows share ตามปกติ ไม่อ่าน subfolder ไม่ย้าย/archive/delete ไฟล์ SAP

tests เพิ่มเป็น 101 กรณีใน HvpRegression รวมการอ่านไฟล์, DB boundary, transaction, config suffix และการแก้จาก review ส่วน Host/Runtime smoke source ใช้ชุดเดิม แยก tests ออกจาก production

NuGet direct 4 ตัวเดิมคือ Dapper 1.50.5, Oracle.ManagedDataAccess 19.32.0, System.Text.Json 10.0.12 และ System.Formats.Asn1 10.0.12 รวม transitive เป็น 13 packages และ output DLL 12 ไฟล์ ดู REFERENCES.md ไม่มี dependency ใหม่จาก requirement INSERT

การย้อน binary ไม่ย้อนข้อมูลที่ INSERT/UPDATE ไปแล้ว ไม่ควรนำ State เก่ามาทับ State ปัจจุบันโดยไม่ประเมินการ replay
