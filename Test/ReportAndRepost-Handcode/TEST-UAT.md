# TEST/UAT — Report and Repost

ทดสอบกับ config/connection ของ TEST หรือ UAT แยกจาก PROD โค้ดชุดนี้ยังไม่ได้เรียก Oracle หรือ SmartWH จริงจาก workspace

## Build และเมนู

1. เพิ่มไฟล์/เมธอดตาม README แล้ว build ใน solution บริษัท ตรวจ namespace ของ PULLDAO, PulllistService และ reference เวอร์ชันเดิม
2. เพิ่มลิงก์ใน Report submenu ข้าง CTN เปิด `/ReportAndRepost/Index` ได้ โดยหน้า CTN เดิมยังทำงาน
3. ผู้ไม่มี session หรือไม่มี `ctnReport` ต้องเปิดหน้า/ค้นหา/POST ไม่ได้ POST ที่ไม่มี anti-forgery token ต้องถูกปฏิเสธ

## Query และหน้าเว็บ

1. เทียบผล CTN, Part, Batch, Slip, Location และช่วง APPROVE_DATE กับ SQL เดิม ตรวจวันที่ปลายช่วงว่ารวมครบทั้งวัน
2. เปลี่ยนหน้าแต่ละครั้งต้องโหลดเพียง 25 แถวจาก server จำนวนรวมและจำนวนหลัง filter ต้องตรง SQL บน Oracle
3. ทดสอบข้อมูลทุกกลุ่ม Success ที่พบ, Fail/Error, NULL, blank และ Unknown ตรวจ filter และ checkbox ตรงกัน
4. ตรวจ TPHEADER มี SAP_DOC1, TPHEADER มีแต่ SAP_DOC1 ว่าง, มีเฉพาะ WIPHEADER, ไม่มี header และ header ซ้ำ โดยกรณี TPHEADER ว่างห้ามหยิบ SAP_DOC1 จาก WIPHEADER มาแทน
5. CTN/Slip ซ้ำหลาย log rows, Issue by ว่าง หรือ CTN ว่าง ต้องไม่มี checkbox พร้อมเหตุผล
6. เลือกสองแถวแล้วเปลี่ยนหน้า/ค้นหา/เปลี่ยน status filter ต้องล้างการเลือก ไม่มี Select All ไม่มี checkbox ของ Success/NULL และไม่มี Excel
7. Search ช้าแล้วกด Search ใหม่ ผลของ request เก่าต้องไม่ทับตาราง/filter ใหม่ ระหว่างค้นหาต้องส่งรายการค้างจากหน้าก่อนไม่ได้

## ส่ง SmartWH และบันทึกผล

1. เลือก 2–3 รายการ กดยกเลิก confirmation ต้องไม่มี HTTP ส่งไป SmartWH กดยืนยันแล้วต้องส่งเฉพาะรายการที่เห็นใน confirmation ทีละรายการ
2. ตรวจ request POST/query `ctn`, `pl_no`, `user` และ Authorization เทียบ endpoint TEST จริง ไม่ใช้ค่า demo ผู้ issue ต้องมาจาก header ใน DB
3. HTTP 200 และ `result=Success` ต้องเขียน `{"result":"Success","msg":"success"}` ตรงตัว UPDATE ได้หนึ่งแถวและ commit แล้วจึงแสดง Success
4. HTTP 200 แต่ `result=Fail`, HTTP 503 แม้ body ดูเป็น Success, response ว่าง, timeout และ JSON อ่านไม่ได้ ต้องไม่เขียน Success สถานะเดิมยังอยู่และแสดงรายละเอียด
5. เปลี่ยนสถานะหรือ SAP_DOC1 หลังค้นหาแต่ก่อนกดส่ง ต้องถูกตรวจจาก DB ใหม่และข้ามรายการที่ไม่ผ่าน
6. ให้ผู้ใช้สองคนเลือกแถวเดียวกัน เมื่อคนแรกล็อกแถว คนที่สองต้องไม่ส่ง SmartWH ขณะล็อก หลังคนแรก commit Success คนที่สองต้องส่งซ้ำไม่ได้
7. ทดสอบ SmartWH รับสำเร็จ แต่ UPDATE หรือ commit DB ล้มเหลว ต้องไม่แสดง Success รวม ต้องแสดง `SMART_SUCCESS_DB_FAILED` และตรวจ SmartWH/DB ก่อนส่งซ้ำ
8. ตัดการเชื่อมต่อ browser ระหว่างส่ง ชุดต้องหยุด ไม่ส่งรายการถัดไปหรือ retry รายการเดิมอัตโนมัติ ตรวจ transaction จริงของรายการที่ไม่รู้ผลก่อนทดลองซ้ำ
   ทดสอบอีกกรณีที่ browser ยังเชื่อมต่อ แต่ Controller รอ SmartWH จน timeout/transport exception หรือได้ HTTP 2xx ที่ body ว่าง ต้องได้ `SMART_RESULT_UNKNOWN` คงสถานะ DB เดิมและเลือกแถวเดิมซ้ำไม่ได้ในหน้าที่เปิดอยู่
   หาก DB connection หลุดจน rollback ล้มพร้อมกับ SmartWH timeout ต้องยังได้ `SMART_RESULT_UNKNOWN` พร้อมรายละเอียด SmartWH เดิมและ cleanup error ไม่เปลี่ยนเป็น `FAILED` และไม่เปิดให้เลือกซ้ำในหน้าเดิม
9. ตรวจ SAP document, FIFO และ WIP quantity ก่อน/หลัง ต้องไม่เปลี่ยนจาก Re-post นี้ ตรวจว่าโค้ดชุดใหม่ไม่ได้เรียกเส้นทาง post SAP

## ข้อที่ test doubles พิสูจน์แทนระบบจริงไม่ได้

Oracle ROWID/driver binding/transaction/lock, ประสิทธิภาพ query กับข้อมูลจริง, ความหมายและการรับซ้ำของ endpoint CONFIRM_CTN, header/permission menu ของบริษัท, RestSharp timeout และ serialization ของ MVC/Razor ต้องผ่าน TEST/UAT ข้างต้น ไม่สรุปว่าพร้อม PROD จากผล harness อย่างเดียว
