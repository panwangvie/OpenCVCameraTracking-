# OpenCVCameraTracking MSIX 打包与微软商店发布指南

本文说明如何为 `OpenCVCameraTracking` 生成本地测试包与 Microsoft Store 上传包，并完成 Partner Center 发布。

## 1. 先理解两种包

| 用途 | 输出文件 | 是否使用本地自签名证书 | 适用场景 |
| --- | --- | --- | --- |
| 本地侧载测试 | `.msix` | 是 | 在开发电脑或测试电脑安装验证 |
| Microsoft Store 提交 | `.msixupload` | 可不签名 | 上传 Partner Center，认证通过后由 Store 重新签名并分发 |

本地自签名证书的私钥不需要、也无法与 Microsoft Store 的私钥相同。需要一致的是 MSIX 清单的包身份：`Identity Name` 与 `Publisher`，其中 `Publisher` 必须和 Partner Center 分配的 CN 完全一致。

当前应用的 Store 身份为：

```text
Identity Name: wuty.CameraTracking
Publisher: CN=REPLACE-WITH-YOUR-MICROSOFT-STORE-PUBLISHER
PublisherDisplayName: wuty
```

## 2. 工程与文件说明

| 文件或目录 | 用途 |
| --- | --- |
| `src/OpenCVCameraTracking.Package/OpenCVCameraTracking.Package.wapproj` | Windows Application Packaging Project，负责生成 MSIX |
| `src/OpenCVCameraTracking.Package/Package.appxmanifest.template` | MSIX 清单模板；构建时由本机身份配置替换占位符 |
| `src/OpenCVCameraTracking.Package/StoreIdentity.props` | 本机身份、版本和本地证书密码配置；已被 Git 忽略 |
| `src/OpenCVCameraTracking.Package/StoreIdentity.props.example` | 可提交的配置模板 |
| `src/OpenCVCameraTracking.Package/New-LocalMsixCertificate.ps1` | 创建本地测试 PFX 并可选导入当前用户信任库 |
| `src/OpenCVCameraTracking.Package/AppPackages/` | 生成的 `.msix` 与 `.msixupload` 输出目录；已被 Git 忽略 |
| `src/OpenCVCameraTracking.Package/Certificates/` | 本地测试 PFX 目录；已被 Git 忽略 |

`StoreIdentity.props`、PFX、密码与最终包都不会被提交到 Git。

## 3. 前置条件

1. 安装 Visual Studio，并选择 Windows 应用打包/MSIX 相关组件。
2. 确保 Visual Studio 的 MSBuild 可用。本机示例路径：

   ```text
   D:\vs2026\MSBuild\Current\Bin\MSBuild.exe
   ```

3. 打开 PowerShell，切换至仓库根目录：

   ```powershell
   Set-Location E:\Code\AICoding\OpenCVCamera
   ```

4. 不要使用 `dotnet build` 构建 `.wapproj`。`.wapproj` 依赖 Visual Studio 的 DesktopBridge/MSIX 目标；`dotnet build` 可以识别解决方案中的普通 .NET 项目，但无法提供打包目标。

## 4. 配置 Store 身份

首次配置时，将模板复制为本机文件：

```powershell
Copy-Item .\src\OpenCVCameraTracking.Package\StoreIdentity.props.example `
  .\src\OpenCVCameraTracking.Package\StoreIdentity.props
```

编辑 `src/OpenCVCameraTracking.Package/StoreIdentity.props`：

```xml
<Project>
  <PropertyGroup>
    <StorePackageIdentityName>wuty.CameraTracking</StorePackageIdentityName>
    <StorePublisher>CN=REPLACE-WITH-YOUR-MICROSOFT-STORE-PUBLISHER</StorePublisher>
    <StorePublisherDisplayName>wuty</StorePublisherDisplayName>
    <StorePackageDisplayName>CameraTracking</StorePackageDisplayName>
    <StorePackageVersion>1.0.1.0</StorePackageVersion>
    <LocalSigningCertificatePath>Certificates\OpenCVCameraTracking.LocalTest.pfx</LocalSigningCertificatePath>
    <LocalSigningCertificatePassword></LocalSigningCertificatePassword>
  </PropertyGroup>
</Project>
```

其中，`StorePackageIdentityName`、`StorePublisher` 与 `StorePublisherDisplayName` 应复制自 Partner Center 的“产品标识详细信息”。请不要自行更改已分配的 Name 或 CN。

## 5. 修改版本号

每次向 Microsoft Store 提交新包前，都必须提高版本号。修改文件：

```text
src\OpenCVCameraTracking.Package\StoreIdentity.props
```

修改这一行：

```xml
<StorePackageVersion>1.0.1.0</StorePackageVersion>
```

例如下一次提交改为：

```xml
<StorePackageVersion>1.0.2.0</StorePackageVersion>
```

Microsoft Store 提交要求第 4 段必须为 `0`。建议采用 `主版本.次版本.修订.0` 的格式，例如 `1.2.0.0`、`1.2.1.0`。新版本必须高于 Partner Center 已提交或已发布的版本；不要使用 `1.0.0.1` 这类第 4 段非零的版本。

## 6. 首次生成本地测试证书

本地安装 MSIX 需要签名证书。运行一次：

```powershell
powershell -ExecutionPolicy Bypass `
  -File .\src\OpenCVCameraTracking.Package\New-LocalMsixCertificate.ps1 `
  -TrustForCurrentUser
```

脚本会执行以下操作：

1. 从 `StoreIdentity.props` 读取 `StorePublisher`。
2. 创建 Subject 与 Store Publisher CN 完全相同的自签名证书。
3. 导出 PFX 到 `Certificates\OpenCVCameraTracking.LocalTest.pfx`。
4. 生成密码并写入本机 `StoreIdentity.props`。
5. 将公钥导入当前用户的 `TrustedPeople`，使当前 Windows 用户可安装该测试包。

如果测试电脑不是当前电脑，需要将生成的 `.cer` 公钥导入测试电脑的 `CurrentUser\TrustedPeople`，然后再安装 `.msix`。

## 7. 生成本地签名 MSIX 并安装测试

使用 Visual Studio MSBuild 生成本地测试包：

```powershell
D:\vs2026\MSBuild\Current\Bin\MSBuild.exe `
  .\src\OpenCVCameraTracking.Package\OpenCVCameraTracking.Package.wapproj `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 `
  /p:AppxBundle=Never /p:GenerateAppxPackageOnBuild=true `
  /p:UseLocalPackageSigning=true /p:UapAppxPackageBuildMode=SideloadOnly
```

输出示例：

```text
src\OpenCVCameraTracking.Package\AppPackages\
  OpenCVCameraTracking.Package_1.0.1.0_x64_Test\
    OpenCVCameraTracking.Package_1.0.1.0_x64.msix
```

安装方式一：双击 `.msix` 文件。

安装方式二：PowerShell：

```powershell
Add-AppxPackage `
  .\src\OpenCVCameraTracking.Package\AppPackages\OpenCVCameraTracking.Package_1.0.1.0_x64_Test\OpenCVCameraTracking.Package_1.0.1.0_x64.msix
```

安装后，在开始菜单搜索 `CameraTracking`。如果新包的版本更高，Windows 会自动更新同一包身份的旧版本。

## 8. 生成 Microsoft Store 上传包

先确认 `StorePackageVersion` 已提高，然后执行：

```powershell
D:\vs2026\MSBuild\Current\Bin\MSBuild.exe `
  .\src\OpenCVCameraTracking.Package\OpenCVCameraTracking.Package.wapproj `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 `
  /p:AppxBundle=Never /p:GenerateAppxPackageOnBuild=true `
  /p:UseLocalPackageSigning=false /p:UapAppxPackageBuildMode=StoreUpload
```

输出示例：

```text
src\OpenCVCameraTracking.Package\AppPackages\
  OpenCVCameraTracking.Package_1.0.1.0_x64.msixupload
```

推荐上传 `.msixupload`，因为其中包含用于崩溃分析的符号文件。Partner Center 同时支持 `.msix` 与 `.msixupload`，但优先使用后者。

## 9. 上传 Partner Center

1. 登录 [Partner Center](https://partner.microsoft.com/dashboard)。
2. 打开产品 `CameraTracking`。
3. 新建提交或选择“更新”。
4. 在“包/Packages”页面上传最新的 `.msixupload` 文件。
5. 检查上传后的包身份：

   ```text
   Name: wuty.CameraTracking
   Publisher: CN=REPLACE-WITH-YOUR-MICROSOFT-STORE-PUBLISHER
   Architecture: x64
   Version: 当前 StorePackageVersion
   ```

6. 补齐商店列表、隐私策略、年龄分级与提交说明后，提交认证。

Microsoft Store 会在认证通过后用 Microsoft 证书重新签名 MSIX/AppX 包。因此本地自签名 PFX 只用于侧载测试，不会成为商店分发版本的最终签名。

## 10. 应用无法在开始菜单显示

若包安装成功但找不到应用，检查 `Package.appxmanifest.template` 中的 `uap:VisualElements`：

```xml
<uap:VisualElements ...>
```

不要设置：

```xml
AppListEntry="none"
```

该值会隐藏开始菜单入口。当前工程已移除此配置；修复后需提高版本号并重新安装。

可用以下命令检查当前用户是否已安装包：

```powershell
Get-AppxPackage -Name wuty.CameraTracking |
  Select-Object Name, PackageFullName, Status, InstallLocation
```

## 11. 常见问题

### Q1：提示 Publisher 与证书不匹配

本地 PFX 的 Subject 必须与清单的 `Publisher` 完全相同，包括大小写、空格与字段顺序。重新运行证书脚本即可按当前本机配置创建正确 Subject 的测试证书。

### Q2：提示新包版本不能安装

提高 `StorePackageVersion`，重新生成包。MSIX 更新不允许降低版本号。

### Q3：`dotnet build OpenCVCameraTracking.slnx` 报 DesktopBridge 缺失

这是预期行为。改用 Visual Studio 的“生成解决方案”，或用本指南中的 `D:\vs2026\...\MSBuild.exe` 命令打包。

### Q4：如何只检查主 WPF 项目能否编译

```powershell
dotnet build .\src\OpenCVCameraTracking\OpenCVCameraTracking.csproj -c Debug
```

### Q5：本地测试证书需要提交到 Git 吗

不需要，也不能提交。`StoreIdentity.props`、`Certificates`、`AppPackages` 都已在 `.gitignore` 中排除。

## 12. 发布前检查清单

- [ ] `StorePackageIdentityName` 与 Partner Center 的 Name 一致。
- [ ] `StorePublisher` 与 Partner Center 的 CN 完全一致。
- [ ] `StorePackageVersion` 高于上次提交版本。
- [ ] 使用 `Release`、`x64`、`StoreUpload` 模式生成包。
- [ ] 上传的是最新 `.msixupload`。
- [ ] 检查摄像头、RTSP 和白名单功能。
- [ ] 检查应用能在开始菜单显示。
- [ ] 不提交 `StoreIdentity.props`、PFX、密码或 `AppPackages`。
