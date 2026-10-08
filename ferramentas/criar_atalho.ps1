# ============================================================
# LUNA - cria o atalho "Luna" na area de trabalho
#
#   powershell -ExecutionPolicy Bypass -File ferramentas\criar_atalho.ps1
#
# O atalho roda Luna.pyw com o pythonw.exe do .venv (sem console).
# Rode de novo se mover a pasta do projeto ou recriar o .venv.
# ============================================================

$ErrorActionPreference = "Stop"

$raiz = Split-Path -Parent $PSScriptRoot
$pythonw = Join-Path $raiz ".venv\Scripts\pythonw.exe"
if (-not (Test-Path $pythonw)) {
    $pythonw = (Get-Command pythonw.exe).Source
}

$launcher = Join-Path $raiz "Luna.pyw"
$icone = Join-Path $PSScriptRoot "luna.ico"
$destino = Join-Path ([Environment]::GetFolderPath("Desktop")) "Luna.lnk"

$atalho = (New-Object -ComObject WScript.Shell).CreateShortcut($destino)
$atalho.TargetPath = $pythonw
$atalho.Arguments = "`"$launcher`""
$atalho.WorkingDirectory = $raiz
$atalho.Description = "Luna - Analise de tempos operacionais"
if (Test-Path $icone) { $atalho.IconLocation = "$icone,0" }
$atalho.Save()

Write-Host "Atalho criado: $destino"
Write-Host "  Python : $pythonw"
