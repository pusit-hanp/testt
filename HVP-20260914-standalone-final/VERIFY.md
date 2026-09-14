# ผลตรวจชุดส่งมอบ final 2026-09-14

รวม standalone net48 ที่ถอด Pulllist references ทั้งหมด พร้อมแก้ P3 ของ smoke runner แล้ว ไฟล์ project/source/config/docs 13 ไฟล์ตรงกับ HVP-20260914-standalone-source เดิมทุก byte ไม่ได้เปลี่ยนพฤติกรรม service หรือเพิ่ม dependency หลังจากชุดนั้น

## P3 ปิดแล้ว

ก่อนแก้ Verify-Isolation รัน build ครบผ่าน 6 กรณี แล้ว build ที่จงใจขาด System.Text.Json.dll ยังผ่านจาก DLL ค้าง จึงได้ผล regression ล้มเหลวตามคาด

หลังแก้ Run.ps1 ให้ใช้ bin/<run-id> ใหม่ทุกครั้ง build ครบผ่าน 6 กรณี และ build ที่ขาด JSON DLL ล้มเหลวด้วย CS0234 จาก missing Json reference ได้ผล isolation regression 2 passed ผู้ตรวจอีกคนตรวจโค้ดและหลักฐานแบบ read-only แล้วไม่พบข้อแก้ไขเพิ่มเติมในขอบเขตนี้

หลักฐาน: Evidence/isolation-before-fix.log, isolation-after-fix.log, runtime-6-in-complete-build.log และ expected-missing-json-failure.log การ compile failure ในไฟล์สุดท้ายเป็นผลที่ตั้งใจทดสอบ ไม่ใช่ build ของ service ล้มเหลว

## ผลการตรวจแต่ละส่วน

| การตรวจ | ผล |
|---|---|
| Restore แบบ locked และ Rebuild net48 จากสำเนาชุดส่งมอบใน folder ใหม่ | ผ่าน 0 warnings / 0 errors โดยใช้ warnings as errors |
| File/State/DB boundary regression ใน workspace เดิม | ผ่าน 56 กรณี |
| Host regression จากสำเนาชุดส่งมอบ | ผ่าน 14 กรณี |
| Actual DLL smoke ใน complete fixture ของ isolation regression | ผ่าน 6 กรณี ไม่เปิด Oracle connection |
| Isolation regression หลังแก้ | ผ่าน 2 กรณี |
| ตรวจขอบเขต source/dependency | production 13 ไฟล์เหมือน standalone รุ่นก่อน ไม่มี Pulllist refs; tests แยกจาก project production |

การทดสอบซ้ำบางรอบจากสำเนาใหม่ไม่ผ่านข้อจำกัดของเครื่อง: Application Control บล็อก HvpRegression.dll แม้รันด้วย execution approval; runtime smoke ในสำเนาใหม่นั้นผ่าน 4 กรณีและมี FileLoadException 2 กรณี อีกครั้งที่ทดสอบ runtime ใน workspace เดิม Windows บล็อกการเริ่ม HvpRuntimeSmoke.exe เช่นกัน เก็บ log ทั้งสำเร็จและบล็อกไว้ใน Evidence จึงไม่อ้างว่าทุก suite ผ่านจากสำเนาใหม่ทั้งหมดหรือว่าการทดสอบรอบสุดท้ายทั้งหมดเป็นสีเขียว

ไม่มีการเปลี่ยน security policy เพื่อให้ tests ผ่าน ผล green ของ P3 เกิดก่อนการบล็อกรอบถัดมาและใช้ runner source รุ่นเดียวกับที่ส่ง ตรวจ hash ของ source ทั้งชุดร่วมกับผล build สำเร็จแล้ว ชุดส่งมอบเป็น source ให้ build บนเครื่องบริษัทตาม TESTING.md และนโยบายของบริษัท

## ยังต้องตรวจบน QA

ยังไม่ได้อ่าน SAP, เชื่อม Oracle/UPDATE หรือรัน Task Scheduler ด้วยบัญชีบริษัทจริง ต้องตรวจ connection target, สิทธิ์ UNC/UPDATE, ผลทุกแถวของ Material, การล้าง flag และการทำงานหลัง logout/reboot ก่อน Production

Running ใน Scheduler ไม่ยืนยัน DB success และ completedRecords ไม่ใช่ affected rows กติกา State ว่างทำ backlog ทั้งหมดและการหยุด/Disable Task ก่อนเปิด manual fallback ยังคงเดิมตาม DEPLOY.md ไม่มีการสร้าง service DB table

โฟลเดอร์ Evidence และ 05-Tests ใช้ตรวจสอบใน development เท่านั้น ไม่ deploy ไปกับ service ใช้ EXE/EXE.config/DLL ที่ build จาก Z02JHVPService/bin/Release เท่านั้น
