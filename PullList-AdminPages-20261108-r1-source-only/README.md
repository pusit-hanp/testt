# Pull List: Cost Rollup และ Service Monitoring

ชุด source เพิ่มหน้า Administrator สองหน้า สำหรับเว็บ ASP.NET MVC 5 / .NET Framework 4.8 เดิม แยก Controller, View, Service, DAO และ JavaScript ของแต่ละหน้า ใช้ connection `MATERIAL` ผ่าน `BaseService` และ `SessionFactory` ของบริษัท ไม่ได้แก้หรือบรรจุ service r3 และไม่มี credential ในชุดนี้

นี่เป็น source kit สำหรับ merge และ build กับ solution บริษัท ไม่ใช่เว็บพร้อมติดตั้ง ไม่มี `.sln`, `.csproj`, `_Layout.cshtml`, `BaseService`, `BaseDAO` หรือ `SessionFactory` ฉบับเต็มของบริษัทอยู่ใน workspace จึงยังยืนยัน company build, Oracle runtime และหน้าจอใน layout จริงไม่ได้

เริ่มจาก [FILE-LIST.md](FILE-LIST.md) เพื่อดูไฟล์ใหม่ 15 ไฟล์และจุดที่ต้อง merge ในไฟล์เดิม ผลทดสอบอยู่ใน [verification/RESULTS.md](verification/RESULTS.md) และ checksum ของทุกไฟล์อยู่ใน `MANIFEST.csv` ไม่ต้องคัดลอก SQL, integration fragments หรือเอกสารเข้า web root

## ก่อนติดตั้ง

สำรอง source, project files, Web.config, layout/menu และเว็บที่ deploy อยู่ เก็บ export ของ `PULLLIST_COSTROLLUP` พร้อม schema, constraints, จำนวนแถวและค่าทุกคอลัมน์ถ้ามีตารางแล้ว ระบุ environment และผู้รับผิดชอบให้ชัด โดยเริ่มที่ DEV/TEST และตรวจ UAT ก่อน PROD

ต้องตรวจสถานะ DB และ service แยกกัน อย่าอนุมานว่ามี r2 หรือ r3 ติดตั้งแล้วจากชื่อไฟล์ในชุดส่งมอบ หน้า Cost Rollup ทำ CRUD ได้เมื่อตารางและสิทธิ์พร้อม แต่การ skip posting ตาม hold/schedule จะทำงานเมื่อ **ติดตั้งและเปิดใช้ service r3 แยกต่างหาก** เท่านั้น หาก service เดิมยังไม่รองรับ ห้ามแจ้งผู้ใช้ว่าการบันทึกบนเว็บหยุด posting แล้ว

## ตรวจและเตรียม Oracle

ใช้ SQL*Plus, SQLcl หรือ SQL Developer แบบ Run Script (F5) ใน session ใหม่ ไม่มี transaction อื่นค้าง เก็บ output ของทุกคำสั่งไว้กับ deployment record ชุดนี้ไม่ได้รันคำสั่งใดกับ DB บริษัท

1. รัน `database/00-preflight.sql` ซึ่งเป็น read-only ตรวจ `DB_NAME`, `SERVICE_NAME`, `SESSION_USER`, `CURRENT_SCHEMA`, ตาราง/synonym, schema และเวลาของ Oracle ให้ DBA ยืนยัน connection จริง การพิมพ์ชื่อ environment ไม่ได้พิสูจน์ว่าเป็น TEST
2. ถ้ามีตาราง canonical เจ็ดคอลัมน์แล้ว รัน `database/02-preflight-existing.sql` ซึ่งเรียก `04-validate.sql` แบบ read-only ไม่สร้างตาราง ไม่เปลี่ยน status/owner และไม่ reset business data
3. เฉพาะระบบ legacy ที่ไม่มีทั้งตารางและ synonym `PULLLIST_COSTROLLUP` ให้รัน `database/01-create-new.sql` แล้ว `database/04-validate.sql` สคริปต์สร้างตารางมี guard ตรวจ DB/service ที่ระบุ, user/schema `MATERIAL`, stage DEV/TEST/UAT และข้อความยืนยัน DDL โดยปฏิเสธ PROD ห้ามแก้ guard เพื่อข้ามการตรวจ Oracle DDL commit อัตโนมัติ จึงย้อนกลับด้วย `ROLLBACK` ไม่ได้
4. ถ้าพบ schema อื่น เช่น ขาดคอลัมน์วันที่ มี `COSTUPDATE_STATUS`, ใช้ CHAR แทน VARCHAR2, เป็น synonym หรือ validation ไม่ผ่าน ให้หยุดและทำ migration เฉพาะกับ DBA ชุดนี้ไม่มีสคริปต์แปลง schema เก่า ไม่ให้ drop/recreate หรือเติมค่าที่เดาเอง

Schema ที่ validator รับเป็น local table ใน `MATERIAL` มีเจ็ดคอลัมน์เท่านั้น โดยไม่บังคับลำดับคอลัมน์

| Column | Type / nullability |
| --- | --- |
| `CUST_CODE` | `VARCHAR2(2 BYTE) NOT NULL`, PK เฉพาะคอลัมน์นี้ |
| `COSTROLLUP_STATUS` | `VARCHAR2(1 BYTE) DEFAULT 'N' NOT NULL` |
| `MISLIP_NO_REF` | `VARCHAR2(50 BYTE)`, NULL ได้ |
| `CHANGE_BY` | `VARCHAR2(50 BYTE)`, NULL ได้ |
| `FROM_DATE`, `TO_DATE`, `LAST_UPDATE` | `DATE`, NULL ได้ |

PK, status CHECK ที่รับเฉพาะ `Y/N` และ date-pair CHECK ต้อง ENABLED/VALIDATED วันต้องว่างทั้งคู่หรือมีทั้งคู่และ `FROM_DATE <= TO_DATE` Validator ตรวจชนิด/ขนาด/nullability/default, PK, CHECK และข้อมูลที่มีอยู่ ไม่ลบ constraints ที่ไม่รู้จัก สถานะที่ไม่ใช่ Y/N, customer ที่ไม่ตรง `[A-Z0-9]{2}` และวันที่ผิดต้องแก้ตาม business owner ก่อนเปิดใช้

แถว `CHANGE_BY IS NULL` จะถูกแสดงให้ตรวจ ไม่ถือว่าเป็น `SERVICE` โดยอัตโนมัติ โดยเฉพาะ NULL-owner/Y ต้องตกลงความหมายก่อนเปิด service r3 ค่า customer เช่น `0C` ต้องคงเลขศูนย์ข้างหน้า ห้ามแปลงเป็นตัวเลข

`SYSDATE` เป็นเวลา Oracle server ส่วน `DATE` ไม่เก็บ timezone ต้องให้ DBA ยืนยันว่าเวลาที่ admin กรอกตรงกับ Oracle wall-clock การตั้ง From/To ไม่ได้สั่ง SAP ปิด Cost Rollup

SQL สืบจาก `PullList-CostRollup-20261108-r3-legacy-only` โดยคง guard เดิม เพิ่มทางเข้าตรวจตารางที่มีอยู่ และตรวจ customer ให้เป็น ASCII A-Z/0-9 ให้ตรงเว็บ ไม่ได้ถือว่าสคริปต์ผ่าน Oracle จริงแล้ว สำหรับ PROD ให้ใช้กระบวนการ deployment ของบริษัทหลัง UAT พร้อม target checks และ backup ไม่ใช้ DDL ชุด DEV/TEST/UAT นี้ตรง ๆ

## Merge source และ configuration

1. วางไฟล์ใหม่จาก `source/Pulllist.DAL`, `source/Pulllist.Service` และ `source/PulllistMaterial` ลง project ที่ตรงกัน โฟลเดอร์ reference บางชุดสะกด `PullListMaterial` แต่ namespace ที่พบจริงคือ `PulllistMaterial` ตรวจ project/root namespace ของบริษัทก่อนวาง ห้ามสร้าง web project ซ้ำจากการสะกดต่างกัน
2. เพิ่มรายการใน `integration/*.csproj.fragment.xml` เข้ากับ `<ItemGroup>` ของ project เดิม หรือ Include ไฟล์ผ่าน Visual Studio ให้มี Compile/Content ครบและไม่ซ้ำ Fragment ไม่ใช่ project file ฉบับเต็ม ตรวจ ProjectReference จาก Service ไป DAL และจากเว็บไป Service/DAL ตามโครงสร้างเดิม
3. ใช้ references เดิมของ MVC 5, System.Web, System.Configuration, System.Core, System.Data, Dapper และ Oracle provider ผ่าน `SessionFactory` รวมถึง jQuery ที่ layout โหลดอยู่ ไม่แทนที่ `packages.config`, `Views/Web.config`, binding redirects หรือ connection strings และไม่เพิ่ม package upgrade พร้อม feature นี้ ตรวจ signature/visibility ของ `BaseService`, `BaseDAO`, `SetConnection`, `GetConnection`, `SessionFactory.Conn`, `Exec<T>`/`Exec` และ transaction/commit ใน solution จริง
4. เพิ่มสอง key จาก `integration/appSettings.fragment.config` ใน `<appSettings>` ของ **web** `Web.config` เดิม ไม่เขียนทับไฟล์ทั้งฉบับ เริ่มด้วย `costRollup=XX` และ `serviceMonitoring=XX` ซึ่ง deny เสมอ รวมถึงเมื่อ user มี token XX ตั้งเป็น TCODE จริงที่บริษัทออกให้เมื่อพร้อมทดสอบสิทธิ์
5. เพิ่มรายการจาก `integration/Administrator-menu.cshtml.txt` ภายในเมนู Administrator เดิม รักษา layout และเมนูอื่นไว้ Snippet ใช้ permission helper เดียวกับ endpoint การซ่อนเมนูไม่ทดแทนการตรวจสิทธิ์ฝั่ง server
6. Build solution บริษัท จากนั้นตรวจ Razor view และ JavaScript ผ่านเว็บจริงใน TEST ตรวจ URL ที่อยู่ใต้ virtual directory, script order และ session timeout ก่อน UAT

Views ใช้ `@section Styles` และ `@section scripts` ตาม reference ของ MatMaster/StoreVLM ต้องให้ layout เดิม render ทั้งสอง section และโหลด jQuery ก่อน scripts ของหน้า คง encoding ของ `.cshtml` ใหม่เป็น UTF-8 BOM เพื่อไม่ให้ข้อความไทยเสียเมื่อ Razor บนเครื่องเดิมอ่านไฟล์

Session ต้องมี `Session["auth"]` เป็น EN ของผู้ใช้ และ `Session["auth_item"]` เป็นรายการ token TCODE ตามระบบเดิม `CHANGE_BY` อ่านจาก session ฝั่ง server เท่านั้น ไม่เชื่อ user/owner ที่ browser ส่งมา หลัง trim แล้วค่า EN ว่าง, `SERVICE`, ยาวเกิน 50 bytes ใน UTF-8 หรือมี whitespace/control character ถูกปฏิเสธ ต้องยืนยันรูปแบบ EN จริงในบริษัท

Permission เปรียบเทียบเต็ม token ไม่ใช้ substring: ตัวอักษร/ตัวเลขและ `_ . : / -` ถือเป็นส่วนของ token จึงต้องทดสอบ delimiter ของ `auth_item` ที่ login จริงสร้าง เช่น TCODE `12` ต้องไม่ตรงกับ `112` หรือ `12-ADMIN` อย่าแก้เป็น `Contains` เพื่อให้ผ่าน หาก format จริงไม่ตรงให้ปรับ parser โดยมี test จากตัวอย่างที่ตัดข้อมูลส่วนตัวแล้ว

ทุก action รวม JSON/read/refresh ต้องผ่าน authorization ของหน้านั้น ส่วน Add/Change/Delete ต้อง POST พร้อม anti-forgery token และไม่ให้ GET เขียนข้อมูล ตรวจทั้ง request ที่ login, ไม่ login, session หมดอายุ, TCODE ผิด/ว่าง/XX และ token หาย/ผิด

## การใช้ Cost Rollup

ค้นหา/เพิ่ม/เปลี่ยน/ลบ customer ได้ โดย customer เป็นตัวอักษรอังกฤษหรือตัวเลขสองตัวและเป็น PK เพิ่มได้เฉพาะสถานะ Y เปลี่ยนได้ Y/N แต่เปลี่ยน customer key ไม่ได้ การแก้ manual ใช้ EN จาก session, ล้าง `MISLIP_NO_REF` และตั้ง `LAST_UPDATE` ด้วย Oracle `SYSDATE` หน้า list/read ไม่ reset แถว service

เว้น From/To ทั้งคู่เพื่อ hold ไม่มีกำหนด หรือใส่ทั้งคู่โดย From ไม่เกิน To ช่วงวันที่รวมขอบเขต Request ใช้รูปแบบ `yyyy-MM-ddTHH:mm:ss` เป็น Oracle wall-clock ไม่ใช่ UTC เมื่อใช้ service r3 ที่เปิด `Check_CostRollUp=true` การเปลี่ยนผ่านเว็บมีผลใน snapshot ของ `ProcessRun` รอบถัดไป รอบที่กำลังรันใช้ snapshot เดิม

ตามสัญญา r3: manual Y ที่ยังไม่ถึง From เก็บ schedule แต่ยังไม่ hold, อยู่ในช่วงให้ hold, เลย To จึงปรับ N และล้าง EN ตอนเริ่มรอบใหม่ ส่วน manual N ล้าง EN ตอนเริ่มรอบใหม่ ไม่จำเป็นต้อง Delete เพื่อคืน auto ตารางนี้เป็น current state ไม่ใช่ประวัติ audit

Change/Delete ใช้ค่าต้นฉบับของแถวตรวจ concurrent change ถ้า service หรือ admin อื่นแก้หลังโหลด หน้าเว็บต้องแจ้ง conflict และให้โหลดข้อมูลใหม่ ห้าม retry โดยเขียนทับอัตโนมัติ แม้ `LAST_UPDATE` อยู่ในวินาทีเดียวกัน DAO ใช้ PL/SQL ตรวจ `SQL%ROWCOUNT` แล้วคืนผลผ่าน output parameter เพราะ `SessionFactory.Exec` เดิมคืนค่า void ต้องทดสอบ OUT bind และ commit ของ provider จริงในบริษัท

## การใช้ Service Monitoring

ส่วนยอดคงค้างอ่าน `TPHEADER` ด้วย aggregate เดียว ไม่ join item และไม่เพิ่ม filter จาก flow posting ตัวเลขจึงเป็นจำนวน header ใน status ที่กำหนด ไม่ใช่จำนวน item และไม่ยืนยันว่า scheduler/service process ยังทำงานหรือพร้อม post SAP

โหลดเมื่อเปิดหน้าและกด Refresh counts เท่านั้น ไม่ยิง query แยกช่องหรือ polling อัตโนมัติ Query เดียวลดจำนวนรอบเรียก DB แต่ยังต้องวัด execution plan และเวลาบนข้อมูลบริษัทก่อนอ้างว่าเร็วขึ้น ชุดนี้ไม่สร้าง index โดยเดาจากชื่อคอลัมน์

| Movement | Prefix | Waiting status | Posting status |
| --- | --- | --- | --- |
| 311 | GT, MU | 5 | 8 |
| 311 | PK | N/A | N/A |
| 313 | GT, MU, PK | 10 | 11 |
| 315 | GT, MU, PK | 14 | 15 |

Tracking ค้น `MISLIP_NO` เต็มค่า เริ่มจาก TPHEADER/TPITEMS ถ้าไม่มี TPHEADER จึงใช้ WIPHEADER/WIPITEMS หากมี TPHEADER แต่ไม่มี item ต้องแสดง TP โดยไม่ดึง item จาก WIP มาปะปน ค่า status/header/item บนหน้าคือข้อมูล DB ล่าสุดที่ request อ่านได้ การ batch ใช้ POST พร้อม anti-forgery token แต่เป็น read-only query

ค้นครั้งใหม่เพิ่มต่อท้ายตามลำดับ ค้น Slip เดิม refresh กลุ่มเดิม Remove ลบทั้งกลุ่ม Slip และ Clear All ล้างทั้ง board ทั้งสองปุ่มไม่ลบข้อมูล DB เก็บได้สูงสุด 50 Slips

Board ใช้ `sessionStorage` แยกตาม application/user เก็บเฉพาะลำดับ Slip IDs และเวลาหมดอายุ ไม่เก็บ header/item/error เป็น cache กลับมาหน้าเดิมในแท็บเดิมภายใน 30 นาทีจะ batch อ่านข้อมูลล่าสุดใหม่ อายุคิดจาก Add/Remove/Clear ล่าสุด การ refresh อย่างเดียวไม่ต่ออายุ ไม่ใช่ board กลางร่วมกันทุกเครื่องหรือทุกแท็บ ปิดแท็บ/หมดอายุ/ล้าง storage อาจทำให้รายการหาย แต่ข้อมูล DB คงเดิม

## ตรวจ TEST/UAT และ rollback

ผล local test และ review อยู่ในเอกสาร verification ที่ส่งมากับชุด การทดสอบด้วย stub หรือ fake SessionFactory ไม่แทน company build หรือ Oracle integration ต้องตรวจอย่างน้อยกรณีต่อไปนี้ใน TEST/UAT ด้วย account และข้อมูลทดสอบที่กำหนด

- Cost Rollup: customer `0C`, duplicate add, Add Y/Change N, date ว่างทั้งคู่/ครบคู่/ข้างเดียว/กลับด้าน, owner จาก session, reference ถูกล้าง และบันทึก Oracle DATE/NULL/commit จริง ตรวจ concurrent edit/delete กับอีก session รวมถึงการแก้ในวินาทีเดียวกัน
- Monitoring: เทียบยอดกับ TPHEADER โดยตรงทุก mapping, PK311 N/A, TP มี header แต่ item ว่าง, มีทั้ง TP/WIP แล้วเลือก TP, พบเฉพาะ WIP, ไม่พบ Slip และข้อความพิเศษไม่กลายเป็น HTML
- Board: ค้นซ้ำ, Remove/Clear ระหว่าง request ค้าง, refresh/ค้นตอบสลับลำดับ, ข้อมูล storage เสีย, storage ใช้ไม่ได้, อายุ 30 นาที, cap 50, เปลี่ยน user, app virtual path และ logout/login ใหม่
- Security: เปิด URL/เรียก JSON ตรงโดยไม่มีสิทธิ์, XX, token ที่คล้ายกัน, anti-forgery หาย/ผิด, ส่ง EN ปลอม และ session หมดอายุ ข้อผิดพลาด DB ต้องไม่เปิด connection string/SQL/รายละเอียด exception แก่ผู้ใช้
- ถ้าจะเปิดผล hold จริง: ติดตั้ง r3 ตามคู่มือของ r3 แยกต่างหาก ยืนยัน flag/matcher, scheduler ไม่รันซ้อน, เวลา Oracle และทดสอบ next-ProcessRun snapshot, future/active/expired schedule และ flow posting เดิมใน SAP TEST/UAT

Rollback เฉพาะเว็บโดยนำเมนู/ไฟล์ใหม่/Compile/Content entries/key ที่เพิ่มออก หรือ restore เว็บ/config ที่สำรองไว้ ห้าม drop `PULLLIST_COSTROLLUP`, truncate, reset status หรือคืนข้อมูลทั้งตารางจาก backup โดยไม่ตรวจการเปลี่ยนแปลงหลัง deploy เพราะ service และ admin อาจใช้ข้อมูลร่วมกัน การ rollback service r3 เป็นอีก change หนึ่ง ต้องใช้คู่มือของ service และเจ้าของระบบตัดสินผลต่อ posting
