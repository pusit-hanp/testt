# Transfer ไปเครื่อง company: ReportAndRepost 2026-09-26

ชุดนี้ใช้กับหน้า ReportAndRepost ที่ติดตั้งแล้ว โดยเทียบจากชุด `ReportAndRepost-20260914-r2` ใช้โฟลเดอร์นี้หรือ ZIP ชื่อเดียวกัน นำไปเครื่อง company แล้ววาง source ใน solution เว็บก่อน build ไม่ใช่ไฟล์ DLL สำหรับวางทับ IIS โดยตรง

แก้สามเรื่องที่ตกลงกัน: เก็บ Fail ล่าสุดจาก SMART, checkbox หัว Select ตัวเดียวสำหรับเลือก/ยกเลิกทั้งหน้าปัจจุบัน และ popup ยืนยันเป็นตาราง Slip / CTN ไม่มี Clear selection

## ไฟล์ที่ต้องนำไปวาง

ตำแหน่งปลายทางด้านล่างอ้างจาก root ของ solution เว็บบริษัท ไม่ใช่ root ของ `pulllistservice_2017`

| ไฟล์ใน transfer folder | ตำแหน่งปลายทาง / วิธีวาง |
|---|---|
| `01-Replace/PulllistMaterial/Controllers/ReportAndRepostController.cs` | แทน `PulllistMaterial/Controllers/ReportAndRepostController.cs` |
| `01-Replace/Pulllist.DAL/PULLLIST/ReportAndRepostModels.cs` | แทน `Pulllist.DAL/PULLLIST/ReportAndRepostModels.cs` |
| `01-Replace/PulllistMaterial/Views/ReportAndRepost/Index.cshtml` | แทน `PulllistMaterial/Views/ReportAndRepost/Index.cshtml` |
| `01-Replace/PulllistMaterial/Scripts/Views/ReportAndRepost.js` | แทน `PulllistMaterial/Scripts/Views/ReportAndRepost.js` |
| `02-Merge/Pulllist.DAL/PULLLIST/PULLDAO.RepostReportCtn.method.cs.txt` | เปิด `Pulllist.DAL/PULLLIST/PULLDAO.cs` แล้วแทนเฉพาะเมธอด `RepostReportCtn(...)` เดิมทั้งเมธอด |

รวม 4 ไฟล์แทนที่ + 1 เมธอด ห้ามใช้ snippet ไปแทนทั้ง `PULLDAO.cs` หรือเพิ่มเมธอดชื่อเดิมซ้ำ ถ้า `ReportAndRepostModels.cs` อยู่ที่ root ของ DAL project อยู่แล้ว ให้แทนไฟล์นั้นโดยคง namespace `Pulllist.DAL.PULLLIST` ไม่สร้างไฟล์ซ้ำ

ต้องวาง Controller, Models และ DAO method พร้อมกัน เพราะส่งค่า `StatusToSave` ระหว่างชั้น ส่วน View และ JavaScript ต้องวางคู่กันเพื่อให้ checkbox และรูปแบบ popup ตรงกัน

## ขั้นตอน

1. สำรอง source 5 ตำแหน่งข้างต้นจากเครื่อง company ก่อนแก้ ถ้ามีการแก้เฉพาะบริษัทหลังชุด r2 ให้เทียบและรวมการแก้ส่วนนั้นด้วย
2. คัดลอก 4 ไฟล์ใน `01-Replace` และแทนเมธอดจาก `02-Merge` ตามตาราง เก็บ encoding UTF-8
3. Build solution บริษัทด้วย target/framework และ references เดิม ไม่เพิ่ม NuGet package, appSetting หรือ database column ตรวจว่า project ใช้ Models ตัวใหม่เพียงไฟล์เดียว
4. Publish/build ไป TEST หรือ UAT ตามขั้นตอนของบริษัท แล้วเปิดหน้าและกด Ctrl+F5 ให้โหลด JavaScript ใหม่ ไม่ใช้ config/connection ของ PROD ในรอบทดสอบ
5. ทดสอบกรณีด้านล่างก่อนนำขึ้น PROD ตามขั้นตอนบริษัท ดูรายละเอียดเพิ่มใน `03-Checks/TEST-UAT.md`

ไม่ต้องแก้ `PulllistService`, `ReportAndRepostStatus`, menu, `Web.config`, SAP service หรือไฟล์ reference reconstructed ในรอบนี้ โดยชุดก่อนหน้าต้องมี Status helper ที่รองรับ Fail JSON และ `Error 300–599:` อยู่แล้ว

## สิ่งที่ต้องเห็นหลังวาง

- CTN ที่ตั้งใจให้ไม่มีใน SMART: เมื่อได้ `HTTP 200` + `result=Fail` ตารางต้องแสดง response Fail ล่าสุดแทนข้อความเก่า และยังอยู่ใน Fail/Error filter ผลส่งไม่เปลี่ยนเป็น Success
- HTTP error เก็บรูปแบบ `Error <code>: <reason>` ตาม service ส่วน `HTTP 200: ...` เป็นข้อความแสดงผล ไม่ถูกบันทึกเป็น prefix หน้า JSON
- Fail ตอบกลับแล้วแต่ DB save ไม่ยืนยัน: แสดง `SMART_FAIL_DB_FAILED` พร้อมคำตอบ SMART และข้อผิดพลาด DB ไม่กล่าวว่าบันทึกแล้ว
- Checkbox หัว Select เลือกเฉพาะแถวที่ Repost ได้ในหน้าปัจจุบัน ขีดกลางเมื่อเลือกบางแถว กดเพื่อเลือกครบ กดซ้ำเพื่อล้าง แถวที่ส่งไม่ได้ไม่ถูกเลือก ไม่มี Clear selection
- Popup แสดงหนึ่งรายการต่อแถว จำนวนตรงรายการที่เลือก Cancel ไม่ส่งข้อมูล ยืนยันแล้วส่งเฉพาะรายการที่แสดงทีละรายการ
- Success ปกติยังผ่านและบันทึก canonical Success เหมือนเดิม ไม่มีการ post SAP ซ้ำ ส่วน timeout/ผลไม่แน่นอนยังกันเลือกซ้ำในหน้าที่เปิดอยู่และไม่มี automatic retry

ถ้าใช้ exact-status filter ของข้อความ Fail เก่า หลังบันทึกข้อความใหม่แถวอาจออกจาก filter นั้น ให้เลือก Fail/Error หรือ All จะพบข้อความล่าสุด ผลรอบส่งด้านล่างยังคงแสดงอยู่

## ผลตรวจและ rollback

ผลทดสอบ local อยู่ใน `03-Checks/VERIFICATION.md` และผล review อยู่ใน `03-Checks/FINAL-REVIEW.md` ยังต้อง build กับ DLL บริษัท และทดสอบ Oracle/SMART จริงใน TEST/UAT รวมถึงขนาด `STATUS_TRANFER` กับ response จริง ไม่มีการเดาความยาวแล้วตัดข้อมูลทิ้งในโค้ด

ถ้าต้อง rollback ให้คืน source ที่สำรองทั้ง Controller/Models/DAO method และ View/JS เป็นชุดเดียวกัน แล้ว build/publish ใหม่ การ rollback source ไม่ย้อนสถานะที่บันทึกไปแล้ว จึงไม่ควรแก้ DB ย้อนอัตโนมัติ

`SHA256SUMS.txt` ใช้ตรวจว่าไฟล์ที่คัดลอกครบและตรงชุดส่งมอบ โฟลเดอร์ `03-Checks` และไฟล์คู่มือต่าง ๆ เป็นเอกสาร ไม่ต้องวางใน web project
