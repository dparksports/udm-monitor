$r = 21,22,23,25,80,110,135,139,443,445,1433,3306,3389,5900
foreach ($p in $r) {
  $c = New-Object Net.Sockets.TcpClient
  $t = $c.BeginConnect('192.168.2.2', $p, $null, $null)
  if ($t.AsyncWaitHandle.WaitOne(400)) {
    try { $c.EndConnect($t); "port $p OPEN" } catch { "port $p refused" }
  } else { "port $p filtered" }
  $c.Close()
}
