#requires -Version 7
<#
.SYNOPSIS
    A minimal SMTP server for local development only: it listens on 127.0.0.1, accepts every message without
    authentication or TLS, and writes each one to a .eml file in -Folder. It lets the contact form be driven end to end on
    a developer machine without a real mail relay. Never expose it beyond loopback.

.EXAMPLE
    pwsh scripts/dev/smtp-sink.ps1 -Port 2525 -Folder $env:TEMP/ng-smtp-sink
#>
param(
    [int] $Port = 2525,
    [Parameter(Mandatory)] [string] $Folder
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Folder | Out-Null
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
Write-Host "SMTP sink on 127.0.0.1:$Port, writing to $Folder"

while ($true)
{
    $client = $listener.AcceptTcpClient()
    try
    {
        $stream = $client.GetStream()
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII)
        $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::ASCII)
        $writer.NewLine = "`r`n"
        $writer.AutoFlush = $true
        $writer.WriteLine('220 localhost smtp-sink')
        $data = $null
        while ($null -ne ($line = $reader.ReadLine()))
        {
            if ($null -ne $data)
            {
                if ($line -eq '.')
                {
                    $file = Join-Path $Folder ("{0:yyyyMMdd-HHmmss-fff}.eml" -f (Get-Date))
                    [System.IO.File]::WriteAllText($file, $data.ToString())
                    Write-Host "Received a message: $file"
                    $data = $null
                    $writer.WriteLine('250 OK queued')
                }
                else
                {
                    [void]$data.AppendLine($(if ($line.StartsWith('..')) { $line.Substring(1) } else { $line }))
                }
                continue
            }
            switch -Regex ($line)
            {
                '^(EHLO|HELO)' { $writer.WriteLine('250-localhost'); $writer.WriteLine('250 8BITMIME') }
                '^MAIL FROM'   { $writer.WriteLine('250 OK') }
                '^RCPT TO'     { $writer.WriteLine('250 OK') }
                '^DATA'        { $writer.WriteLine('354 End data with <CR><LF>.<CR><LF>'); $data = [System.Text.StringBuilder]::new() }
                '^RSET'        { $writer.WriteLine('250 OK') }
                '^NOOP'        { $writer.WriteLine('250 OK') }
                '^QUIT'        { $writer.WriteLine('221 Bye'); break }
                default        { $writer.WriteLine('502 Not implemented') }
            }
            if ($line -match '^QUIT') { break }
        }
    }
    catch
    {
        Write-Host "Connection error: $($_.Exception.Message)"
    }
    finally
    {
        $client.Dispose()
    }
}
