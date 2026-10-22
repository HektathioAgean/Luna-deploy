# ============================================================
# LUNA - gera o instalador por usuário (Inno Setup 6) do Luna Desktop
#
#   powershell -ExecutionPolicy Bypass -File ferramentas\publicar_instalador.ps1 [-ISCC <ISCC.exe>] [-PermitirArvoreSuja]
#
# 1. Roda ferramentas\publicar_desktop.ps1 (pasta, ZIP portátil, .sha256 e
#    manifesto, sem nenhuma mudança).
# 2. Confere a pasta publicada contra o MANIFESTO.txt dela.
# 3. Compila ferramentas\instalador\Luna.iss com essa pasta.
# Gera em dist\ (fora do git), além do que o publicar_desktop.ps1 gera:
#   Luna-Setup-<versão>-win-x64.exe            o instalador
#   Luna-Setup-<versão>-win-x64.exe.sha256     o SHA-256 do instalador
#   Luna-Setup-<versão>-win-x64.BUILD.txt      versão, commit e origem do build
# ============================================================
param([string] $ISCC, [switch] $PermitirArvoreSuja)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $raiz 'dotnet\src\Luna.Desktop\Luna.Desktop.csproj'
$utf8 = New-Object System.Text.UTF8Encoding $false

function Falhar([string] $motivo) { throw "publicar_instalador: $motivo" }

# ---------- compilador ----------
$candidatos = @($ISCC, (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
  (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
  (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
  (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')) | Where-Object { $_ }
$iscc = $candidatos | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Falhar 'ISCC.exe (Inno Setup 6) não encontrado: instale o Inno Setup 6 ou passe -ISCC.' }

# ---------- publicação portátil (mesma pasta do ZIP) ----------
$argumentos = @{}
if ($PermitirArvoreSuja) { $argumentos.PermitirArvoreSuja = $true }
& (Join-Path $PSScriptRoot 'publicar_desktop.ps1') @argumentos

# Mesmo nome que o publicar_desktop.ps1 acabou de usar.
$sujo = [bool](git -C $raiz status --porcelain)
$commit = (git -C $raiz rev-parse --short=7 HEAD).Trim()
if ($sujo) { $commit += '-sujo' }
$versao = ([xml](Get-Content $projeto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path $raiz 'dist'
$nome = "Luna-$versao-$commit-win-x64"
$pasta = Join-Path $dist $nome
if (-not (Test-Path (Join-Path $pasta 'MANIFESTO.txt'))) { Falhar "pasta publicada não encontrada: $pasta" }

# ---------- a pasta é a do manifesto, sem nada a mais ----------
$conferencia = & (Join-Path $PSScriptRoot 'conferir_instalacao.ps1') -Pasta $pasta
if ($conferencia -match 'fora do manifesto') { Falhar "pasta publicada com arquivo fora do manifesto: $conferencia" }

# ---------- instalador ----------
$instalador = Join-Path $dist "Luna-Setup-$versao-win-x64.exe"
$build = Join-Path $dist "Luna-Setup-$versao-win-x64.BUILD.txt"
Remove-Item -Force $instalador, "$instalador.sha256", $build -ErrorAction SilentlyContinue
$saida = & $iscc "/DVersao=$versao" "/DOrigem=$pasta" "/O$dist" (Join-Path $PSScriptRoot 'instalador\Luna.iss') 2>&1
if ($LASTEXITCODE -ne 0) { $saida | Write-Output; Falhar "ISCC terminou com código $LASTEXITCODE" }
# O ISCC.exe não traz versão no arquivo: vale a que ele imprime.
$versaoIscc = ($saida | Select-String 'Compiler engine version: (.+)$' | Select-Object -First 1).Matches.Groups[1].Value
if (-not $versaoIscc) { Falhar 'versão do Inno Setup não encontrada na saída do ISCC.' }
if (-not (Test-Path $instalador)) { Falhar "instalador não gerado: $instalador" }

$hash = (Get-FileHash $instalador -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$instalador.sha256", "$hash  $(Split-Path $instalador -Leaf)`r`n", $utf8)

$hashZip = ([IO.File]::ReadAllText((Join-Path $dist "$nome.zip.sha256")) -split '\s+')[0]
$hashManifesto = (Get-FileHash (Join-Path $pasta 'MANIFESTO.txt') -Algorithm SHA256).Hash
$arquivos = @(Get-Content (Join-Path $pasta 'MANIFESTO.txt')).Count + 1
[IO.File]::WriteAllLines($build, [string[]]@(
  "Luna $versao - instalador por usuário (win-x64)",
  "Instalador: $(Split-Path $instalador -Leaf)",
  "SHA-256 do instalador: $hash",
  "Commit: $commit$(if ($sujo) { ' (árvore com alterações não commitadas: só para teste)' })",
  "Repositório: https://github.com/HektathioAgean/Luna-deploy",
  "Gerado em: $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'))",
  "Compilador: $versaoIscc ($iscc)",
  "Origem: $nome ($arquivos arquivos, idênticos ao ZIP portátil)",
  "SHA-256 do MANIFESTO.txt: $hashManifesto",
  "ZIP portátil correspondente: $nome.zip",
  "SHA-256 do ZIP: $hashZip",
  "Destino da instalação: %LOCALAPPDATA%\Programs\Luna (sem administrador)"
), $utf8)

Write-Output ("instalador: {0} ({1:N1} MB)" -f (Split-Path $instalador -Leaf), ((Get-Item $instalador).Length / 1MB))
Write-Output ("SHA-256: {0}" -f $hash)
Write-Output ("compilador: {0}" -f $versaoIscc)
