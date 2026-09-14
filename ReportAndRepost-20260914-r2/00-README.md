# Report and Repost: ชุดส่งมอบ 2026-09-14 r2

ชุดนี้รวมโค้ดจาก `ReportAndRepost-20260914` ที่อ้างใน annotation พร้อมแก้ finding เรื่องหน้าว่างหลัง Repost ใช้โฟลเดอร์หรือ ZIP `ReportAndRepost-20260914-r2` เป็นชุดส่งมอบปัจจุบัน

ถ้าวางชุด `20260914` ใน annotation แล้ว ให้แทนเฉพาะ `ReportAndRepost.js` ถ้าวางชุด `20260913` แล้ว ให้แทน 3 ไฟล์: `ReportAndRepostController.cs`, `ReportAndRepostStatus.cs` และ `ReportAndRepost.js` พร้อมอ่านคู่มือ config ฉบับนี้ หากยังไม่เคยวาง ReportAndRepost ให้ติดตั้งครบตามตารางด้านล่าง

Controller ส่ง SMART ตาม PS_TpostFromSlip service ล่าสุด ส่วน Status รองรับ Timeout และ HTTP error ที่ service บันทึกไว้ JavaScript แก้ให้กลับหน้าแรกหลัง batch จบหรือ request ล้มเหลว โดยเก็บ filter เดิม โครง DAO/Service, query, checkbox และ SAP_DOC1 guard คงเดิม หลังวาง JavaScript ให้ refresh โดยล้าง cache เพื่อโหลดไฟล์ใหม่

## ตำแหน่งไฟล์

| ไฟล์ในชุดนี้ | ตำแหน่งใน solution บริษัท |
|---|---|
| ReportAndRepostController.cs | PulllistMaterial/Controllers/ReportAndRepostController.cs |
| ReportAndRepostStatus.cs | Pulllist.DAL/PULLLIST/ReportAndRepostStatus.cs |
| ReportAndRepostModels.cs | Pulllist.DAL/PULLLIST/ReportAndRepostModels.cs |
| Index.cshtml | PulllistMaterial/Views/ReportAndRepost/Index.cshtml |
| ReportAndRepost.js | PulllistMaterial/Scripts/Views/ReportAndRepost.js |
| PULLDAO.methods.cs.txt | เพิ่มเมธอดภายใน class PULLDAO เดิม พร้อม using ตามต้น snippet |
| PulllistService.methods.cs.txt | เพิ่มเมธอดภายใน class PulllistService เดิม |
| Report-menu.cshtml.txt | แทรกลิงก์ใน Report menu ของบริษัท ข้าง CTN Report |
| Web.config.appSettings.xml.txt | คำแนะนำ config เท่านั้น ไม่ใช้แทน Web.config |

สองไฟล์ DAL ใช้ namespace `Pulllist.DAL.PULLLIST` ถ้ามีไฟล์อยู่แล้วที่ root ของ project และ namespace ถูก ไม่ต้องสร้างสำเนาซ้ำ ไฟล์ `.methods.cs.txt` เป็นเฉพาะเมธอด ห้ามนำไปแทนทั้ง DAO/Service class

## Config และ reference

ไม่ต้องเพิ่ม `CONFIRM_CTN_AUTHORIZATION` แล้ว Controller ชุดนี้ไม่อ่านค่านี้ หากเคยเพิ่มไว้สำหรับ ReportAndRepost อย่างเดียว จะเอาออกได้

ใช้ `CONFIRM_CTN` เดิมใน `PulllistMaterial/Web.config` ที่ root ของเว็บ ตรวจให้เป็น URL สำหรับ confirm CTN ของ environment เดียวกับ `ConfirmCtn` ใน service ล่าสุด ไม่ใช่ค่า host หรือ URL สำหรับค้น CTN ไม่ต้องเพิ่ม key ซ้ำ ไม่ต้องใส่ Basic/X-Client headers ของ SAP

Controller ใช้ `System.Net.Http` ของ .NET Framework 4.8 ถ้า web project ยังไม่มี assembly reference นี้ ให้เพิ่มผ่าน Add Reference > Assemblies > Framework > System.Net.Http ไม่ต้องเพิ่ม NuGet package ไม่ต้องลบ RestSharp ที่ส่วนอื่นใช้อยู่

คง permission key `ctnReport`, connection `MATERIAL`, Session และ anti-forgery เดิม Controller เรียก `Unauthorize()` ตาม BaseController ที่ได้รับจากบริษัท ไม่ต้องแก้ BaseController ใช้ Newtonsoft.Json เวอร์ชันที่บริษัทมีอยู่สำหรับ DAL Status

กรณีติดตั้งครั้งแรก ให้แทรกเมนูจาก snippet ในไฟล์ที่สร้าง Report submenu ของบริษัท และ Include ไฟล์ใหม่ใน project ส่วน Layout ใช้ jQuery, DataTables, moment, SweetAlert2 และ bootstrap-datepicker ที่มีอยู่ Location lookup ใช้ `GTReport.getUserLocation` เดิม

## พฤติกรรมหลังแก้

ส่ง POST ไป SMART พร้อม query `ctn`, `pl_no`, `user` ที่ encode แล้ว body ว่างชนิด UTF-8 `text/plain` ค่า user มาจาก ISSUE_BY ที่ server โหลดจาก header ไม่รับจาก browser และไม่ใช้ผู้กดแทนผู้ issue

Success/NULL/Unknown ไม่มี checkbox Fail/Error รวม Timeout และ `Error 300–599:` เลือกได้เมื่อผ่าน guard เดิม: SAP_DOC1, header/CTN/Slip/ISSUE_BY ครบและคู่ CTN/Slip ไม่ซ้ำ ใช้ WIPHEADER สำรองเฉพาะไม่พบ Slip ใน TPHEADER กติกา SAP_DOC1/fallback เป็น requirement ของหน้า Repost ไม่ได้สรุปว่า service ใช้ SAPDocField เท่ากับ SAP_DOC1

ส่งเฉพาะแถวที่เลือกในหน้าปัจจุบันทีละรายการ ไม่เรียก post SAP เมื่อ HTTP และ business result ยืนยันสำเร็จ จึงบันทึก `STATUS_TRANFER` เป็น `{"result":"Success","msg":"success"}` แล้ว commit ก่อนแจ้งสำเร็จ

เมื่อรอบส่งจบหรือ request ล้มเหลว ตารางกลับหน้าแรกของ filter เดิม ตัวอย่าง Fail มี 26 แถวแล้วส่งแถวเดียวในหน้าสองสำเร็จ จะเห็น 25 แถวที่เหลือในหน้าแรก ผลของรอบส่งยังแสดงอยู่ด้านล่าง

เมื่อส่งไม่สำเร็จคงสถานะ DB เดิมและแสดงรายละเอียดในผลของรอบนั้น ถ้า timeout/ผลไม่ชัดเจน หรือ SMART สำเร็จแต่ DB save ไม่ยืนยัน จะกันเลือกซ้ำในหน้าที่เปิดอยู่ ไม่มี automatic retry การส่ง ctn/pl_no เดิมซ้ำยังขึ้นกับพฤติกรรม SMART จริง ไม่แปลข้อความว่าเคยรับแล้วเป็น Success เอง

ตรวจตาม [TEST-UAT.md](TEST-UAT.md) และดูผลทดสอบใน [VERIFICATION.md](VERIFICATION.md) ชุดนี้ยังไม่ได้ build/deploy เข้า solution บริษัทหรือเรียก Oracle/SMART จริง
