# ============================================================
# LUNA - publica o Luna Desktop portátil (ZIP self-contained win-x64)
#
#   powershell -ExecutionPolicy Bypass -File ferramentas\publicar_desktop.ps1 [-PermitirArvoreSuja]
#
# Gera em dist\ (fora do git):
#   Luna-<versão>-<commit>-win-x64\            a pasta publicada (Luna.exe + runtime)
#   Luna-<versão>-<commit>-win-x64.zip         o ZIP para distribuir
#   Luna-<versão>-<commit>-win-x64.zip.sha256  o SHA-256 do ZIP
#   Luna-<versão>-<commit>-win-x64.MANIFESTO.txt  caminho, tamanho e SHA-256 de cada arquivo
#
# THIRD-PARTY-NOTICES.txt sai do que foi de fato publicado (Luna.deps.json):
# cada pacote e runtime pack com a licença que ele mesmo declara (nuspec ou
# arquivo de licença) e os avisos que ele traz. Nada é presumido: licença sem
# texto conhecido ou componente sem licença resolvida faz o script falhar.
# Depois de compactar, o ZIP é extraído e conferido arquivo a arquivo.
# ============================================================
param([switch] $PermitirArvoreSuja)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$raiz = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $raiz 'dotnet\src\Luna.Desktop\Luna.Desktop.csproj'
$utf8 = New-Object System.Text.UTF8Encoding $false

function Falhar([string] $motivo) { throw "publicar_desktop: $motivo" }

# ---------- versão, commit e árvore ----------
$sujo = [bool](git -C $raiz status --porcelain)
if ($sujo -and -not $PermitirArvoreSuja) { Falhar 'há alterações não commitadas (use -PermitirArvoreSuja só para teste).' }
$commit = (git -C $raiz rev-parse --short=7 HEAD).Trim()
if ($sujo) { $commit += '-sujo' }
$versao = ([xml](Get-Content $projeto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $versao) { Falhar "sem <Version> em $projeto" }
$nome = "Luna-$versao-$commit-win-x64"
$dist = Join-Path $raiz 'dist'
$pasta = Join-Path $dist $nome
$zip = "$pasta.zip"
Remove-Item -Recurse -Force $pasta, $zip, "$zip.sha256", "$pasta.MANIFESTO.txt" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist | Out-Null

# ---------- publicação ----------
# Sem PDB: os símbolos carregariam caminhos locais da máquina de build.
& dotnet publish $projeto -c Release -r win-x64 --self-contained true -o $pasta -p:DebugType=None -p:DebugSymbols=false --nologo -v q
if ($LASTEXITCODE -ne 0) { Falhar "dotnet publish terminou com código $LASTEXITCODE" }

# ---------- licenças de terceiros ----------
$pacotes = ((& dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
if (-not (Test-Path $pacotes)) { Falhar "pasta de pacotes do NuGet não encontrada: $pacotes" }

# Textos das licenças SPDX que algum componente declarar. Licença fora desta lista faz o script falhar.
$textosSpdx = @{
  'MIT' = @'
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@
}

# Componentes que só declaram licenseUrl: resolução conferida à mão, com a fonte. Um por linha.
$resolvidos = @{
  'OpenTK.GLWpfControl' = @{
    Licenca = 'MIT'
    Fonte = 'https://github.com/varon/GLWpfControl/blob/master/LICENSE.md (conferido em 20/10/2026)'
    Copyright = 'Copyright (c) 2019 varon'
  }
  'Microsoft.Windows.SDK.NET.Ref' = @{
    Licenca = 'Microsoft Software License Terms - Windows SDK (Distributable Code)'
    Fonte = 'https://aka.ms/WinSDKLicenseURL e https://learn.microsoft.com/en-us/legal/windows-sdk/redist (conferidos em 21/10/2026)'
    Copyright = 'Copyright (c) Microsoft Corporation'
    Texto = @'
Lista de redistribuição do Windows SDK (learn.microsoft.com/en-us/legal/windows-sdk/redist):
"The files included in the Microsoft.Windows.SDK.NET.Ref Nuget package
(https://www.nuget.org/packages/Microsoft.windows.sdk.net.ref) may be
distributed unmodified as a NuGet package, or as part of your program in order
to enable your application to call WinRT Apis."

Distribuídos aqui, sem modificação: Microsoft.Windows.SDK.NET.dll e WinRT.Runtime.dll.

Termos de licença do Windows SDK, seção 2.a (Distributable Code), na íntegra em
https://aka.ms/WinSDKLicenseURL. Requisitos de distribuição (2.a.ii):
"For any Distributable Code you distribute, you must
 - Add significant primary functionality to it in your programs;
 - For any Distributable Code having a filename extension of .lib, distribute
   only the results of running such Distributable Code through a linker with
   your program;
 - Distribute Distributable Code included in a setup program only as part of
   that setup program without modification;
 - Require distributors and external end users to agree to terms that protect
   it at least as much as this agreement;
 - (...)
 - Display your valid copyright notice on your programs; and
 - Indemnify, defend, and hold harmless Microsoft from any claims, including
   attorneys' fees, related to the distribution or use of your programs."
Restrições (2.a.iii): não alterar avisos de copyright, marca ou patente; não
usar as marcas da Microsoft no nome do programa nem sugerir endosso; não
distribuir para outra plataforma que não o sistema operacional da Microsoft;
não incluir em programas maliciosos, enganosos ou ilegais; não sujeitar o
código a licença excluída.
'@
  }
}

function Nuspec([string] $id, [string] $versaoPacote) {
  $dir = Join-Path $pacotes "$($id.ToLower())\$versaoPacote"
  $arquivo = Join-Path $dir "$($id.ToLower()).nuspec"
  if (-not (Test-Path $arquivo)) { Falhar "nuspec ausente: $arquivo" }
  return @{ Dir = $dir; Meta = ([xml](Get-Content $arquivo -Raw)).package.metadata }
}

# Avisos e licenças que o pacote traz na raiz (THIRD-PARTY-NOTICES, LICENSE, COPYING...).
function ArquivosDeAviso([string] $dir) {
  return @(Get-ChildItem $dir -File | Where-Object { $_.Name -match '^(third[-_ ]?party[-_ ]?notices?|thirdpartynotices|license|licence|copying)(\.(txt|md))?$' } | Sort-Object Name)
}

$deps = Get-Content (Join-Path $pasta 'Luna.deps.json') -Raw | ConvertFrom-Json
$componentes = @()
foreach ($l in $deps.libraries.PSObject.Properties) {
  if ($l.Value.type -eq 'project') { continue }
  $chave = $l.Name -replace '^runtimepack\.', ''
  $id, $v = $chave.Split('/')
  $n = Nuspec $id $v
  $m = $n.Meta
  $c = [ordered]@{ Id = $id; Versao = $v; Tipo = $l.Value.type; Licenca = $null; Copyright = $null; Fonte = $null; Textos = @(); Avisos = @() }
  $c.Copyright = if ($m.copyright) { "$($m.copyright)" } else { "Autores: $($m.authors)" }
  $c.Fonte = if ($m.projectUrl) { "$($m.projectUrl)" } elseif ($m.repository.url) { "$($m.repository.url)" } else { "https://www.nuget.org/packages/$id/$v" }
  if ($m.license -and $m.license.type -eq 'expression') {
    $c.Licenca = $m.license.'#text'
    foreach ($spdx in ($c.Licenca -split '\s+(?:AND|OR|WITH)\s+|[()]' | Where-Object { $_ })) {
      if (-not $textosSpdx.ContainsKey($spdx)) { Falhar "$id $v declara '$spdx', sem texto conhecido no script: incluir o texto e conferir as obrigações." }
      $c.Textos += "Licença $spdx`r`n`r`n$($c.Copyright)`r`n`r`n$($textosSpdx[$spdx])"
    }
  } elseif ($m.license -and $m.license.type -eq 'file') {
    $arquivo = Join-Path $n.Dir $m.license.'#text'
    if (-not (Test-Path $arquivo)) { Falhar "$id $v declara a licença no arquivo $($m.license.'#text'), ausente no pacote." }
    $c.Licenca = "arquivo do pacote: $($m.license.'#text')"
    $c.Textos += [IO.File]::ReadAllText($arquivo)
  } elseif ($resolvidos.ContainsKey($id)) {
    $r = $resolvidos[$id]
    $c.Licenca = $r.Licenca; $c.Copyright = $r.Copyright; $c.Fonte = $r.Fonte
    $c.Textos += $(if ($r.Texto) { $r.Texto } else { "Licença $($r.Licenca)`r`n`r`n$($r.Copyright)`r`n`r`n$($textosSpdx[$r.Licenca])" })
  } else {
    Falhar "$id $v não declara licença no nuspec (licenseUrl: $($m.licenseUrl)) e não está resolvido no script."
  }
  foreach ($a in (ArquivosDeAviso $n.Dir)) {
    if ($m.license.type -eq 'file' -and $a.Name -eq $m.license.'#text') { continue }
    $c.Avisos += @{ Nome = $a.Name; Texto = [IO.File]::ReadAllText($a.FullName) }
  }
  $componentes += [pscustomobject]$c
}

$sb = New-Object System.Text.StringBuilder
$traco = '=' * 78
[void]$sb.AppendLine("LUNA $versao ($commit) - COMPONENTES DE TERCEIROS")
[void]$sb.AppendLine($traco)
[void]$sb.AppendLine('O Luna é distribuído sob a Licença Apache 2.0 (LICENSE.txt). Esta pasta inclui os')
[void]$sb.AppendLine('componentes abaixo, cada um sob a licença que ele mesmo declara, com os avisos que')
[void]$sb.AppendLine('ele traz. Gerado por ferramentas/publicar_desktop.ps1 a partir de Luna.deps.json.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('RESUMO')
foreach ($c in $componentes) { [void]$sb.AppendLine(("- {0} {1} ({2}): {3}" -f $c.Id, $c.Versao, $c.Tipo, $c.Licenca)) }
foreach ($c in $componentes) {
  [void]$sb.AppendLine(''); [void]$sb.AppendLine($traco)
  [void]$sb.AppendLine("$($c.Id) $($c.Versao)")
  [void]$sb.AppendLine("Licença: $($c.Licenca)")
  [void]$sb.AppendLine("Copyright: $($c.Copyright)")
  [void]$sb.AppendLine("Fonte: $($c.Fonte)")
  [void]$sb.AppendLine($traco)
  foreach ($t in $c.Textos) { [void]$sb.AppendLine(''); [void]$sb.AppendLine($t.Trim()) }
  foreach ($a in $c.Avisos) { [void]$sb.AppendLine(''); [void]$sb.AppendLine("--- $($a.Nome) (incluído no pacote) ---"); [void]$sb.AppendLine($a.Texto.Trim()) }
}
[IO.File]::WriteAllText((Join-Path $pasta 'THIRD-PARTY-NOTICES.txt'), $sb.ToString(), $utf8)
Copy-Item (Join-Path $raiz 'LICENSE') (Join-Path $pasta 'LICENSE.txt')
# LEIA-ME com o nome do ZIP e o número de arquivos desta publicação (+1: o MANIFESTO.txt, gravado depois).
$leiaMe = Join-Path $pasta 'LEIA-ME.txt'
[IO.File]::WriteAllText($leiaMe, '', $utf8)
$texto = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'distribuicao\LEIA-ME.txt')).
  Replace('{{ZIP}}', "$nome.zip").
  Replace('{{ARQUIVOS}}', [string]((Get-ChildItem $pasta -Recurse -File).Count + 1))
if ($texto -match '\{\{') { Falhar 'LEIA-ME com marcador não substituído.' }
[IO.File]::WriteAllText($leiaMe, $texto, $utf8)

# ---------- conferências da pasta ----------
if (-not (Test-Path (Join-Path $pasta 'Luna.exe'))) { Falhar 'Luna.exe não foi publicado.' }
$rc = Get-Content (Join-Path $pasta 'Luna.runtimeconfig.json') -Raw | ConvertFrom-Json
$fw = @($rc.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
if (-not ($fw -contains 'Microsoft.NETCore.App' -and $fw -contains 'Microsoft.WindowsDesktop.App')) { Falhar "não é self-contained (includedFrameworks: $($fw -join ', '))." }
$pdb = @(Get-ChildItem $pasta -Recurse -Filter *.pdb)
if ($pdb.Count) { Falhar "há .pdb na publicação: $($pdb.Name -join ', ')" }

# ---------- manifesto ----------
function Manifesto([string] $dir) {
  $base = (Resolve-Path $dir).Path.TrimEnd('\') + '\'
  # Uma linha por arquivo: caminho relativo, tamanho e SHA-256, separados por tabulação.
  return @(Get-ChildItem $dir -Recurse -File | Where-Object { $_.Name -ne 'MANIFESTO.txt' } | Sort-Object { $_.FullName.Substring($base.Length) } | ForEach-Object {
    "$($_.FullName.Substring($base.Length).Replace('\', '/'))`t$($_.Length)`t$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)"
  })
}
$manifesto = Manifesto $pasta
[IO.File]::WriteAllLines((Join-Path $pasta 'MANIFESTO.txt'), [string[]]$manifesto, $utf8)
Copy-Item (Join-Path $pasta 'MANIFESTO.txt') "$pasta.MANIFESTO.txt"

# ---------- ZIP, integridade e hash ----------
[IO.Compression.ZipFile]::CreateFromDirectory($pasta, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$teste = Join-Path ([IO.Path]::GetTempPath()) "luna_zip_$([guid]::NewGuid().ToString('N'))"
try {
  [IO.Compression.ZipFile]::ExtractToDirectory($zip, $teste)
  $extraido = Manifesto $teste
  $diferencas = @(Compare-Object $manifesto $extraido)
  if ($diferencas.Count) { Falhar "o ZIP extraído difere da pasta publicada em $($diferencas.Count) linha(s) do manifesto." }
  if (-not (Test-Path (Join-Path $teste 'MANIFESTO.txt'))) { Falhar 'MANIFESTO.txt ausente no ZIP.' }
} finally {
  Remove-Item -Recurse -Force $teste -ErrorAction SilentlyContinue
}
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zip.sha256", "$hash  $(Split-Path $zip -Leaf)`r`n", $utf8)

$arquivos = $manifesto.Count + 1
$tamanhoPasta = (Get-ChildItem $pasta -Recurse -File | Measure-Object Length -Sum).Sum
Write-Output ("Luna {0} ({1})" -f $versao, $commit)
Write-Output ("pasta: {0} arquivos, {1:N1} MB" -f $arquivos, ($tamanhoPasta / 1MB))
Write-Output ("ZIP: {0} ({1:N1} MB), integridade conferida" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB))
Write-Output ("SHA-256: {0}" -f $hash)
Write-Output ("licenças: {0} componentes de terceiros resolvidos" -f $componentes.Count)
