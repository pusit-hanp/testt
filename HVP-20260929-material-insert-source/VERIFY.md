# ผลตรวจ 2026-09-29

- Regression 101 PASS, Failures: 0 ใช้ source จริงสำหรับ parser/DB service/State/log โดยแทน Oracle และ Dapper boundary ด้วย stubs
- net48 Release Rebuild ผ่าน 0 warnings / 0 errors ใช้ warnings as errors และ dependency ชุดเดิม
- Locked restore/Rebuild จากสำเนา source ที่จัดส่งใน folder ใหม่ผ่าน 0 warnings / 0 errors ใช้ RestoreLockedMode=true, NuGetAudit=false เฉพาะคำสั่งตรวจสำเนานี้ ส่วน project ยังเปิด audit ตามเดิม ไม่มีการเปลี่ยน package versions
- Host regression 14 PASS ทดสอบ background/manual/service dispatch ด้วย subprocess และ host stub
- Actual DLL offline smoke 6 PASS ตรวจ net48 assembly loading, Oracle descriptor, Dapper parameter/null binding และยืนยันไม่มี Pulllist assembly references ไม่เปิด Oracle connection
- ผู้ตรวจโค้ดอีกคนตรวจ UPDATE/INSERT, mapping, transaction และผลแก้จาก review แล้ว ไม่พบประเด็นสำคัญคงค้างในขอบเขตที่ตรวจ

review พบว่าถ้า Issue St Loc ตัวแรกหายจะเผลอใช้คอลัมน์ BOM และ validation error ไม่ระบุ field ใน log เพิ่ม tests ให้ล้มเหลวก่อนแก้ 3 กรณี แล้วแก้จนผ่านทั้งหมด หลักฐานอยู่ใน Evidence/review-red.log และ regression-final.log ส่วน duplicate Material test ยืนยันว่าเพิ่มครั้งเดียว เก็บ fields จากแถวแรก และใช้ flag จากแถวหลังสุด

ไฟล์ *-red.log เก็บผลที่ตั้งใจให้ล้มเหลวก่อนเพิ่มหรือแก้พฤติกรรม ไม่ใช่ผลทดสอบของ release สุดท้าย

เริ่มแรก sandbox บล็อกการรัน test assembly ด้วย Windows Application Control จากนั้นคำสั่งเดิมรันผ่าน execution approval ได้ ไม่มีการแก้ security policy ของเครื่อง ผล green ข้างต้นเป็นผลจากการรันที่ได้รับอนุญาต

ยังไม่ได้เชื่อม SAP/Oracle หรือรัน Task Scheduler ของบริษัทจริง จึงยังไม่ยืนยันสิทธิ์ INSERT/UPDATE, trigger/default อื่นของ MAT_MASTER, encoding และ byte length, หรือการทำงานร่วมกับ feed 4 ชั่วโมง ต้องตรวจบน QA ตาม DEPLOY.md

transaction ครอบคำสั่งของ service ต่อไฟล์ ไม่ได้ล็อก job SAP อื่นทั้งระบบ ORA-00001 recovery รองรับการชน composite PK เดียวกัน หากอีก job เพิ่ม Material เดียวกันต่าง PLNT/SLOC พร้อมกัน schema ปัจจุบันยังอนุญาตหลายแถวได้

DB commit กับ checkpoint คนละระบบ หาก process หยุดระหว่างสองขั้นตอนอาจ replay ได้ เช่นเดิม completedRecords ใน log เป็นจำนวน input records ไม่ใช่จำนวนแถว DB ที่เปลี่ยน

ไม่มีการอัปเดต package versions หรือทำ audit vulnerability รอบใหม่ในงาน INSERT นี้ รายการ dependency อยู่ใน REFERENCES.md

SHA256SUMS.txt ใช้ตรวจทุกไฟล์ในชุด ยกเว้น manifest เอง หลักฐาน build/test เป็นไฟล์พัฒนา ห้าม deploy Evidence หรือ test stubs ไปกับ service
