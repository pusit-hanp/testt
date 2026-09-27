# Snyk Open Redirect: follow-up หลังสแกนบน company

วันที่ 2026-09-27

ภาพล่าสุดจากเครื่อง company แสดง Code Security เหลือ 1 Medium ที่ `application-users.js:19` ตรง `window.location.assign(url)` ใน helper ที่เพิ่มจากชุดก่อนหน้า ยังไม่ใช่ผลสแกนของ follow-up นี้ ส่วน Open Source scan failed เป็นอีกปัญหาหนึ่ง

## การแก้

แก้ helper เดิม `navigateWithinOrigin` เพียงจุดเดียว คง origin/protocol checks และ handlers สำหรับเปลี่ยน application กับ sort ไว้:

```js
const safePath = "/." + url.pathname + url.search + url.hash;
if (loadingMask) loadingMask.hidden = false;
window.location.assign(safePath);
```

ส่ง path แทน full URL และเติม `/.` เพื่อไม่ให้ pathname ที่ขึ้นต้นด้วย `//` ถูกตีความเป็น host ใหม่เมื่อนำไป navigate การต่อ `pathname + search + hash` ตรง ๆ ตาม AI suggestion ยังมีกรณีนี้ ตาม [URL Standard](https://url.spec.whatwg.org/) `//host` เป็น scheme-relative URL

การแก้เดิมมี origin guard อยู่แล้ว การที่ Snyk ยังรายงานไม่ได้ยืนยันว่ามีช่องโหว่ใหม่จาก helper รอบนี้ทำให้ค่าที่ส่งเข้า navigation ชัดเจนขึ้น และคง path, query, hash, PathBase และ repeated filters เดิม ไม่เปลี่ยน env, dependency หรือ Snyk exclusions

## ผลตรวจ local

- JavaScript behavior suite: 45 ผ่าน, 0 fail; `node --check` ผ่าน
- Release build ของ Web.Tests และ dependencies: 0 warnings, 0 errors
- C# test ที่ปรับ `ApplicationSelection_Should_ReloadWithFreshMappedRoles`: 1 ผ่าน, 0 fail
- Independent review ของ diff 3 ไฟล์ไม่พบข้อที่ต้องแก้

เพิ่ม regression tests สำหรับ sink ที่เป็น local path และการ parse pathname `//` ทั้งสอง handlers รอบนี้ไม่ได้รัน .NET ทั้ง solution ซ้ำ และยังไม่ได้สแกน Snyk บน company จึงยังไม่ยืนยันว่า findings เป็นศูนย์

## นำเข้าเครื่อง company

ใช้ ZIP `snyk-remaining-redirect-2026-09-27.zip` ต่อจากชุด `snyk-minimal-batch-2026-09-26.zip` ที่นำเข้าแล้ว ZIP ใหม่มี production JS 1 ไฟล์, tests 2 ไฟล์, เอกสารนี้ และ `FILES.sha256` ไม่มี bin/obj หรือการตั้งค่าเครื่อง

เทียบไฟล์ก่อนแทนที่ถ้ามีการแก้เพิ่มบน company แล้ววางตามโครงสร้างใต้โฟลเดอร์ที่มี `RoleValidation.slnx` จากนั้น build/test และสแกน Code Security ด้วย settings เดิม

```powershell
dotnet build RoleValidation.Web.Tests/RoleValidation.Web.Tests.csproj --configuration Release
dotnet test --project RoleValidation.Web.Tests/RoleValidation.Web.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName=RoleValidation.Web.Tests.ApplicationUsers.ApplicationUsersViewContractTests.ApplicationSelection_Should_ReloadWithFreshMappedRoles'
node --test "RoleValidation.Web.Tests/JavaScript/*.test.js"
```

ลองเปลี่ยน application และ sort บนหน้า ApplicationUsers ตรวจ query/filter และ loading mask แล้วเก็บผล Code Security หลังสแกน หากยังเหลือ finding ให้ใช้ Data Flow ของรายการใหม่ตัดสินขั้นถัดไป
