# Project references และไฟล์ที่ต้องนำไป

อ้างอิง Z02JHVPService.csproj, packages.lock.json และ DLL ใน Release output ของชุด standalone นี้ ใช้ .NET Framework 4.8, C# 7.3, AnyCPU และ Prefer32Bit=false

Project ไม่มี ProjectReference และไม่อ้าง Pulllist.Service, Pulllist.DAL, ชนิด MatMaster จาก DLL เดิม หรือ DLL ของเว็บไซต์ การอ่าน connection และการส่ง UPDATE อยู่ใน HvpService.cs โดยตรง ใช้ PackageReference แทน packages.config เดิม

## NuGet ที่ restore

มี 13 packages ตาม lock file เป็น direct 4 และ transitive 9 รายการ Version ในตารางเป็น package version ไม่ใช่ assembly version ของ DLL

| Package | Version | ประเภท | DLL ที่ deploy |
|---|---|---|---|
| Dapper | 1.50.5 | Direct | Dapper.dll |
| Oracle.ManagedDataAccess | 19.32.0 | Direct | Oracle.ManagedDataAccess.dll |
| System.Formats.Asn1 | 10.0.12 | Direct | System.Formats.Asn1.dll |
| System.Text.Json | 10.0.12 | Direct | System.Text.Json.dll |
| Microsoft.Bcl.AsyncInterfaces | 10.0.12 | Transitive | Microsoft.Bcl.AsyncInterfaces.dll |
| System.Buffers | 4.6.1 | Transitive | System.Buffers.dll |
| System.IO.Pipelines | 10.0.12 | Transitive | System.IO.Pipelines.dll |
| System.Memory | 4.6.3 | Transitive | System.Memory.dll |
| System.Numerics.Vectors | 4.6.1 | Transitive | System.Numerics.Vectors.dll |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | Transitive | System.Runtime.CompilerServices.Unsafe.dll |
| System.Text.Encodings.Web | 10.0.12 | Transitive | System.Text.Encodings.Web.dll |
| System.Threading.Tasks.Extensions | 4.6.3 | Transitive | System.Threading.Tasks.Extensions.dll |
| System.ValueTuple | 4.6.2 | Transitive | ใช้ของ .NET Framework 4.8 ไม่ copy DLL แยก |

Oracle package ระบุ minimum ของ System.Formats.Asn1 6.0.1 และ System.Text.Json 6.0.10 แต่ project นี้เลือก 10.0.12 โดยตรงตาม lock file ห้ามเปลี่ยนกลับตาม minimum แล้วผสม DLL กับ output ชุดนี้ ถ้าจะเปลี่ยน package ต้อง restore/build และตรวจ runtime ใหม่ทั้งชุด

คง Dapper เดิมเพื่อลดการเปลี่ยน DB execution ส่วน Microsoft packages ใช้รุ่นใหม่ที่ยังรองรับ net48 แทนสาย JSON 6 ที่ deprecated รายละเอียดจากผู้เผยแพร่อยู่ที่ [Oracle.ManagedDataAccess 19.32.0](https://www.nuget.org/packages/Oracle.ManagedDataAccess/19.32.0), [System.Text.Json 6.0.11](https://www.nuget.org/packages/System.Text.Json/6.0.11), [System.Text.Json 10.0.12](https://www.nuget.org/packages/System.Text.Json/10.0.12) และ [System.Formats.Asn1 10.0.12](https://www.nuget.org/packages/System.Formats.Asn1/10.0.12)

มี dependency DLL ที่ deploy จริง 12 ไฟล์ ไม่รวม Z02JHVPService.exe นำ EXE และ EXE.config จาก build ไปด้วยกันทั้งหมด ไม่คัดเลือก DLL จากการเห็นหรือไม่เห็น using ใน source และไม่เอา DLL ของ tests ไปปะปน

## Framework references

Project อ้าง System, System.Core, System.Configuration, System.Data, System.ServiceProcess และ System.ValueTuple จาก .NET Framework 4.8 โดย System.ValueTuple ตั้ง SpecificVersion=true เพื่อใช้ของ framework และไม่สร้าง redirect ไปยัง DLL ที่ไม่มีใน output

ไฟล์ C# ที่ compile มี 6 ไฟล์: Program.cs, HvpWinService.cs, HvpFileProcessor.cs, HvpService.cs, HvpStateStore.cs และ HvpLog.cs ใช้ csproj ที่ให้มาได้เลย ไม่ต้องเพิ่ม Compile item ด้วยมืออีก

## Config และผลตรวจ

App.config เป็น source template ส่วน Z02JHVPService.exe.config ใน output มี binding redirects ที่ MSBuild สร้างจาก dependency ที่ resolve แล้ว ใช้ output config และเปลี่ยนเฉพาะค่าของ environment ห้าม copy source template ทับ หรือเขียน redirects จาก package version ด้วยมือ

packages.lock.json เก็บ version/hash ที่ restore ใช้ RestoreLockedMode ตาม DEPLOY.md เมื่อ build ชุดเดิม การ restore/build ได้ไม่ใช่หลักฐานว่า Oracle login/UPDATE หรือ Task Scheduler ของบริษัทผ่านแล้ว

ผลตรวจวันที่ 2026-09-14: .NET Framework 4.8 locked restore/Rebuild ผ่าน 0 warnings/0 errors, DB/file mock 56 กรณี, host 14 กรณี และ runtime smoke ที่ใช้ DLL จริง 6 กรณีผ่าน NuGet audit ของ 13 packages ไม่พบ vulnerability ที่ feed รายงานในรอบตรวจนั้น ยังไม่ได้ทดสอบ SAP/Oracle ของบริษัทจริง ต้องตรวจการทำงานบนเครื่อง QA ก่อนนำขึ้น Production
