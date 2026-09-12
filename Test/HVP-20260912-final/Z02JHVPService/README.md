# Z02JHVPService ฉบับแก้

ตามขอบเขตล่าสุดของผู้ใช้ เปลี่ยนเฉพาะ project `Z02JHVPService` ไม่ต้องนำ MatMaster Controller/View/Service/DAO ไปแทนไฟล์เดิม

แก้จาก source ที่ถอดจากภาพ คง .NET Framework 4.8, BaseService, SessionFactory และ Dapper เดิม ไม่มี package ใหม่ โค้ดนี้ยังไม่ได้ทดสอบกับ Oracle/Windows Service จริง

นำ `HvpFileProcessor.cs`, `HvpService.cs`, `HvpWinService.cs`, `Program.cs` ไปแทนใน project HVP เดิม และเพิ่ม `HvpLog.cs` เป็น Compile item ถ้าเป็น .csproj แบบเก่า เว็บไซต์ Controller/View/MatMasterService ไม่ต้องเปลี่ยนเพื่อแสดง flag จาก service นี้

App.config เป็น template: merge เข้าของเดิม อย่าทับ settings ที่ใช้งานอยู่ ต้องกำหนด HvpFilePath เป็น path จริงก่อนรัน ช่องว่างตั้งไว้โดยเจตนาเพราะอ่าน path ในภาพไม่ครบ

1. ตั้ง `HvpFilePath` เป็น UNC เช่น `\\server\share\SAP` โดยเปลี่ยนเป็นค่าจริง ไม่ใช้ mapped R: ของผู้ใช้ หากเป็น local disk ที่ service อ่านได้ใช้ absolute local path ได้
2. คง `TimerIntervalMinutes=5`; ไม่มี FileLookbackMinutes แล้ว `HvpStatePath` ว่างจะใช้ `%ProgramData%\Z02JHVPService` ให้ service account มี Modify ที่นั่น ส่วน SAP folder ใช้ Read เท่านั้น
3. วาง config ที่อนุมัติของ environment นั้นที่ `<exe directory>\patch\patch.xml` และใส่ appSetting `DecryptCTN` หรือ `Encs_T` ที่ BaseService ต้องใช้ใน `Z02JHVPService.exe.config` ไม่มีการสร้างคีย์/connection string ให้แทนค่าจริง อย่าใช้ `patch.snapshot.xml` เพราะเป็นภาพที่ปิดค่าและไม่ครบ
4. Build กับ DLL และ Oracle provider version/bitness เดิมของ Pulllist ตรวจว่า exe/config/patch ที่ Windows Service ชี้อยู่เป็น output ชุดใหม่จริง
5. รันใน TEST ก่อน ตรวจ log `hvp-yyyyMMdd.log` จะเห็น SERVICE START, SCAN, START, DB COMMANDS, DONE หรือ FAILED การนับ completedRecords เป็นจำนวนรายการที่ส่งคำสั่ง ไม่ใช่ affected rows

เมื่อ start จะสแกนทันทีแล้วทุก 5 นาที ครั้งแรกทำ backlog ทั้งหมด; รอบต่อไปทำไฟล์ใหม่/ไฟล์ที่เวลา-ขนาดเปลี่ยนตาม CreationTimeUtc ถ้าเก่าเปลี่ยนจะ replay ไฟล์ที่ใหม่กว่าตามลำดับ ไม่ย้าย/archive/delete ไฟล์ SAP

ถ้า metadata เปลี่ยนหลังจัดคิวแต่ก่อนอ่านไฟล์ จะบันทึก `DEFER` และเลื่อนไฟล์นั้นกับไฟล์ที่ตามหลังไปรอบถัดไปเพื่อเรียงใหม่ ไม่เพิ่มจำนวน retry และยังเก็บสถานะ pending ไว้หลัง restart

ตั้ง `<add key="MaxFileAttempts" value="3" />` ใน `Z02JHVPService.exe.config` แล้ว restart service ค่า 3 รวมครั้งแรกกับ retry อีก 2 ครั้ง ระหว่างยังไม่ครบจะรอรอบถัดไปก่อนทำไฟล์หลังจากนั้น หาก fail ครั้งที่ 3 จะบันทึก SKIP แล้วทำไฟล์ถัดไปในรอบเดียวกัน จำนวนครั้งและสถานะเก็บใน checkpoint จึงอยู่ต่อหลัง restart ค่า config ที่ขาด/ไม่ใช่จำนวนเต็มบวกใช้ 3

ไฟล์ที่ SKIP แล้วจะไม่ลองซ้ำตราบใดที่ metadata เดิม แม้เพิ่ม MaxFileAttempts; เมื่อ CreationTimeUtc, LastWriteTimeUtc หรือ Length เปลี่ยนจะเริ่มนับใหม่ ข้อผิดพลาดระหว่างทำไฟล์รวมถึง DB error นับด้วย แต่ folder/lock/checkpoint error ที่เกิดนอกการทำไฟล์จะหยุดรอบนั้นโดยไม่กินจำนวนครั้งของไฟล์

อ่าน checkpoint เดิม 4 คอลัมน์ได้ และบันทึกเป็น 6 คอลัมน์เพิ่ม attempts/status ควรสำรอง checkpoint ก่อนอัปเกรดหากต้อง rollback เพราะโปรแกรมรุ่นเก่าอ่านรูปแบบใหม่ไม่ได้

หลัง DB เปลี่ยน กด Search บน Material Master เพื่อโหลด HVP Flag ใหม่ ไม่มี UI polling เพิ่ม

## ตรวจใน TEST

- ใช้ material ทดสอบที่มีหลาย PLNT/SLOC ตรวจด้วย `Verify-Hvp.sql` ก่อนเริ่ม และยืนยันว่าเป็น DB เดียวกับ service
- ส่ง TXT tab-separated ที่มี header Material และ Special Control Flag; ให้ material เดียวมีหลายแถวใน DB แล้วตรวจว่าเปลี่ยนครบทั้งหมดเป็น HVP
- ส่งไฟล์สร้างใหม่ที่ material เดิมมี flag ว่าง/NA ตรวจว่าทุกแถวเป็น NULL/ช่องว่าง กด Search แล้วดู UI
- วางหลายไฟล์ที่ CreationTime ต่างกัน ค่าสุดท้ายต้องเป็นของไฟล์ที่สร้างใหม่กว่า ถ้าเวลาเท่ากันดูชื่อไฟล์ใน log
- ใช้ไฟล์ CreationTime เก่ากว่า 5 นาทีแต่ LastWriteTime ใหม่ ตรวจว่ารอบถัดไปอ่านได้; ถ้าแก้ไฟล์เก่าหลังไฟล์ใหม่เคยสำเร็จ ให้ตรวจ replay ของไฟล์ที่ตามหลังด้วย
- Restart แล้วไฟล์สำเร็จเดิมไม่ถูกทำซ้ำ ตรวจ checkpoint ยังอยู่ และไฟล์ SAP byte-for-byte เท่าเดิม
- ทดลองไฟล์ header ผิด/แถวสั้น/ถูกเปิดเขียนอยู่ และ DB unavailable ต้องเห็น FAILED, retry ตามรอบ แล้ว SKIP เมื่อครบ MaxFileAttempts ก่อนทำไฟล์ถัดไป ตรวจว่า restart ไม่ล้างจำนวนครั้ง
- ทดสอบสิทธิ์ด้วย service account จริง ไม่ใช่แค่บัญชีที่ login แล้วเห็น R: และทดสอบ stop/start ขณะมี backlog

`Verify-Hvp.sql` เป็น read-only ยังไม่ได้รันกับ DB ใด

Regression harness รันผ่าน 21 กรณีด้วยไฟล์ชั่วคราวและ DB จำลองบน .NET 10 รวมไฟล์ที่ CreationTime เปลี่ยนให้ใหม่ขึ้น/เก่าลงระหว่างสแกน ยังไม่ใช่ผลทดสอบ Oracle หรือ Windows Service จริง

รายละเอียดหลักฐานและข้อจำกัดอยู่ใน `06-Review-Reports/HVP-DEBUG-REVIEW.md` ของ workspace
