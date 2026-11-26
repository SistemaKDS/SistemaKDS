$ErrorActionPreference = "Stop"
$carpeta = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $carpeta
Add-Type -AssemblyName System.Speech
$voz = New-Object System.Speech.Synthesis.SpeechSynthesizer
$instaladas = $voz.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo }
$instaladas | ForEach-Object { "{0}|{1}|{2}" -f $_.Name, $_.Culture.Name, $_.Gender } | Out-File -FilePath "voces.txt" -Encoding utf8
$espanol = @($instaladas | Where-Object { $_.Culture.TwoLetterISOLanguageName -eq "es" })
if ($espanol.Count -eq 0) {
    Write-Host ""
    Write-Host "No hay voces en espanol instaladas en Windows." -ForegroundColor Red
    Write-Host "Ve a Configuracion > Hora e idioma > Voz > Agregar voces, instala una de Espanol (Mexico) y vuelve a abrir este archivo."
    exit 1
}
$orden = @("es-CO", "es-MX", "es-US", "es-ES")
$elegida = $null
foreach ($cultura in $orden) {
    $candidata = $espanol | Where-Object { $_.Culture.Name -eq $cultura } | Sort-Object { if ($_.Gender -eq "Female") { 0 } else { 1 } } | Select-Object -First 1
    if ($candidata) { $elegida = $candidata; break }
}
if (-not $elegida) { $elegida = $espanol[0] }
$voz.SelectVoice($elegida.Name)
$voz.Rate = 0
$voz.Volume = 100
Write-Host ("Voz: {0} ({1})" -f $elegida.Name, $elegida.Culture.Name) -ForegroundColor Green
$lineas = Get-Content -Path "lineas.txt" -Encoding UTF8
foreach ($linea in $lineas) {
    if ($linea.Trim().Length -eq 0) { continue }
    $partes = $linea.Split("|", 2)
    $archivo = "voz_" + $partes[0] + ".wav"
    $voz.SetOutputToWaveFile((Join-Path $carpeta $archivo))
    $voz.Speak($partes[1])
    $voz.SetOutputToNull()
    Write-Host ("Listo " + $archivo)
}
$voz.Dispose()
Write-Host ""
Write-Host "Terminado. Avisale a Claude." -ForegroundColor Green
