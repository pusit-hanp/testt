# รายการไฟล์สำหรับ merge

เพิ่ม source ใหม่ 15 ไฟล์ตามเส้นทางด้านล่าง โดยจับคู่ชื่อ project กับ solution บริษัท ไม่มี source เดิมทั้งไฟล์ถูกเขียนทับจากชุดนี้

- source/Pulllist.DAL/CostRollup/CostRollupDAO.cs
- source/Pulllist.DAL/CostRollup/CostRollupModels.cs
- source/Pulllist.DAL/ServiceMonitoring/ServiceMonitoringDAO.cs
- source/Pulllist.DAL/ServiceMonitoring/ServiceMonitoringModels.cs
- source/PulllistMaterial/Controllers/CostRollupController.cs
- source/PulllistMaterial/Controllers/ServiceMonitoringController.cs
- source/PulllistMaterial/Infrastructure/PullListPageAccess.cs
- source/PulllistMaterial/Infrastructure/PullListPageAuthorizeAttribute.cs
- source/PulllistMaterial/Scripts/Views/CostRollup.js
- source/PulllistMaterial/Scripts/Views/ServiceMonitoringBoard.js
- source/PulllistMaterial/Scripts/Views/ServiceMonitoring.js
- source/PulllistMaterial/Views/CostRollup/Index.cshtml
- source/PulllistMaterial/Views/ServiceMonitoring/Index.cshtml
- source/Pulllist.Service/CostRollupService.cs
- source/Pulllist.Service/ServiceMonitoringService.cs

ต้อง merge ในไฟล์เดิม

- .csproj ของ DAL, Service และ web รวมสามไฟล์ ใช้ integration fragments ที่ตรง project
- Web Web.config เพิ่ม appSettings สอง key และตั้ง TCODE จริง
- Layout/menu Administrator เพิ่มลิงก์สองหน้า โดยรักษา sections Styles/scripts และ jQuery เดิม

ไฟล์ประกอบ

- database/ ตรวจ target/schema หรือสร้างตารางใหม่ตาม README ถ้าไม่มีตารางเดิมเท่านั้น
- verification/ ผลทดสอบและรายงาน review ไม่ต้อง deploy เข้าเว็บ
- MANIFEST.csv SHA256 และขนาดไฟล์ทั้งหมดในชุด ยกเว้นตัว manifest เอง

ไม่รวม service r3, company DLLs, credentials, test host, test binaries หรือ package dependencies ต้อง build/publish ด้วย solution บริษัท
