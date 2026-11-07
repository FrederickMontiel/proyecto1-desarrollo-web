# --------------------------------------------------------------------------------------
# Publica el Ingress del K3s de WSL en el puerto 80 de Windows. EJECUTAR COMO ADMINISTRADOR.
#
#   powershell -ExecutionPolicy Bypass -File infrastructure\k8s\scripts\publicar-wsl.ps1
#
# Por qué hace falta: Traefik de K3s se publica con reglas de iptables dentro de WSL, y el
# reenvío automático de "localhost" de WSL solo ve procesos que escuchan un puerto. Con un
# portproxy de Windows:
#   - el navegador entra por http://localhost (la URL que se registra en Google/GitHub), y
#   - la Raspberry Pi de la red local envía sus lecturas a http://<IP-de-esta-PC>.
#
# La IP de WSL cambia al reiniciar Windows: vuelva a ejecutar el script después de reiniciar.
# Para quitarlo:  netsh interface portproxy delete v4tov4 listenport=80 listenaddress=0.0.0.0
#                 Remove-NetFirewallRule -DisplayName "SWMATRC K3s (HTTP 80)"
# --------------------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Ejecute PowerShell como administrador.'
}

$distro = if ($env:WSL_DISTRO) { $env:WSL_DISTRO } else { 'Ubuntu-24.04' }
$ipWsl = ((wsl.exe -d $distro -- hostname -I) -split '\s+' | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1)
if (-not $ipWsl) { Write-Error "No se pudo obtener la IP de WSL ($distro). ¿Está iniciado?" }

netsh interface portproxy delete v4tov4 listenport=80 listenaddress=0.0.0.0 2>$null | Out-Null
netsh interface portproxy add v4tov4 listenport=80 listenaddress=0.0.0.0 connectport=80 connectaddress=$ipWsl | Out-Null

# Entrada desde la red local solamente (la Raspberry); nunca desde Internet.
if (-not (Get-NetFirewallRule -DisplayName 'SWMATRC K3s (HTTP 80)' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'SWMATRC K3s (HTTP 80)' -Direction Inbound -Protocol TCP -LocalPort 80 `
        -RemoteAddress LocalSubnet -Profile Private, Domain -Action Allow | Out-Null
}

Write-Host "Puerto 80 de Windows -> K3s en WSL ($ipWsl):80"
netsh interface portproxy show v4tov4
$ipLan = Get-NetIPAddress -AddressFamily IPv4 -PrefixOrigin Dhcp -ErrorAction SilentlyContinue | Select-Object -ExpandProperty IPAddress
Write-Host "Navegador: http://localhost   ·   Raspberry: SWMATRC_URL=http://$($ipLan -join ' | http://')"
Write-Host 'Nota: si la red Wi-Fi está marcada como "Pública", cámbiela a "Privada" para que la Raspberry pueda entrar.'
