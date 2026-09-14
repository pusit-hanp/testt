# ทดสอบจากชุดส่งมอบนี้

รันจาก root ที่มี START-HERE.md ใช้ Windows, Visual Studio C# compiler และ .NET Framework 4.8 targeting pack ส่วน HvpRegression ต้องใช้ .NET 10 SDK เฉพาะเครื่อง development ไม่ใช่ข้อกำหนดของ service/server

คำสั่งด้านล่างกำหนด SourceDir/BuildDir ชัดเจน เพราะ default ใน test source อ้าง layout ของ workspace พัฒนาเดิม เอกสารผลตรวจวันที่เก่าใน README ของแต่ละ suite เป็นประวัติ ให้ใช้คำสั่งและ VERIFY.md ของชุดนี้เป็นหลัก

Build จาก Developer PowerShell/Developer Command Prompt ที่มี msbuild และ restore NuGet ได้:

```powershell
$sourceDir = (Resolve-Path -LiteralPath '.\Z02JHVPService').Path
$buildDir = Join-Path $sourceDir 'bin\Release'
msbuild "$sourceDir\Z02JHVPService.csproj" /restore /t:Rebuild /p:RestoreLockedMode=true /p:Configuration=Release /p:Platform=AnyCPU /warnaserror
```

ทดสอบไฟล์/State/DB boundary 56 กรณีด้วย DB จำลอง:

```powershell
dotnet run --project .\05-Tests\HvpRegression\HvpRegression.csproj "-p:SourceDir=$sourceDir"
```

ทดสอบ host 14 กรณีและ actual DLL แบบไม่เปิด Oracle connection 6 กรณี:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\05-Tests\HvpHostRegression\Run.ps1 -SourceDir "$sourceDir"
powershell -NoProfile -ExecutionPolicy Bypass -File .\05-Tests\HvpRuntimeSmoke\Run.ps1 -BuildDir "$buildDir"
```

ยืนยันการแก้ P3 ด้วย isolation regression:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\05-Tests\HvpRuntimeSmoke\Verify-Isolation.ps1 -BuildDir "$buildDir"
```

ต้องเห็น 2 passed โดยใช้ runner จริงกับ build ครบก่อน แล้ว build ที่จงใจขาด JSON DLL ต้องถูกปฏิเสธด้วย compiler error จาก missing reference การล้มเหลวที่ตั้งใจไว้นี้จะอยู่ใน incomplete.log ของ fixture ไม่ใช่ความล้มเหลวของ regression

Tests ใช้โฟลเดอร์ชั่วคราว/bin ของ tests และไม่อ่าน SAP หรือเขียน DB จริง แต่ละ smoke invocation สร้าง bin/<run-id> ใหม่ เก็บไว้เพื่อตรวจผล ไม่มี automatic cleanup ในตัว service การ deploy ใช้เฉพาะผล build จาก Z02JHVPService/bin/Release ห้ามนำ test bin, fixture หรือ stubs ไปด้วย
