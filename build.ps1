param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Python tools/build_payload.py
    if ($LASTEXITCODE -ne 0) { throw 'Payload build failed.' }
    New-Item -ItemType Directory -Force dist | Out-Null
    $csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
    if (!(Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /utf8output /out:dist\Amnesia_Rebirth_AR_1.0.0_Setup.exe /win32manifest:installer\app.manifest /win32icon:installer\app.ico /resource:build\payload.zip,payload.zip /reference:System.Windows.Forms.dll,System.Drawing.dll,System.Core.dll,System.Xml.dll,System.Web.Extensions.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll .\installer\Program.cs .\installer\InstallerEngine.cs .\installer\AssemblyInfo.cs
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $manual = Join-Path $PSScriptRoot 'build/manual'
    New-Item -ItemType Directory -Force "$manual/data" | Out-Null
    Copy-Item build/payload.zip "$manual/data/payload.zip" -Force
    Copy-Item installer/InstallerEngine.cs,installer/Install-Manual.ps1,installer/Uninstall-Manual.ps1,installer/Install.cmd,installer/Uninstall.cmd,LICENSE "$manual/" -Force
    Copy-Item README.md "$manual/README.md" -Force
    Copy-Item docs "$manual/docs" -Recurse -Force
    Compress-Archive -Path "$manual/*" -DestinationPath dist/Amnesia_Rebirth_AR_1.0.0_Manual.zip -Force
    Get-ChildItem dist/* | Where-Object Name -ne SHA256SUMS.txt | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$_.Name } | Set-Content dist/SHA256SUMS.txt -Encoding ascii
    Write-Host 'Built installer, manual ZIP, and SHA256SUMS.txt in dist/.'
} finally { Pop-Location }
