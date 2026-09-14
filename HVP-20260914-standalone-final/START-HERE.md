# ชุดส่งมอบ HVP standalone final 2026-09-14

ชุดนี้รวมการเปลี่ยนแปลงทั้งหมดตั้งแต่รุ่น .NET Framework 4.8 ที่ถอด Pulllist references จนถึงการแก้ P3 จาก final review ใช้ชุดนี้แทน HVP-20260914-standalone-source.zip ได้เลย ไม่ต้องนำ ZIP เก่ามาประกอบเพิ่ม

## นำไปใช้

1. เปิด Z02JHVPService/Z02JHVPService.csproj เป็น project แยก หรือ Add Existing Project เข้า solution ของ repo service ไม่ต้องเพิ่ม Pulllist project/DLL
2. Restore ตาม packages.lock.json แล้ว build Release ด้วย .NET Framework 4.8 targeting pack ตาม [DEPLOY.md](Z02JHVPService/DEPLOY.md) เครื่อง build ต้องเข้าถึง NuGet feed/cache ที่มี packages ชุดนี้ได้
3. นำ EXE, EXE.config ที่ build สร้าง และ dependency DLL ทั้ง 12 ตัวไปด้วยกัน ใส่ Material connection string, HvpFilePath และ HvpStatePath ของ QA หรือ Production ใน output config โดยเก็บ generated binding redirects ไว้

ชุดนี้เป็น source ต้อง build ก่อน deploy ไม่มี EXE/DLL พร้อมใช้ ค่า connection string ยังเป็น xxx placeholders ใส่ credentials จริงที่เครื่องบริษัท ไม่ commit เข้า repo

ถ้าแทน project HVP เดิม ให้สำรองก่อน ใช้ .csproj ใหม่แทนและลบ packages.config เดิมของ HVP อย่าให้ ProjectReference/Reference/HintPath ของ Pulllist ค้างอยู่ ไม่ต้องลบ shared project ของระบบ Pulllist รุ่นนี้ไม่ต้องใช้ patch.xml, DecryptCTN หรือ Encs_T

Task Scheduler ใช้ --background ครั้งเดียว ให้ timer ภายในสแกนทุก 5 นาที เปิด EXE ไม่มี argument ยังเป็นทางสำรองได้ โดยต้องหยุดและ Disable Task ก่อน ใช้ State เดิมของ environment นั้น QA และ Production ต้องแยก State กัน ขั้นตอนบัญชี/UNC และ fallback อยู่ใน DEPLOY.md

## สิ่งที่อยู่ในชุด

- Z02JHVPService/: project/source/config/lock และคู่มือพร้อมรายการ references ครบ ใช้สร้าง production output จากโฟลเดอร์นี้เท่านั้น
- 05-Tests/: optional test source รวมตัวทดสอบที่แก้ P3 แล้ว มี Oracle/Dapper/host stubs สำหรับทดสอบ ห้ามเพิ่ม tests เข้า production project หรือ deploy ไป server
- [CHANGES.md](CHANGES.md): รายการเปลี่ยนสะสมเทียบกับก่อน standalone
- [TESTING.md](TESTING.md): คำสั่งสำหรับรัน tests จาก layout ของ ZIP นี้
- [VERIFY.md](VERIFY.md): ผลตรวจล่าสุดและสิ่งที่ยังต้องยืนยันบน QA
- [COMMIT-MESSAGE.txt](COMMIT-MESSAGE.txt): commit message รวมทั้ง standalone และ P3 fix

การแก้ P3 ครั้งนี้เปลี่ยนเฉพาะ test runner ให้แต่ละรอบใช้ bin/<run-id> ใหม่ พร้อม regression ยืนยันว่าชุดที่ขาด DLL ต้องไม่ผ่าน ไฟล์ project/source/config ทั้ง 13 ไฟล์เหมือน standalone ZIP ก่อนหน้า รวมถึงกติกา retry, State และการอ่าน SAP ไม่มีการเปลี่ยนพฤติกรรม service เพิ่ม

ไม่ต้องสร้าง service DB table ส่วน Verify-Hvp.sql เป็น SELECT สำหรับตรวจด้วยมือ ใช้หรือไม่ใช้ก็ได้ SHA256SUMS.txt ใช้ตรวจทุกไฟล์ในชุดยกเว้นตัว manifest เอง
