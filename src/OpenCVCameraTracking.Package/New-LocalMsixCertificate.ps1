[CmdletBinding()]
param(
    [switch]$TrustForCurrentUser
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSCommandPath
$identityPath = Join-Path $projectDirectory 'StoreIdentity.props'
if (-not (Test-Path -LiteralPath $identityPath)) {
    throw 'StoreIdentity.props was not found. Copy StoreIdentity.props.example first.'
}

[xml]$identity = Get-Content -LiteralPath $identityPath -Raw
$properties = $identity.Project.PropertyGroup
$publisher = $properties.StorePublisher
$certificateRelativePath = $properties.LocalSigningCertificatePath
if ([string]::IsNullOrWhiteSpace($publisher) -or [string]::IsNullOrWhiteSpace($certificateRelativePath)) {
    throw 'StorePublisher and LocalSigningCertificatePath must be configured in StoreIdentity.props.'
}

$certificatePath = Join-Path $projectDirectory $certificateRelativePath
$certificateDirectory = Split-Path -Parent $certificatePath
New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null

$passwordText = [Guid]::NewGuid().ToString('N')
$password = ConvertTo-SecureString -String $passwordText -AsPlainText -Force
$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $publisher `
    -FriendlyName 'OpenCVCameraTracking local MSIX test certificate' `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyUsage DigitalSignature `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

Export-PfxCertificate -Cert $certificate -FilePath $certificatePath -Password $password | Out-Null

if ($TrustForCurrentUser) {
    $certificateFile = [IO.Path]::ChangeExtension($certificatePath, '.cer')
    Export-Certificate -Cert $certificate -FilePath $certificateFile | Out-Null
    Import-Certificate -FilePath $certificateFile -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null
}

if ($null -eq $properties.LocalSigningCertificatePassword) {
    $node = $identity.CreateElement('LocalSigningCertificatePassword')
    [void]$properties.AppendChild($node)
    $properties = $identity.Project.PropertyGroup
}

$properties.LocalSigningCertificatePassword = $passwordText
$identity.Save($identityPath)

Write-Host "Created local MSIX certificate: $certificatePath"
Write-Host 'The certificate password was saved to the ignored StoreIdentity.props file.'
if ($TrustForCurrentUser) {
    Write-Host 'The public certificate was also trusted for the current Windows user.'
}
