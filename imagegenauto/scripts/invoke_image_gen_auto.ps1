param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('generate', 'edit')]
    [string]$Command,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = 'Stop'
$PythonInstallerUrl = 'https://mirrors.aliyun.com/python-release/windows/3.14.6/python-3.14.6-amd64.exe'
$SkillScripts = $PSScriptRoot

function Get-PythonCommand {
    $launcher = Get-Command py -ErrorAction SilentlyContinue
    if ($launcher) { return @('py', '-3') }
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) { return @('python') }
    return $null
}

function Add-PythonToUserPath([string]$PythonExe) {
    $pythonDirectory = Split-Path -Parent $PythonExe
    $scriptsDirectory = Join-Path $pythonDirectory 'Scripts'
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $pathEntries = @($userPath -split ';' | Where-Object { $_ })
    foreach ($directory in @($pythonDirectory, $scriptsDirectory)) {
        if ($pathEntries -notcontains $directory) {
            $pathEntries += $directory
        }
        if (($env:Path -split ';') -notcontains $directory) {
            $env:Path = "$directory;$env:Path"
        }
    }
    [Environment]::SetEnvironmentVariable('Path', ($pathEntries -join ';'), 'User')
}

$pythonCommand = Get-PythonCommand
if (-not $pythonCommand) {
    $installer = Join-Path $env:TEMP 'python-3.14.6-amd64.exe'
    Invoke-WebRequest -Uri $PythonInstallerUrl -OutFile $installer
    Start-Process -FilePath $installer -ArgumentList '/quiet', 'InstallAllUsers=0', 'PrependPath=1', 'Include_pip=1' -Wait
    $candidate = Join-Path $env:LocalAppData 'Programs\Python\Python314\python.exe'
    if (Test-Path $candidate) {
        Add-PythonToUserPath $candidate
        # Codex may retain its original environment block; use this absolute path now.
        $pythonCommand = @($candidate)
    } else {
        $pythonCommand = Get-PythonCommand
    }
    if (-not $pythonCommand) { throw 'Python 3.14.6 installation finished but no Python executable was found.' }
}

$pythonPrefix = @($pythonCommand | Select-Object -Skip 1)
& $pythonCommand[0] @pythonPrefix -c 'import openai'
if ($LASTEXITCODE -ne 0) {
    & $pythonCommand[0] @pythonPrefix -m pip install --disable-pip-version-check --quiet openai
    if ($LASTEXITCODE -ne 0) { throw 'Unable to install the required Python package: openai.' }
}

& $pythonCommand[0] @pythonPrefix (Join-Path $SkillScripts 'image_gen_auto.py') $Command @Arguments
exit $LASTEXITCODE
