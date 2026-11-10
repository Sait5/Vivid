# Build Backdrop with the C# compiler that ships with Windows (.NET Framework 4.8).
# Nothing to install: run   powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
$wpf = Join-Path $fw 'WPF'
New-Item -ItemType Directory -Force "$root\bin" | Out-Null

if (-not (Test-Path "$root\assets\app.ico")) {
  & $csc /nologo /codepage:65001 /out:"$root\bin\MakeIcon.exe" /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" /r:System.Xaml.dll "$root\tools\MakeIcon.cs"
  if ($LASTEXITCODE -ne 0) { throw 'icon tool build failed' }
  & "$root\bin\MakeIcon.exe" "$root\assets"
  Remove-Item "$root\bin\MakeIcon.exe"
}

# WinRT (системный композитор, видеоплеер Windows) — метаданные уже лежат в самой Windows
$wm = Join-Path $env:WINDIR 'System32\WinMetadata'
$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'
$facade = { param($n) (Get-ChildItem (Join-Path $gac $n) -Recurse -Filter "$n.dll" | Select-Object -First 1).FullName }

& $csc /nologo /codepage:65001 /optimize+ /platform:x64 /target:winexe `
  /win32icon:"$root\assets\app.ico" /win32manifest:"$root\src\app.manifest" `
  /out:"$root\bin\Backdrop.exe" `
  /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" `
  /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Core.dll `
  /r:System.Numerics.dll /r:"$(& $facade 'System.Numerics.Vectors')" /r:"$(& $facade 'System.Runtime')" `
  /r:"$fw\System.Runtime.WindowsRuntime.dll" `
  /r:"$wm\Windows.Foundation.winmd" /r:"$wm\Windows.UI.winmd" /r:"$wm\Windows.Media.winmd" /r:"$wm\Windows.Graphics.winmd" `
  "$root\src\*.cs"
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Write-Host "OK: $root\bin\Backdrop.exe"


