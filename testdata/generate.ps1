# Regenerates the encoding samples in this folder (PowerShell 7+). The files are checked in, so this only
# needs to run again if the sample text changes; tests/TextEncoding.Core.Tests/TestData.cs holds the same strings.
# (The app can also make test files from your own text: "Test files…" in the main window.)
$ErrorActionPreference = 'Stop'
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
Set-Location $PSScriptRoot

# PowerShell treats curly quotes as string delimiters, so they and the non-BMP emoji are spliced in.
$ldq = [char]0x201C; $rdq = [char]0x201D; $lsq = [char]0x2018; $rsq = [char]0x2019
$emoji = [char]::ConvertFromUtf32(0x1F600)

# Danish, Swedish and German letters only: fits ISO-8859-1, Windows-1252 and the DOS code page 850.
$nordic = "Dansk: Rødgrød med fløde, blåbær på Ærø. ÆØÅ æøå`r`n" +
          "Svenska: Smörgåsbord och köttbullar i Göteborg. ÅÄÖ åäö`r`n" +
          "Deutsch: Grüße aus Köln, Äpfel, Öl und Übermaß. ÄÖÜ äöüß`r`n"

# Adds punctuation that only Windows-1252 has (curly quotes, en dash, euro sign).
$latin = "Hello, world!`r`n" + $nordic +
         "Tegn: ${ldq}citat${rdq} ${lsq}enkelt${rsq} – 5 € · naïve café`r`n"

# Adds characters no legacy code page here can hold: capital sharp s, Omega and a non-BMP emoji
# (a surrogate pair in UTF-16).
$unicode = $latin + "Unicode: ẞ Ω $emoji`r`n"

$ascii = "Plain ASCII text.`r`nLine two.`r`n"

function Save([string] $name, [System.Text.Encoding] $encoding, [string] $text, [bool] $bom) {
    $bytes = [System.Collections.Generic.List[byte]]::new()
    if ($bom) { $bytes.AddRange($encoding.GetPreamble()) }
    $bytes.AddRange($encoding.GetBytes($text))
    [System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot $name), $bytes.ToArray())
}

function Strict([int] $codePage) {
    [System.Text.Encoding]::GetEncoding($codePage,
        [System.Text.EncoderFallback]::ExceptionFallback, [System.Text.DecoderFallback]::ExceptionFallback)
}

$utf8    = [System.Text.UTF8Encoding]::new($true, $true)
$utf16le = [System.Text.UnicodeEncoding]::new($false, $true, $true)
$utf16be = [System.Text.UnicodeEncoding]::new($true, $true, $true)
$utf32le = [System.Text.UTF32Encoding]::new($false, $true, $true)
$utf32be = [System.Text.UTF32Encoding]::new($true, $true, $true)

Save 'ascii.txt'            $utf8    $ascii   $false
Save 'utf8.txt'             $utf8    $unicode $false
Save 'utf8-bom.txt'         $utf8    $unicode $true
Save 'utf16le-bom.txt'      $utf16le $unicode $true
Save 'utf16be-bom.txt'      $utf16be $unicode $true
Save 'utf16le-nobom.txt'    $utf16le $unicode $false
Save 'utf16be-nobom.txt'    $utf16be $unicode $false
Save 'utf32le-bom.txt'      $utf32le $unicode $true
Save 'utf32be-bom.txt'      $utf32be $unicode $true
Save 'utf8-latin.txt'       $utf8    $latin   $false
Save 'windows-1252.txt'     (Strict 1252)  $latin  $false
Save 'utf8-nordic.txt'      $utf8    $nordic  $false
Save 'iso-8859-1.txt'       (Strict 28591) $nordic $false
Save 'ibm850.txt'           (Strict 850)   $nordic $false

[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'empty.txt'), [byte[]]@())

# Valid UTF-8 up to a multi-byte sequence (the start of "€") cut off at the very end of the file.
$truncated = [System.Collections.Generic.List[byte]]::new()
$truncated.AddRange($utf8.GetBytes("Truncated: "))
$truncated.AddRange([byte[]]@(0xE2, 0x82))
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'utf8-truncated.txt'), $truncated.ToArray())
