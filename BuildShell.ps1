param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$pf = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$vcvars = Join-Path $pf 'Microsoft Visual Studio 14.0\VC\vcvarsall.bat'
$vswhere = Join-Path $pf 'Microsoft Visual Studio\Installer\vswhere.exe'
if (Test-Path -LiteralPath $vswhere) {
    $install = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ($install) { $vcvars = Join-Path $install 'VC\Auxiliary\Build\vcvarsall.bat' }
}
if (-not (Test-Path -LiteralPath $vcvars)) {
    throw 'Install the Visual C++ x86/x64 build tools and the Windows SDK.'
}

$output = [IO.Directory]::CreateDirectory($OutputDir).FullName
$work = [IO.Directory]::CreateDirectory((Join-Path ([IO.Path]::GetTempPath()) ('RenamerShellBuild-' + [guid]::NewGuid().ToString('N')))).FullName
Push-Location -LiteralPath $work
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\SelectionDrop.cpp'),(Join-Path $PSScriptRoot 'src\SelectionDrop.def') -Destination $work
    foreach ($target in @(@('amd64', '64'), @('x86', '32'))) {
        $name = 'SelectionDrop' + $target[1]
        $batch = @(
            '@echo off'
            ('call "' + $vcvars + '" ' + $target[0] + ' >nul')
            'if errorlevel 1 exit /b 1'
            ('cl /nologo /W4 /EHsc /O2 /MT /LD SelectionDrop.cpp /link /DEF:SelectionDrop.def /OUT:' + $name + '.dll ole32.lib shell32.lib uuid.lib')
        )
        [IO.File]::WriteAllLines((Join-Path $work 'compile.cmd'), $batch, [Text.Encoding]::Default)
        & $env:ComSpec /d /c compile.cmd
        if ($LASTEXITCODE -ne 0) { throw ('Failed to compile ' + $name + '.dll') }
        Copy-Item -LiteralPath (Join-Path $work ($name + '.dll')) -Destination $output -Force
    }
}
finally {
    Pop-Location
    # The staging directory is unique to this build and contains only flat compiler outputs.
    foreach ($file in [IO.Directory]::GetFiles($work)) { [IO.File]::Delete($file) }
    [IO.Directory]::Delete($work)
}
