$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$path = Join-Path $env:TEMP ('ffxi-ocr-scale-' + [Guid]::NewGuid().ToString('N') + '.png')
$process = $null
function Write-Fixture([int]$width, [int]$height, [bool]$game = $false) {
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $font = [Drawing.Font]::new('Arial', 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    try {
        $graphics.Clear([Drawing.Color]::Navy)
        $graphics.ScaleTransform($width / 640.0, $height / 480.0)
        if ($game) {
            $graphics.DrawString('FINAL FANTASY XI', $font, [Drawing.Brushes]::White, 240, 80)
            $graphics.DrawString('User Agreement', $font, [Drawing.Brushes]::White, 240, 180)
            $graphics.DrawString('Accept', $font, [Drawing.Brushes]::White, 300, 260)
            $graphics.DrawString('Decline', $font, [Drawing.Brushes]::White, 390, 260)
        } else {
            $graphics.DrawString('Member List', $font, [Drawing.Brushes]::White, 55, 85)
            $graphics.DrawString('Add Member', $font, [Drawing.Brushes]::White, 55, 180)
            $graphics.FillRectangle([Drawing.Brushes]::LightGray, 205, 85, 425, 295)
            $row = 110
            foreach ($id in @('TestMember123', 'AnotherUser456', 'ExampleAlt789', 'FourthMember321')) {
                $graphics.FillRectangle([Drawing.Brushes]::Black, 255, $row, 355, 23)
                $graphics.DrawString('SQUARE ENIX ID: ' + $id, $font, [Drawing.Brushes]::White, 260, $row + 2)
                $graphics.DrawString('Example member', $font, [Drawing.Brushes]::Black, 260, $row + 28)
                $row += 60
            }
        }
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
function Read-Fixture([string]$command) {
    $process.StandardInput.WriteLine($command); $process.StandardInput.Flush()
    $read = $process.StandardOutput.ReadLineAsync()
    if (-not $read.Wait(15000)) { throw 'OCR fixture read timed out.' }
    if (-not $read.Result) { throw 'OCR fixture returned no frame.' }
    return $read.Result | ConvertFrom-Json
}
try {
    Write-Fixture 640 480
    $script = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tools/windows-ocr.ps1'))
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    $info.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $script + '" -ImagePath "' + $path + '" -Server'
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    $errors = $process.StandardError.ReadToEndAsync()
    foreach ($width in @(640, 1280, 1920, 2560)) {
        $height = [int]($width * .75)
        Write-Fixture $width $height
        $frame = Read-Fixture 'read-pol'
        if ($frame.Width -ne $width -or $frame.Height -ne $height) { throw 'OCR changed the client coordinate system.' }
        $text = ($frame.Lines.Text -join ' ') -replace '[^a-zA-Z0-9]', ''
        foreach ($id in @('TestMember123', 'AnotherUser456', 'ExampleAlt789', 'FourthMember321')) {
            if (-not $text.ToLowerInvariant().Contains($id.ToLowerInvariant())) { throw ('Scaled member identity was not read at width ' + $width) }
        }
        foreach ($word in @($frame.Lines.Words)) {
            if ($word.X -lt 0 -or $word.Y -lt 0 -or $word.X + $word.Width -gt $width + 1 -or $word.Y + $word.Height -gt $height + 1) { throw 'OCR coordinates escaped the client bounds.' }
        }
        Write-Output ('PASS: POL identities and client coordinates at ' + $width + 'x' + $height)
    }
    Write-Fixture 640 480 $true
    $frame = Read-Fixture 'read-game'
    if (($frame.Lines.Text -join ' ') -notmatch 'Accept' -or ($frame.Lines.Text -join ' ') -notmatch 'Decline') { throw 'Small game window was not read using game context.' }
    Write-Output 'PASS: the same OCR worker switches to a small game window without a width-based classification.'
} finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }; $process.Dispose() }
    Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
}
