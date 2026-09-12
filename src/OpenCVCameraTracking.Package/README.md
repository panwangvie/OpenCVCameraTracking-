# Microsoft Store MSIX packaging

`OpenCVCameraTracking.Package.wapproj` is the x64 MSIX packaging project for Microsoft Store submission.

The project is registered in `OpenCVCameraTracking.slnx` with the Windows Application Packaging Project type. Build the solution or the packaging project with **Visual Studio/MSBuild**, not `dotnet build`: the .NET SDK does not contain the Visual Studio DesktopBridge/MSIX targets required by `.wapproj`.

## Local Store identity

Copy `StoreIdentity.props.example` to `StoreIdentity.props`, then fill in the identity values from Partner Center. `StoreIdentity.props` is intentionally ignored by Git, so the Store publisher certificate subject stays only on the build machine.

Increase `StorePackageVersion` before every new Store submission. Microsoft Store requires the fourth version segment to be `0`, such as `1.0.1.0`; do not use `1.0.0.1`.

Keep the same four-part version in the WPF project and the repository-root `update-manifest.json`. The application compares that public manifest at startup and shows an update card that opens the Microsoft Store search/listing URL.

## Local manual package and certificate

Microsoft Store's private signing key cannot be reproduced locally. For local testing, run the following once to create a self-signed certificate whose **Subject** exactly matches the Store `Publisher` value, export it to an ignored PFX file, and trust its public certificate for the current Windows user:

```powershell
powershell -ExecutionPolicy Bypass -File .\src\OpenCVCameraTracking.Package\New-LocalMsixCertificate.ps1 -TrustForCurrentUser
```

The script writes the local PFX path and password to the ignored `StoreIdentity.props` file. Do not commit that file or the `Certificates` directory.

Build a locally installable, signed MSIX package with:

```powershell
msbuild .\src\OpenCVCameraTracking.Package\OpenCVCameraTracking.Package.wapproj `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 `
  /p:AppxBundle=Never /p:GenerateAppxPackageOnBuild=true `
  /p:UseLocalPackageSigning=true /p:UapAppxPackageBuildMode=SideloadOnly
```

The package manifest is generated during the build from `Package.appxmanifest.template`; do not place the Store publisher value in tracked files.

## Build an upload package

Run from a Visual Studio Developer PowerShell prompt:

```powershell
msbuild .\src\OpenCVCameraTracking.Package\OpenCVCameraTracking.Package.wapproj `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 `
  /p:AppxBundle=Never /p:GenerateAppxPackageOnBuild=true `
  /p:UapAppxPackageBuildMode=StoreUpload
```

The output upload package is created under:

```text
src\OpenCVCameraTracking.Package\AppPackages\*.msixupload
```

For Store submission, the `.msixupload` output is recommended because it includes symbol data. A locally signed `.msix` whose `Identity Name` and `Publisher` match the Partner Center identity can also be uploaded; Microsoft Store replaces any existing package signature after certification. The local self-signed certificate remains useful only for sideload testing before submission.

The manifest requests `webcam`, `internetClient`, `privateNetworkClientServer`, and `runFullTrust` capabilities for local cameras, RTSP/network streams, and the desktop WPF process.
