# Report and Repost: ไฟล์สำหรับ handcode

รวมไฟล์ล่าสุดหลังแก้ final review วันที่ 2026-09-12 ทั้งหมดอยู่ในโฟลเดอร์นี้ ใช้สร้างหน้าใหม่ใต้ Report menu ข้าง CTN Report

มีไฟล์ใหม่ 5 ไฟล์ และ snippet สำหรับแก้ของเดิม 4 จุด ทำตามลำดับในตาราง โดย `.methods.cs.txt` ต้องนำเฉพาะเมธอดไปใส่ใน class เดิม ไม่ใช้แทนทั้งไฟล์

| ลำดับ | ไฟล์ในโฟลเดอร์นี้ | ทำอะไรในระบบบริษัท |
|---|---|---|
| 1 | [ReportAndRepostModels.cs](ReportAndRepostModels.cs) | สร้างใน project `Pulllist.DAL`, namespace `Pulllist.DAL.PULLLIST` |
| 2 | [ReportAndRepostStatus.cs](ReportAndRepostStatus.cs) | สร้างใน project และ namespace เดียวกับข้อ 1 |
| 3 | [PULLDAO.methods.cs.txt](PULLDAO.methods.cs.txt) | เพิ่มเมธอดใน class `PULLDAO` เดิม เติม using ตามต้น snippet |
| 4 | [PulllistService.methods.cs.txt](PulllistService.methods.cs.txt) | เพิ่มสองเมธอดใน class `PulllistService` เดิม เติม using ตามต้น snippet |
| 5 | [ReportAndRepostController.cs](ReportAndRepostController.cs) | สร้าง `PulllistMaterial/Controllers/ReportAndRepostController.cs` |
| 6 | [Index.cshtml](Index.cshtml) | สร้าง `PulllistMaterial/Views/ReportAndRepost/Index.cshtml` |
| 7 | [ReportAndRepost.js](ReportAndRepost.js) | สร้าง `PulllistMaterial/Scripts/Views/ReportAndRepost.js` |
| 8 | [Report-menu.cshtml.txt](Report-menu.cshtml.txt) | แทรกรายการใน Report submenu ข้าง CTN ใช้ permission wrapper เดิม ไม่แทนทั้งเมนู |
| 9 | [Web.config.appSettings.xml.txt](Web.config.appSettings.xml.txt) | เพิ่ม `CONFIRM_CTN_AUTHORIZATION` ใน `<appSettings>` เดิมของ web project แล้วใส่ค่าจริงของ environment |

ไฟล์เมนูจริงไม่ได้อยู่ในหลักฐานที่ได้รับ จึงยังระบุชื่อไฟล์เมนูบริษัทไม่ได้ ให้แก้ไฟล์ที่มี Report submenu เดิม ส่วนไฟล์ใหม่ต้อง Include ใน project ผ่าน Visual Studio ตามรูปแบบ solution เดิม

## จุดที่ต้องตรวจตอนวาง

- ใช้ `CONFIRM_CTN`, permission key `ctnReport` และ connection `MATERIAL` เดิมของ environment นั้น ให้ `CONFIRM_CTN_AUTHORIZATION` เป็น Authorization header เดิมครบทั้ง scheme และค่า ไม่ปล่อยว่างเมื่อทดสอบส่งจริง และไม่เพิ่ม key ซ้ำ
- ใช้ reference ที่มีในบริษัท ไม่ติดตั้ง package รุ่นใหม่ทับ หลักฐาน packages.config ระบุ MVC 5.2.3, RestSharp 112.1.0, Newtonsoft.Json 13.0.3 และ net48
- DAL ใช้ Dapper, System.Data และ Newtonsoft.Json ถ้า project DAL ยังไม่มี Newtonsoft.Json ให้เพิ่ม reference เวอร์ชันเดิม
- Controller เรียก `Unauthorized()` ตาม BaseController ที่ได้รับ ตรวจชื่อกับ solution บริษัท เพราะ CTN snapshot อีกชุดใช้ชื่อ `Unauthorize()`
- Layout ต้องโหลด jQuery, DataTables, moment และ SweetAlert2 ตามหน้า CTN เดิม ส่วน datepicker ใช้ path เดิมใน View นี้
- Location lookup ใช้ `GTReport.getUserLocation` ตามหน้า CTN เดิม จึงไม่ต้องสร้าง endpoint Location ใหม่

## พฤติกรรมของชุดนี้

ค้นหาจาก DB พร้อม status filter เลือก Fail/Error ที่ผ่านเงื่อนไขเป็นชุดในหน้าปัจจุบันแล้วส่ง SmartWH ทีละรายการ Success/NULL/Unknown ไม่มี checkbox มี SAP_DOC1 guard และไม่เรียก post SAP ไม่มี Excel หรือ interface ใหม่

เมื่อ SmartWH ยืนยันสำเร็จและ DB commit ผ่าน จะเขียน `STATUS_TRANFER` เป็น `{"result":"Success","msg":"success"}` ถ้าผล SmartWH ยังไม่แน่นอนจะคงสถานะ DB เดิมและกันเลือกซ้ำในหน้าที่เปิดอยู่ รวมถึงกรณี rollback ล้มที่แก้จาก final review แล้ว

โค้ดที่คัดมาผ่านการทดสอบ logic 109 checks และ JavaScript ใน workspace ยังไม่ได้ build กับ solution บริษัทหรือเชื่อม Oracle/SmartWH จริง ให้ตรวจตาม [TEST-UAT.md](TEST-UAT.md) ก่อนเปิดใช้งาน
