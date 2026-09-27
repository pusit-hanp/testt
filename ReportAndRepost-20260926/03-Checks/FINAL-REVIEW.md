# Review: Report and Repost 2026-09-26

ขอบเขตที่อนุมัติ: บันทึก Fail ล่าสุดจาก SMART, checkbox หัว Select ตัวเดียวสำหรับหน้าปัจจุบัน และ popup ยืนยันเป็นตาราง Slip/CTN โดยคงโครง Controller → Service → DAO/callback เดิม

## สาเหตุและการแก้

ภาพทดสอบแสดงข้อความเก่า `CAN NOT RECEIVE DATA FROM SMART` ในตาราง ขณะที่ผลส่งด้านล่างเป็น HTTP 200 + Fail ว่าไม่พบ CTN ตรวจโค้ดชุด r2 พบว่า DAO rollback/return เมื่อ `reply.Success=false` ก่อนถึง UPDATE จึงไม่มีการบันทึก Fail ใหม่ ต่อให้ refresh ตารางก็อ่านค่าเดิมกลับมาได้ ไม่ใช่หลักฐานว่า SMART ส่งสำเร็จหรือว่า refresh เสีย

เพิ่ม `StatusToSave` ใน `ReportAndRepostSmartReply` ให้ Controller ส่งข้อความที่จะเก็บแยกจากข้อความแสดงผล DAO บันทึกเมื่อสถานะนั้นจำแนกเป็น Fail ได้และผลไม่เป็น uncertain เท่านั้น เก็บ Success path และ canonical Success เดิม ผล Fail ที่ commit แล้วคง `Success=false` และ `Outcome=FAILED` ส่วน DB write/commit ที่ไม่ยืนยันรายงาน `SMART_FAIL_DB_FAILED` แยกจาก SMART Fail

Header checkbox เลือกเฉพาะ eligible rows ในหน้าปัจจุบัน แสดงขีดกลางเมื่อเลือกบางแถว ล็อกระหว่างโหลด/ยืนยัน/ส่ง และรีเซ็ตเมื่อเปลี่ยนผลค้นหา ไม่มี Clear selection

Popup ใช้ตารางหนึ่งรายการต่อแถว สร้าง identifier ด้วย `.text()` จัดข้อความชิดซ้ายและไม่ตัดรหัสขึ้นบรรทัดใหม่ รายการยาวเลื่อนใน popup ได้ ปุ่ม Confirm แสดงจำนวนจริง Cancel ไม่ส่งและคืน selection/focus

## ฐานหลักฐาน

ยึด `pulllistservice_2017/01-Reference-Reconstructed/PS_TpostFromSlip/Form1.cs`, `TranferToSmartAsync` และ `UpdateStatus`: POST พร้อม query `ctn/pl_no/user`, body ว่าง UTF-8 `text/plain`; เมื่อ HTTP สำเร็จ service เก็บ response body แม้ business result เป็น Fail เมื่อ HTTP error เก็บ `Error <code>: <reason>` แล้ว update `STATUS_TRANFER`

ไม่ได้ใช้ sender/controller ของ CTN ที่ถูก comment เป็นฐานอ้างอิง ไม่แก้ reference reconstructed หรือเรียก flow post SAP ทั้ง slip หน้า Repost ส่งเฉพาะแถวที่เลือก

คง SAP_DOC1 guard, WIPHEADER fallback เฉพาะไม่พบ TPHEADER, duplicate CTN/Slip guard, ISSUE_BY จาก DB, row lock, guarded update หนึ่งแถว และ commit ก่อนกล่าวว่า save สำเร็จ Timeout/คำตอบไม่ชัดเจนยังไม่ถูกตีความเป็น Fail ที่ยืนยันแล้วและไม่มี automatic retry

## ผล review และขอบเขต

ตรวจ backend และ UI โดยผู้ตรวจแยกจากผู้แก้ พร้อมเทียบกับ r2 และ service ล่าสุด ไม่พบ actionable finding เพิ่มในขอบเขตนี้ รายละเอียดผลทดสอบอยู่ใน `VERIFICATION.md` ขั้นตอนสร้างชุด transfer ตรวจ source กับไฟล์ส่งมอบ และตรวจ SHA256 ทุกไฟล์ใน ZIP เทียบ transfer folder

ไม่มี package, appSetting หรือ database schema ใหม่ การแก้ใช้ 4 ไฟล์แทนที่และ 1 DAO method ชุด transfer มีวิธีวางที่ `00-START-HERE.md` ต้องวาง Controller/Models/DAO พร้อมกัน และ View/JS คู่กัน

ยังไม่ได้ build/deploy solution บริษัทหรือเรียก Oracle/SMART จริง จึงต้องยืนยันขนาด column, transaction และ library ของหน้าเว็บใน TEST/UAT ตาม `TEST-UAT.md` การกรองด้วย exact status เก่าทำให้แถวที่เปลี่ยนข้อความออกจาก filter ได้ ใช้ Fail/Error หรือ All เพื่อดูสถานะล่าสุด
