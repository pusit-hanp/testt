# ผลตรวจชุด Admin Pages 2026-11-08

สร้าง Cost Rollup และ Service Monitoring ตามข้อสรุปใน chat พร้อม final review แยกจากผู้เขียน feature ไม่พบ finding ค้างในขอบเขต source ที่ตรวจ แก้ปัญหาที่พบและรันทดสอบหลังแก้แล้ว

| ชุดทดสอบ | ผลล่าสุด | ขอบเขต |
| --- | --- | --- |
| Cost Rollup C# | 42 ผ่าน / 0 ไม่ผ่าน | validation, date/actor, bind parameters, conditional writes, controller; company/MVC boundaries จำลอง |
| Cost Rollup JavaScript | 17 / 0 | วันเวลา, safe text, original snapshot, ยืนยัน Y และยกเลิกก่อนเขียน |
| Service Monitoring C# | 8 / 0 | count mapping, TP/WIP source, batch validation, response dates; company boundaries จำลอง |
| Board JavaScript | 17 / 0 | TTL 30 นาที, cap 50, ลำดับ, response ช้า, ลบ/ล้าง, storage เสียหรือปิด |
| SELECT semantics | 4 / 0 | ใช้ SELECT จาก DAO กับ SQLite fixture ตาม schema reference ไม่ใช่ Oracle |
| Shared authorization | 28 / 0 | session/actor, TCODE exact token, XX deny, 401/403, cache headers |
| Schema default regression | 1 / 0 | ห้ามยอมรับ default `' N'`/`'N '` เป็น `'N'`; source pattern test |
| HTTP integration | 100 / 0 | IIS Express, MVC 5.2.3 และ Razor จริง; session cookie, permissions, anti-forgery, conflicts, batch 50/51 |
| Browser workflows | 19 / 0 | Chromium + jQuery + HTTP จริง; CRUD, date wall-clock, matrix, append/remove/clear, storage/TTL, XSS, mobile width |

Compile C# ใหม่ทั้งหมดกับ reference assemblies ของ .NET Framework 4.8, C# 7.3, MVC 5.2.3 และ Dapper สำเร็จ ใน test host ใช้ BaseDAO/BaseService/SessionFactory และ layout จำลองเฉพาะส่วนที่ไม่มี source บริษัท ไม่มีการเรียก Oracle/SAP จริง

Browser tests ใช้ timezone America/Los_Angeles เพื่อจับการเปลี่ยน Oracle DATE ตาม timezone โดยไม่ตั้งใจ ค่า schedule และ document date ยังคง wall-clock เดิม ตรวจ screenshot ของทั้งสองหน้าและ Monitoring ที่กว้าง 390px แล้ว ภาษาไทยอ่านได้ ตารางใหญ่เลื่อนแนวนอนในพื้นที่ของตัวเอง ไม่มี JavaScript error ที่ไม่ถูกจัดการ

จุดที่แก้ระหว่างตรวจ

- ค้น slip เดิมเคยต่อ TTL เปลี่ยนให้ refresh ไม่ต่ออายุ เก็บเฉพาะ IDs และเวลาที่เพิ่ม/ลบ/ล้างล่าสุด
- Oracle DATE ส่งเป็นข้อความ ไม่ใช้ JSON Date ที่ browser แปลง timezone
- Razor views ใช้ UTF-8 BOM หลังพบภาษาไทยเสียบน host ที่อ่านไฟล์ด้วย encoding เดิม
- จำกัด actor เป็น 50 UTF-8 bytes ให้ตรงคอลัมน์ ตรวจซ้ำทั้ง shared access และ business validation
- SQL validator เก็บช่องว่างภายใน default literal ไว้ ไม่ตัดจนยอมรับค่าที่ผิด
- ยืนยันก่อนบันทึก Y และแสดงช่วงเวลา/ผลใน service รอบถัดไป ยกเลิกแล้วไม่มี POST
- Summary เป็น matrix 311/313/315 พร้อมยอดรวมต่อ prefix
- คำเตือนเมื่อ storage ใช้ไม่ได้แยกจากข้อความค้นหา จึงไม่หายเมื่อค้น slip

รายงานละเอียดอยู่ใน [FINAL-REVIEW.md](FINAL-REVIEW.md) และ [HTTP-REVIEW.md](HTTP-REVIEW.md) การทดสอบทุกชุดจบด้วย exit code 0 ส่วน test scripts, fixtures และ screenshots เก็บใน workspace `05-Tests/AdminPages` ไม่บรรจุ test host/binaries/dependencies ลงชุดติดตั้ง

สิ่งที่ยังต้องยืนยันใน TEST/UAT บริษัท

- Build solution จริงและตรวจ BaseService/BaseDAO/SessionFactory/Oracle provider, bind types, PL/SQL OUT parameter, commit และ conflict ระหว่างสอง session
- TCODE จริงแทน XX, delimiter ของ auth_item, Session["auth"] เป็น EN ที่เชื่อถือได้, charset และ Oracle timezone
- ตรวจผล/ประสิทธิภาพ query บนข้อมูลจริง, layout/menu/virtual directory และการเปิดใช้ service r3 แยกจากเว็บ

ไม่มีการ deploy เข้าเครื่องบริษัทหรือเปลี่ยน DB บริษัทจากงานนี้ ไม่ได้แก้ archive service r3 เดิม
