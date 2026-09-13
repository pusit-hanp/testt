# HVP เฉพาะไฟล์ที่เปลี่ยน

ใช้เมื่อเครื่องบริษัทลง HVP-20260912-final แล้ว ชุดนี้มีเฉพาะ 4 ไฟล์ที่ใหม่หรือเปลี่ยน ไม่ใช่ source service ครบชุด หากใช้รุ่นเก่ากว่านั้นหรือไม่แน่ใจ ให้ใช้ HVP-20260913-service-source.zip

Source version: HVP-20260912-longrun. จัดชุดไฟล์วันที่ 2026-09-13 โดยไม่ได้เปลี่ยน logic เพิ่มจากรุ่น longrun

## ไฟล์ที่เปลี่ยนจาก HVP-20260912-final

| ไฟล์ | นำไปใช้อย่างไร |
|---|---|
| Z02JHVPService/HvpFileProcessor.cs | แทนไฟล์เดิม ลดการ sort และใช้ State cache/append พร้อม scan metrics |
| Z02JHVPService/HvpLog.cs | แทนไฟล์เดิม เพิ่มการเก็บ log ตามจำนวนวันที่ตั้งไว้ |
| Z02JHVPService/HvpStateStore.cs | ไฟล์ใหม่ เพิ่มเข้า project และ Build Action = Compile |
| Z02JHVPService/App.config | เป็น template ให้ merge เฉพาะ setting ที่ต้องใช้ ไม่ทับ config บริษัททั้งไฟล์ |

HvpService.cs, HvpWinService.cs, Program.cs และ packages.config ไม่เปลี่ยนจากรุ่น final ไม่เพิ่ม NuGet package หรือ reference ใหม่สำหรับการแก้ครั้งนี้

## ขั้นตอนนำไปวาง

1. สำรอง source/config เดิม บน server ให้ Stop service ก่อนเปลี่ยน build output และสำรอง State ของ environment นั้น เก็บ processed-files.tsv เดิมไว้ ไม่ลบหรือคัดลอกจาก QA ไป Production
2. วางไฟล์ C# ใน project Z02JHVPService เดิมบนเครื่อง development สำหรับ .csproj แบบเก่า เพิ่มไฟล์ใหม่ผ่าน Add Existing Item หรือเพิ่ม Compile item ด้านล่างเพียงครั้งเดียว

```xml
<Compile Include="HvpStateStore.cs" />
```

3. เพิ่มหรือแก้ setting นี้ใน appSettings เดิมของ App.config โดยไม่เพิ่ม key ซ้ำ

```xml
<add key="LogRetentionDays" value="30" />
```

   30 เก็บ log ตามวันในชื่อไฟล์และลบเฉพาะ daily log ของ service ที่เก่ากว่าเกณฑ์ ตั้ง 0 เพื่อปิด cleanup ไม่ลบ checkpoint หรือไฟล์ SAP คง TimerIntervalMinutes=5 และ MaxFileAttempts=3 ตามที่ตกลง

   คง HvpFilePath, HvpStatePath, DecryptCTN/Encs_T, binding redirects และ patch/patch.xml ของ environment ที่ใช้งานจริง App.config ใน ZIP ตั้ง path ว่างเป็น template อย่านำไปทับ config ที่รันอยู่

4. Build Release ด้วย solution/.NET Framework 4.8 และ legacy DLL/Oracle provider เดิมของบริษัท ให้ Platform ตรงกับชุดที่ใช้งาน ทดสอบ QA ก่อน แล้วนำ Release output ที่ทดสอบผ่านไปลง server ตามคู่มือ DEPLOY-QA-PRODUCTION.md ไม่ต้องเพิ่ม project ของเว็บไซต์หรือ MatMaster
5. ตรวจ Z02JHVPService.exe.config หลัง deploy ว่าคง setting ของ environment นั้น Start service แล้วดู SCAN END รอบที่ไม่มีงานและ State ไม่เปลี่ยนควรเห็น stateReloaded=False, sortedFiles=0, stateBytesWritten=0; รอบแรกหลัง Start จะโหลด State ใหม่

ชุดนี้เป็น source สำหรับ build ไม่มี EXE/DLL ที่พร้อมติดตั้ง คู่มือ deploy ครบอยู่ใน DEPLOY-QA-PRODUCTION.md

## พฤติกรรมที่คงเดิม

- สแกนทันทีเมื่อ Start แล้วทุก 5 นาที ถ้า State ว่างจะทำ backlog ที่ตรงชื่อทั้งหมด รวมไฟล์เก่า ไม่ใช่เฉพาะ 5 นาทีล่าสุด
- Retry รวม 3 ครั้งแล้ว skip ปรับ MaxFileAttempts ได้ และเก็บ attempts/status ต่อหลัง restart
- จับ Material อย่างเดียวแล้ว update ทุกแถวที่ match เป็น HVP หรือว่างตามข้อมูลไฟล์ ไม่แก้ MatMaster/UI/DAO
- ไม่สร้าง DB/table สำหรับ service และไม่ย้าย/archive/delete ไฟล์ SAP
- เก็บ State history ของแต่ละ path เพื่อกันทำไฟล์เก่าซ้ำ ส่วนประวัติสถานะซ้ำจะรวมเป็นระยะ

## หลักฐานการตรวจ

วันที่จัดชุดนี้ตรวจ SHA-256 แล้ว source ทั้ง 8 ไฟล์ตรงกับรุ่น longrun ที่เคยทดสอบ 48 regression cases ผ่านด้วย DB stub เมื่อ 2026-09-12 ไม่มีการเปลี่ยน code เพิ่มและไม่ได้รัน tests ซ้ำสำหรับการจัด ZIP

การ compile เดิมใช้ Windows .NET Framework compiler กับ test-only legacy stubs จึงยังไม่ใช่การ build solution บริษัทจริง ไม่ได้ทดสอบ Oracle, SAP share หรือ Windows Service บน server บริษัท ต้องตรวจบน QA ตามคู่มือ

FILE-HASHES.json เก็บ SHA-256 ของทุกไฟล์ในชุด ยกเว้นตัว manifest เอง COMMIT-MESSAGE.txt คือข้อความสำหรับ commit source change นี้ ไม่ได้สร้าง git commit ให้ใน workspace