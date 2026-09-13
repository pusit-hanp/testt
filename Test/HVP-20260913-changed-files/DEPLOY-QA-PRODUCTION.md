หมายเหตุสำหรับ ZIP นี้: เริ่มที่ 00-START-HERE.md หากใช้ changed-files ให้เปลี่ยนเฉพาะ 4 ไฟล์ตามรายการ เพราะไม่มี host/business source ที่ไม่เปลี่ยนรวมมาด้วย ส่วน service-source มี source ครบ 8 ไฟล์

# Deploy Z02JHVPService ไป QA และ Production

อ้างอิง source รุ่น `HVP-20260912-longrun` ตรวจขั้นตอนวันที่ 2026-09-12 คู่มือนี้เป็นขั้นตอนสำหรับผู้ deploy ยังไม่ได้ติดตั้งหรือสั่งรันบน server จริง

เปลี่ยนเฉพาะ project `Z02JHVPService` ไม่ต้อง deploy เว็บไซต์หรือแก้ MatMaster Controller/View/Service/DAO และไม่ต้องสร้าง DB table ใหม่

## 1. ตรวจพฤติกรรมก่อนเปิดใช้งาน

เมื่อ Start service จะสแกนทันที จากนั้นสแกนทุก 5 นาที หาก State ยังไม่มีประวัติ จะทำไฟล์ที่ตรงชื่อทั้งหมดในโฟลเดอร์ รวมไฟล์เก่า ไม่ได้เลือกเฉพาะไฟล์ที่แก้ใน 5 นาทีล่าสุด

ถ้า Production ต้องการเริ่มเฉพาะไฟล์หลัง go-live ต้องปรับนโยบายเริ่มต้นก่อน รุ่นนี้ยังไม่มี config สำหรับข้าม backlog อย่านำ State จาก local/QA มาวางเพื่อข้ามไฟล์ เพราะประวัตินั้นเกิดจากการเขียนคนละ DB

อ่านเฉพาะไฟล์ TXT ปกติในโฟลเดอร์ที่ตั้งไว้ ชื่อลงท้าย `Z02J-HVP.txt` ไม่สนตัวพิมพ์เล็กใหญ่ ไม่อ่าน subfolder และไม่แตก gzip เรียงตาม CreationTimeUtc ไม่ใช่วันที่ในชื่อไฟล์ ไม่มีการย้าย/archive/delete ไฟล์ SAP

## 2. Build ชุด Release

ทำบนเครื่อง development ที่มี solution และ dependency จริงของ Pulllist:

1. ใช้ไฟล์จาก `Z02JHVPService` รุ่น longrun แทน `HvpFileProcessor.cs`, `HvpService.cs`, `HvpWinService.cs`, `Program.cs` และเพิ่ม `HvpLog.cs` กับ `HvpStateStore.cs` เข้า project ถ้าเป็น .csproj แบบเก่า ตรวจว่าเป็น Compile item
2. Merge `App.config` template เข้าของเดิม เก็บ appSettings และ binding redirects ที่ระบบใช้อยู่
3. เลือก Release และ Platform ให้ตรงกับ Oracle provider/client และ legacy DLL ที่ใช้งานจริง ไม่เปลี่ยน bitness โดยเดา
4. Build project Z02JHVPService ด้วย .NET Framework 4.8 ให้ผ่าน แล้วเก็บ output Release ทั้งชุด รวม DLL ที่ dependency ต้องใช้

ZIP ที่ส่งเป็น source package ต้อง build ก่อน ไม่ใช่ ZIP ที่แตกแล้วติดตั้งได้ทันที บน server ไม่ต้องติดตั้ง .NET 10 ของ regression harness และไม่ต้องนำ test executable ไปวาง

## 3. เตรียม QA server และโฟลเดอร์

ตัวอย่างใช้ `lcbwebq2` ตามภาพ ให้ยืนยันว่าเครื่องนี้เป็น QA จริง ส่วนชื่อ Production server ยังไม่ได้ระบุ ขั้นตอนนี้สมมติ QA และ Production เป็นคนละเครื่อง

บน server สร้างโฟลเดอร์นี้ โดยใช้ชื่อให้ตรงกับ path ที่ตั้งจริง:

```text
D:\PulllistService\Z02JHVPService\
    Z02JHVPService.exe
    Z02JHVPService.exe.config
    <DLL dependencies จาก Release output>
    patch\
        patch.xml
    State\
    QA-Input\                 ใช้เฉพาะทดสอบ QA
```

จากเครื่อง development สามารถ copy ไป QA ผ่าน `\\lcbwebq2\d$\PulllistService\Z02JHVPService` แต่ path ที่ service ใช้รันบน server เป็น `D:\PulllistService\Z02JHVPService` ไม่ใช่ D: ของเครื่องที่กำลัง F5

ตรวจว่า server มี .NET Framework 4.8 และ Oracle provider/client ที่ตรงกับ build หาก dependency เดิมใช้ TNS alias ต้องให้ service account เข้าถึง Oracle configuration ที่เกี่ยวข้องด้วย อย่าถือว่า Oracle connection ของบัญชีที่ login จะเท่ากับของ service

## 4. ตั้ง config และสิทธิ์ QA

แก้ไฟล์ที่ exe ใช้จริงคือ `Z02JHVPService.exe.config` ตัวอย่างค่าช่วงทดสอบ QA:

```xml
<add key="HvpFilePath" value="D:\PulllistService\Z02JHVPService\QA-Input" />
<add key="TimerIntervalMinutes" value="5" />
<add key="MaxFileAttempts" value="3" />
<add key="LogRetentionDays" value="30" />
<add key="HvpStatePath" value="D:\PulllistService\Z02JHVPService\State" />
```

ใส่รายการเหล่านี้ใน `<appSettings>` เดิม อย่าสร้าง key ซ้ำ ค่า 3 คือรวมครั้งแรกกับ retry อีก 2 ครั้ง เปลี่ยน config แล้วต้อง restart service

LogRetentionDays=30 ลบเฉพาะ log service ที่วันที่เก่ากว่า 30 วัน ค่า 0 ปิดการลบ ไม่ลบประวัติ checkpoint หรือไฟล์ SAP รุ่นนี้ cache State และบันทึกการเปลี่ยนสถานะต่อท้าย TSV แล้วรวมประวัติซ้ำเป็นระยะ อ่าน State 4/6 คอลัมน์จาก service รุ่นเดิมได้

วาง `patch\patch.xml` ที่อนุมัติของ QA มี node MATERIAL ที่ BaseService ใช้ และนำ `DecryptCTN`/`Encs_T` ที่ branch การ decrypt ของระบบต้องใช้จาก config ของ QA มาใส่ใน appSettings ของ service ด้วย Windows Service ไม่อ่าน Web.config ของเว็บไซต์โดยอัตโนมัติ ไม่ใช้ไฟล์ patch snapshot ที่ปิดค่าไว้

ตรวจ DB target จาก configuration จริงก่อน Start ต้องเป็น QA/TEST ที่ตั้งใจใช้ อย่าตัดสินจากชื่อ folder หรือชื่อ service ส่วน Production ใช้ patch และ secret ของ Production เท่านั้น ไม่เก็บ secret ใน commit หรือเอกสารนี้

ให้ Windows service account ที่องค์กรอนุมัติมีสิทธิ์:

- Read & Execute ที่โฟลเดอร์โปรแกรม และ Read ที่ exe.config/patch/Oracle configuration
- Modify ที่ State เพื่อเขียน log, checkpoint และ lock
- Read/List ที่ input; หากเป็น UNC ต้องมีทั้งสิทธิ์ share และ NTFS
- Log on as a service บน server ให้ผู้ดูแลตรวจ policy หากบัญชีเริ่ม service ไม่ได้

สิทธิ์ DB เป็นของบัญชีใน connection configuration ซึ่งอาจเป็นคนละบัญชีกับ Windows service account ต้อง UPDATE `MAT_MASTER` ของ environment นั้นได้ ไม่ต้องมี CREATE TABLE

เมื่อเปลี่ยนไปอ่าน SAP ผ่าน network ให้ใช้ UNC ที่ยืนยันแล้ว ตัวอย่างจากภาพคือ `\\lcbfsv01.asia.ad.celestica.com\sap\IMOONMU` แต่ภาพไม่ได้ยืนยันว่า share นี้เป็น QA หรือ Production ต้องเลือกให้ตรงงาน ห้ามใช้ `R:` ของบัญชีที่ login เพราะ drive mapping ไม่ได้ส่งต่อให้ service ตามปกติ [Microsoft: Services and Redirected Drives](https://learn.microsoft.com/en-us/windows/win32/services/services-and-redirected-drives)

## 5. ติดตั้ง Windows Service ครั้งแรก

RDP เข้า server เป้าหมาย เปิด PowerShell แบบ Run as administrator ตรวจว่ามี service เดิมหรือไม่:

```powershell
sc.exe query Z02JHVPService
```

ถ้ามีอยู่แล้ว ให้ใช้ขั้นตอนอัปเดตในข้อ 8 ไม่ต้อง create/delete service ใหม่ ถ้ารายงานว่า service ไม่มีอยู่ ให้สร้างแบบ Manual ก่อน:

```powershell
sc.exe create Z02JHVPService binPath= "D:\PulllistService\Z02JHVPService\Z02JHVPService.exe" start= demand DisplayName= "Z02J HVP Service"
```

คำสั่งนี้ยังไม่ Start service และค่าเริ่มต้นของบัญชีเป็น LocalSystem ให้ตั้งบัญชีจริงในขั้นต่อไปก่อน Start ใช้ `sc.exe` เต็มชื่อและเว้นวรรคหลัง `=` ตาม syntax ของ Windows [Microsoft: sc.exe create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create)

เปิด `services.msc` → Z02J HVP Service → Properties → Log On → This account ใส่ service account ที่ได้รับอนุมัติและรหัสผ่านผ่านหน้าจอนี้ หากองค์กรใช้ gMSA ให้ผู้ดูแลตั้งตามมาตรฐาน gMSA ขององค์กร

ตรวจ Path to executable และ Log On แล้วตรวจ config/patch/สิทธิ์ State อีกครั้ง:

```powershell
sc.exe qc Z02JHVPService
```

ชื่อ service ต้องเป็น `Z02JHVPService` ตรงกับโค้ด QA กับ Production ใช้ชื่อนี้ได้เมื่ออยู่คนละ server รุ่นนี้ยังไม่รองรับติดตั้งสอง instance คนละชื่อบนเครื่องเดียวโดยเปลี่ยนแค่คำสั่ง create

ปิด console/F5 ที่ยังทำงานกับ input/DB เดียวกันก่อนเริ่ม service จากนั้น:

```powershell
sc.exe start Z02JHVPService
sc.exe query Z02JHVPService
```

สถานะควรเป็น RUNNING แต่ต้องตรวจ log และ DB ต่อด้วย เพราะ RUNNING ไม่ได้ยืนยันว่าอ่าน SAP หรือเขียน DB สำเร็จ ไม่ต้องเปิด exe ค้างหรือให้ผู้ใช้ login ค้างเพื่อให้ Windows Service ทำงาน

## 6. ทดสอบ QA ให้ครบก่อน Production

เริ่มจาก QA-Input ว่าง วาง TXT ทดสอบที่มี header `Material` และ `Special Control Flag` คั่นด้วย tab และใช้ Material ที่รู้ว่ามีใน QA DB:

1. Material เดียวมีหลาย PLNT/SLOC: ค่า HVP ต้องอัปเดตทุกแถว จากนั้นส่งไฟล์ใหม่ค่า flag ว่างหรือค่าอื่น ต้องล้าง flag ของ Material นั้นทุกแถว ใน Oracle จะแสดง NULL กด Search ที่หน้า Material Master เพื่อโหลด HVP Flag ใหม่
2. วางหลายไฟล์ที่ CreationTime ต่างกัน ตรวจ START/DONE ใน log ว่าเก่าก่อนใหม่ ค่าของไฟล์ที่สร้างใหม่กว่าต้องเป็นผลสุดท้าย การแก้ไฟล์เก่าอาจทำให้ replay ไฟล์ที่ตามหลัง
3. รอรอบ 5 นาที ตรวจว่า service ยัง SCAN และรับไฟล์ใหม่ได้ จากนั้น Stop/Start ตรวจว่าไฟล์สำเร็จเดิมไม่ถูกทำซ้ำเมื่อ State เดิมยังอยู่
4. ใช้ไฟล์ทดสอบ header ผิดใน QA ตรวจ FAILED ครั้งที่ 1/3 และ 2/3 ในคนละรอบ แล้ว SKIP ที่ 3/3 ก่อนทำไฟล์ถัดไป ตัวเลข retry ต้องคงอยู่หลัง restart
5. ตรวจว่าไฟล์ input ยังอยู่และเนื้อหาไม่เปลี่ยน แล้วทดสอบการอ่าน UNC ที่อนุมัติด้วย service account จริงก่อน go-live

ดู log ที่ `D:\PulllistService\Z02JHVPService\State\hvp-yyyyMMdd.log` ลำดับปกติคือ SERVICE START → SCAN → START → DB COMMANDS → DONE → SCAN END

`completedRecords` นับรายการที่ส่งคำสั่ง ไม่ใช่จำนวนแถว DB ที่อัปเดต ต้องตรวจ DB ด้วย Material จริงและหน้า UI ที่ชี้ DB เดียวกัน ไม่ต้อง deploy หรือ execute `Verify-Hvp.sql` เพื่อให้ service ทำงาน ไฟล์นี้เป็น SELECT สำหรับตรวจผลเท่านั้น

ทดสอบกับจำนวนไฟล์สะสมจริงบน QA แล้วดู SCAN END: รอบที่ไม่มีงานควรได้ `stateReloaded=False`, `sortedFiles=0`, `stateBytesWritten=0` ส่วน `elapsedMs` กับ `enumerateMs` ใช้ดูต้นทุนรวมและการอ่านรายชื่อ SAP ถ้า State ไม่เปลี่ยนแต่ enumerateMs สูง การปรับ State ไม่ได้แก้ความช้าของ share/network ข้อมูล benchmark local ในรายงาน longrun ไม่ใช่ผลความเร็ว SMB/Oracle

ไฟล์ที่ SKIP แล้วจะไม่ retry version เดิมอีก แม้เพิ่ม MaxFileAttempts; เมื่อ metadata เปลี่ยนจึงเริ่มนับใหม่ หากพบ DB/config error ให้แก้สาเหตุก่อนปล่อยให้ครบ limit เพราะข้อผิดพลาด DB ระหว่างทำไฟล์ก็นับเป็น attempt และอาจมีบางรายการอัปเดตไปแล้ว

เมื่อทดสอบผ่าน ตั้งให้เริ่มอัตโนมัติหลัง reboot:

```powershell
sc.exe config Z02JHVPService start= auto
```

`auto` เริ่ม service เมื่อเครื่องเริ่มทำงานโดยไม่ต้องมีผู้ใช้ login [Microsoft: sc.exe config](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-config)

## 7. นำขึ้น Production

ใช้ exe/DLL ชุดเดียวที่ผ่าน QA ไม่ rebuild คนละชุด แล้วทำข้อ 3–5 บน Production server ที่ยืนยันแล้ว โดยเปลี่ยนเฉพาะ configuration ที่จำเป็นของ Production ได้แก่ input path, State path, patch.xml, decrypt settings และ service account

การติดตั้ง Production ใหม่ใช้ State ของ Production เอง ไม่ copy State/log จาก QA หรือ local หากเป็นการอัปเดต Production ที่เคยรันแล้วต้องรักษา State เดิมตามข้อ 8

ก่อน Start ตรวจว่า DB target ถูกต้องและยอมรับพฤติกรรมทำ backlog ในข้อ 1 แล้ว เริ่มแบบ Manual ตรวจ log, Material ตัวอย่าง และผลใน UI ของ Production ก่อนตั้ง `start= auto` ตรวจอย่างน้อยหนึ่งรอบ timer ถัดไปด้วย อย่าใช้ไฟล์ทดลองล้าง flag ของ Material จริงเพื่อทดสอบ Production

ให้มีตัวประมวลผลสำหรับ input/DB ชุดนี้เพียง instance เดียว `job.lock` ป้องกันเฉพาะ process ที่ใช้ State directory เดียวกัน ไม่ได้ป้องกันอีก server ที่ใช้ State คนละแห่ง

## 8. อัปเดต version เดิมและ rollback

บน server เป้าหมาย:

```powershell
sc.exe stop Z02JHVPService
sc.exe query Z02JHVPService
```

รอจนเป็น STOPPED ก่อนสำรองหรือทับไฟล์ รุ่นนี้รอ scan ที่กำลังทำให้จบ หากยัง STOP_PENDING ให้ตรวจงาน/log ก่อน ไม่ทับ DLL ขณะยังรัน

สำรอง exe/DLL, exe.config, patch และ State ทั้งชุดไว้ในโฟลเดอร์ backup แยกจากโปรแกรม จด version และ DB target โดยไม่บันทึกรหัสผ่านใน release note แล้ว copy exe/DLL รุ่นใหม่ เก็บ config/patch ของ environment เดิมและ merge เฉพาะ key ที่ต้องเปลี่ยน รักษา State เดิมไว้ ไม่ลบ `processed-files.tsv` ไม่ทับด้วย State ว่าง

รักษารูปแบบ input path เดิมด้วย การเปลี่ยนจาก hostname เป็น IP หรือเปลี่ยนชื่อ share แม้ชี้ไฟล์เดียวกัน อาจทำให้ถูกนับเป็นไฟล์ใหม่ เพราะ checkpoint ใช้ full path เป็น key

หยุด service ก่อนแก้หรือ restore checkpoint ด้วยมือ รุ่น longrun cache จาก path/ขนาด/เวลาของไฟล์ และเก็บหลายบรรทัดต่อ path ได้โดยบรรทัดล่าสุดมีผล ถ้า log แจ้ง STATE RECOVERED หมายถึงตัดเฉพาะบรรทัดท้ายที่เขียนไม่ครบ อาจ retry รายการนั้น ไม่ได้ล้างประวัติทั้งหมด

ตรวจ `sc.exe qc Z02JHVPService` ว่าชี้ output ที่อัปเดตจริง ถ้าใช้ path เดิมไม่ต้อง create service ใหม่ จากนั้น Start และตรวจผลตามข้อ 6

หาก rollback ให้ Stop และรอ STOPPED ก่อนคืน binary/config/patch ชุดที่เข้ากันได้ รุ่นล่าสุดเขียน checkpoint 6 คอลัมน์ รุ่นเก่าที่อ่านได้เพียง 4 คอลัมน์ใช้ State ใหม่นี้ไม่ได้ ต้องวางแผนคืน backup ที่เข้ากันด้วย การคืน State เก่าอาจทำให้ replay ไฟล์หลังเวลา backup และการคืน binary ไม่ได้ย้อนข้อมูลที่เขียนใน DB ไปแล้ว ถ้าต้องคืนข้อมูลให้ประสานผู้ดูแล DB

## Commit message สำหรับชุดแก้ service

ใช้เมื่อ commit source, regression tests และ deployment guide ชุดนี้ ไม่รวม secret ของ QA/Production:

```text
fix(hvp): process SAP files in order with persistent retry tracking

Scan for new or changed files every five minutes and preserve source files.
Update all matching Material rows to HVP or blank without changing MatMaster.
Persist file status locally and skip after configurable failed attempts (default 3).
Defer files changed after queuing so the next scan restores creation-time order.
Add regression coverage and QA/production deployment instructions.
```

ถ้า commit เฉพาะเอกสารใช้ `docs(hvp): add QA and production deployment guide` คู่มือนี้ไม่ได้สั่ง git commit ให้

สำหรับชุดปรับระยะยาวใช้:

```text
perf(hvp): reduce checkpoint I/O and add log retention

Cache unchanged state and sort only files that need processing or replay.
Append durable state updates and compact duplicate history periodically.
Retain service logs for configurable days and report scan timings.
Preserve retry, skip and file ordering without adding a service database.
```

## ขอบเขตผลทดสอบที่มี

Regression harness รุ่น longrun ผ่าน 48 กรณีด้วย DB จำลอง และมี host probe เดิมตรวจ Start/Stop โค้ด service 6 ไฟล์ compile ด้วย .NET Framework compiler ผ่านเมื่อใช้ stub แทน legacy dependencies แต่ยังไม่ได้ build กับ legacy dependencies จริงหรือทดสอบ Windows SCM/Oracle บน QA/Production ขั้นตอนทดสอบบน server ในคู่มือนี้จึงยังต้องทำ
