# Z02JHVPService standalone

ตามขอบเขตล่าสุดของผู้ใช้ เปลี่ยนเฉพาะ project `Z02JHVPService` ไม่ต้องนำ MatMaster Controller/View/Service/DAO ไปแทนไฟล์เดิม

รุ่นนี้เป็น project แยกบน .NET Framework 4.8 ต่อ Oracle โดยตรง ไม่อ้าง Pulllist.Service, Pulllist.DAL หรือ project ของเว็บไซต์ และไม่เพิ่ม DB table ยังไม่ได้ทดสอบกับ SAP/Oracle หรือ Task Scheduler ของบริษัทจริง

ใช้ `Z02JHVPService.csproj` กับ `packages.lock.json` ที่ให้มาแทน project เดิม มี Compile item ครบ 6 ไฟล์แล้ว ใช้ PackageReference และเอา packages.config เดิมออก รายการ package/DLL อยู่ใน [REFERENCES.md](REFERENCES.md) ขั้นตอน build, Task Scheduler และเปิด EXE ด้วยมืออยู่ใน [DEPLOY.md](DEPLOY.md)

Task Scheduler ต้องส่ง `--background` เพื่อให้ process ทำงานต่อเนื่องโดยไม่อ่าน stdin โปรแกรมมี timer 5 นาทีอยู่แล้ว ไม่ตั้ง Scheduler ให้เปิด process ใหม่ทุก 5 นาที เปิด EXE โดยไม่มี argument บน desktop เพื่อรันด้วยมือได้ ใช้ Enter หรือ Ctrl+C เพื่อหยุดหลัง scan ที่กำลังทำจบ

ก่อนสลับจาก Scheduler ต้องหยุด instance เดิมและ Disable Task ใช้ input, State และ DB environment เดียวกัน QA กับ Production ต้องแยก State กัน การเปิดมือไม่ได้เปลี่ยน Windows account ให้เหมือนบัญชีของ Task

App.config เป็น source template ที่มีค่า xxx และ input path ว่าง แก้ค่าจริงใน `Z02JHVPService.exe.config` ที่ได้จาก build โดยรักษา generated binding redirects ไว้ ห้าม copy App.config ทับ EXE.config

1. ตั้ง `HvpFilePath` เป็น UNC เช่น `\\server\share\SAP` โดยเปลี่ยนเป็นค่าจริง ไม่ใช้ mapped R: ของผู้ใช้ หากเป็น local disk ที่ service อ่านได้ใช้ absolute local path ได้
2. คง `TimerIntervalMinutes=5`; ไม่มี FileLookbackMinutes แล้ว `HvpStatePath` ว่างจะใช้ `%ProgramData%\Z02JHVPService` ให้ service account มี Modify ที่นั่น ส่วน SAP folder ใช้ Read เท่านั้น
3. ตั้ง XML `connectionStrings` ชื่อ `Material` เป็น Oracle connection ของ environment นั้น ใช้ SID descriptor ตาม template ใส่ secret ที่ปลายทาง ไม่เก็บค่าจริงใน repo รุ่นนี้ไม่อ่าน connection/config จากเว็บไซต์โดยอัตโนมัติ
4. Build ด้วย csproj/lock file ของชุดนี้ แล้วนำ EXE, EXE.config และ dependency DLL ทั้ง 12 ไฟล์จาก Release output ไปด้วยกัน ไม่ใช้ DLL ของ Pulllist เดิม
5. รันใน TEST ก่อน ตรวจ log `hvp-yyyyMMdd.log` จะเห็น SERVICE START, SCAN, START, DB COMMANDS, DONE หรือ FAILED การนับ completedRecords เป็นจำนวนรายการที่ส่งคำสั่ง ไม่ใช่ affected rows

เมื่อ start จะสแกนทันทีแล้วทุก 5 นาที ครั้งแรกทำ backlog ทั้งหมด; รอบต่อไปทำไฟล์ใหม่/ไฟล์ที่เวลา-ขนาดเปลี่ยนตาม CreationTimeUtc ถ้าเก่าเปลี่ยนจะ replay ไฟล์ที่ใหม่กว่าตามลำดับ ไม่ย้าย/archive/delete ไฟล์ SAP อ่านเฉพาะชื่อที่ลงท้าย Z02J-HVP.txt ใน folder ที่ตั้งไว้ ไม่อ่าน subfolder

ไฟล์ต้องคั่นด้วย tab และมี header Material กับ Special Control Flag อ่านด้วย UTF-8 พร้อมตรวจ BOM ตรวจทุกแถวก่อนส่ง DB; header หาย แถวสั้น หรือ Material ว่างทำให้ไฟล์ fail ถ้าไฟล์ถูกเปิดเพื่อเขียนอยู่จะอ่านไม่ได้และนับเป็น failed attempt

ค่า flag ที่ trim แล้วเท่ากับ HVP โดยไม่สนตัวพิมพ์เล็กใหญ่จะเป็น HVP ค่าอื่นในคอลัมน์ที่มีอยู่จะเป็นค่าว่าง ใน Oracle character column ค่าว่างจะแสดงเป็น NULL ใช้ `WHERE TRIM(MATERIAL) = TRIM(:material)` เพื่ออัปเดตทุกแถวของ Material นั้น รวมหลาย PLNT/SLOC และตั้ง MODIFY_DATE=SYSDATE ไม่มี INSERT หรือการล้าง Material ที่ไม่อยู่ในไฟล์ ถ้า Material ซ้ำในไฟล์เดียวกัน แถวหลังสุดมีผลท้ายสุด

ถ้า metadata เปลี่ยนหลังจัดคิวแต่ก่อนอ่านไฟล์ จะบันทึก `DEFER` และเลื่อนไฟล์นั้นกับไฟล์ที่ตามหลังไปรอบถัดไปเพื่อเรียงใหม่ ไม่เพิ่มจำนวน retry และยังเก็บสถานะ pending ไว้หลัง restart

ตั้ง `<add key="MaxFileAttempts" value="3" />` ใน `Z02JHVPService.exe.config` แล้ว restart service ค่า 3 รวมครั้งแรกกับ retry อีก 2 ครั้ง ระหว่างยังไม่ครบจะรอรอบถัดไปก่อนทำไฟล์หลังจากนั้น หาก fail ครั้งที่ 3 จะบันทึก SKIP แล้วทำไฟล์ถัดไปในรอบเดียวกัน จำนวนครั้งและสถานะเก็บใน checkpoint จึงอยู่ต่อหลัง restart ค่า config ที่ขาด/ไม่ใช่จำนวนเต็มบวกใช้ 3

ไฟล์ที่ SKIP แล้วจะไม่ลองซ้ำตราบใดที่ metadata เดิม แม้เพิ่ม MaxFileAttempts; เมื่อ CreationTimeUtc, LastWriteTimeUtc หรือ Length เปลี่ยนจะเริ่มนับใหม่ ข้อผิดพลาดระหว่างทำไฟล์รวมถึง DB error นับด้วย แต่ folder/lock/checkpoint error ที่เกิดนอกการทำไฟล์จะหยุดรอบนั้นโดยไม่กินจำนวนครั้งของไฟล์

อ่าน checkpoint เดิม 4 คอลัมน์ได้ และบันทึกเป็น 6 คอลัมน์เพิ่ม attempts/status ควรสำรอง checkpoint ก่อนอัปเกรดหากต้อง rollback เพราะโปรแกรมรุ่นเก่าอ่านรูปแบบใหม่ไม่ได้

หลัง DB เปลี่ยน กด Search บน Material Master เพื่อโหลด HVP Flag ใหม่ ไม่มี UI polling เพิ่ม

## ใช้งานระยะยาว รุ่น longrun

ใช้ `processed-files.tsv` เดิม โหลดแบบ streaming ครั้งแรกและ cache ใน processor เมื่อ path/ขนาด/CreationTime/LastWriteTime ของ checkpoint เปลี่ยนจะ reload รอบที่ไม่มีงานจึงไม่อ่านเนื้อหา State ซ้ำหรือเรียงไฟล์ทั้งหมด แต่ยังตรวจ metadata ของไฟล์ SAP ทุกไฟล์เพื่อหาไฟล์เก่าที่ถูกแก้ ตรวจ version จาก CreationTimeUtc, LastWriteTimeUtc และ Length ไม่ได้ hash เนื้อหาทุกไฟล์

บันทึกเฉพาะสถานะที่เปลี่ยนต่อท้าย TSV โดยบรรทัดล่าสุดของแต่ละ path มีผล เก็บ pending tail ให้ลง disk ก่อนเริ่ม DB และเก็บผลหลังแต่ละไฟล์เหมือนเดิม รวมกลับเหลือหนึ่งบรรทัดต่อ path เมื่อจำนวนบรรทัดถึง `max(1000, 2 × จำนวน path)` หรือมีการบันทึกหลังข้ามวัน UTC ใน process เดิม การรวมใช้ไฟล์ .tmp แล้ว replace แบบเดิม ไม่ลบประวัติไฟล์ SAP เก่า

การ restart จะเริ่มนับวันสำหรับ compaction ใหม่ แต่เงื่อนไขจำนวนบรรทัดยังคุมประวัติซ้ำไว้ ถ้าไม่มีการเปลี่ยนสถานะจะไม่เขียน State เพียงเพราะเปลี่ยนวัน

checkpoint รุ่นเดิม 4/6 คอลัมน์ที่สร้างโดย service อ่านต่อได้ หากไฟล์เขียนค้างและบรรทัดสุดท้ายไม่มี newline จะตัดเฉพาะส่วนท้ายที่ไม่จบ แล้ว log `STATE RECOVERED` ก่อน retry ตามข้อมูลที่บันทึกครบ ห้ามแก้ checkpoint ด้วยมือขณะ service รัน หาก checkpoint ที่แก้เองไม่มี newline ท้ายไฟล์ รายการสุดท้ายนั้นอาจถูก replay; บรรทัดที่จบแล้วแต่ format เสียจะหยุด scan ไม่ข้ามเงียบ ๆ

เพิ่ม `<add key="LogRetentionDays" value="30" />` ใน appSettings โดยลบเฉพาะชื่อ `hvp-yyyyMMdd.log` ที่วันที่เก่ากว่า local today - 30 วัน ตรวจวันละครั้งต่อ State ต่อ process ค่า 0 ปิดการลบ ค่าขาด/ผิด/ติดลบใช้ 30 ไม่แตะ checkpoint, subfolder, reparse path หรือโฟลเดอร์ที่ตั้งเป็น SAP source โดยตรง หากลบ log ไม่ได้จะเตือนแบบไม่แสดง secret และยังทำงานต่อ

`SCAN END` เพิ่ม `elapsedMs`, `files`, `stateCount`, `stateReloaded`, `stateLoadMs`, `enumerateMs`, `sortedFiles`, `stateBytesWritten`, `stateCompactions` เวลาครอบคลุมการโหลด State/ตรวจ source/ประมวลผล แต่ไม่รวมเวลาติดตั้งหรือเริ่ม process `stateBytesWritten` คือ byte ที่เขียนใน checkpoint/temp snapshot สำเร็จ ไม่ใช่ disk hardware I/O

สำรอง State ก่อน upgrade และเก็บ State เดิมของ environment นั้น ห้ามคัดลอก State จาก QA ไป Production หรือใช้การลบ State ตามอายุเพื่อลดจำนวนรายการ เพราะจะทำไฟล์ SAP ที่ยังอยู่ซ้ำ หลัง restart หรือเปลี่ยน State จะมีค่าใช้จ่ายโหลดเต็มหนึ่งครั้ง การแก้ไฟล์เก่ามากยังอาจทำให้ replay ไฟล์หลังจากนั้นจำนวนมากตามกติกาเดิม

การเปลี่ยนรูปแบบ source path แม้ชี้ไฟล์เดิมก็อาจทำให้ replay เพราะ key ใช้ full path `job.lock` ล็อกเฉพาะระหว่าง scan ของ process ที่ใช้ State directory เดียวกัน ไม่ใช่ lock ตลอดอายุ process และไม่ป้องกันอีกเครื่องที่ใช้ State คนละแห่ง

DB อาจเปลี่ยนไปบางรายการก่อนรายการถัดไป fail และถ้า process หยุดหลัง DB เปลี่ยนแต่ก่อนเก็บ checkpoint อาจ replay UPDATE เมื่อเริ่มใหม่ ไม่มี transaction ร่วมระหว่าง Oracle กับ State จึงไม่รับประกันการทำงานครั้งเดียวต่อไฟล์

## ตรวจใน TEST

- ใช้ material ทดสอบที่มีหลาย PLNT/SLOC ตรวจด้วย `Verify-Hvp.sql` ก่อนเริ่ม และยืนยันว่าเป็น DB เดียวกับ service
- ส่ง TXT tab-separated ที่มี header Material และ Special Control Flag; ให้ material เดียวมีหลายแถวใน DB แล้วตรวจว่าเปลี่ยนครบทั้งหมดเป็น HVP
- ส่งไฟล์สร้างใหม่ที่ material เดิมมี flag ว่าง/NA ตรวจว่าทุกแถวเป็น NULL/ช่องว่าง กด Search แล้วดู UI
- วางหลายไฟล์ที่ CreationTime ต่างกัน ค่าสุดท้ายต้องเป็นของไฟล์ที่สร้างใหม่กว่า ถ้าเวลาเท่ากันดูชื่อไฟล์ใน log
- ใช้ไฟล์ CreationTime เก่ากว่า 5 นาทีแต่ LastWriteTime ใหม่ ตรวจว่ารอบถัดไปอ่านได้; ถ้าแก้ไฟล์เก่าหลังไฟล์ใหม่เคยสำเร็จ ให้ตรวจ replay ของไฟล์ที่ตามหลังด้วย
- Restart แล้วไฟล์สำเร็จเดิมไม่ถูกทำซ้ำ ตรวจ checkpoint ยังอยู่ และไฟล์ SAP byte-for-byte เท่าเดิม
- ทดลองไฟล์ header ผิด/แถวสั้น/ถูกเปิดเขียนอยู่ และ DB unavailable ต้องเห็น FAILED, retry ตามรอบ แล้ว SKIP เมื่อครบ MaxFileAttempts ก่อนทำไฟล์ถัดไป ตรวจว่า restart ไม่ล้างจำนวนครั้ง
- ทดสอบสิทธิ์ด้วย service account จริง ไม่ใช่แค่บัญชีที่ login แล้วเห็น R: และทดสอบ stop/start ขณะมี backlog

`Verify-Hvp.sql` เป็น read-only ยังไม่ได้รันกับ DB ใด

DB/file regression ผ่าน 56 กรณีด้วยไฟล์ชั่วคราวและ Oracle/Dapper จำลองบน .NET 10 รวม direct connection, replay/retry, cache invalidation, compaction, partial append และ log retention ส่วน host regression ผ่าน 14 กรณี โดย compile Program.cs จริงแล้วใช้ boundary stubs ตรวจ background/manual, stdin, exit code และ cleanup บน .NET Framework

ตรวจวันที่ 2026-09-14: .NET Framework 4.8 locked restore/Rebuild ผ่านโดยไม่มี warning/error และ runtime smoke ที่ใช้ DLL จริงผ่าน 6 กรณี

ผลนี้ไม่ใช่การทดสอบ SAP, Oracle หรือ Task Scheduler ของบริษัทจริง ยังต้องตรวจบน QA ตาม DEPLOY.md ก่อนนำขึ้น Production
