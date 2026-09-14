# Review: Report and Repost 2026-09-14 r2

ขอบเขตตามผู้ใช้: รวมโค้ดชุด 20260914 ใน annotation ที่ส่ง SMART ตาม service ล่าสุด พร้อมแก้ finding P2 จาก final review โดยคงโครงเดิมและไม่ถือ sender/controller ของ CTN ที่ถูก comment เป็นฐานอ้างอิง

## ฐานหลักฐานและการตัดสินใจ

`pulllistservice_2017/01-Reference-Reconstructed/PS_TpostFromSlip/Form1.cs`, `TranferToSmartAsync` บรรทัด 508–575 เป็นหลักฐานรูปแบบส่ง SMART: POST, query ctn/pl_no/user, ISSUE_BY จาก caller, body ว่าง UTF-8 text/plain ไม่มีการตั้ง Authorization หรือ SAP headers ใน request นี้

เก็บโครง Controller → Service → DAO/callback เดิม ชุด 20260914 เปลี่ยน Controller เป็น HttpClient ของ framework และตัวจำแนกสถานะให้รับ Timeout/HTTP error ที่ service เขียนจริง ชุด r2 เพิ่มการแก้ JavaScript ให้ reset paging หลัง batch จบหรือ request ล้มเหลว ไม่แก้ source reference

กติกา SAP_DOC1, WIPHEADER fallback เฉพาะไม่พบ TPHEADER และ success string อ้าง requirement ที่บันทึกไว้ใน `06-Review-Reports/SMARTWH-REPOST-REVIEW.md` ส่วน “ผลต่อ Report and Re-post requirement” ไม่ได้นำ commented sender มาใช้ คง duplicate CTN/Slip guard เพราะ request ของ serviceไม่มี ROWID ที่ใช้แยกหลาย log ในคู่เดียวกัน

ไม่เรียก `PostingMaterial` หรือ `TranferToSmartAsync` ทั้ง slip: หน้า Repost ต้องส่งเฉพาะแถวที่เลือก และไม่ post SAP ซ้ำ คง re-read/row lock, UPDATE หนึ่งแถว, business success และ DB commit ก่อนแจ้งสำเร็จ

## ประเด็นที่แก้

P2 จาก final review: หาก filter Fail มี 26 แถว เปิดหน้าสองแล้ว Repost แถวเดียวสำเร็จ จะเหลือ 25 แถว แต่ `table.ajax.reload(null, false)` ยังใช้ offset 25 ทำให้หน้าตารางว่าง DAO รับ offset เดิมตามที่ส่งมา สาเหตุอยู่ที่การเก็บหน้าปัจจุบันทั้งในทางจบรอบและทาง request ล้มเหลว

แก้ `ReportAndRepost.js` เฉพาะสองจุดเป็น `table.ajax.reload(null, true)` ให้กลับหน้าแรกโดยไม่แก้ criteria หรือผลการส่ง เพิ่ม regression สองกรณี 26 → 25: ได้รับ Success และ browser ไม่ได้รับคำตอบหลัง server บันทึกสำเร็จ ก่อนแก้ทั้งสองกรณีล้มเพราะ start ยังเป็น 25 หลังแก้ผ่าน ตรวจด้วย JS จริงที่ขอบเขต DataTables/network จำลอง ไม่ใช่การ render browser บริษัท

การแก้เดิมจากชุดใน annotation ที่รวมมาด้วย:

1. ข้อกำหนด CONFIRM_CTN_AUTHORIZATION และ JSON/CTN headers ใน Controller ไม่ตรง sender ล่าสุด เอาออกแล้ว ใช้ CONFIRM_CTN เดิมโดยไม่เพิ่ม appSetting
2. สถานะที่ service เก็บเป็น Timeout และ Error รหัสอื่นนอกจาก 503 ถูกจัดเป็น Unknown ทำให้เลือกไม่ได้ เพิ่มการรับรหัส 300–599 ในรูปแบบข้อความจาก service โดยไม่เปิดให้ Unknown ทั้งหมดส่งซ้ำ
3. ทดสอบ query encoding บน .NET Framework พบ `HttpUtility.ParseQueryString(...).ToString()` สร้าง Unicode แบบ `%uXXXX` ที่ผ่าน UriBuilder แล้ว decode ไม่กลับเป็นค่าเดิม แก้เฉพาะการประกอบ query ด้วย UTF-8 UrlEncode ทีละ key/value โดยเก็บ parameter เดิมและค่าซ้ำของ endpoint

## ขอบเขตการรับรอง

ผลทดสอบล่าสุดและคำสั่งรันอยู่ใน VERIFICATION.md ส่วนการวางกับ solution บริษัทอยู่ใน TEST-UAT.md

ไม่มีการแก้โครงฐานข้อมูล ไม่มีการเพิ่ม package หรือ interface และไม่มีการเรียก SAP/SMART/Oracle บริษัทจาก workspace นี้ ยังไม่ได้ build MVC5/net48 ทั้ง solution หรือ deploy

พฤติกรรม SMART เมื่อรับ ctn/pl_no เดิมซ้ำยังไม่ได้ยืนยันจากปลายทาง จึงไม่มี automatic retry หรือการแปลข้อความ “เคยรับแล้ว” เป็น Success เอง ผลไม่แน่นอนยังรายงานแยกและกันเลือกซ้ำในหน้าที่เปิดอยู่ ข้อจำกัดนี้ไม่ถูกใช้เป็นเหตุหยุดการปรับโค้ดที่ผู้ใช้อนุมัติ
