# ภาพรวม RoleValidation

RoleValidation เป็นเว็บภายในสำหรับตรวจว่าใครมีสิทธิ์อะไรในระบบงานของบริษัท โดยนำบัญชีจากระบบต้นทางมาเทียบกับข้อมูลพนักงานและ role กลาง
ตัวอย่างเช่น ระบบต้นทางเรียกสิทธิ์ว่า `APPROVER` ส่วนทีมใช้ชื่อกลางว่า `Reviewer` จึงตั้ง mapping ให้รู้ว่าสองชื่อนี้ตรงกัน หากยังไม่ได้จับคู่ ระบบจะแสดงให้เห็นว่าต้องตรวจเพิ่ม

วิธีติดตั้ง ตั้งค่า และรันระบบอยู่ที่ [README](../README.md)

## ผู้ใช้และสิทธิ์

- `Admin` เปิดดูข้อมูลผู้ใช้ ค้นหา กรอง เรียง และ export Excel ได้
- `Local_IT_Admin` ทำสิ่งที่ `Admin` ทำได้ และแก้ configuration ผ่านหน้าจัดการได้

ทั้งสองแบบต้องผ่าน Company Login และการตรวจพนักงาน/สิทธิ์เข้าเว็บด้วย การล็อกอินสำเร็จเพียงอย่างเดียวยังไม่ทำให้เข้า RoleValidation ได้

## งานหลักที่ระบบทำได้

- ดูผู้ใช้ของ 7 application ได้แก่ eRSN, Pull List, Indirect Material, MFM Plus, EVA Batch Control, IC Programming และ Open Market RFQ
- ค้นหา/กรองผู้ใช้ ตรวจ role และสถานะพนักงาน แล้ว export ผลเป็น Excel
- จัดการรายการ application, role กลาง, ผู้รับผิดชอบ role, mapping และผู้มีสิทธิ์เข้าเว็บ
- ดูประวัติการแก้ข้อมูลและการ login
- ตั้งรอบ email, สั่ง Run now, ติดตาม email run และดาวน์โหลด workbook หรือ ZIP

หน้าจัดการและการส่ง email จำกัดไว้สำหรับ `Local_IT_Admin`
ส่วนไฟล์ผลลัพธ์ของ email run เปิดอ่านและดาวน์โหลดได้ด้วยสิทธิ์ `Admin` หรือ `Local_IT_Admin`

## เมื่อผู้ใช้เปิดเว็บ

1. ถ้ายังไม่มี session เว็บส่งผู้ใช้ไป Company Login
2. เมื่อกลับมาที่ callback ระบบตรวจผล login, ข้อมูลพนักงาน และสิทธิ์เข้าเว็บ ก่อนสร้าง authentication cookie
3. เมื่อเลือก application ระบบอ่านบัญชีจากแหล่งข้อมูลของ application นั้น แล้วเทียบพนักงานและ role กลาง
4. ระบบกรอง/เรียงผลตามที่เลือก แล้วแสดงบนหน้าเว็บหรือสร้าง Excel

ถ้าบัญชีไม่มี Employee No ระบบจะพยายามหาพนักงานจาก username หาก role ไม่มี mapping หรือชี้ไปยัง role ที่ไม่พบ จะแสดงสถานะให้ผู้ดูแลตรวจต่อ

## เมื่อถึงรอบส่งเมล

งานเบื้องหลังสร้างรอบส่งตามตาราง หรือผู้ดูแลกด `Run now` จากนั้นระบบแบ่งงานตาม owner เตรียมไฟล์ Excel และส่งคำขอไป ApiEmail พร้อม path ของไฟล์แนบ ApiEmail จึงต้องอ่านไฟล์จาก path นั้นได้ด้วย

ระบบเก็บผลของแต่ละงานและให้ดาวน์โหลด workbook หรือ ZIP ได้ สถานะ `Accepted` หมายถึง API รับคำขอแล้ว ยังไม่ใช่หลักฐานว่าเมลถึง inbox

Local ใช้ `Fake` จึงไม่ส่งจริง QA ส่งไปผู้ทดสอบผ่าน `SafeRedirect` ส่วน Production ส่งตาม `RoleOwner` หาก Email config ไม่ผ่าน validation เว็บส่วนอื่นยังเปิดได้ แต่ workers จะไม่เริ่มและหน้า Email จะถูกจำกัด

## โครงสร้าง 4 ชั้น

- `RoleValidation.Core` เก็บกฎหลักที่ไม่ผูกกับเว็บหรือฐานข้อมูล เช่น access role, mapping, owner assignment, schedule และสถานะ email
- `RoleValidation.Application` เก็บ use case และ interface เช่น โหลดผู้ใช้ จัดการข้อมูล สร้าง email run และประมวลผล delivery
- `RoleValidation.Infrastructure` เชื่อมของจริง เช่น Oracle, provider ของแต่ละ application, Excel/ZIP, file artifact, encryption และ email transport
- `RoleValidation.Web` เป็น ASP.NET Core MVC มี controller, Razor View, authentication, authorization และ background worker

แต่ละชั้นมี test project คู่กัน ชื่อขึ้นต้นด้วย `RoleValidation.` และลงท้ายด้วย `.Tests` ทั้ง solution ใช้ .NET 10

## ระบบภายนอกและข้อมูล

- Company Login ใช้ยืนยันตัวตนและส่งข้อมูลผู้ใช้กลับมายัง callback
- Oracle `Master` เก็บข้อมูลกลางของ RoleValidation และข้อมูลพนักงานที่ระบบอ่านผ่าน adapter
- Oracle `Material` และ `AppSim` เป็นแหล่งข้อมูลของ legacy application บางตัว
- ApiEmail รับคำขอส่ง email เมื่อเลือก transport แบบ `ApiEmail`
- file system เก็บ workbook และ ZIP ของ email run ตาม artifact path ที่ตั้งค่าไว้

ค่าเริ่มต้นของ Local คือ `Hybrid` ใช้ข้อมูลจัดการใน memory ร่วมกับ Oracle จริง จึงต้องเชื่อมต่อระบบบริษัทและข้อมูลใน memory จะหายเมื่อ restart ส่วน QA/Production ใช้ `Oracle` เก็บข้อมูลจัดการลงฐานข้อมูล

## จุดเริ่มต้นสำหรับนักพัฒนา

- เริ่มที่ `RoleValidation.Web/Program.cs` เพื่อดู pipeline และ service registration
- ดูการเลือกแหล่งข้อมูลที่ `RoleValidation.Web/Configuration/RoleValidationServiceRegistration.cs`
- ตาม request จาก `RoleValidation.Web/Controllers` ไปยัง handler ใน `RoleValidation.Application`
- ดู business rule ที่ `RoleValidation.Core/Features`
- ดู SQL, Oracle adapter, exporter และ email transport ที่ `RoleValidation.Infrastructure`
- schema และ seed สำหรับฐานข้อมูลอยู่ใน `database/schema.sql` และ `database/seed.sql`

ก่อนแก้การทำงานให้ดู test ของชั้นนั้นประกอบ ส่วน JavaScript tests อยู่ใน `RoleValidation.Web.Tests/JavaScript` คำสั่ง build/test อยู่ใน README

## ขอบเขตการยืนยันผล

Automated tests ตรวจตรรกะและการทำงานของส่วนต่าง ๆ ในโค้ด การทดสอบกับ Company Login, Oracle, ApiEmail, file permission และ IIS ต้องตรวจบน environment เป้าหมายอีกครั้ง โดยใช้บัญชี/config ของ environment นั้น

กรณี deploy ที่ผู้ติดตั้งแจ้งคือ IIS ตั้งเป็น HTTP แต่บริษัทบล็อก HTTP จึงต้องตั้ง HTTPS binding พร้อม certificate และเข้า HTTPS โดยตรง ดูขั้นตอนใน README
