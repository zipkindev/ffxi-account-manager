param([Parameter(Mandatory=$true)][string]$ImagePath, [ValidateRange(1,3)][int]$Scale = 2, [switch]$Server, [ValidateSet("Auto","POL","FFXI")][string]$WindowKind = "Auto")
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Add-Type -AssemblyName System.Runtime.WindowsRuntime
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class FfxiOcrContrast {
    public static void Apply(Bitmap bitmap) {
        var data = bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try {
            var bytes = new byte[data.Stride * data.Height]; Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (int i=0; i<bytes.Length; i+=4) {
                bool white = bytes[i] >= 230 && bytes[i+1] >= 230 && bytes[i+2] >= 230;
                bool gold = bytes[i] <= 140 && bytes[i+1] >= 160 && bytes[i+2] >= 210;
                byte value = white || gold ? (byte)255 : (byte)0;
                bytes[i]=bytes[i+1]=bytes[i+2]=value; bytes[i+3]=255;
            }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        } finally { bitmap.UnlockBits(data); }
    }
}
'@
[void][Windows.Graphics.Imaging.BitmapDecoder,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
[void][Windows.Graphics.Imaging.SoftwareBitmap,Windows.Graphics.Imaging,ContentType=WindowsRuntime]
[void][Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
[void][Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime]
[void][Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime]
[void][Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
[void][Windows.Storage.Streams.DataWriter,Windows.Storage.Streams,ContentType=WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
function AwaitOperation($Operation, [Type]$ResultType) {
    $task = $asTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    $task.GetAwaiter().GetResult()
}
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('en-US'))
if ($null -eq $engine) { throw 'English Windows OCR language support is unavailable.' }
function ReadRegion($Image, $Rectangle, [bool]$Contrast = $false, [int]$RegionScale = 0) {
    if ($RegionScale -eq 0) { $RegionScale = $Scale }
    $factor = [Math]::Min($RegionScale, [Windows.Media.Ocr.OcrEngine]::MaxImageDimension / [Math]::Max($Rectangle.Width, $Rectangle.Height))
    $scaled = [System.Drawing.Bitmap]::new([int][Math]::Floor($Rectangle.Width * $factor), [int][Math]::Floor($Rectangle.Height * $factor))
    $graphics = [System.Drawing.Graphics]::FromImage($scaled)
    $encoded = [IO.MemoryStream]::new()
    $stream = [Windows.Storage.Streams.InMemoryRandomAccessStream]::new()
    $writer = [Windows.Storage.Streams.DataWriter]::new($stream)
    $bitmap = $null
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($Image, [System.Drawing.Rectangle]::new(0,0,$scaled.Width,$scaled.Height), $Rectangle, [System.Drawing.GraphicsUnit]::Pixel)
        if ($Contrast) { [FfxiOcrContrast]::Apply($scaled) }
        $scaled.Save($encoded, [System.Drawing.Imaging.ImageFormat]::Png)
        $writer.WriteBytes($encoded.ToArray())
        [void](AwaitOperation $writer.StoreAsync() ([uint32]))
        [void]$writer.DetachStream()
        $stream.Seek(0)
        $decoder = AwaitOperation ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = AwaitOperation $decoder.GetSoftwareBitmapAsync() ([Windows.Graphics.Imaging.SoftwareBitmap])
        $result = AwaitOperation $engine.RecognizeAsync($bitmap) ([Windows.Media.Ocr.OcrResult])
        foreach ($line in $result.Lines) {
            $words = @($line.Words | ForEach-Object { @{ Text=$_.Text; X=($_.BoundingRect.X / $factor + $Rectangle.X); Y=($_.BoundingRect.Y / $factor + $Rectangle.Y); Width=($_.BoundingRect.Width / $factor); Height=($_.BoundingRect.Height / $factor) } })
            @{ Text=$line.Text; Words=$words }
        }
    } finally {
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        $writer.Dispose(); $stream.Dispose(); $encoded.Dispose(); $graphics.Dispose(); $scaled.Dispose()
    }
}
function ReadLoginFrame([bool]$LoadingOnly = $false, [bool]$ConfirmationOnly = $false, [bool]$CharacterGridOnly = $false, [ValidateSet("Auto","POL","FFXI")][string]$ScreenKind = $WindowKind) {
$image = [System.Drawing.Bitmap]::new($ImagePath)
try {
    # Window context selects the UI layout; client dimensions only scale regions.
    $isPol = $ScreenKind -ne "FFXI"
    $isGame = $ScreenKind -ne "POL"
    $lines = [System.Collections.Generic.List[object]]::new()
    $rectangles = @([System.Drawing.Rectangle]::new(0,0,$image.Width,$image.Height))
    # OCR small FFXI buttons at their native detail without exceeding the Windows OCR bitmap limit.
    if ($image.Width * $Scale -gt [Windows.Media.Ocr.OcrEngine]::MaxImageDimension) {
        $rectangles += [System.Drawing.Rectangle]::new([int]($image.Width * 0.25), [int]($image.Height * 0.3), [int]($image.Width * 0.5), [int]($image.Height * 0.55))
    }
    $passes = @($rectangles | ForEach-Object { @{ Rect=$_; Contrast=$false } })
    if ($isPol) {
        # POL's small account IDs need independent high-resolution reads.
        # Preserve the exact resulting text; do not substitute similar glyphs.
        $identity = [System.Drawing.Rectangle]::new([int]($image.Width * 0.32), [int]($image.Height * 0.18), [int]($image.Width * 0.64), [int]($image.Height * 0.52))
        # Native-resolution evidence complements magnification on enlarged glyphs.
        $passes += @{ Rect=$identity; Contrast=$false; Scale=1 }
        $passes += @{ Rect=$identity; Contrast=$true; Scale=1 }
        $passes += @{ Rect=$identity; Contrast=$false; Scale=3 }
        $passes += @{ Rect=$identity; Contrast=$true; Scale=3 }

    }
    if ($isGame) {
        $passes += @{ Rect=[System.Drawing.Rectangle]::new([int]($image.Width * 0.25), [int]($image.Height * 0.3), [int]($image.Width * 0.5), [int]($image.Height * 0.55)); Contrast=$true; Scale=3 }
    }
    if ($isGame) {
        $detail = [System.Drawing.Rectangle]::new(0, [int]($image.Height * 0.65), [int]($image.Width * 0.25), [int]($image.Height * 0.25))
        $passes += @{ Rect=$detail; Contrast=$false }
        $passes += @{ Rect=$detail; Contrast=$true }
    }
    if ($isGame) {
        $confirmation = [System.Drawing.Rectangle]::new([int]($image.Width * 0.3), [int]($image.Height * 0.38), [int]($image.Width * 0.4), [int]($image.Height * 0.28))
        $passes += @{ Rect=$confirmation; Contrast=$false; Scale=3 }
        $passes += @{ Rect=$confirmation; Contrast=$true; Scale=3 }
        $counts = [System.Drawing.Rectangle]::new([int]($image.Width * 0.8), 0, [int]($image.Width * 0.2), [int]($image.Height * 0.1))
        $passes += @{ Rect=$counts; Contrast=$false; Scale=3 }
        $passes += @{ Rect=$counts; Contrast=$true; Scale=3 }
        # The agreement's italic buttons need a tighter crop and 3x magnification.
        $buttons = [System.Drawing.Rectangle]::new([int]($image.Width * 0.45), [int]($image.Height * 0.52), [int]($image.Width * 0.25), [int]($image.Height * 0.045))
        $passes += @{ Rect=$buttons; Contrast=$false; Scale=3 }
        $passes += @{ Rect=$buttons; Contrast=$true; Scale=3 }
    }
    if ($ConfirmationOnly) {
        # Always retain a full-frame error/screen read, then only the prompt
        # crops. Counters, handwritten details and agreement buttons add no
        # evidence when waiting for the exact character confirmation.
        $confirmation = [System.Drawing.Rectangle]::new([int]($image.Width * 0.3), [int]($image.Height * 0.38), [int]($image.Width * 0.4), [int]($image.Height * 0.28))
        $passes = @(
            @{ Rect=[System.Drawing.Rectangle]::new(0,0,$image.Width,$image.Height); Contrast=$false },
            @{ Rect=$confirmation; Contrast=$false; Scale=3 },
            @{ Rect=$confirmation; Contrast=$true; Scale=3 }
        )
    }
    if ($CharacterGridOnly) {
        $grid = [System.Drawing.Rectangle]::new([int]($image.Width * .7), [int]($image.Height * .5), [int]($image.Width * .3), [int]($image.Height * .4))
        $passes = @(
            @{ Rect=[System.Drawing.Rectangle]::new(0,0,$image.Width,$image.Height); Contrast=$false },
            @{ Rect=$grid; Contrast=$false; Scale=2 }
        )
    }
    $passIndex = 0
    foreach ($pass in $passes) {
        foreach ($line in @(ReadRegion $image $pass.Rect $pass.Contrast $pass.Scale)) {
            $duplicate = $false
            foreach ($existing in $lines) {
                if (($existing.Text -replace '[^a-zA-Z0-9]','') -eq ($line.Text -replace '[^a-zA-Z0-9]','') -and $existing.Words.Count -gt 0 -and $line.Words.Count -gt 0 -and [Math]::Abs($existing.Words[0].X - $line.Words[0].X) -lt 12 -and [Math]::Abs($existing.Words[0].Y - $line.Words[0].Y) -lt 12) { $duplicate = $true; break }
            }
            if (-not $duplicate) { $lines.Add($line) }
        }
        if ($passIndex -eq 0) {
            # Loading polls cannot authorize credentials or character identity.
            # The caller schedules a full fallback at least every third poll.
            if ($LoadingOnly) { break }
            # The full frame is normally sufficient for Enter-only game screens.
            # Supplemental crops are fallback evidence, not an unconditional
            # eleven-pass tax on every transition. POL identity reads still use
            # their independent crops before credentials can be authorized.
            $text = (@($lines | ForEach-Object { $_.Text }) -join ' ') -replace '[^a-zA-Z0-9]', ''
            $text = $text.ToLowerInvariant()
            $clearGameScreen = $isGame -and (
                ($text.Contains('selectcharacter') -and $text.Contains('createcharacter') -and $text.Contains('deletecharacter')) -or
                ($text.Contains('selectacharactertoplay') -and $text.Contains('morecharacterscanbemade')) -or
                ((@($lines | Where-Object { $_.Text -match '(?i)log\s*i\s*n\s*with\s*["“][A-Za-z]+["”]\s*\?' }).Count -eq 1) -and $text.Contains('yes') -and $text.Contains('no')) -or
                ($text.Contains('accept') -and ($text.Contains('decline') -or $text.Contains('useragreement')) -and
                    ($text.Contains('finalfantasyxi') -or $text.Contains('rulesofconduct'))))
            $clearPolScreen = $isPol -and (
                $text.Contains('softwarekeyboard') -or $text.Contains('connecttoplayonline') -or
                ($text.Contains('navigator') -and $text.Contains('logout') -and $text.Contains('finalfantasy')) -or
                ($text.Contains('awordtoourplayers') -and $text.Contains('play') -and $text.Contains('back')))
            if ($ConfirmationOnly) {
                # A list still visible behind a fading dialog must not suppress
                # the prompt crops that can read its actual selected name.
                $clearGameScreen = ((@($lines | Where-Object { $_.Text -match '(?i)log\s*i\s*n\s*with\s*["“][A-Za-z]+["”]\s*\?' }).Count -eq 1) -and $text.Contains('yes') -and $text.Contains('no')) -or
                    $text.Contains('errorcode') -or $text.Contains('incorrectpassword') -or $text.Contains('connectionfailed')
                $clearPolScreen = $false
            }
            if (!$CharacterGridOnly -and ($clearGameScreen -or $clearPolScreen)) { break }
        }
        $passIndex++
    }
    @{ Width=$image.Width; Height=$image.Height; Lines=@($lines.ToArray()) } | ConvertTo-Json -Depth 5 -Compress
} finally { $image.Dispose() }

}
if ($Server) {
    while ($null -ne ($command = [Console]::ReadLine())) {
        $command = $command.TrimStart([char]0xFEFF)
        if ($command -ceq 'read-pol') { ReadLoginFrame -ScreenKind POL }
        elseif ($command -ceq 'read-game') { ReadLoginFrame -ScreenKind FFXI }
        elseif ($command -ceq 'read-pol-loading') { ReadLoginFrame -LoadingOnly $true -ScreenKind POL }
        elseif ($command -ceq 'read-game-loading') { ReadLoginFrame -LoadingOnly $true -ScreenKind FFXI }
        elseif ($command -ceq 'read') { ReadLoginFrame }
        elseif ($command -ceq 'read-confirmation') { ReadLoginFrame -ConfirmationOnly $true -ScreenKind FFXI }
        elseif ($command -ceq 'read-grid') { ReadLoginFrame -CharacterGridOnly $true -ScreenKind FFXI }
        elseif ($command -ceq 'read-loading') { ReadLoginFrame $true }
        else { break }
    }
} else { ReadLoginFrame }
