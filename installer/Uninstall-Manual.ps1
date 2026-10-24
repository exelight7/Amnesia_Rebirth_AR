param([Parameter(Mandatory=$true)][string]$GamePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path (Join-Path $PSScriptRoot 'InstallerEngine.cs') -ReferencedAssemblies System.Core,System.Xml,System.Web.Extensions,System.IO.Compression,System.IO.Compression.FileSystem
[InstallerEngine]::Uninstall($GamePath, [Action[int,string]]{param($percent,$message) Write-Host "$percent% $message"})
