# Admin Pages final review

วันที่ 2026-11-08 ตรวจโดย reviewer แยกจากผู้เขียน feature อ้างอิง `docs/superpowers/plans/2026-11-08-pulllist-admin-pages.md`, กติกา service r3 ใน `pulllistservice_2017/Project-Control/COSTROLLUP-CLARIFICATION-20261108.md` และ schema reference ของ TP/WIP ทั้งสี่ตาราง

ขอบเขตคือ `02-Enhancement-Working/AdminPages/source`, `database`, `integration` และ `README.md` ไม่ได้แก้ feature files ระหว่าง review และไม่ได้รันคำสั่งกับ Oracle/SAP บริษัท

## Findings

ไม่พบ finding ที่ยังค้างหลังตรวจ code ล่าสุดและตรวจซ้ำเฉพาะการแก้ DEFAULT, actor documentation, UTF-8 BOM, board TTL, summary matrix, Y confirmation และ storage warning ไม่ได้แก้ production source ระหว่าง review

### P3: DEFAULT validator ลบ whitespace ภายใน string literal (แก้แล้ว)

เดิม `database/04-validate.sql:69-73` ลบ whitespace ทุกตำแหน่งจาก `DATA_DEFAULT` ทำให้ literal `' N'` หรือ `'N '` ถูกยอมรับเหมือน `'N'` ปัจจุบันบรรทัด 72-74 ใช้ pattern `^[[:space:]]+|[[:space:]]+$` จึงลบเฉพาะ whitespace รอบ literal และคงเนื้อหาใน quote ไว้ ตรวจ `schema-default.test.cjs` ผ่านแล้ว ทั้ง valid literal ที่มี whitespace รอบนอกและ invalid literal ที่มี whitespace ภายใน

การยืนยันส่วนนี้เป็น source review และทดสอบ pattern ที่แปลง character class เป็น JavaScript ยังไม่ได้รัน validator กับ Oracle จริง

## จุดที่พบและตรวจแก้แล้ว

- Board เดิมเรียก `persist()` เมื่อค้น slip เดิม ทำให้ duplicate refresh ต่อ TTL ขัดกับ plan ปัจจุบัน `Scripts/Views/ServiceMonitoringBoard.js:81-94` persist เฉพาะเพิ่มรายการใหม่/ลบรายการที่มีอยู่/clear และ `Views/ServiceMonitoring/Index.cshtml:62` ระบุกติกาตรงกัน ตรวจ regression test ผ่านแล้ว
- ตรวจ code ล่าสุดของ `Controllers/ServiceMonitoringController.cs:45-54,80-83` แล้ว Oracle DATE ถูกส่งเป็นข้อความ wall-clock ไม่มีการแปลง timezone ส่วน loaded-at ยังเป็น UTC สำหรับแสดงเวลาที่โหลด
- ตรวจ code ล่าสุดของ `Infrastructure/PullListPageAccess.cs:25-31` และ `Pulllist.DAL/CostRollup/CostRollupModels.cs:94-110` แล้ว actor ใช้กติกาเดียวกัน รับจาก session, ปฏิเสธ blank/whitespace/control/`SERVICE` และจำกัด UTF-8 50 bytes เอกสาร README บรรทัด 49 ระบุหน่วย bytes ตรงกับ code แล้ว
- `Views/CostRollup/Index.cshtml` และ `Views/ServiceMonitoring/Index.cshtml` ใช้ UTF-8 BOM แล้ว ตรวจ bytes `EF BB BF`, strict UTF-8 decoding และข้อความไทยโดยไม่พบ replacement character การแก้นี้สอดคล้องกับ mojibake ที่ root พบตอนรัน Razor host ซึ่งใช้ ANSI locale ผล render หลังแก้ให้ดูหลักฐานของ root แยกจากการตรวจ bytes นี้
- `Scripts/Views/ServiceMonitoring.js:26-54` เปลี่ยน summary เป็น matrix เดียว มี movement 311/313/315 และ Total แยก Waiting/Posting ต่อ prefix การบวกรวมใช้ค่าของ prefix เดียวกัน ข้าม PK311 และยังแสดง N/A โดยไม่เปลี่ยน aggregate SQL ไม่พบปัญหาใหม่จากการเปลี่ยน layout นี้
- `Scripts/Views/CostRollup.js:39-42,150-155` ยืนยัน Status Y หลัง validation และก่อน mutation โดยข้อความมี customer, ช่วงเวลา Oracle หรือ hold ไม่มีกำหนด และผลในรอบ service ถัดไป Cancel return ก่อนเรียก mutation ส่วน N ไม่แสดง hold confirmation ตรวจ regression assertions ผ่านแล้ว
- Root พบจาก browser test ว่าเมื่อ sessionStorage ใช้ไม่ได้ คำเตือนถูก search submit ล้าง แต่ board ส่งคำเตือนครั้งเดียวจึงไม่แสดงอีก ปัจจุบัน `ServiceMonitoringBoard.js:15-17` ส่ง persistent flag, `ServiceMonitoring.js:139,146-148` แยกคำเตือน storage ออกจาก notice ชั่วคราว และ `Views/ServiceMonitoring/Index.cshtml:64-65` มี status region แยกกัน ข้อความยังเขียนด้วย `.text()` และ search ไม่ล้างคำเตือน storage แล้ว Reviewer ตรวจ code และ regression test ผ่าน 17/17 ไม่พบปัญหาใหม่ใน fix scope และตรวจ UTF-8 BOM ของ view หลังแก้ซ้ำผ่าน

## หลักฐานที่ตรวจ

Cost Rollup ใช้คอลัมน์ canonical ทั้งเจ็ด เก็บ `0C` เป็น string เพิ่มได้ Y เท่านั้น เปลี่ยน Y/N โดยไม่เปลี่ยน key manual write ล้าง reference และใช้ `SYSDATE` ไม่มี write ระหว่าง list/read Date pair ตรวจทั้ง client และ server และคง Oracle wall-clock

`CostRollupDAO.cs:12-19,52-86` ใช้ conditional UPDATE/DELETE เทียบค่าต้นฉบับทุก field รวม nullable values จึงไม่อาศัย `LAST_UPDATE` อย่างเดียวและไม่ retry ทับ concurrent change ผล conflict/not-found แยกกัน SQL ใช้ bind parameters และไม่ประกอบ user input เป็น SQL

Authorization อยู่ระดับ controller ทั้ง HTML และ JSON ใช้ TCODE เต็ม token และ XX deny ทุกครั้ง Mutation ทั้งสาม action มี POST และ anti-forgery ส่วน tracking POST แม้อ่านอย่างเดียวก็ตรวจ anti-forgery ด้วย ไม่รับ actor จาก request ข้อผิดพลาดทั่วไปส่งข้อความกลางและ log ฝั่ง server UI ใส่ค่าจาก DB/error ด้วย `.text()` หรือ `textContent`

Monitoring aggregate อ่าน TPHEADER เท่านั้น mapping GT/MU 311 = 5/8, GT/MU/PK 313 = 10/11, 315 = 14/15 และ PK311 เป็น N/A Tracking query ใช้ column ที่มีใน schema reference จริง WIP ไม่มี STATUS/POST_NO/TCO จึงใช้ typed NULL แทน TP header ที่มีอยู่เป็นตัวตัดสิน source แม้ไม่มี TP items และใช้หนึ่ง SQL statement สำหรับ batch

Board จำกัด 50 IDs และคงลำดับตอนเริ่มค้นหา response ใช้ entry identity พร้อม sequence ป้องกันผลเก่าคืนแถวหลัง remove/clear หรือทับ refresh ล่าสุด sessionStorage แยก app/user เก็บ IDs กับ expiry metadata เท่านั้น restore อ่านใหม่หนึ่ง batch ไม่เก็บ header/items ใน cache มีการจัดการ expired/corrupt/blocked storage

ตรวจ accessibility ใน source แล้ว inputs มี label, ปุ่มใช้ native button, ตารางมี header, ข้อความ error/status ใช้ live-region semantics และการเปิด editor ส่ง focus ไป field ที่เหมาะสม การตรวจนี้ไม่แทน keyboard/screen-reader และ layout ของเว็บบริษัทจริง

Integration fragments ตรงกับ source ทั้งหมด ไม่มี entry ตกหล่นหรือซ้ำ: DAL 4 files, Service 2 files, web 9 files รวม `ServiceMonitoringBoard.js` ชื่อ namespace/paths ตรงกับ reference ที่พบ README แยกการติดตั้งเว็บกับการเปิด service r3 ชัดเจน และไม่อ้างว่า CRUD ทำให้ SAP หยุดเอง

DDL แยกกรณีสร้างใหม่กับตรวจตารางที่มีอยู่ guard ห้ามแทนที่ object เดิมและระบุ DEV/TEST/UAT พร้อมยืนยัน target ไม่พบ DML ที่ reset business rows ใน preflight/validation หรือคำสั่ง drop/truncate ในชุดส่งมอบ Rollback ระบุไม่คืนทั้งตารางทับข้อมูลที่เปลี่ยนหลัง deploy

## Verification ที่ reviewer รัน

- `node --test 05-Tests/AdminPages/ServiceMonitoring/board.test.js`: 16/16 ผ่าน รวม duplicate refresh ไม่ต่อ TTL และ absent remove ไม่ต่อ TTL
- `node --test 05-Tests/AdminPages/ServiceMonitoring/sql.test.js 05-Tests/AdminPages/CostRollup/costrollup.test.js`: query semantics 4/4 และ Cost Rollup client assertions 17/17 ผ่าน
- เปรียบเทียบ XML Compile/Content includes กับ source จริงทั้งสาม project: ไม่พบความต่าง
- Scoped re-review รัน `schema-default.test.cjs`, `CostRollup/costrollup.test.js` และ `ServiceMonitoring/board.test.js` ซ้ำหลังแก้: default pattern 1/1, client assertions 17/17 และ board 16/16 ผ่าน
- ตรวจ BOM/strict UTF-8 ของ Razor views ทั้งสองและ `node --check ServiceMonitoring.js`: ผ่าน การทดสอบ summary matrix ใน browser อยู่ในหลักฐานของ root ไม่ได้อ้างว่าการอ่าน source พิสูจน์ layout จริง
- Scoped re-review รอบ storage warning รัน `ServiceMonitoring/board.test.js` ล่าสุด 17/17 ผ่าน รวม persistent warning เมื่อ restore แล้วค้นหา และตรวจ ServiceMonitoring view ยังเป็น UTF-8 BOM มีข้อความไทย ไม่มี replacement character ไม่ได้แตะ host/fixtures หรืออ้างผล browser rerun ของ root เป็นผลที่ reviewer รัน

SQL tests ใช้ SQLite fixture จาก schema reference เพื่อเช็ก SELECT semantics ไม่ได้พิสูจน์ Oracle parser/provider, bind type, PL/SQL output parameters, commit หรือ concurrent sessions จริง ผล MVC/.NET Framework/Razor/browser ที่ root ตรวจแยกต้องดูเอกสาร verification ของงานนั้น ไม่ได้ถือเป็นผลที่ reviewer รันซ้ำ

ก่อนใช้งานบริษัท ยังต้อง build กับ solution/dependencies/SessionFactory จริง ตรวจ Oracle bindings และ commit, TCODE delimiter/EN/charset, timezone, layout/virtual directory และ TEST/UAT ตาม README ข้อเหล่านี้เป็นขอบเขต integration ที่ขาด environment ไม่ใช่ bug ที่ยืนยันจาก source นี้
