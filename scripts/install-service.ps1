[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [string]$ServiceName = "SystemMonitorAgent",

    [string]$DisplayName = "System Monitor Agent",

    [string]$Description = "Агент мониторинга Windows, собирающий системные метрики и отправляющий их на HTTP API.",

    [ValidateSet("Automatic", "Manual", "Disabled")]
    [string]$StartupType = "Automatic",

    [switch]$StartAfterInstall,

    [switch]$ForceReinstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)

    Write-Host "[SystemMonitorAgent] $Message"
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ServiceInstance {
    param([string]$Name)

    $escapedName = $Name.Replace("'", "''")
    return Get-CimInstance -ClassName Win32_Service -Filter "Name = '$escapedName'" -ErrorAction Stop
}

function Get-NormalizedServicePath {
    param([string]$PathName)

    if ([string]::IsNullOrWhiteSpace($PathName)) {
        return $null
    }

    if ($PathName -match '^"([^"]+)"') {
        return $Matches[1]
    }

    return ($PathName -split "\s+", 2)[0]
}

function Wait-ForServiceDeletion {
    param(
        [string]$Name,
        [int]$TimeoutSeconds = 15
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if (-not (Get-ServiceInstance -Name $Name)) {
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Служба '$Name' всё ещё существует спустя $TimeoutSeconds секунд."
}

function Set-ServiceDescription {
    param(
        [string]$Name,
        [string]$Value
    )

    $serviceInstance = Get-ServiceInstance -Name $Name
    if (-not $serviceInstance) {
        return
    }

    try {
        $result = Invoke-CimMethod -InputObject $serviceInstance -MethodName Change -Arguments @{
            Description = $Value
        }

        if ($result.ReturnValue -ne 0) {
            Write-Warning "Не удалось обновить описание службы '$Name'. Win32 код: $($result.ReturnValue)"
        }
    }
    catch {
        Write-Warning "Не удалось обновить описание службы '$Name': $($_.Exception.Message)"
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Для установки Windows Service нужны права администратора. Запустите PowerShell от имени администратора и повторите попытку."
}

$resolvedExecutablePath = (Resolve-Path -Path $ExecutablePath -ErrorAction Stop).Path

if ([IO.Path]::GetExtension($resolvedExecutablePath) -ne ".exe") {
    throw "Параметр ExecutablePath должен указывать на опубликованный .exe файл. Текущее значение: '$resolvedExecutablePath'."
}

$existingService = Get-ServiceInstance -Name $ServiceName

if ($existingService) {
    $existingExecutablePath = Get-NormalizedServicePath -PathName $existingService.PathName

    if (-not $ForceReinstall) {
        if ($existingExecutablePath -ieq $resolvedExecutablePath) {
            Write-Step "Служба '$ServiceName' уже установлена для '$resolvedExecutablePath'."

            if ($PSCmdlet.ShouldProcess($ServiceName, "Обновить метаданные службы")) {
                Set-Service -Name $ServiceName -StartupType $StartupType
                Set-ServiceDescription -Name $ServiceName -Value $Description
            }

            if ($StartAfterInstall -and (Get-Service -Name $ServiceName).Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
                if ($PSCmdlet.ShouldProcess($ServiceName, "Запустить службу")) {
                    Write-Step "Запускается существующая служба '$ServiceName'."
                    Start-Service -Name $ServiceName
                }
            }

            Write-Step "Повторная установка не требуется: служба уже присутствует."
            return
        }

        throw "Служба '$ServiceName' уже существует и указывает на '$existingExecutablePath'. Используйте -ForceReinstall, если нужно переустановить её."
    }

    if ($PSCmdlet.ShouldProcess($ServiceName, "Переустановить службу")) {
        if ($existingService.State -eq "Running") {
            Write-Step "Останавливается существующая служба '$ServiceName'."
            Stop-Service -Name $ServiceName -ErrorAction Stop
            (Get-Service -Name $ServiceName).WaitForStatus(
                [System.ServiceProcess.ServiceControllerStatus]::Stopped,
                [TimeSpan]::FromSeconds(30))
        }

        Write-Step "Удаляется существующая служба '$ServiceName'."
        $deleteResult = Invoke-CimMethod -InputObject $existingService -MethodName Delete
        if ($deleteResult.ReturnValue -ne 0) {
            throw "Не удалось удалить существующую службу '$ServiceName'. Win32 код: $($deleteResult.ReturnValue)"
        }

        Wait-ForServiceDeletion -Name $ServiceName
    }
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Создать Windows Service")) {
    Write-Step "Создаётся служба '$ServiceName' для '$resolvedExecutablePath'."

    $newServiceParameters = @{
        Name = $ServiceName
        BinaryPathName = ('"{0}"' -f $resolvedExecutablePath)
        DisplayName = $DisplayName
        StartupType = $StartupType
    }

    if ((Get-Command New-Service).Parameters.ContainsKey("Description")) {
        $newServiceParameters["Description"] = $Description
    }

    New-Service @newServiceParameters | Out-Null

    Set-ServiceDescription -Name $ServiceName -Value $Description

    if ($StartAfterInstall) {
        Write-Step "Запускается служба '$ServiceName'."
        Start-Service -Name $ServiceName
    }

    Write-Step "Служба '$ServiceName' успешно установлена."
}
