# RoleValidation — คู่มือติดตั้ง

เว็บสำหรับดูสิทธิ์ผู้ใช้ของระบบต่าง ๆ จัดการ role/ผู้รับผิดชอบ และส่งไฟล์ Excel ให้ตรวจสิทธิ์ อ่านภาพรวมการทำงานได้ที่ [เอกสารโปรเจกต์](docs/PROJECT_OVERVIEW.md)

คำสั่งในคู่มือนี้รันจากโฟลเดอร์ที่มี `RoleValidation.slnx` ค่าที่อยู่ใน `<...>` เป็นตัวอย่าง ต้องแทนด้วยค่าของ environment นั้นก่อนรัน

## เตรียมก่อนเริ่ม

- เครื่องพัฒนาใช้ .NET 10 SDK และ Visual Studio รุ่นที่รองรับ .NET 10 หรือใช้ CLI
- Server ใช้ Windows/IIS พร้อม .NET 10 Hosting Bundle ซึ่งรวม ASP.NET Core Module ตรวจ runtime ด้วย `dotnet --list-runtimes` ให้มี `Microsoft.NETCore.App 10.x` และ `Microsoft.AspNetCore.App 10.x`
- เตรียม Company Login URL, passphrase และ encrypted connection strings ของ `Master`, `Material`, `AppSim` จากผู้ดูแลระบบ พร้อมสิทธิ์เข้าถึงฐานข้อมูลจากเครื่องที่จะรัน

| ที่รัน | Config เฉพาะเครื่อง | DataSource | การส่งเมล |
| --- | --- | --- | --- |
| Local / Development | User Secrets | `Hybrid` | `Fake + SafeRedirect` ไม่ส่งเมลจริง |
| QA | `appsettings.QA.json` บน server | `Oracle` | `ApiEmail + SafeRedirect` |
| Production | `appsettings.Production.json` บน server | `Oracle` | `ApiEmail + RoleOwner` |

QA ต้องใช้ `SafeRedirect` และ Production ต้องใช้ `RoleOwner` ตาม validation ในโค้ด ไม่ใช่ตัวเลือกที่สลับกันได้อิสระ ส่วนชื่อ environment `UAT` ยังไม่มี config/กฎ email รองรับในชุดนี้

## 1. รัน Local

เปิด `RoleValidation.slnx` แล้วคลิกขวา `RoleValidation.Web → Manage User Secrets` ใส่ข้อมูลนี้ใน `secrets.json` ใช้ค่าของ DEV/QA ที่ได้รับสิทธิ์ ไม่ใช้ credentials ของ Production

```json
{
  "Authentication": {
    "CompanyLogin": {
      "LoginUrl": "https://<login-host>/<login-path>",
      "PublicOrigin": "https://localhost:57165",
      "SessionLifetimeMinutes": 480
    }
  },
  "Security": {
    "TextEncryption": {
      "Passphrase": "<COMPANY_LOGIN_PASSPHRASE>",
      "EncryptedConfiguration": true
    }
  },
  "ConnectionStrings": {
    "Master": "<ENCRYPTED_MASTER_CONNECTION>",
    "Material": "<ENCRYPTED_MATERIAL_CONNECTION>",
    "AppSim": "<ENCRYPTED_APPSIM_CONNECTION>"
  }
}
```

Connection strings ต้องเข้ารหัสด้วย passphrase/รูปแบบที่ระบบเดิมใช้ ไม่ใช่นำข้อความ connection string ธรรมดามาใส่เมื่อ `EncryptedConfiguration=true` ดูโครงสร้างจาก QA ได้ แต่เก็บค่าจริงไว้นอก Git รวมถึง connection string ที่เข้ารหัสแล้วด้วย

```powershell
dotnet dev-certs https --trust
dotnet restore RoleValidation.slnx
dotnet run --project RoleValidation.Web --launch-profile RoleValidation.Web
```

เปิด `https://localhost:57165` หรือเลือก `RoleValidation.Web` เป็น Startup Project แล้วกด Run ใน Visual Studio หากเปลี่ยน HTTPS port ต้องเปลี่ยน `PublicOrigin` ให้ตรงกันด้วย

Local ใช้ `Hybrid` จึงยังอ่านพนักงาน/ข้อมูลผู้ใช้จาก Oracle แต่ข้อมูลจัดการบางส่วนอยู่ใน memory และหายเมื่อ restart การล็อกอินยังต้องใช้ Company Login และบัญชีที่มีสิทธิ์ในข้อมูล Development

User Secrets ใช้เฉพาะ Development และไม่ได้เข้ารหัสไฟล์ให้ ห้ามย้าย `secrets.json` ไปไว้ใน project หรือ commit secrets ลง `appsettings*.json` ดู [วิธีใช้ User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0)

## 2. เตรียม QA / Production

ฐานข้อมูลใหม่ให้ DBA ตรวจและรัน [schema.sql](database/schema.sql) แล้ว [seed.sql](database/seed.sql) ตาม environment โดย `schema.sql` ใช้สำหรับ clean install เท่านั้น ไม่ใช่ migration และแอปไม่รัน SQL เหล่านี้ให้เอง ถ้ามีฐานข้อมูลอยู่แล้วไม่ต้องรันซ้ำเพียงเพราะ deploy เว็บรุ่นใหม่

ผู้ดูแลคนแรกต้องมีสิทธิ์ `Local_IT_Admin` ตรวจข้อมูลเริ่มต้นใน seed ก่อน หากต้องเพิ่มบัญชี ให้ผู้รับผิดชอบใช้ [bootstrap_local_it_admin.sql](database/bootstrap_local_it_admin.sql) ตามขั้นตอนในไฟล์

Publish จากเครื่อง build โดยเลือกคำสั่งของ environment ที่จะติดตั้ง:

```powershell
# QA
dotnet publish RoleValidation.Web/RoleValidation.Web.csproj -c Release -p:EnvironmentName=QA -o artifacts/publish/QA

# Production
dotnet publish RoleValidation.Web/RoleValidation.Web.csproj -c Release -p:EnvironmentName=Production -o artifacts/publish/Production
```

`EnvironmentName` ทำให้ publish ใส่ชื่อ environment ลง `web.config` เพื่อเลือก `appsettings.QA.json` หรือ `appsettings.Production.json` ส่วนค่า URL, connection strings และ token ยังเก็บใน appsettings บน server ตามเดิม ดู [การสร้าง web.config ตอน publish](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/transform-webconfig?view=aspnetcore-10.0#environment)

## 3. ตั้ง IIS ให้ใช้ HTTPS

เหตุการณ์ที่พบในการ deploy ครั้งก่อนคือไปตั้ง HTTP ขณะที่เครือข่ายบริษัทบล็อก HTTP ต้องเปิดใช้ HTTPS ที่ IIS จริงด้วย การใส่ `https://` ใน appsettings อย่างเดียวไม่ได้สร้าง HTTPS binding ให้

1. ที่เว็บไซต์แม่ใน IIS เปิด `Bindings → Add/Edit → https` ใช้ port `443`, hostname และ certificate ที่ตรงกับชื่อ server ถ้าใช้หลาย hostname บน IP เดียวให้ตั้ง SNI ให้เหมาะกับเว็บไซต์
2. สร้าง App Pool แยกสำหรับ RoleValidation ตั้ง `.NET CLR Version = No Managed Code` และให้ architecture ตรงกับ runtime/publish ถ้าใช้ x64 ให้ `Enable 32-Bit Applications = False`
3. เพิ่ม application ชื่อ `RoleValidation` ใต้เว็บไซต์แม่ หรือใช้ `Convert to Application` แล้วผูก App Pool นี้ ตั้ง Physical Path ไปยังโฟลเดอร์รับไฟล์ publish
4. Copy **เนื้อหาจาก output ของ publish** ไปยัง Physical Path ต้องมี `web.config`, `RoleValidation.Web.dll`, `RoleValidation.Web.deps.json`, `RoleValidation.Web.runtimeconfig.json` และไฟล์ประกอบครบ ไม่ใช่ copy เฉพาะ source project
5. ให้ Identity ของ App Pool อ่าน/รันไฟล์เว็บได้ แล้วเติม config ตามหัวข้อถัดไปก่อน Start App Pool และเปิด `https://<server>/RoleValidation` โดยตรง

ขั้นตอนอ้างอิง: [ASP.NET Core บน IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)

## 4. เติม appsettings บน server

แก้เฉพาะไฟล์ environment ที่ใช้งานอยู่บน server โดย merge `Authentication`, `Security` และ `ConnectionStrings` จากตัวอย่าง Local ลงใน JSON เดิม ใส่ค่าของ QA หรือ Production ให้ถูกชุด และเปลี่ยน `PublicOrigin` เป็น `https://<server>/RoleValidation`

คง `CallbackPath` ใน `appsettings.json` เป็น `/authentication/callback` แอปจะต่อเป็น `https://<server>/RoleValidation/authentication/callback` ไม่ต้องใส่ `/RoleValidation` ซ้ำใน CallbackPath ทั้ง `LoginUrl` และ `PublicOrigin` ต้องใช้ HTTPS

ตัวอย่างส่วน Email สำหรับ QA ให้ merge ลงไฟล์เดิม ไม่สร้าง `Email` ซ้ำ:

```json
{
  "RoleValidation": {
    "DataSource": "Oracle"
  },
  "Email": {
    "TransportMode": "ApiEmail",
    "RecipientMode": "SafeRedirect",
    "SafeRedirectEmployeeNo": "<QA_EMPLOYEE_NO>",
    "ArtifactRootPath": "D:\\RoleValidationData\\EmailArtifacts",
    "PreparingStaleMinutes": 30,
    "Content": {
      "SubjectTemplate": "[RoleValidation] Annual access review - {ApplicationName}",
      "BodyTemplate": "Please review the attached workbook for {ApplicationName}.\nIntended owner: {OwnerEmployeeNo}"
    },
    "PreSubmitRetry": {
      "MaxAttempts": 3,
      "DelayMinutes": [5, 15]
    },
    "ApiEmail": {
      "BaseUrl": "https://<server>/api_email",
      "Route": "/API/v2/EmailCenterRequest",
      "ApplicationName": "RoleValidation",
      "BearerToken": "<APIEMAIL_TOKEN>",
      "TimeoutSeconds": 30
    }
  }
}
```

- QA: ใส่ employee number ของผู้ทดสอบที่มี email ในข้อมูลพนักงาน เช่น `C1008267` เมลจะส่งไปผู้ทดสอบแทน owner
- Production: ใช้ `RecipientMode = RoleOwner` ส่งตาม owner ของแต่ละงาน ค่า `SafeRedirectEmployeeNo` ไม่ได้เปลี่ยนผู้รับในโหมดนี้
- `BaseUrl` คือ root ของ ApiEmail ไม่ต้องต่อ `/API/v2/EmailCenterRequest` ซ้ำ ตัวอย่างข้างต้นจะเรียก `https://<server>/api_email/API/v2/EmailCenterRequest` ยืนยันว่า ApiEmail ที่ติดตั้งรองรับ endpoint นี้ด้วย
- `Content` รองรับ `{ApplicationName}` และ `{OwnerEmployeeNo}` ใน JSON ใช้ `\n` เมื่อต้องการขึ้นบรรทัดใหม่ และใช้ `\\` ใน Windows path
- คง `MaxAttempts = 3` และ `DelayMinutes = [5, 15]` ตามกฎ retry ปัจจุบัน

### ไฟล์แนบและ token

สร้าง `ArtifactRootPath` ไว้นอกโฟลเดอร์ publish และนอกเว็บไซต์ที่เปิดดาวน์โหลดได้ ตัวอย่างใช้ `D:\RoleValidationData\EmailArtifacts` ให้ process ของ RoleValidation อ่าน/เขียนได้ และ process ของ ApiEmail อ่าน path เดียวกันได้ หากคนละเครื่องต้องใช้ shared/UNC path ที่ทั้งสองเข้าถึงได้ ไม่ใช้ local drive คนละเครื่องแล้วถือว่าเป็นไฟล์เดียวกัน

ตามวิธีใช้งาน ApiEmail ที่ทีมระบุ ให้เปิด `https://<server>/api_email` แล้ว sign in จากหน้า Home เลือก `View page source` ค้นหา `const token` และ copy เฉพาะค่าระหว่างเครื่องหมาย quote ไปใส่ `Email:ApiEmail:BearerToken` ไม่ต้องเติม `Bearer ` ไม่ copy ชื่อตัวแปร/เครื่องหมาย quote และไม่ใช้ JWT signing key หรือ cookie ของ Company Login

วิธีนี้ตรงกับ source อ้างอิงของ ApiEmail ที่มี แต่ token ผูกกับบัญชีที่ล็อกอิน อายุและวิธีเปลี่ยน token สำหรับงานตามตารางต้องยืนยันกับผู้ดูแล ApiEmail หากได้ `401` ให้ตรวจ token/สิทธิ์และขอ token ใหม่ตามขั้นตอนของทีม ไม่ตัดสินว่า token ใช้ได้จากการขึ้นต้นด้วย `eyJ` เพียงอย่างเดียว

จำกัดผู้ที่อ่าน/แก้ appsettings บน server ได้ เก็บ credentials, token และ passphrase ของแต่ละ environment แยกกัน และห้าม copy ไฟล์ที่เติม secrets แล้วกลับเข้า Git

หลังแก้ config บนเว็บที่รันอยู่ ให้ recycle App Pool ของ RoleValidation เพื่อโหลดค่าที่ผูกไว้ตอน startup โดยเฉพาะ Email การแก้ไฟล์อย่างเดียวจะยังไม่เปิด workers ที่ถูกปิดไว้กลับมา ก่อน recycle บน Production ให้ตรวจงานค้างตามหัวข้อถัดไป

## 5. ตรวจหลังติดตั้งและอัปเดต

- เปิด HTTPS โดยไม่มี certificate error ล็อกอินด้วยบัญชีที่ได้รับสิทธิ์ ดู Application users และ export Excel ได้ จากนั้น logout/login แล้ว URL ยังอยู่ใต้ `/RoleValidation`
- QA ทดสอบ `Run now` หนึ่ง Application ตรวจผู้รับ, ไฟล์แนบ และ inbox จริง การตอบรับ `202` จาก ApiEmail ยังไม่ยืนยันว่าเมลถึง inbox
- ก่อน Start/Recycle บน Production ตรวจ active schedules และ pending runs เพราะ workers เริ่มทำงานได้ทันทีเมื่อ config ครบ Production ส่งเมลถึง owner จริง
- เมื่ออัปเดต สำรอง publish/config เดิม หยุดเฉพาะ App Pool ของแอป แล้ววางไฟล์รุ่นใหม่ รักษา config ของ server และ HTTPS binding ตรวจ environment ใน `web.config` ก่อน Start ห้ามลบโฟลเดอร์ artifacts ไปกับ publish หากต้องย้อนรุ่นให้คืนชุดไฟล์/config ที่สำรองไว้ โดยตรวจความเข้ากันได้กับฐานข้อมูลก่อน

## ถ้าเปิดไม่ได้

| อาการ | จุดตรวจแรก |
| --- | --- |
| HTTP ถูกบล็อก / เปิดเว็บไม่ได้ | เข้า HTTPS โดยตรง ตรวจ IIS binding, hostname และ certificate |
| HTTP 500 / แอปเริ่มไม่สำเร็จ | Event Viewer → Windows Logs → Application ดู exception เวลาเดียวกับ request; ตรวจ Hosting Bundle, config และสิทธิ์ไฟล์ตาม error |
| Login ไม่ผ่าน / 403 | `LoginUrl`, `PublicOrigin`, callback, passphrase และสิทธิ์บัญชี |
| เว็บเปิดได้แต่หน้า Email ใช้ไม่ได้ | ตรวจ Email config ของ environment นั้น โค้ดปัจจุบันปิด workers เมื่อ Email config ไม่ผ่าน |
| ApiEmail ตอบ 401/403 | ตรวจ token และสิทธิ์กับผู้ดูแล ApiEmail |

หากต้องเก็บ startup log ให้ตั้ง `stdoutLogEnabled="true"` และ `stdoutLogFile=".\logs\stdout"` ใน `<aspNetCore>` เดิมของ `web.config` ให้ App Pool เขียนโฟลเดอร์ `logs` ได้ แล้วเปิดเว็บเพื่อสร้าง `stdout_*.log` ปิดกลับเป็น `false` เมื่อเก็บ error แล้ว และปิดบัง secrets ก่อนส่ง log ดู [การตรวจ error บน IIS](https://learn.microsoft.com/en-us/aspnet/core/test/troubleshoot-azure-iis?view=aspnetcore-10.0#aspnet-core-module-stdout-log-iis)

## ตรวจโค้ดก่อนส่งงาน

```powershell
dotnet build RoleValidation.slnx -c Release
dotnet test --solution RoleValidation.slnx --configuration Release
node --test "RoleValidation.Web.Tests/JavaScript/*.test.js"
```

Node.js ใช้รัน JavaScript tests บนเครื่องพัฒนา/CI ไม่จำเป็นสำหรับ IIS server การผ่าน tests ในเครื่องยังต้องตามด้วยการตรวจ HTTPS, Company Login, Oracle และการส่งเมลบน environment เป้าหมาย
