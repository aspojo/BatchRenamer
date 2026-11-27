$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath $framework)) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
$source = Join-Path $PSScriptRoot 'src'
$output = Join-Path $PSScriptRoot 'dist'
[void][IO.Directory]::CreateDirectory($output)
$app = Join-Path $output 'BatchRenamer.exe'
$setup = Join-Path $output 'BatchRenamerSetup.exe'
$icon = Join-Path $source 'App.ico'
$manifest = Join-Path $source 'application.manifest'
$common = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',"/win32icon:$icon","/win32manifest:$manifest")
$references = @('WindowsBase','PresentationCore','PresentationFramework') | ForEach-Object {
    '/reference:' + (Join-Path $framework "WPF\$_.dll")
}
$references += '/reference:System.Xaml.dll'
$appFiles = @('Program.cs','MainWindow.cs','RulesPanel.cs','RenameEngine.cs','ShellIntegration.cs') | ForEach-Object { Join-Path $source $_ }
$layout = Join-Path $source 'MainView.xaml'
& $compiler @common @references "/out:$app" "/resource:$layout,MainView.xaml" "/resource:$icon,App.ico" @appFiles
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
$nativeDir = Join-Path ([IO.Path]::GetTempPath()) ('BatchRenamer-build-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($nativeDir)
try {
    & (Join-Path $PSScriptRoot 'BuildShell.ps1') -OutputDir $nativeDir
    $resources = @('SelectionDrop64.dll','SelectionDrop32.dll') | ForEach-Object {
        '/resource:' + (Join-Path $nativeDir $_) + ',' + $_
    }
    & $compiler @common '/reference:System.Windows.Forms.dll' "/out:$setup" "/resource:$app,BatchRenamer.exe" @resources (Join-Path $source 'Setup.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
finally {
    foreach ($library in @('SelectionDrop64.dll','SelectionDrop32.dll')) {
        [IO.File]::Delete((Join-Path $nativeDir $library))
    }
    if ([IO.Directory]::GetFileSystemEntries($nativeDir).Length -eq 0) { [IO.Directory]::Delete($nativeDir) }
}
Get-Item -LiteralPath $app,$setup | Select-Object FullName,Length
