# HVP material insert final review 2026-09-29

ผู้ใช้ยืนยันเพิ่ม Material ใหม่ตาม mapping และคง Material-only flag update สำหรับข้อมูลเดิม ขอบเขตเฉพาะ service standalone net48 ไม่แก้ MatMaster/เว็บไซต์ ไม่เพิ่ม service DB และไม่เกี่ยวกับ ReportAndRepost

Production delta จาก HVP-20260914-standalone-final มี 7 ไฟล์: HvpService.cs, HvpFileProcessor.cs, App.config, README.md, DEPLOY.md, REFERENCES.md และ Verify-Hvp.sql ส่วน host/timer/State/log source, csproj และ packages.lock.json ไม่เปลี่ยน

UPDATE ก่อนด้วย Material อย่างเดียว ทุกแถวที่พบเปลี่ยน flag/modify date หาก affected rows เป็น 0 จึง INSERT mapped fields ของ ROH/HALB/FERT ใส่ CREATE_DATE จาก DB, fields ที่ไม่ได้ map เป็น NULL, SLOC ตามประเภทและ default STR9 ต้องมี Material/Plant ตอน insert ใช้หนึ่ง transaction ต่อไฟล์

Independent review พบ P2: หาก Issue St Loc ตัวแรกหายจะเลือก BOM column ชื่อซ้ำ และพบ validation log ไม่ระบุฟิลด์ แก้โดยตรวจ boundary Min. Safety Stock/BOM Usage และใช้ InvalidDataException สำหรับข้อความ validation ที่ไม่มีค่าข้อมูลหรือ credentials เพิ่ม tests ให้ fail ก่อนแก้ 3 cases ผู้ตรวจยืนยันปิด findings แล้ว

Verification:

- File/State/DB-boundary regression 101 PASS, Failures 0: create_image_temp/hvp-20260929-regression-final.log
- Net48 Release rebuild warnings-as-errors: 0 warnings/errors, hvp-20260929-net48-build-final.log
- Host 14 PASS: hvp-20260929-host.log
- Actual DLL offline smoke 6 PASS: hvp-20260929-runtime-final.log ไม่มี Oracle Open
- Locked restore/rebuild จากสำเนา source สำหรับส่งมอบ: 0 warnings/errors, hvp-20260929-package-build.log ใช้ lock เดิมและ NuGetAudit=false เฉพาะคำสั่งนี้ ไม่มี package update/audit ใหม่

Tests ครั้งแรกถูก sandbox/Application Control บล็อก คำสั่งเดิมรันผ่าน execution approval โดยไม่แก้ security policy ไม่ถือผลบล็อกเป็น product failure หรือผล test pass

No real Oracle/SAP/company Task Scheduler execution. ต้อง QA สิทธิ์ INSERT/UPDATE, schema constraints/triggers, TXT encoding/header จริง, byte length, rollback และผลเมื่อ SAP feed 4 ชั่วโมงทำงานร่วมกัน ต้องรักษา State เดิม ไม่ลบเพื่อบังคับ reprocess ไฟล์ done

Known concurrency scope: ORA-00001 recovery รองรับ same composite PK race; Material เดียวกันต่าง PLNT/SLOC จาก job อื่นยังเกิดได้ ไม่มี global serialization หรือ schema change. File DB commit กับ checkpoint ไม่ atomic ร่วมกัน จึงอาจ replay เมื่อ process หยุดหลัง commit

ส่ง source project/tests/docs/evidence แยกจาก binary ไม่มี credentials จริง ไม่สร้าง Git commit ใน workspace นี้เพราะไม่มี Git repository; มีข้อความ commit ให้ใช้ที่ repo บริษัทในชุดส่งมอบ
