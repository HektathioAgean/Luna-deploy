# ============================================================
# LUNA - confere uma pasta do Luna contra o MANIFESTO.txt dela
#
#   powershell -ExecutionPolicy Bypass -File ferramentas\conferir_instalacao.ps1 [-Pasta <pasta>]
#
# Sem -Pasta, confere a instalação em %LOCALAPPDATA%\Programs\Luna.
# Cada arquivo do manifesto tem de existir com o mesmo tamanho e SHA-256, e
# não pode haver arquivo do programa a mais. Fora da conferência: o próprio
# MANIFESTO.txt, o desinstalador (unins*) e arquivos que não são do programa
# (os do usuário), listados à parte.
# ============================================================
param([string] $Pasta = (Join-Path $env:LOCALAPPDATA 'Programs\Luna'))

$ErrorActionPreference = 'Stop'

$manifesto = Join-Path $Pasta 'MANIFESTO.txt'
if (-not (Test-Path $manifesto)) { throw "conferir_instalacao: MANIFESTO.txt ausente em $Pasta" }

$base = (Resolve-Path $Pasta).Path.TrimEnd('\') + '\'
$esperados = @{}
foreach ($linha in [IO.File]::ReadAllLines($manifesto)) {
  $caminho, $tamanho, $hash = $linha.Split("`t")
  $esperados[$caminho] = @{ Tamanho = [long]$tamanho; Hash = $hash }
}

$erros = @()
foreach ($caminho in $esperados.Keys) {
  $arquivo = Join-Path $base $caminho.Replace('/', '\')
  if (-not (Test-Path -LiteralPath $arquivo)) { $erros += "ausente: $caminho"; continue }
  $e = $esperados[$caminho]
  if ((Get-Item -LiteralPath $arquivo).Length -ne $e.Tamanho) { $erros += "tamanho difere: $caminho"; continue }
  if ((Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash -ne $e.Hash) { $erros += "SHA-256 difere: $caminho" }
}

# A mais: só conta como erro o que tem cara de arquivo do programa.
$extras = @(Get-ChildItem $Pasta -Recurse -File | ForEach-Object { $_.FullName.Substring($base.Length).Replace('\', '/') } |
  Where-Object { $_ -ne 'MANIFESTO.txt' -and $_ -notmatch '^unins\d+\.(exe|dat|msg)$' -and -not $esperados.ContainsKey($_) })
$programa = @($extras | Where-Object { $_ -match '\.(dll|exe|json)$' })
foreach ($x in $programa) { $erros += "arquivo do programa a mais: $x" }
$usuario = @($extras | Where-Object { $programa -notcontains $_ })

if ($usuario.Count) { Write-Output "fora do manifesto (não são do programa): $($usuario -join ', ')" }
if ($erros.Count) {
  $erros | ForEach-Object { Write-Output $_ }
  throw "conferir_instalacao: $($erros.Count) problema(s) em $Pasta"
}
Write-Output "conferido: $($esperados.Count) arquivos iguais ao MANIFESTO.txt em $Pasta"
