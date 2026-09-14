# Deploy HVP standalone ด้วย Task Scheduler

สำหรับ .NET Framework 4.8 รุ่น direct Oracle ใช้ project และ output ของ HVP ชุดนี้ ไม่มี ProjectReference หรือ DLL ของ Pulllist เดิม ขั้นตอนนี้ยังไม่ได้ทำบน QA/Production ของบริษัท

## 1. Build และเตรียมไฟล์

เปิด Z02JHVPService.csproj ใน Visual Studio ที่มี .NET Framework 4.8 targeting pack หรือใช้ Developer Command Prompt จาก folder project:

```text
msbuild Z02JHVPService.csproj /restore /p:RestoreLockedMode=true /p:Configuration=Release /p:Platform=AnyCPU
```

ใช้ packages.lock.json ที่ให้มาเพื่อ restore version เดิม ไม่ใส่ packages.config จาก project เก่ากลับมา หากรวมเข้า solution ของ repo ใหม่ ให้เพิ่ม project นี้โดยไม่เพิ่ม reference ไปยังเว็บ/DAL/Service ของ Pulllist

นำไฟล์จาก bin\Release ไปด้วยกัน: Z02JHVPService.exe, Z02JHVPService.exe.config และ dependency DLL ทั้ง 12 ไฟล์ตาม [REFERENCES.md](REFERENCES.md) ส่วน PDB ใช้ประกอบการ debug ได้ ไม่ต้องนำ test EXE, bin/obj ของ tests หรือ .NET 10 SDK ไปติดตั้งที่ server

เครื่องปลายทางต้องมี .NET Framework 4.8 ใช้ Oracle.ManagedDataAccess ที่มากับ output รุ่นนี้ ไม่ต้อง copy DLL จากเว็บหรือแก้ไปอ้าง Oracle provider รุ่นอื่นเอง

## 2. ตั้ง config และสิทธิ์ของ environment

ตัวอย่างใช้ folder `D:\PulllistService\Z02JHVPService` ให้เปลี่ยนเป็น path จริงที่อนุมัติ เก็บ State แยกจากไฟล์โปรแกรม เช่น `D:\PulllistService\State\HVP-QA` และ `D:\PulllistService\State\HVP-PRD`

แก้ Z02JHVPService.exe.config ที่ได้จาก build โดยรักษาส่วน runtime และ generated binding redirects ไว้ ห้าม copy App.config ทับ หรือแทน EXE.config รุ่นใหม่ด้วย config เก่าทั้งไฟล์

```xml
<connectionStrings>
  <add name="Material" connectionString="User Id=MATERIAL;Password=xxx;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=xxx)(PORT=1521))(CONNECT_DATA=(SID=xxx)));" />
</connectionStrings>
```

แทน xxx ด้วย password, host และ SID ของ environment เดียวกัน นี่คือ XML connectionStrings ชื่อ Material ไม่ใช่ appSetting ชื่อ ConnectionStrings:Material และไม่ใช่ JSON ใส่ secret ที่ปลายทาง ไม่เก็บค่าจริงใน repo หรือ command line ระวัง XML escaping เช่น `&` ต้องเป็น `&amp;`

ใน appSettings เดิม กำหนดค่าต่อไปนี้โดยไม่สร้าง key ซ้ำ:

```xml
<add key="HvpFilePath" value="\\server\share\SAP" />
<add key="HvpStatePath" value="D:\PulllistService\State\HVP-QA" />
<add key="TimerIntervalMinutes" value="5" />
<add key="MaxFileAttempts" value="3" />
<add key="LogRetentionDays" value="30" />
```

เปลี่ยน UNC และ State path เป็นค่าจริง คงรูปแบบ source path เดิมเมื่ออัปเกรด เพราะ checkpoint ใช้ full path เป็น key หากไม่กำหนด HvpStatePath จะใช้ `%ProgramData%\Z02JHVPService` อย่าให้ QA และ Production ใช้ State ร่วมกัน

บัญชี Windows ที่ Task ใช้ต้องมี Read & Execute ที่โปรแกรม/EXE.config, Read/List ที่ input ทั้ง share และ NTFS และ Modify ที่ State รวมสิทธิ์สร้าง/แทนที่/ลบไฟล์ บัญชี DB ใน connection string ต้อง UPDATE MAT_MASTER ได้ ไม่ต้องมี CREATE TABLE

ใช้บัญชีที่องค์กรอนุมัติและมีสิทธิ์รัน scheduled task สำหรับการอ่าน UNC อย่าใช้ S4U ซึ่งไม่เก็บ password และไม่ให้เข้าถึง network ตาม [Microsoft TASK_LOGON_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/ne-taskschd-task_logon_type) อย่าถือว่าการอ่าน share จากบัญชีที่ login พิสูจน์สิทธิ์ของ Task แล้ว การเปลี่ยน config ต้อง restart process

## 3. สร้าง Task

ก่อนเปิดใช้งาน ยืนยัน DB target และ backlog: ถ้า State ไม่มีประวัติ โปรแกรมจะอ่านไฟล์ที่ตรงชื่อทั้งหมด รวมไฟล์เก่า ไม่มี config สำหรับข้าม backlog หากเคยติดตั้ง HVP เป็น Windows Service ต้องหยุดและปิดการเริ่มอัตโนมัติของตัวเดิมก่อนใช้ Task

สร้าง Task ใน Task Scheduler แยกชื่อตาม environment แล้วตั้งค่า:

- General: เลือกบัญชีที่เตรียมไว้ และ Run whether user is logged on or not สำหรับการรันเบื้องหลัง ไม่เลือก Do not store password ที่จำกัดเฉพาะ local computer สำหรับบัญชี user ที่ต้องอ่าน SAP ผ่าน UNC ให้เก็บ credentials ผ่านหน้าต่าง Task Scheduler ตามนโยบายบริษัท
- Trigger: At startup เพื่อเปิด process ครั้งเดียวหลังเครื่องเริ่ม ไม่ตั้ง Repeat every 5 minutes เพราะโปรแกรมมี timer อยู่แล้ว
- Action / Program: `D:\PulllistService\Z02JHVPService\Z02JHVPService.exe`
- Action / Add arguments: `--background`
- Action / Start in: `D:\PulllistService\Z02JHVPService`
- Settings: If the task is already running ให้เลือก Do not start a new instance และปิด Stop the task if it runs longer than เพื่อไม่ให้ตัด process ที่ตั้งใจรันต่อเนื่อง

ถ้าใช้ Start when available หรือ Restart on failure ให้ผู้ดูแลตั้งช่วงเวลาตามมาตรฐานของระบบ โดยยังคง Do not start a new instance และไม่เพิ่มรอบสแกนด้วย Scheduler

สั่ง Run ครั้งแรกแล้วตรวจ State log ว่ามี SERVICE START และ SCAN ทันที จากนั้นต้องเห็น scan รอบถัดไปตาม TimerIntervalMinutes ไม่ต้องมีผู้ใช้ login ค้างไว้

Task ที่แสดง Running ยืนยันเพียงว่า process ยังอยู่ ไม่ยืนยันการอ่าน SAP หรือเขียน DB โปรแกรมคืน exit code 2 เมื่อ argument ผิด, 1 เมื่อ host เริ่ม/หยุดแล้วเกิด exception และ 0 เมื่อ host จบตามปกติ ส่วน file/DB error ระหว่าง scan ใช้ retry/SKIP และ log เดิม จึงอาจยังแสดง Running แม้ DB ใช้งานไม่ได้ ยังไม่มีการตรวจ DB ตั้งแต่เริ่ม process หากไม่มีไฟล์รอประมวลผล

## 4. ตรวจใน QA ก่อน Production

เริ่มด้วย input ทดสอบที่ควบคุมได้และ DB ของ QA ตรวจด้วยบัญชี Windows ของ Task จริง:

1. Material ที่มีหลาย PLNT/SLOC: ส่ง HVP แล้วตรวจทุกแถว จากนั้นส่งไฟล์ใหม่ที่ flag ว่าง/NA แล้วตรวจว่าค่าถูกล้าง ใช้ Verify-Hvp.sql ช่วย SELECT ก่อน/หลังได้
2. วางหลายไฟล์ ตรวจลำดับ CreationTimeUtc แล้วชื่อไฟล์ แก้ไฟล์เก่าแล้วตรวจ replay ของไฟล์ที่ตามหลัง
3. Restart โดยใช้ State เดิม ตรวจว่าไฟล์ done เดิมไม่ถูกทำซ้ำ และไฟล์ SAP ยังอยู่โดยเนื้อหาไม่เปลี่ยน
4. ใช้ไฟล์ผิดรูปแบบใน QA ตรวจ retry ข้ามรอบและ SKIP เมื่อครบ MaxFileAttempts รวมการคง attempts หลัง restart อย่าทดสอบ DB outage ด้วยข้อมูล Production
5. ตรวจ Enter และ Ctrl+C เมื่อเปิดมือ แล้วทดสอบ Task, สิทธิ์ UNC และหนึ่งรอบ timer ถัดไปบนเครื่องจริง

completedRecords ใน log ไม่ใช่จำนวนแถว DB ที่แก้ ต้องตรวจ Material จริงใน DB/หน้า Material Master ที่ชี้ environment เดียวกัน ถ้าพบ DB/config error ให้แก้สาเหตุก่อนปล่อยให้ครบ retry limit เพราะไฟล์ version เดิมที่ SKIP แล้วจะไม่กลับมาลองเอง

นำ exe/DLL ชุดที่ผ่าน QA ไป Production เปลี่ยนเฉพาะ config และบัญชีที่จำเป็นของ Production การติดตั้งใหม่ใช้ State ของ Production เอง ไม่ copy QA State ไป ส่วนการอัปเดตต้องรักษา State เดิมของ Production

## 5. เปิดมือเมื่อ Task ใช้ไม่ได้

หยุด Task ที่กำลังรันและ Disable Task ก่อน ตรวจว่า process HVP เดิมจบแล้ว หากเป็นการกด End/kill อาจไม่ได้เรียก StopService และอาจ replay งานที่ DB ทำแล้วแต่ยังไม่เก็บ checkpoint ห้ามลบ State เพื่อแก้ปัญหานี้

เปิด Z02JHVPService.exe จาก folder/config ชุดเดิมโดยไม่ใส่ argument ใช้ input, State และ DB target เดียวกับ Task บัญชีที่เปิดมือยังต้องมีสิทธิ์ครบ ไม่ได้สลับเป็นบัญชีของ Task ให้อัตโนมัติ หน้าต่าง console ต้องเปิดค้างไว้ กด Enter หรือ Ctrl+C เพื่อหยุดหลัง scan ที่กำลังทำจบ

เมื่อจะกลับไปใช้ Task ให้หยุด console และรอ process จบก่อน Enable/Run Task อย่าเปิดสอง instance พร้อมกัน job.lock ป้องกันเฉพาะช่วง scan ที่ใช้ State เดียวกัน ไม่ใช่ process lock ตลอดเวลา และไม่ป้องกัน instance อีกเครื่องที่ใช้ State คนละแห่ง

## 6. อัปเดตและย้อนรุ่น

Disable Task เพื่อกันการเริ่มซ้ำ หยุด instance เดิมแล้วรอ process จบก่อนสำรองหรือทับไฟล์ สำรอง binary, EXE.config และ State ทั้งชุด เก็บ backup แยกจาก input และ folder โปรแกรม

วาง output ใหม่ทั้งชุด แล้วนำค่า connectionStrings/appSettings ของ environment เดิมใส่ใน EXE.config ใหม่โดยรักษา generated redirects ของ build ใหม่ไว้ เก็บ source path และ processed-files.tsv เดิม เมื่อพร้อมจึง Enable/Run Task และตรวจ log/DB ตามขั้นตอน QA

ก่อนย้อนรุ่น ให้เลือก binary/config/State ที่เข้ากันได้ รุ่นที่อ่าน checkpoint ได้เพียง 4 คอลัมน์ใช้ State 6 คอลัมน์ไม่ได้ การคืน State เก่าอาจ replay ไฟล์หลังเวลา backup และการคืน binary ไม่ได้ย้อน UPDATE ที่เกิดใน Oracle แล้ว
