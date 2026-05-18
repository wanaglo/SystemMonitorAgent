[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServiceName = "SystemMonitorAgent"
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

if (-not (Test-IsAdministrator)) {
    throw "Для удаления Windows Service нужны права администратора. Запустите PowerShell от имени администратора и повторите попытку."
}

$serviceInstance = Get-ServiceInstance -Name $ServiceName

if (-not $serviceInstance) {
    Write-Step "Служба '$ServiceName' не установлена. Удалять нечего."
    return
}

if ($PSCmdlet.ShouldProcess($ServiceName, "Удалить Windows Service")) {
    if ($serviceInstance.State -eq "Running") {
        Write-Step "Останавливается служба '$ServiceName'."
        Stop-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service -Name $ServiceName).WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(30))
    }

    Write-Step "Удаляется служба '$ServiceName'."
    $deleteResult = Invoke-CimMethod -InputObject $serviceInstance -MethodName Delete
    if ($deleteResult.ReturnValue -ne 0) {
        throw "Не удалось удалить службу '$ServiceName'. Win32 код: $($deleteResult.ReturnValue)"
    }

    Wait-ForServiceDeletion -Name $ServiceName

    Write-Step "Служба '$ServiceName' успешно удалена."
}
