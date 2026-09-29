# HVP material insert: ชุดส่งมอบ 2026-09-29

ชุดนี้รวม standalone .NET Framework 4.8 เดิมและ requirement INSERT Material ใหม่แล้ว ใช้ project ทั้งโฟลเดอร์ Z02JHVPService ได้ ไม่ต้องประกอบกับ ZIP รุ่นเก่า ไม่มี Pulllist references และไม่เพิ่ม service DB/table

Material ที่มีอยู่จะอัปเดต HVP flag ทุกแถวของ Material พร้อม MODIFY_DATE โดยไม่เปลี่ยน field อื่น ส่วน Material ที่ไม่พบเลยจะ INSERT ตาม mapping ของ ROH/HALB/FERT ถ้า SLOC ว่างใช้ STR9; Material/Plant ที่จำเป็นขาดจะ fail และ rollback ทั้งไฟล์ กติกาครบอยู่ใน [README.md](Z02JHVPService/README.md)

1. เปิด Z02JHVPService/Z02JHVPService.csproj ใน repo service แล้ว restore ตาม packages.lock.json และ build Release ด้วย .NET Framework 4.8 targeting pack
2. ใช้ output EXE, EXE.config และ dependency DLL 12 ไฟล์จาก build เดียวกัน ตั้ง Material connection string, HvpFilePath, HvpFileSuffix และ HvpStatePath ของ QA โดยเก็บ generated binding redirects ไว้
3. บัญชี DB ต้อง INSERT และ UPDATE MAT_MASTER ได้ ตรวจด้วยไฟล์ SAP จริงบน QA ตาม [DEPLOY.md](Z02JHVPService/DEPLOY.md) ก่อน Production

ZIP นี้เป็น source ไม่มี EXE/DLL พร้อม deploy และไม่มี credentials จริง ไม่ต้องแก้ MatMaster/UI/DAO ของเว็บไซต์ ถ้าแทน project เก่าที่ใช้ packages.config ให้ใช้ csproj ของชุดนี้และนำ packages.config เก่าของ HVP ออก ไม่เพิ่ม Pulllist DLL กลับมา

Task Scheduler ใช้ --background หนึ่ง process โดย timer ภายในทำงานทุก 5 นาที เปิด EXE ด้วยมือได้เมื่อหยุดและ Disable Task เดิมแล้ว ไม่ตั้ง Task ให้เปิด process ใหม่ทุก 5 นาที

อัปเกรดต้องเก็บ State เดิมของ environment นั้น ไฟล์ done ที่ไม่เปลี่ยนจะไม่ถูกทำซ้ำเพียงเพราะเพิ่ม INSERT ใช้ไฟล์ทดสอบใหม่ ห้ามลบ State เพื่อบังคับอ่านย้อนหลัง เพราะจะเริ่ม backlog ทั้งหมด QA/Production ต้องแยก State และ connection กัน

[CHANGES.md](CHANGES.md) ระบุไฟล์ที่เปลี่ยนจาก standalone-final วันที่ 2026-09-14, [TESTING.md](TESTING.md) มีคำสั่งทดสอบ, [VERIFY.md](VERIFY.md) บอกผลตรวจและข้อจำกัด, [COMMIT-MESSAGE.txt](COMMIT-MESSAGE.txt) ใช้ประกอบ commit ใน repo บริษัทได้

05-Tests และ Evidence เป็นของ development ห้ามใส่ใน production project หรือ deploy ไป server ส่วน Verify-Hvp.sql เป็น SELECT สำหรับตรวจด้วยมือ ไม่ใช่ script สร้างตาราง และไม่จำเป็นต่อการรัน service
