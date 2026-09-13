# Report and Repost — ชุดส่งมอบ 2026-09-13

สร้างหน้าใหม่ `/ReportAndRepost/Index` ใต้ Report menu ข้าง CTN ใช้ project และรูปแบบ Controller → PulllistService → PULLDAO เดิม ไม่มี Excel, interface ใหม่ หรือการแก้ schema

โฟลเดอร์นี้มีครบ 11 ไฟล์สำหรับ handcode รวมคู่มือนี้และ TEST-UAT ไม่ต้องนำไฟล์ทดสอบหรือ snapshot จาก workspace ไปใส่ใน solution บริษัท

## ถ้าวางชุดก่อนหน้าไปแล้ว

รอบล่าสุดแก้โค้ด 5 ไฟล์ ได้แก่ `ReportAndRepostController.cs`, `Index.cshtml`, `ReportAndRepost.js`, `ReportAndRepostModels.cs` และเฉพาะเมธอด `GetReportAndRepost` ใน `PULLDAO.methods.cs.txt`

เปลี่ยนเป็น `Unauthorize()` ให้ตรง BaseController บริษัท คืนคอลัมน์ RFID Cart/Tag/Reason และลำดับข้อมูลตาม CTN เดิม เปลี่ยนหัว Material doc 313 เป็น SAP document และแสดงเหตุผลตรงช่อง Select เมื่อเลือกไม่ได้ ถ้าหนึ่ง Slip มีหลาย Cart จะแสดงชื่อที่ไม่ซ้ำรวมในช่องเดียวเพื่อไม่ให้แถว CTN ซ้ำ

รักษา config และ Authorization ของ environment ที่ตั้งไว้ในบริษัท ไม่แก้ BaseController และไม่เพิ่มไฟล์หรือเมธอดชื่อเดิมซ้ำ

## ไฟล์และตำแหน่งที่นำไปวาง

| ไฟล์ในชุดนี้ | ตำแหน่ง / วิธีนำไปใช้ |
|---|---|
| [ReportAndRepostModels.cs](ReportAndRepostModels.cs) | ไฟล์ใหม่ `Pulllist.DAL/PULLLIST/ReportAndRepostModels.cs` |
| [ReportAndRepostStatus.cs](ReportAndRepostStatus.cs) | ไฟล์ใหม่ `Pulllist.DAL/PULLLIST/ReportAndRepostStatus.cs` |
| [PULLDAO.methods.cs.txt](PULLDAO.methods.cs.txt) | เพิ่มหรือปรับเมธอดใน class `PULLDAO` เดิมที่ `Pulllist.DAL/PULLLIST/PULLDAO.cs` เติม using ตามต้น snippet |
| [PulllistService.methods.cs.txt](PulllistService.methods.cs.txt) | เพิ่มหรือปรับสองเมธอดใน class `PulllistService` เดิม เติม using ตามต้น snippet |
| [ReportAndRepostController.cs](ReportAndRepostController.cs) | ไฟล์ใหม่ `PulllistMaterial/Controllers/ReportAndRepostController.cs` |
| [Index.cshtml](Index.cshtml) | ไฟล์ใหม่ `PulllistMaterial/Views/ReportAndRepost/Index.cshtml` |
| [ReportAndRepost.js](ReportAndRepost.js) | ไฟล์ใหม่ `PulllistMaterial/Scripts/Views/ReportAndRepost.js` |
| [Report-menu.cshtml.txt](Report-menu.cshtml.txt) | แทรกรายการใน Report submenu เดิมข้าง CTN ใช้ HTML และเงื่อนไขสิทธิ์เดิม ลิงก์ไป `@Url.Action("Index", "ReportAndRepost")` |
| [Web.config.appSettings.xml.txt](Web.config.appSettings.xml.txt) | ใช้ประกอบการตั้ง Authorization ตามรายละเอียดด้านล่าง |
| [TEST-UAT.md](TEST-UAT.md) | รายการตรวจ build, สิทธิ์, ตาราง และผล Re-post ใน TEST/UAT |

ไฟล์ `.methods.cs.txt` เป็นบางส่วนของ class ห้ามใช้ทับไฟล์ DAO/Service ทั้งไฟล์ และไม่ Include `.txt` เข้า project ส่วนไฟล์ใหม่ 5 ไฟล์ให้ Include ผ่าน Visual Studio ตาม project เดิม สองไฟล์ DAL ใช้ namespace `Pulllist.DAL.PULLLIST` ไม่ต้องสร้างโฟลเดอร์ Z02JHVP หรือ project ใหม่

## Config และสิทธิ์

ใช้ `CONFIRM_CTN` สำหรับ URL, `ctnReport` สำหรับสิทธิ์ และ connection `MATERIAL` เดิมให้ตรง environment หน้าใหม่ใช้ `Unauthorize()` ตาม BaseController บริษัท

Controller ชุดนี้อ่าน Authorization header จาก `CONFIRM_CTN_AUTHORIZATION` ใน `<appSettings>` ของ `PulllistMaterial/Web.config` ที่ root web project ค่าใน snippet เว้นว่างไว้ ต้องใช้ header เดิมของ endpoint นี้ครบทั้ง scheme และค่า

ถ้าบริษัทมี config key สำหรับ Authorization นี้อยู่แล้ว ให้เปลี่ยน Controller ไปอ่าน key เดิม ไม่ต้องเพิ่ม key ซ้ำ การเพิ่ม `CONFIRM_CTN_AUTHORIZATION` เป็นวิธีเก็บ header ของชุดนี้ ไม่ใช่ endpoint ใหม่หรือข้อบังคับของ SmartWH ห้ามคัดลอก snippet ไปทับ Web.config ทั้งไฟล์ และอย่าใส่ใน `Views/Web.config` หรือ `packages.config`

ใช้ MVC, RestSharp, Newtonsoft.Json และ Dapper เวอร์ชันที่มีใน solution เดิม Layout ต้องโหลด jQuery, DataTables, moment และ SweetAlert2 ตามหน้า CTN ส่วน Location ใช้ `GTReport.getUserLocation` เดิม

## พฤติกรรมที่ต้องคงไว้

ค้นตาม CTN, Part, Batch, Slip, Location, Issue Date และ Status เริ่มต้นที่ Fail / Error เลือกส่งได้เฉพาะแถวในหน้าปัจจุบัน Success/NULL/Unknown ไม่มี checkbox ส่วน Fail ต้องผ่านเงื่อนไข header, SAP_DOC1, ผู้ issue และคู่ CTN/Slip ไม่ซ้ำ เหตุผลที่ไม่ผ่านจะแสดงในช่อง Select

SAP document อ่าน SAP_DOC1 จาก TPHEADER ก่อน ใช้ WIPHEADER เฉพาะเมื่อไม่มี Slip ใน TPHEADER การมี TPHEADER แต่ SAP_DOC1 ว่างจะไม่ใช้ค่าจาก WIPHEADER แทน หน้านี้ส่งซ้ำหา SmartWH ไม่ได้ post SAP ใหม่

เมื่อ SmartWH ยืนยันสำเร็จและ DB commit ผ่าน จึงบันทึก STATUS_TRANFER เป็น `{"result":"Success","msg":"success"}` ถ้าไม่สำเร็จคงสถานะเดิม ผลที่ยังไม่แน่นอนจะกันเลือกซ้ำในหน้าที่เปิดอยู่และให้ตรวจผลก่อนส่งอีกครั้ง ไม่มี automatic retry

## ผลตรวจ

โค้ดชุดนี้ตรงกับรุ่นที่ทดสอบวันที่ 2026-09-12: C# 128 checks และ JavaScript selection/batch, columns/block reasons ผ่าน ตรวจความตรงกันของไฟล์ตอนจัดส่งวันที่ 2026-09-13 โดยไม่ได้เปลี่ยน logic เพิ่ม

ยังไม่ได้ build กับ solution บริษัทหรือทดสอบ Oracle/SmartWH จริง ใช้ [TEST-UAT.md](TEST-UAT.md) ตรวจใน TEST/UAT ก่อนนำไปใช้ใน PROD
