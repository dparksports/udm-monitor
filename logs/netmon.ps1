$log = 'C:\Users\honey\myudmpro\logs\netmon.log'
New-Item -ItemType Directory -Force -Path (Split-Path $log) | Out-Null
"=== netmon started $(Get-Date -Format o) ===" | Add-Content $log
for ($i = 0; $i -lt 450; $i++) {
  $t = Get-Date -Format 'HH:mm:ss'
  $e  = (Get-NetIPAddress -InterfaceIndex 3 -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {$_.IPAddress -notlike '169.254*'}).IPAddress -join ','
  $e2 = (Get-NetIPAddress -InterfaceIndex 9 -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {$_.IPAddress -notlike '169.254*'}).IPAddress -join ','
  $e2api = (Get-NetIPAddress -InterfaceIndex 9 -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object {$_.IPAddress -like '169.254*'}).IPAddress -join ','
  $gwTest = ''
  if ($e2 -like '192.168.2.*') {
    $p = ping -S $e2 -n 1 -w 1200 192.168.2.1 2>$null
    if ($LASTEXITCODE -eq 0) { $gwTest = 'gw:OK' } else { $gwTest = 'gw:FAIL' }
    $p2 = ping -S $e2 -n 1 -w 1200 1.1.1.1 2>$null
    if ($LASTEXITCODE -eq 0) { $gwTest += ' net:OK' } else { $gwTest += ' net:FAIL' }
  }
  $inet = ping -n 1 -w 1000 1.1.1.1 2>$null; if ($LASTEXITCODE -eq 0) { $inet = 'inet:OK' } else { $inet = 'inet:FAIL' }
  $line = "$t eth3=$e eth9=$e2 apipa=$e2api $gwTest $inet"
  Add-Content $log $line
  Start-Sleep -Seconds 2
}
"=== netmon ended $(Get-Date -Format o) ===" | Add-Content $log
