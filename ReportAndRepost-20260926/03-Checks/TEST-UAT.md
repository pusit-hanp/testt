# TEST/UAT: Report and Repost 2026-09-26

ทดสอบกับ config/connection ของ TEST หรือ UAT แยกจาก PROD โค้ดชุดนี้ยังไม่ได้เรียก Oracle หรือ SmartWH จริงจาก workspace

## Build และเมนู

1. เพิ่มไฟล์/เมธอดตาม README แล้ว build ใน solution บริษัท ตรวจ namespace ของ PULLDAO, PulllistService และ reference เวอร์ชันเดิม Controller ต้องมี reference `System.Net.Http` ของ .NET Framework 4.8 และเรียก `Unauthorize()` ตาม BaseController จริง ไม่ต้องเพิ่ม NuGet package
2. เพิ่มลิงก์ใน Report submenu ข้าง CTN เปิด `/ReportAndRepost/Index` ได้ โดยหน้า CTN เดิมยังทำงาน
3. ผู้ไม่มี session หรือไม่มี `ctnReport` ต้องเปิดหน้า/ค้นหา/POST ไม่ได้ POST ที่ไม่มี anti-forgery token ต้องถูกปฏิเสธ

## Query และหน้าเว็บ

1. เทียบผล CTN, Part, Batch, Slip, Location และช่วง APPROVE_DATE กับ SQL เดิม ตรวจวันที่ปลายช่วงว่ารวมครบทั้งวัน
2. เปลี่ยนหน้าแต่ละครั้งต้องโหลดเพียง 25 แถวจาก server จำนวนรวมและจำนวนหลัง filter ต้องตรง SQL บน Oracle
3. ทดสอบข้อมูลทุกกลุ่ม Success ที่พบ, Fail/Error รวม `Timeout`, `Error 400: Bad Request`, `Error 502: Bad Gateway`, `Error 504: Gateway Timeout`, NULL, blank และ Unknown ตรวจ filter และ checkbox ตรงกัน ข้อความที่ไม่รู้จักต้องไม่กลายเป็น Fail โดยอัตโนมัติ
4. ตรวจ TPHEADER มี SAP_DOC1, TPHEADER มีแต่ SAP_DOC1 ว่าง, มีเฉพาะ WIPHEADER, ไม่มี header และ header ซ้ำ โดยกรณี TPHEADER ว่างห้ามหยิบ SAP_DOC1 จาก WIPHEADER มาแทน
5. CTN/Slip ซ้ำหลาย log rows, Issue by ว่าง หรือ CTN ว่าง ต้องไม่มี checkbox พร้อมเหตุผลที่อ่านได้ในช่อง Select โดยไม่ต้องชี้เมาส์ Success/NULL ยังต้องไม่มี checkbox
6. Checkbox หัว Select ต้องเลือกเฉพาะแถวที่ Repost ได้ในหน้าปัจจุบัน ขีดกลางเมื่อเลือกบางแถว ถูกเมื่อเลือกครบ กดจากขีดกลางเลือกครบแล้วกดซ้ำล้าง เมื่อไม่มีแถวที่เลือกได้ต้อง disabled และไม่ checked ไม่มี Clear selection เลือกสองแถวแล้วเปลี่ยนหน้า/ค้นหา/เปลี่ยน status filter ต้องล้างการเลือก ไม่มี checkbox ของ Success/NULL และไม่มี Excel
7. Search ช้าแล้วกด Search ใหม่ ผลของ request เก่าต้องไม่ทับตาราง/filter ใหม่ ระหว่างค้นหาต้องส่งรายการค้างจากหน้าก่อนไม่ได้ และต้องปิด header/row checkbox ให้จำนวนที่เลือกตรงกับหน้าจอ
8. เทียบลำดับคอลัมน์และค่า RFID Tag, Reason, Scan Issue by กับ CTN เดิม รวมทั้งเลื่อนตารางแนวนอนบนจอแคบ ต้องเห็นครบทุกคอลัมน์ `SAP document` ต้องตรง SAP_DOC1 ใน header
9. Slip ที่ไม่มี Cart, มี Cart เดียว, หลาย Cart หรือชื่อ Cart ซ้ำ ต้องแสดงชื่อที่ไม่ซ้ำครบในช่อง RFID Cart โดยจำนวนแถว, ROW_ID, pagination และ checkbox ไม่เปลี่ยนเพราะจำนวน Cart
10. ใช้ filter Fail ให้ได้ 26 แถว ขนาดหน้า 25 เปิดหน้าสองแล้ว Repost แถวเดียวจนสำเร็จ ต้องกลับหน้าแรกและเห็น 25 แถวที่เหลือ ค่า filter เดิมและผลรอบส่งยังอยู่ หลังวาง JavaScript ใหม่ให้ล้าง cache ก่อนทดสอบ

## ส่ง SmartWH และบันทึกผล

1. เลือก 2–3 รายการ กดยกเลิก confirmation ต้องไม่มี HTTP ส่งไป SmartWH และกลับมาเลือกได้ กดยืนยันแล้วต้องส่งเฉพาะรายการที่เห็นใน confirmation ทีละรายการ ลอง 25 รายการ: แยก # / Slip No. / CTN ชัดเจน ไม่มีเลข Slip/CTN แตกบรรทัดกลางรหัส ตารางเลื่อนได้ ปุ่ม Cancel / Confirm re-post (จำนวน) ยังมองเห็น ไม่มี question icon ใหญ่ และข้อความจากข้อมูลไม่กลายเป็น HTML
2. ตรวจ `CONFIRM_CTN` ของเว็บให้เป็น endpoint confirm CTN ของ environment เดียวกับ `ConfirmCtn` ของ service ล่าสุด ไม่ใช้ค่า host หรือ URL lookup ทดสอบโดยไม่เพิ่ม `CONFIRM_CTN_AUTHORIZATION`: POST/query `ctn`, `pl_no`, `user` พร้อม body ว่าง `text/plain; charset=utf-8` ต้องส่งได้ ผู้ issue ต้องมาจาก header ใน DB ไม่มี Authorization/X-Client headers ของ SAP ลองค่าที่มี `+`, `&`, ช่องว่าง และภาษาไทยถ้ามีในข้อมูล ให้ปลายทางได้ค่าตรงต้นฉบับ
3. HTTP 200 และ `result=Success` ต้องเขียน `{"result":"Success","msg":"success"}` ตรงตัว UPDATE ได้หนึ่งแถวและ commit แล้วจึงแสดง Success
4. HTTP 200 แต่ `result=Fail` ต้องบันทึก Fail ล่าสุดลง `STATUS_TRANFER` และ commit โดยผล Repost ยังเป็น Fail ทดสอบ CTN ที่ไม่มีอยู่จริง: จาก `CAN NOT RECEIVE DATA FROM SMART` ต้องเปลี่ยนเป็น response `CTN not found in smart system.` หลัง reload และยังพบใน Fail/Error filter ไม่บันทึก prefix `HTTP 200:` รวมลง JSON
   HTTP 503 แม้ body ดูเป็น Success ต้องบันทึก `Error 503: Service Unavailable` และไม่เขียน Success ส่วน response ว่าง, timeout และ HTTP 2xx ที่อ่านผลไม่ได้คงสถานะ DB เดิม พร้อมผลไม่แน่นอน
   ทดสอบ exact-status filter ที่เลือกข้อความ Fail เก่า: เมื่อข้อความเปลี่ยน แถวอาจออกจาก filter เก่าได้ ให้เลือก Fail/Error หรือ All จะเห็นข้อความใหม่ ขณะที่ผลรอบส่งด้านล่างยังอยู่
   ตรวจชนิด/ความยาว `STATUS_TRANFER` จริงกับ response ตัวอย่างและข้อความยาวใน UAT ไม่มีการเดาความยาวแล้วตัดข้อความทิ้งหรือเปลี่ยน schema ในชุดนี้
5. เปลี่ยนสถานะหรือ SAP_DOC1 หลังค้นหาแต่ก่อนกดส่ง ต้องถูกตรวจจาก DB ใหม่และข้ามรายการที่ไม่ผ่าน
6. ให้ผู้ใช้สองคนเลือกแถวเดียวกัน เมื่อคนแรกล็อกแถว คนที่สองต้องไม่ส่ง SmartWH ขณะล็อก หลังคนแรก commit Success คนที่สองต้องส่งซ้ำไม่ได้
7. ทดสอบ SmartWH รับสำเร็จ แต่ UPDATE หรือ commit DB ล้มเหลว ต้องไม่แสดง Success รวม ต้องแสดง `SMART_SUCCESS_DB_FAILED` และตรวจ SmartWH/DB ก่อนส่งซ้ำ
   ทดสอบ SmartWH ตอบ Fail แต่ UPDATE หรือ commit DB ล้มเหลว ต้องแสดง `SMART_FAIL_DB_FAILED` พร้อมข้อความ Fail ล่าสุดและข้อความ DB save was not confirmed ไม่กล่าวว่าบันทึกแล้ว และไม่ส่งซ้ำอัตโนมัติ
8. ตัดการเชื่อมต่อ browser ระหว่างส่ง ชุดต้องหยุด ไม่ส่งรายการถัดไปหรือ retry รายการเดิมอัตโนมัติ ตรวจ transaction จริงของรายการที่ไม่รู้ผลก่อนทดลองซ้ำ
   ทดสอบให้บางรายการสำเร็จก่อน request ถัดไปล้มเหลว เมื่อโหลดตารางได้ต้องกลับหน้าแรกโดยเก็บ filter เดิม รวมกรณี server บันทึกสำเร็จจน Fail เหลือ 25 จาก 26 แถว แต่ browser ไม่ได้รับคำตอบ ต้องยังแสดงผลไม่แน่นอนและกันส่งรายการนั้นซ้ำในหน้าที่เปิดอยู่
   ทดสอบอีกกรณีที่ browser ยังเชื่อมต่อ แต่ Controller รอ SmartWH จน timeout/transport exception หรือได้ HTTP 2xx ที่ body ว่าง ต้องได้ `SMART_RESULT_UNKNOWN` คงสถานะ DB เดิมและเลือกแถวเดิมซ้ำไม่ได้ในหน้าที่เปิดอยู่
   หาก DB connection หลุดจน rollback ล้มพร้อมกับ SmartWH timeout ต้องยังได้ `SMART_RESULT_UNKNOWN` พร้อมรายละเอียด SmartWH เดิมและ cleanup error ไม่เปลี่ยนเป็น `FAILED` และไม่เปิดให้เลือกซ้ำในหน้าเดิม
9. ตรวจ SAP document, FIFO และ WIP quantity ก่อน/หลัง ต้องไม่เปลี่ยนจาก Re-post นี้ ตรวจว่าโค้ดชุดใหม่ไม่ได้เรียกเส้นทาง post SAP

## ข้อที่ test doubles พิสูจน์แทนระบบจริงไม่ได้

Oracle ROWID/driver binding/transaction/lock, ประสิทธิภาพ query กับข้อมูลจริง, การรับ ctn/pl_no ซ้ำและ response ของ endpoint CONFIRM_CTN, permission menu ของบริษัท, TLS/timeout บนเว็บ net48 และ serialization ของ MVC/Razor ต้องตรวจใน TEST/UAT ข้างต้น ผล harness ไม่ใช่ผล deploy หรือการรับรอง PROD

หาก SMART ตอบว่าเคยรับแล้ว แต่ไม่ใช่ business success ที่รู้จัก ให้คงสถานะเดิมและแสดงผลไว้ก่อน ไม่เพิ่มกติกาเปลี่ยนเป็น Success จากข้อความนั้นเอง ทั้งกรณีข้อมูลเก่าที่เป็น Timeout และกรณี service ส่งชนกับผู้ใช้ ต้องตรวจผลที่ SMART ได้รับจริงใน UAT
