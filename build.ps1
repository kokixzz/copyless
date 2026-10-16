# Builds Copyless with the C# compiler included in the .NET Framework 4.8.
# Output: dist\Copyless.exe

$ErrorActionPreference = 'Stop'
$root  = $PSScriptRoot
$fw    = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wm    = "$env:WINDIR\System32\WinMetadata"
$build = Join-Path $root 'build'
$dist  = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $build, $dist | Out-Null

$version = [regex]::Match((Get-Content "$root\src\AssemblyInfo.cs" -Raw), 'AssemblyVersion\("(\d+\.\d+\.\d+)').Groups[1].Value

$refs = @(
  'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
  "$fw\WPF\UIAutomationClient.dll", "$fw\WPF\UIAutomationTypes.dll", "$fw\WPF\WindowsBase.dll",
  "$fw\System.Runtime.dll", "$fw\System.Runtime.InteropServices.WindowsRuntime.dll", "$fw\System.Threading.Tasks.dll",
  "$wm\Windows.Foundation.winmd", "$wm\Windows.Media.winmd", "$wm\Windows.Graphics.winmd",
  "$wm\Windows.Storage.winmd", "$wm\Windows.Globalization.winmd"
) | ForEach-Object { "/r:$_" }
$src = Get-ChildItem "$root\src\*.cs" | ForEach-Object { $_.FullName }
$exe = Join-Path $build 'Copyless.exe'

function Invoke-Compile($extra) {
  $out = & "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /utf8output `
    "/out:$exe" "/win32manifest:$root\app.manifest" "/resource:$root\assets\symbol.png,Copyless.symbol.png" @extra @refs @src 2>&1
  $out | Where-Object { $_ -notmatch 'warning CS1701|warning CS1702' } | ForEach-Object { Write-Host $_ }
  if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
}

# The application icon is drawn in code; build once, export the icon, then build again with it embedded.
Invoke-Compile @()
$ico = Join-Path $build 'Copyless.ico'
Start-Process -FilePath $exe -ArgumentList '--write-icon', "`"$ico`"" -Wait
Invoke-Compile @("/win32icon:$ico")

Copy-Item $exe (Join-Path $dist 'Copyless.exe') -Force
Write-Host "Copyless $version -> $dist\Copyless.exe"
