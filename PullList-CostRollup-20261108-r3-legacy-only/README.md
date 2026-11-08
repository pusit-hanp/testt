# Cost Rollup r3: ติดตั้งจาก legacy เดิม

ชุดวันที่ 2026-11-08 สำหรับ project PS_TpostFromSlip ต้นฉบับที่ให้มา ซึ่งยังไม่เคยติดตั้ง Cost Rollup ไม่ต้องลง r2 ก่อน ไม่มี UI change และไม่มี production EXE ในชุดนี้ ต้อง build ด้วย solution และ DLL ของบริษัท

## ไฟล์ที่ต้องใช้

| ไฟล์ในชุด | วิธีใช้ |
|---|---|
| source/PS_TpostFromSlip/Form1.cs | แทน Form1.cs ของ legacy ที่ตรงกับต้นฉบับ หลังสำรองและเทียบไฟล์ |
| source/PS_TpostFromSlip/Form1.CostRollup.cs | เพิ่มใน project |
| source/PS_TpostFromSlip/CostRollup/CostRollupGuard.cs | เพิ่มใน folder CostRollup |
| source/PS_TpostFromSlip/CostRollup/MyRepositoryCostRollupStore.cs | เพิ่มใน folder CostRollup |
| project/CompileItems.fragment.xml | นำ ItemGroup ไปเพิ่มใน PS_TpostFromSlip.csproj เดิม |
| config/appSettings.fragment.config | นำสอง key ไปเพิ่มใน App.config เดิม |
| database/00-preflight.sql | ตรวจ connection, schema และตารางที่มีอยู่ เป็น read-only |
| database/01-create-new.sql | สร้าง PULLLIST_COSTROLLUP ใหม่ครบ 7 คอลัมน์ |
| database/04-validate.sql | ตรวจ schema/constraints/data หลังสร้าง เป็น read-only |
| database/_assert-material-target.sql | ไฟล์ประกอบที่ 01 เรียกเอง ต้องเก็บใน folder เดียวกัน |

MANIFEST.sha256.json ใช้ตรวจว่าไฟล์ครบและไม่เปลี่ยนจากชุดส่งมอบ ไม่ต้องนำไปวางข้าง EXE

ไม่มี source เดิมที่ไม่เปลี่ยน, r2 upgrade, tests, ภาพ reference หรือไฟล์ credential อยู่ใน ZIP นี้ ไม่ต้องเปลี่ยน Form1.Designer.cs, Program.cs, BizSAP.cs, models, packages.config หรือ Settings.settings/Settings.Designer.cs

## 1. สำรองและเตรียม source

1. แตก ZIP เช่น D:\Work\CostRollup-r3 ไว้คนละ folder กับ project บริษัท
2. สำรอง solution/source และ deployment/config เดิม เก็บ version ที่จะคืนได้
3. ใช้ working copy ของ solution บริษัทที่มี MyRepository, GlobalBiziness, SAP DLL, resources และ config ครบ เปิดด้วย Visual Studio/MSBuild ที่รองรับ .NET Framework 4.8 และ C# 7.3
4. เทียบ Form1.cs ของบริษัทกับต้นฉบับก่อนคัดลอก หากมีการแก้เพิ่มนอกต้นฉบับ ให้ merge การเปลี่ยนแปลงแทนการทับไฟล์ทั้งไฟล์

## 2. ใส่ไฟล์ที่เปลี่ยนใน project

คัดลอกไฟล์ทั้ง 4 ใต้ source/PS_TpostFromSlip ไปยัง project PS_TpostFromSlip โดยรักษา folder CostRollup ไว้ Form1.cs เป็นไฟล์เดิมที่แก้ ส่วนอีก 3 ไฟล์เป็นไฟล์ใหม่

แก้ PS_TpostFromSlip.csproj เดิม โดยนำเฉพาะ ItemGroup จาก project/CompileItems.fragment.xml ไปใส่ก่อน </Project> ไม่คัดลอก XML declaration เข้าไปกลางไฟล์ ถ้าใช้ Visual Studio Add Existing Item เพิ่ม 3 ไฟล์ใหม่แล้ว ให้ตรวจว่ามี Compile entry อย่างละหนึ่งรายการ ไม่เพิ่มซ้ำ ไม่ต้องเพิ่ม Form1.cs ซ้ำ

คง reference/path/platform ของบริษัททั้งหมด และตรวจว่ามี reference System.Configuration อยู่แล้ว ไม่มี NuGet/dependency ใหม่สำหรับ Cost Rollup

แก้ App.config เดิม เพิ่มสอง key ใน configuration/appSettings:

```xml
<add key="Check_CostRollUp" value="false" />
<add key="Check_CostRollUp_Msg" value="has status Mth End" />
```

ถ้ายังไม่มี appSettings ให้ใช้ section จาก config/appSettings.fragment.config ถ้ามีแล้วให้เพิ่มเฉพาะ key ที่ขาด ไม่สร้าง section/key ซ้ำ หากใช้ config แยกไฟล์ให้แก้ที่แหล่ง config จริงของบริษัท

อย่าแทน App.config ทั้งไฟล์ด้วย fragment และอย่าเปลี่ยน connectionStrings, userSettings, credential, MoveMentType, CurrentStatus, InprocStatus, FilterMaterial, API/RFC หรือ AutoRun ของ environment เดิม ค่าใหม่อ่านจาก appSettings จึงไม่ต้องแก้ generated Settings

## 3. รัน SQL สำหรับตารางใหม่

เส้นทางนี้ใช้เมื่อยังไม่มี PULLLIST_COSTROLLUP เท่านั้น การไม่เคยลง r2 ไม่ได้ยืนยันว่า DBA ยังไม่เคยสร้างตาราง ให้ตรวจ 00 ก่อนเสมอ หากพบ table/view/synonym ชื่อนี้ ให้หยุดและตรวจ schema ที่มีอยู่ ไม่ drop/recreate และไม่รัน 01 ทับ

เริ่มใน DEV/TEST/UAT ที่ DBA ยืนยัน เปิด SQL*Plus/SQLcl session ใหม่โดยใช้ MATERIAL และ CURRENT_SCHEMA = MATERIAL อย่าใช้ session ที่มี transaction อื่นค้าง หยุด job/process ที่เกี่ยวข้องระหว่างติดตั้ง และคง Check_CostRollUp=false จน DB และ UAT ผ่าน

จาก folder หลักของ ZIP รันทีละไฟล์:

```sql
@database/00-preflight.sql
```

ตรวจ DB_NAME, SERVICE_NAME, SESSION_USER, CURRENT_SCHEMA และ Oracle server time ให้ถูก environment ตรวจว่าไม่พบ object/synonym ของ PULLLIST_COSTROLLUP แล้วจึงรัน:

```sql
@database/01-create-new.sql
@database/04-validate.sql
```

01 เรียก _assert-material-target.sql อัตโนมัติและถาม DB_NAME, SERVICE_NAME, stage (DEV/TEST/UAT) และ APPLY_COSTROLLUP_DDL ให้ใช้ค่าที่ DBA ยืนยัน ไม่เดาจากชื่อ connection และไม่ใช้ production connection โดยระบุ stage เป็น TEST

04 ต้องจบด้วย PASS: schema and stored-data checks และไม่มี ORA-/SP2-/runner error ตรวจ result sets ที่ระบุว่าต้องว่างด้วย หาก DDL ล้มเหลว ให้ inspect ก่อนรันซ้ำ เพราะ Oracle DDL อาจ commit ไปแล้วบางคำสั่ง ROLLBACK ไม่คืน CREATE/ALTER

ตารางใหม่มี CUST_CODE, COSTROLLUP_STATUS, MISLIP_NO_REF, LAST_UPDATE, CHANGE_BY, FROM_DATE และ TO_DATE พร้อม primary key และ checks วันที่ต้องว่างทั้งคู่ หรือมีทั้งคู่และ FROM_DATE <= TO_DATE เริ่มด้วยตารางว่างได้ ไม่ต้อง seed customer หรือรัน UPDATE reset เอง service จะทำตามกติกาตอนเริ่มรอบ

SQL DDL ในชุดนี้รับเฉพาะ DEV/TEST/UAT และปฏิเสธ PROD การใช้จริงใน PROD ต้องผ่าน UAT และขั้นตอน deployment ของบริษัทก่อน ยังไม่มีการรัน SQL นี้กับ Oracle บริษัทจากเครื่องที่จัดชุด

## 4. Build และทดสอบ

Build solution เดิมด้วย dependency ของบริษัทและ configuration/platform ที่ใช้งานได้อยู่แล้ว หากยืนยันว่า instance ใช้ Release/x64 ให้เปิด Developer PowerShell ใน project แล้วรัน:

```powershell
MSBuild.exe .\PS_TpostFromSlip.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64
```

Reference ที่ได้รับมี SAP NCo AMD64 และ rscp4n x86 ให้ตรวจ DLL ที่ใช้งานจริงกับ build เดิม ไม่เปลี่ยน platform หรือ DLL โดยอาศัยตัวอย่างคำสั่งอย่างเดียว โปรแกรมเดิมเป็น WinForms WinExe ที่มี AutoRun ให้ใช้ scheduler/วิธีเรียกเดิม ไม่ติดตั้งด้วย sc.exe

ก่อนเปิด feature ให้ทดสอบ MyRepository จริงว่ารองรับ nullable DATE, DateTime/DBNull.Value, parameter ที่ใช้ซ้ำ, การ map RUN_STARTED_AT และการ commit ของ UPDATE/MERGE โดยตรวจข้อมูลจาก Oracle อีก session ด้วย local tests ไม่ได้พิสูจน์พฤติกรรม DLL บริษัท

บน TEST/UAT ตรวจอย่างน้อย:

- false: flow เดิมทำงาน และไม่เข้าตาราง Cost Rollup
- true: เริ่มรอบใหม่ reset SERVICE/Y เป็น N; admin/N ล้าง EN; admin/Y ที่ไม่มีช่วงเวลาหรืออยู่ในช่วงยัง hold; future schedule ยังส่งได้; expired เปลี่ยน N และล้าง EN
- เวลาใช้ Oracle SYSDATE ตอนเริ่ม ProcessRun ครั้งเดียว รวมขอบ From/To การแก้ admin หรือเวลาที่ข้ามช่วงระหว่างรันมีผลรอบหน้า
- SAP error ที่มี has status Mth End: เก็บ POST_ERROR และข้าม customer เดียวกันทันทีในรอบนี้ เช่น MU-OU กับ PK-OU; MU-Z0A ต้องได้ customer 0A ข้อมูลใหม่/schedule ของ admin ต้องไม่ถูกทับ
- error อื่น/customer อื่นยังทำงานตาม flow เดิม; SAP success, SMART, ConfirmMovement และ POST_WIP ยังทำงาน กด Process ซ้ำระหว่างรอ SAP ต้องไม่เปิดรอบซ้อน
- ถ้าอ่าน/เขียน Cost Rollup DB ไม่ได้ขณะเปิด feature ต้องหยุดรอบพร้อม log ไม่ปล่อย slip ต่อโดยข้ามการตรวจ

ค่า From/To ที่ admin บันทึกต้องใช้ฐานเวลาเดียวกับ Oracle SYSDATE ตรวจ timezone กับ DBA และจัดให้ enabled instances ที่ใช้ตารางนี้ร่วมกันไม่รันซ้อน รวมต่าง movement และต่างเครื่อง รุ่นนี้ไม่มี distributed lock

## 5. วางบนเครื่องที่รันโปรแกรม

หลัง build และ TEST/UAT ผ่าน ให้หยุด job/process เป้าหมาย สำรอง deployment folder แล้ววาง PS_TpostFromSlip.exe ที่ build ใหม่ตามวิธี deploy เดิม ตรวจ output ว่า DLL อื่นมีการเปลี่ยนจริงหรือไม่ก่อนเลือก deploy เฉพาะไฟล์ ใช้ dependency เดิมที่ตรวจแล้ว ไม่ใช้ไฟล์จาก test harness

เพิ่มสอง key ใน PS_TpostFromSlip.exe.config ข้าง EXE ที่เรียกจริง โดยรักษาค่า environment เดิม การแก้ source App.config อย่างเดียวไม่ได้เปลี่ยน config ของโปรแกรมที่ติดตั้งอยู่ ตั้ง Check_CostRollUp=true เมื่อพร้อมเปิดใช้งาน ตรวจ log, exit code และผลใน DB หลังรันหนึ่งรอบ

ไฟล์ .cs, .csproj และ fragment ใช้ตอน build ไม่ต้องวางใน deployment folder ไม่มีการเปลี่ยนหน้า PullList/UI

## คืนรุ่นเดิม

หยุด process แล้วตั้ง Check_CostRollUp=false เพื่อปิดการตรวจ Cost Rollup หรือคืน EXE/config/dependency จาก backup เดิม หากคืน source legacy ต้องคืน Form1.cs, .csproj และ App.config พร้อมนำ 3 ไฟล์ใหม่ออกจาก project คง PULLLIST_COSTROLLUP และข้อมูลไว้ ไม่ต้อง drop table เพื่อคืน application

## ขอบเขตการตรวจชุดนี้

Source 4 ไฟล์ตรงกับ r3 ที่ผ่าน local core/integration และ final review แล้ว SQL 01/04/_assert ตรงกับ r3; 00 เปลี่ยนเฉพาะข้อความ PROMPT ท้ายไฟล์ให้ตรงกับชุด legacy-only ไม่มี logic SQL เปลี่ยน ชุดเต็ม r3 มี local application/tool tests ผ่าน 155, SQL static/model ผ่าน 148 และ junction test skipped 1 กรณี นี่ไม่ใช่ผล build ด้วย DLL บริษัทหรือผล Oracle/SAP UAT

ชุดย่อนี้ตรวจรายการไฟล์, hashes, XML fragments และ ZIP ที่แตกใหม่อีกครั้ง ไม่ได้เพิ่มหรือเปลี่ยน service code
