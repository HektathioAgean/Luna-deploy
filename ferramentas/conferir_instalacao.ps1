# ============================================================
# LUNA - confere uma pasta do Luna contra o MANIFESTO.txt dela
#
#   powershell -ExecutionPolicy Bypass -File ferramentas\conferir_instalacao.ps1 [-Pasta <pasta>] [-ManifestoAnterior <MANIFESTO.txt>]
#
# Sem -Pasta, confere a instalação em %LOCALAPPDATA%\Programs\Luna.
# Cada arquivo do manifesto tem de existir com o mesmo tamanho e SHA-256.
# Arquivo fora do manifesto (fora o próprio MANIFESTO.txt e o desinstalador
# unins*) só é do aplicativo se o -ManifestoAnterior (o da versão
# substituída) o listar com o mesmo SHA-256: aí é obsoleto e é erro. Qualquer
# outro é do usuário ou desconhecido: fica e é listado à parte.
# ============================================================
param([string] $Pasta = (Join-Path $env:LOCALAPPDATA 'Programs\Luna'), [string] $ManifestoAnterior)

$ErrorActionPreference = 'Stop'

$manifesto = Join-Path $Pasta 'MANIFESTO.txt'
if (-not (Test-Path $manifesto)) { throw "conferir_instalacao: MANIFESTO.txt ausente em $Pasta" }

$base = (Resolve-Path $Pasta).Path.TrimEnd('\') + '\'
function LerManifesto([string] $arquivo) {
  $m = @{}
  foreach ($linha in [IO.File]::ReadAllLines($arquivo)) {
    $caminho, $tamanho, $hash = $linha.Split("`t")
    $m[$caminho] = @{ Tamanho = [long]$tamanho; Hash = $hash }
  }
  return $m
}
$esperados = LerManifesto $manifesto
$anteriores = if ($ManifestoAnterior) { LerManifesto $ManifestoAnterior } else { @{} }

$erros = @()
foreach ($caminho in $esperados.Keys) {
  $arquivo = Join-Path $base $caminho.Replace('/', '\')
  if (-not (Test-Path -LiteralPath $arquivo)) { $erros += "ausente: $caminho"; continue }
  $e = $esperados[$caminho]
  if ((Get-Item -LiteralPath $arquivo).Length -ne $e.Tamanho) { $erros += "tamanho difere: $caminho"; continue }
  if ((Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash -ne $e.Hash) { $erros += "SHA-256 difere: $caminho" }
}

# A mais: obsoleto do aplicativo só com prova (manifesto anterior, mesmo SHA-256).
$extras = @(Get-ChildItem $Pasta -Recurse -File | ForEach-Object { $_.FullName.Substring($base.Length).Replace('\', '/') } |
  Where-Object { $_ -ne 'MANIFESTO.txt' -and $_ -notmatch '^unins\d+\.(exe|dat|msg)$' -and -not $esperados.ContainsKey($_) })
$usuario = @()
foreach ($x in $extras) {
  $hash = (Get-FileHash -LiteralPath (Join-Path $base $x.Replace('/', '\')) -Algorithm SHA256).Hash
  if ($anteriores.ContainsKey($x) -and $anteriores[$x].Hash -eq $hash) { $erros += "obsoleto da versão anterior: $x" }
  else { $usuario += $x }
}

if ($usuario.Count) { Write-Output "fora do manifesto (do usuário ou desconhecidos, preservados): $($usuario -join ', ')" }
if ($erros.Count) {
  $erros | ForEach-Object { Write-Output $_ }
  throw "conferir_instalacao: $($erros.Count) problema(s) em $Pasta"
}
Write-Output "conferido: $($esperados.Count) arquivos iguais ao MANIFESTO.txt em $Pasta"
