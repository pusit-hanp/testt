# ทดสอบ source ชุดนี้

รันจาก root ที่มี START-HERE.md ใช้ Windows และ Visual Studio ที่มี .NET Framework 4.8 targeting pack สำหรับ production ส่วน HvpRegression ใช้ .NET 10 SDK เฉพาะ development ไม่มี test package เพิ่มและไม่ใช่ runtime ที่ต้องติดตั้งบน server

Build จาก Developer PowerShell:

```powershell
$sourceDir = (Resolve-Path -LiteralPath '.\Z02JHVPService').Path
$buildDir = Join-Path $sourceDir 'bin\Release'
msbuild "$sourceDir\Z02JHVPService.csproj" /restore /t:Rebuild /p:RestoreLockedMode=true /p:Configuration=Release /p:Platform=AnyCPU /warnaserror
```

ต้องมี NuGet feed/cache ที่มี package ตาม lock file ใช้ output EXE.config ซึ่งมี generated binding redirects ห้ามนำ App.config ไปทับ output config

File/State/DB boundary regression 101 กรณี:

```powershell
dotnet run --project .\05-Tests\HvpRegression\HvpRegression.csproj "-p:SourceDir=$sourceDir"
```

Host 14 กรณี และ actual DLL smoke 6 กรณีแบบไม่เปิด DB connection:

```powershell
& .\05-Tests\HvpHostRegression\Run.ps1 -SourceDir "$sourceDir"
& .\05-Tests\HvpRuntimeSmoke\Run.ps1 -BuildDir "$buildDir"
```

ใช้การรัน script ที่นโยบายของเครื่องอนุญาต ไม่ต้องเปลี่ยน security policy ของ server เพื่อรัน tests ค่า SourceDir/BuildDir สำคัญเพราะ default ของ harness อ้าง layout ของ workspace พัฒนา

Verify-Isolation.ps1 ที่มากับชุดเดิมเป็น optional check ของ smoke runner ว่าจะไม่หยิบ DLL ค้างจากรอบก่อน ไม่ได้แก้ runner ในรุ่นนี้

Tests ใช้ temporary files กับ Oracle/Dapper stubs หรือ DLL offline ไม่อ่าน SAP และไม่เขียน Oracle จริง ผล transaction/SQL mapping ใน boundary tests ต้องยืนยันกับ Oracle QA อีกครั้ง ชื่อ header มาจากภาพ TXT/mapping ของผู้ใช้ ยังไม่มีไฟล์ต้นฉบับสำหรับตรวจ byte/encoding จริง

บน QA ให้ทดสอบ Material เดิมหลาย PLNT/SLOC, Material ใหม่ทั้งสาม MTYP, HVP/NULL, SLOC ว่าง/หาย, Material/Plant ขาด, Material ซ้ำในไฟล์ และไฟล์ที่แถวหลังผิดต้อง rollback แถวก่อนหน้า ตรวจด้วย Verify-Hvp.sql และขั้นตอน QA ใน DEPLOY.md

เก็บ State เดิมตอน upgrade ไฟล์ done เดิมจะไม่กลายเป็นงาน INSERT อัตโนมัติ ทดสอบด้วยไฟล์ใหม่ใน input ของ QA
