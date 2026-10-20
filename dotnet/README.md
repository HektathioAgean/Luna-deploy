# Luna em C#/.NET 10 (migração em andamento)

Porta do motor Python (`src/`) para C#, em PRs pequenos, cada um validado
contra o contrato matemático gerado pelo Python em `tests/dados/contrato/`
(tag `luna-py-ref-v1.1`). O Python continua sendo o Luna em uso até a
substituição ser aprovada.

| Projeto | Conteúdo | Dependências |
|---|---|---|
| `src/Luna.Domain` | regras e funções matemáticas puras | nenhuma (só BCL) |
| `src/Luna.Application` | caso de uso `ProcessarBase`, `ParametrosProcessamento`, porta `ILeitorDeBase`, `SaidasExportacao` (as 8 tabelas da exportação), `PaineisDeCliente` (painel do cliente) | Domain |
| `src/Luna.Infrastructure` | `CarregadorDeArquivo`, leitura de CSV e de XLSX/XLSM, `Exportador` (gravação em Excel e ZIP/CSV) | Application, DocumentFormat.OpenXml 3.5.1 (MIT) |
| `src/Luna.Apresentacao` | ViewModels da interface (MVVM), sem WPF: Base, Validação, Processamento, mensagens de erro | Application, CommunityToolkit.Mvvm 8.4.2 (MIT) |
| `src/Luna.Desktop` | aplicativo Windows: WPF .NET 10, tema Fluent do sistema, navegação lateral | Apresentacao, Infrastructure |
| `tests/Luna.Domain.Tests` | testes de unidade e paridade com o contrato | xUnit |
| `tests/Luna.Integration.Tests` | leitura de arquivo e fluxo ponta a ponta contra o Python | xUnit |
| `tests/Luna.Apresentacao.Tests` | ViewModels sem WPF: estados, progresso, cancelamento, erros | xUnit |

## Estado

PR1 — `Luna.Domain`: quantil tipo 7 e mediana (forma do `groupby` do pandas),
os três arredondamentos do motor (inteiro half-even, 2 casas do numpy e
`round` do Python), ajuste percentual e formatação `HH:MM:SS`.

PR2 — duração em segundos (nanossegundos / 10⁹, com fração), motivos de
inconsistência na ordem do motor e regras operacionais (expurgo `< 300 s`,
anomalia `> 21.600 s`, limites incluídos). A entrada ainda são datas já
convertidas; a leitura de datas em texto vem no PR5.

PR3 — `TemposPorCliente` (21 campos na ordem do contrato), `Boxplot` (IQR
1,5, limites incluídos, IQR zero sem corte), seleção dos últimos N por
`Chegou_em` e `Tempo_Sec` decrescentes, fallback de poucos apontamentos,
ajuste depois do arredondamento, gap e percentual (2 casas do numpy) e `Kpis`.
Códigos de cliente comparados e ordenados de forma ordinal.

PR4 — janela circular (`JanelaCircular`, `JanelasDeAtendimento`, 14 campos na
ordem do contrato): minutos desde a meia-noite (segundos/60, sem fração de
segundo), menor arco com `max(1, ⌈n·cobertura⌉)` pontos em double, primeira
menor largura vence o empate, cobertura real circular com tolerância 1e-9,
resto com o sinal do divisor (`%` do Python), `HH:MM` half-even, período e
horário comercial pelo centro circular, largura e cobertura com o `round` do
Python. Histórico completo do cliente (diretriz F9).

PR5 — leitura dos dados em texto: `InterpretacaoDatas` (Chegou_em e
Finalizada_em convertidas juntas), `ParserIso8601` (porta do
`parse_iso_8601_datetime` do pandas 2.3.3, em C), `ParserDataBarra` (padrões
do `_strptime` do Python para dd/mm e mm/dd), rejeições com as mensagens
exatas do motor (serial do Excel, fuso misto, fuso numa coluna só),
`CodigoCliente` (zeros à esquerda preservados), `EsquemaColunas` (aliases,
`__dupN`, análise) e `Transformacao` (texto bruto → registro classificado).
`TextoPython` dá a semântica do Python a `strip`, `lower` e à ordenação.

PR6 — importação local e fluxo ponta a ponta. `ProcessarBase` repete o
`_executar` do Python: leitura, base vazia, padronização e validação das
colunas, transformação, tempos por cliente, KPIs e janelas. `LeitorCsv` é o
`read_csv_safely`: seis tentativas de separador × encoding e, em cada uma, a
porta do tokenizer C do pandas 2.3.3 (aspas, linhas vazias e só com espaço,
CR/LF/CRLF, campo a mais/a menos, índice implícito), o cabeçalho do
`parsers.pyx` (`Unnamed: i`, duplicatas `.N`) e os marcadores de nulo do
`read_csv` (`NULL`, `NA`, `#N/A`, `None`, vazio...). Mensagens iguais às do
Python.

**Memória:** o CSV é lido em memória como bytes (`File.ReadAllBytes`) e
decodificado em blocos de 64 KB durante a tokenização (PR7: a `string` do
arquivo inteiro não existe mais); cada linha vira um `string[]` usado direto
na `TabelaBruta`. A `TabelaBruta` continua com todas as linhas antes do
processamento. Benchmarks abaixo, inclusive com uma base real (PR9).

PR6b — leitura de `.xlsx`/`.xlsm` (`LeitorXlsx`) com DocumentFormat.OpenXml
3.5.1 (MIT): pacote, `workbook.xml`, `styles.xml` e `<dimension>` pelo SDK;
a planilha e as strings compartilhadas em sequência pelo `XmlReader` do .NET
(`XmlDaPlanilha`, PR7 — antes `LoadCurrentElement` por `<row>`), aceitando o
namespace transitional e o strict como o SDK. Reproduz o
`read_excel_safely` com o openpyxl 3.1.5 (read_only, data_only): primeira
planilha na ordem do workbook (gráfico pulado, oculta conta), `iter_rows`
com largura/altura do `<dimension>` e linhas ausentes no meio, `parse_cell`
(int/float, datas e durações pelo estilo, épocas 1900/1904 e o dia fictício
de 1900, hora para 0 ≤ valor < 1, estouro vira `#VALUE!`, strings
compartilhadas sem fonética e com o `replace('x005F_', '')`, inline, bool,
erro, `t="d"`, valor em cache de fórmula), coluna do código como texto com a
regra `00000`, linhas vazias do fim descartadas e a inferência de tipo de
coluna do pandas (inteiro com vazio vira float). O domínio passou a aceitar
os tipos de célula do Excel na coluna de data como o pandas (`date` vira
meia-noite; inteiro é lido como texto ISO; float, bool, hora e duração ficam
inválidos; número na faixa de serial é rejeitado).

PR8 — preparação das oito saídas da exportação (`SaidasExportacao`,
`TabelaSaida`, `ColunaSaida`): Base Bruta, Base Validada, Base Detalhada,
Inconsistencias, Expurgados, Anomalias, Medianas Cliente e Janelas
Atendimento, na ordem de `SAIDAS` (`src/engine.py`), com as colunas, os dtypes,
as linhas e a ordem dos DataFrames que `exportar()` entrega aos gravadores:

- bases por linha: colunas padronizadas com o valor lido e as derivadas de
  `transform_base` na ordem dela; nome derivado que já existe na base (ex.:
  `Tempo_Sec`, `Ano`) troca a coluna no lugar, como o pandas, senão vai para o
  fim. Base Detalhada = merge left com as 11 colunas de referência das
  medianas, `Cod_Cliente` como object e `_x`/`_y` em nome repetido;
- dtype de cada coluna como o `pd.DataFrame(linhas)` infere (int64, uint64,
  float64, bool, datetime64[ns] na faixa do ns, timedelta64[ns], object), as
  derivadas com Int8/Int16/string e o fuso de cada coluna de data
  (`datetime64[ns, UTC-03:00]`). Para isso o domínio passou a devolver o
  offset de cada coluna lida com fuso (`FusoChegou`/`FusoFinalizada`), sem
  mudar nenhum cálculo;
- tabela sem colunas (`pd.DataFrame()`: sem linha válida, sem processado,
  sem janela, esquema inválido) distinta de tabela vazia com cabeçalho, como
  no motor (o Excel do Python deixa a aba vazia nos dois casos; o CSV traz o
  cabeçalho no segundo);
- rastreabilidade: `LinhasDaBase` = posição de cada linha na Base Bruta (o
  índice do DataFrame; linha do arquivo = posição + 2). A Base Detalhada traz a
  origem do processado de cada linha; Medianas Cliente e Janelas Atendimento
  se associam às demais por `Cod_Cliente`.

**Memória:** as tabelas são visões, sem cópia: cada linha é montada quando
pedida, a partir da `TabelaBruta` e dos registros classificados, e a Base
Bruta devolve as próprias linhas lidas. São montadas no primeiro acesso a
`ResultadoProcessamento.Saidas` (quem só processa não paga a inferência de
tipos). O resultado passa a manter a `TabelaBruta` viva, como o
`base_padronizada` do Python. A gravação (Excel, ZIP/CSV) é do próximo PR.

### Benchmark das saídas (PR8, 19/10/2026)

Mesma máquina e mesma base de 300.000 linhas do PR7; mediana de 3, cada
medição em processo próprio. "Percorrer" = processar e montar todas as
linhas das 8 tabelas (13,9 milhões de células), sem gravar.

| Operação | Antes (`main` `9ccd183`) | Depois |
|---|---|---|
| Processar XLSX | 2,96 s · pico 302 MB · 686 MB alocados | 3,00 s · 303 MB · 686 MB |
| Processar CSV | 2,28 s · pico 349 MB · 1.095 MB alocados | 2,26 s · 349 MB · 1.095 MB |
| Processar + percorrer XLSX | — | 3,97 s · 311 MB · 1.027 MB |
| Processar + percorrer CSV | — | 2,88 s · 350 MB · 1.436 MB |

- Processar: mesmo SHA-256 e mesma alocação de antes (as saídas só são
  montadas quando pedidas).
- Percorrer custa 0,6–1,0 s e ~340 MB alocados de curta duração (uma
  `object?[]` e os valores por linha); o pico sobe 1–8 MB: nenhuma tabela é
  copiada.

PR9 — gravação da exportação (`Exportador.Exportar`, `GravadorXlsx`,
`GravadorZipCsv`), como o `exportar()` do Python: formato `xlsx` ou `zip`,
destino pasta ou arquivo (`{base}_luna_resultado.{ext}`), mesmas mensagens
de formato inválido e pasta inexistente.

- **ZIP:** um CSV por saída, na ordem de `SAIDAS`, como o `to_csv` do pandas
  2.3.3 no Windows: BOM em cada arquivo, `;`, CRLF, aspas só quando o campo
  tem `;`, aspas ou quebra de linha (`""` num registro de um campo vazio),
  nulo vazio; float com o repr mais curto; datetime64 com a resolução da
  coluna (só datas à meia-noite saem `AAAA-MM-DD`); datetime com fuso como
  `str(Timestamp)` com o offset; timedelta64 como `N days HH:MM:SS`;
  `pd.DataFrame()` vira só a linha vazia.
- **Excel:** como o openpyxl 3.1.5 em write_only: uma aba por saída; tabela
  sem linhas ou sem colunas deixa a aba vazia, sem cabeçalho; número com
  `%.16g`; data como serial do Excel (época 1899-12-30, 29/02/1900 fictício)
  com o formato do tipo (`yyyy-mm-dd h:mm:ss`, `yyyy-mm-dd`, `h:mm:ss`,
  `[hh]:mm:ss`); texto inline cortado em 32.767 caracteres; `#N/A` e afins
  como erro. O XML de cada aba é escrito em sequência direto no ZIP: nada da
  planilha fica em memória.
- **Recusas, antes de criar qualquer arquivo:** Excel com data com fuso
  (o offset não é removido em silêncio; o ZIP continua disponível) e aba
  acima de 1.048.575 linhas de dados ou 16.384 colunas. Caractere de
  controle que o Excel não aceita também é recusado, com aba, linha e coluna.
- **Publicação segura:** grava num temporário `*.parcial` de nome único na
  pasta de destino, força o disco e só então troca o alvo (`File.Move` com
  substituição, como o `os.replace`). Em erro, cancelamento
  (`CancellationToken`) ou alvo aberto em outro programa, o temporário é
  apagado e o arquivo anterior fica como estava.

PR10 — progresso e cancelamento, para a interface (sem UI neste PR):

- `ProcessarBase.Executar(parametros, progresso, cancelamento)`:
  `IProgress<EstagioProcessamento>` com os estágios do `_executar` do Python
  que têm trabalho aqui, na mesma ordem (`Leitura`, `Padronizacao`,
  `Validacao`, `Transformacao`, `RegrasOperacionais`, `Medianas`, `Kpis`,
  `Janelas`, `Concluido`). Sem `base_detalhada` (montada só na exportação).
  Esquema inválido para em `Validacao`, sem `Concluido`, como no Python; em
  erro, os avisos param no estágio que falhou.
- `Exportador.Exportar(..., cancelamento, progresso)`:
  `IProgress<ProgressoExportacao>` com `Validacao` (só xlsx), uma `Gravacao`
  por saída (aba 1–8, nome da aba ou do CSV, linhas da tabela), `Publicacao`
  e `Concluido`. Nenhum percentual: cada aviso é um passo que começou de fato.
- Cancelamento (`CancellationToken`): conferido antes de cada aviso, a cada
  bloco de 64 KB decodificado na leitura do CSV, a cada 1.024 linhas na
  leitura do Excel, na busca de datas com fuso do Excel, no pré-passe de
  formatação dos CSVs e na gravação.
  Cancelado, nada é devolvido nem publicado (o arquivo anterior fica).
  `ILeitorDeBase` ganhou `Carregar(caminho, cancelamento)` com implementação
  padrão: implementações antigas continuam valendo.
- Chamadas antigas (`Executar(parametros)`, `Exportar(r, formato, destino)`)
  não mudam, e o resultado é o mesmo com ou sem progresso/token.

PR11 — painel do cliente, só Application (`PaineisDeCliente.Montar`,
`ClientesDisponiveis`), como o `montar_painel_cliente` do Python:

- seleção do cliente pelo código sem espaços nas pontas (zeros à esquerda
  preservados: `00123` ≠ `123`), filtro por dia da semana: `TODOS`
  (exatamente assim) ou vazio para todas; SEG…DOM sem diferença de
  maiúsculas e com espaços nas pontas ignorados; `todos` ou outro texto dá
  painel vazio, como no Python;
- `Vol_caixas` (primeira das colunas candidatas do motor; sem nenhuma, zero):
  `normalizar_numero_texto` (o último separador decide a casa decimal) e as
  contagens de nulos, zeros (inválido conta como zero, como no motor) e
  inválidos; a média usa a soma pairwise do numpy (`Volumes.SomaNumpy`), igual
  à do `Series.mean()`; `formatar_numero` com arredondamento exato do valor
  binário (empate para o par) e ponto de milhar;
- resumo recalculado no recorte com `TemposPorCliente` (mesmos parâmetros),
  referências de mediana/Q1/gap, janela do cliente com
  `JanelasDeAtendimento`, tabela com as duas linhas de resumo e os dados dos
  dois gráficos (eixo, marcas, faixa inicial, volume, tempo, referências;
  aberturas com a faixa da janela, dupla quando cruza a meia-noite);
- ordem das entregas do cliente: o `sort_values("Chegou_em")` do pandas usa o
  quicksort escalar do numpy em datetime64, que não é estável; o C# o
  reproduz (`OrdenacaoNumpy.ArgsortQuicksort`), empates inclusive;
- lê só as linhas do cliente na base (sem cópia da base): 12–17 ms e ~90 KB
  alocados por painel na base de 300 mil linhas e na base real, pico +3 MB;
  sem gráfico, ScottPlot ou WPF.

Diferenças: (1) no gráfico de aberturas, entregas no mesmo minuto ficam na
ordem do histórico do cliente. O Python ordena os minutos (float64) com o
quicksort vetorizado do numpy, cuja ordem nos empates depende da CPU; x, y e
rótulo são iguais, só o detalhe (data/hora e tempo) de pontos empatados pode
trocar de lugar. (2) Coluna de volume só com booleanos (sem sentido como
volume) vira 1/0 aqui; o pandas mantém True/False na tabela.

### Base real (20/10/2026)

Uma base operacional em XLSX (22,7 MB; 133.133 linhas × 45 colunas; 6.909
clientes), lida no lugar, fora do repositório. Só agregados ficam
registrados aqui: nenhum código, nome ou valor.

- **Igual ao Python:** as 8 tabelas da exportação, comparadas registro a
  registro em fluxo (350.946 registros: colunas, dtypes, origem de cada
  linha, valores e ordem), mais os KPIs; e o conteúdo exportado (os 8 CSVs
  idênticos byte a byte; no XLSX, cada célula das 8 abas com o mesmo tipo,
  valor e formato). Linhas: 133.133 lidas, 70.862 válidas, 5.461
  inconsistentes, 56.768 expurgadas, 42 anomalias.
- **Benchmark** (mesma máquina; C# mediana de 3, Python 1 rodada; pico do
  processo inteiro):

| Operação | C# | Python |
|---|---|---|
| Ler | 5,5 s · pico 276 MB · 894 MB alocados | — |
| Processar | 6,0 s · pico 384 MB · 1,05 GB alocados | 49,6 s |
| Exportar ZIP | 5,6 s · pico 391 MB · +1,8 GB alocados | 67,3 s · pico 759 MB |
| Exportar XLSX | 15,6 s · pico 390 MB · +3,9 GB alocados | 283,9 s · pico 759 MB |

  Arquivos: ZIP 41,2 MB (Python 41,4 MB) e XLSX 102,5 MB (Python 102,3 MB).
  O tempo de processar do Python é o total menos a exportação.

### Benchmark da exportação (PR9, 20/10/2026)

Mesma máquina e base de 300.000 linhas do PR7/PR8, processo próprio por
medição. C#: mediana de 3; Python: 1 rodada. "Exportar" = só a gravação (no
Python inclui a releitura da base bruta, que faz parte do `exportar()`); pico =
do processo inteiro, processamento incluído.

| Exportar | C# | Python |
|---|---|---|
| ZIP, base CSV | 3,2 s · pico 352 MB · +0,9 GB alocados | 9,9 s · pico 409 MB |
| XLSX, base CSV | 11,0 s · pico 352 MB · +2,6 GB alocados | 174,5 s · pico 408 MB |
| ZIP, base XLSX | 3,5 s · pico 308 MB · +1,1 GB alocados | 30,6 s · pico 473 MB |
| XLSX, base XLSX | 11,3 s · pico 309 MB · +3,1 GB alocados | 206,0 s · pico 473 MB |

- O pico do C# fica 1–3 MB acima do de só processar (349 e 307 MB): as abas e
  os CSVs são escritos em fluxo. A alocação é de curta duração (texto de cada
  célula).
- Arquivos: ZIP 33,5 MB (Python 32,7 MB, outro nível de compressão), XLSX
  83,8 MB (Python 85,2 MB). O conteúdo é o comparado, não o contêiner.
- **Conteúdo igual ao Python nessa base** (CSV e XLSX de entrada): os 8 CSVs
  idênticos byte a byte e, no XLSX, cada célula das 8 abas com o mesmo tipo,
  valor e formato lidos pelo openpyxl. Linhas por aba: 300.000, 239.750,
  239.750, 0 (só cabeçalho no CSV; aba vazia no Excel), 30.117, 30.133, 9.000
  e 9.000; mesmos campos vazios e 2,16 M (base CSV) / 2,76 M (base XLSX)
  células de data nas duas saídas.

### Benchmark da leitura otimizada (PR7, 19/10/2026)

Ryzen 7 5700X, 32 GB. Base sintética nova de 300.000 linhas × 6 colunas e
9.000 clientes (21,9 MB em CSV, 13,4 MB em XLSX, mesmo conteúdo; o gerador
de 12/10 não foi versionado), então os números não se comparam com a tabela
de 12/10. Cada
medição em processo próprio; mediana de 3; pico = `PeakWorkingSet64` logo
após a operação; alocado = `GC.GetTotalAllocatedBytes`. Só C#: antes (`main`
`25421a8`) → depois.

| Operação | Tempo | Pico | Alocado |
|---|---|---|---|
| Ler XLSX | 4,74 → 2,05 s | 148 → 140 MB | 3,92 → 0,41 GB |
| Processar XLSX | 5,78 → 3,05 s | 309 → 307 MB | 4,15 → 0,69 GB |
| Ler CSV | 0,36 → 0,23 s | 246 → 159 MB | 223 → 131 MB |
| Processar CSV | 2,52 → 2,29 s | 426 → 349 MB | 1,19 → 1,10 GB |

- Mesma saída antes e depois: SHA-256 das linhas lidas e das saídas do
  processamento (KPIs, medianas, janelas); fuzz de 20.000 jobs (CSV e XLSX)
  com saída idêntica byte a byte à da `main`.
- O pico do XLSX e o do processamento continuam dominados pela
  `TabelaBruta` e pelas listas de registros do domínio, fora deste PR.
- O processamento do CSV ainda aloca ~1 GB no domínio (leitura das datas em
  texto); fora deste PR.

### Benchmark (12/10/2026, mesma máquina, 300.000 linhas × 6 colunas, 9.000 clientes)

Planilha sintética gerada para o teste (13 MB em XLSX, 19 MB em CSV, mesmo
conteúdo). Tempo sem a partida do processo; pico = `PeakWorkingSet` do
processo (C#: `Process.PeakWorkingSet64`; Python: `GetProcessMemoryInfo`,
inclui o interpretador e o pandas). Tempo: média de 2 rodadas (C#) e 3
(Python). Pico do Python: 1 rodada (nas 2 primeiras a medição do pico falhou).

| Operação | C# (Release) | Python (referência) |
|---|---|---|
| Ler XLSX | 6,3 s · pico 124 MB · 3,8 GB alocados | 23,0 s · pico 254 MB |
| Processar XLSX (ponta a ponta) | 7,4 s · pico 298 MB · 4,1 GB alocados | 30,8 s · pico 479 MB |
| Ler CSV | 0,57 s · pico 237 MB · 217 MB alocados | 0,58 s · pico 208 MB |
| Processar CSV (ponta a ponta) | 2,4 s · pico 433 MB · 522 MB alocados | 9,0 s · pico 451 MB |

- Resultado igual nos dois: KPIs idênticos e SHA-256 idêntico das medianas
  e das janelas dos 9.000 clientes, em XLSX e em CSV.
- **Não há afirmação de economia de RAM:** o pipeline ainda materializa a
  `TabelaBruta` inteira (XLSX e CSV) e as listas de registros; a leitura
  do XLSX é em streaming, mas o pico do processamento continua dominado
  pelas tabelas em memória. O CSV continua lido por inteiro.
- A leitura do XLSX aloca muito (3,8 GB no total, coletados ao longo da
  leitura): `LoadCurrentElement` por linha, listas de atributos e regex de
  coordenada por célula. Otimizar só com novo benchmark. (Feito no PR7,
  acima.)
- Medido com uma base sintética; a base real foi medida no PR9 (ver
  "Base real", acima).

## Equivalência Unicode (resolvida no PR5)

- **Ordenação de códigos:** `TextoPython.OrdemCodePoint` compara por code
  point, como o `str` do Python (o ordinal UTF-16 do .NET divergia com
  caracteres fora do BMP). Usada em tempos por cliente, janelas e esquema.
- **Espaços:** `TextoPython.Strip` usa os espaços de `str.isspace()`
  (inclui U+001C–U+001F, que o `Trim` do .NET mantém).
- **Minúsculas:** `TextoPython.Minusculas` = `str.lower()` do Python 3.10.
  Comparado em todo o Unicode com o `ToLowerInvariant`: difere só em U+0130
  (`İ` → `i` + U+0307 no Python, o que muda alias) e em 40 pares criados no
  Unicode 14 (U+2C2F, U+A7C0, U+A7D0, U+A7D6, U+A7D8, U+10570–U+10595), que o
  Python 3.10 (Unicode 13) mantém. Os dois casos são reproduzidos.

## Build e testes

SDK fixado em `global.json` (10.0.401, aceita feature bands mais novas).

```powershell
dotnet test dotnet/Luna.sln
dotnet run --project dotnet/src/Luna.Desktop        # aplicativo Windows (PR12)
```

### Luna.Desktop (PR12, esqueleto)

Abas Base, Validação e Processamento, como na tela atual (`interface/`):
escolha da base local (CSV, XLSX, XLSM, pelo diálogo do Windows ou pelo
caminho), os mesmos parâmetros e padrões do motor, processamento fora do
thread da interface (`Task.Run`) com os estágios do `IProgress` e
cancelamento; laudo do esquema, sugestões, mapeamento, avisos de leitura e
inconsistências por motivo; os 8 KPIs, resumo, parâmetros usados e janelas.
Erro mostra a mensagem do motor (ou um texto genérico), nunca a pilha. Só um
resultado fica em memória: processar de novo solta o anterior antes.

A barra de ação (Processar, Cancelar, estado, estágios e erro) fica na janela,
abaixo de qualquer seção, ligada ao `BaseViewModel`: dá para processar,
acompanhar e cancelar de qualquer aba. Cores de estado vêm dos pincéis do
Fluent (`SystemFillColorCritical/Success/CautionBrush`) e acompanham o tema.
`--tema=claro|escuro` força o tema só no processo (testes visuais), sem mexer
no Windows. Avisos de progresso não vazam para a execução seguinte: o aviso
(`Dispatcher.InvokeAsync`) e a continuação do `await` vão para a mesma fila,
na mesma prioridade, e o comando só aceita outra execução depois do fim.

Sem navegador, servidor, Python nem administrador. Ainda não: Resultados,
Exportação e Painel do Cliente na tela (PR13/PR14), publicação portátil
(PR15). O Luna Python continua independente.

Os testes acham `tests/dados/contrato/` subindo a partir da pasta de saída e
conferem o SHA-256 de cada arquivo contra o `MANIFEST.json`.

## Diferenças de representação conhecidas

- **Precisão de data/hora:** `DateTimeOffset` guarda 100 ns (1 tick); o
  pandas guarda 1 ns. O parser lê até 18 dígitos de fração, como o pandas,
  mas o C# trunca abaixo de 100 ns (`.123456789` vira `.1234567`). As bases
  do Luna têm no máximo microssegundos, que os dois representam exatamente.
- **Faixa de duração:** o pandas guarda a diferença em int64 de
  nanossegundos (±292 anos) e dá `OverflowError` fora disso. O C# calcula em
  `Int128` e não estoura; dentro do int64 o resultado é o do pandas, bit a
  bit, inclusive o arredondamento acima de 2⁵³ ns.

## Divergências conhecidas com o Python

Intencionais (decididas na revisão do PR5):

- **`now` e `today`:** o pandas troca esses textos pela data e hora da
  máquina (inclusive no `strptime`). O C# os deixa como data inválida
  (`datetime_invalido | tempo_nulo`) e devolve um aviso com a coluna e as
  linhas (`ResultadoTransformacao.Avisos`): o relógio da máquina não serve
  para completar data histórica.
- **Fuso acima de ±14:00:** o pandas 2.3.3 aceita offset até ±23:59 (ex.:
  `+14:30`, `+15:00`, `-15:00`, com a hora local mantida); o
  `DateTimeOffset` só representa até ±14:00. O C# rejeita a base com a
  coluna e as linhas (`FormatoDeDataException`), sem mudar o horário. Se a
  coluna também mistura fusos, vale antes a mensagem de fuso misto, como no
  Python.

Sem efeito em alias ou resultado, ou fora do escopo:

- **Sigma final:** `str.lower()` do Python faz `Σ` final virar `ς`; o C#
  dá `σ`. Afeta só o texto normalizado do cabeçalho, nunca o alias (todos
  ASCII).
- **Versão do Unicode:** NFKD e categorias vêm do .NET (Unicode mais novo
  que o 13 do Python 3.10). Só caracteres criados depois do Unicode 13
  podem normalizar diferente num cabeçalho.
- **Célula numérica do Excel em coluna de data ou de código:** fora do PR5
  (a importação de arquivos é do PR6). O domínio recebe texto,
  `DateTime` nativo ou nulo.
- **CSV, casos de borda do tokenizer:** o pandas lê o arquivo em blocos;
  o C# lê o arquivo inteiro. Só muda num caso patológico: fim de linha CR
  e uma linha que começa com espaço/tab seguido de dado. O C do pandas volta
  até o último `\n` do bloco e relê linhas já lidas: às vezes o resultado é
  o mesmo do C#, às vezes duplica linhas ou falha com `Buffer overflow caught`.
  O C# volta só ao começo da linha (o espaço fica no campo); antes voltava ao
  último `\n` também e entrava em laço sem fim, consumindo toda a memória.
  A mensagem de "Último erro" para falha de decodificação UTF-8 também não é
  reproduzida (só aparece se as seis tentativas falharem, e a última é
  cp1252, cuja mensagem é a do Python).
- **Excel, diferenças conhecidas com o openpyxl:** (1) o openpyxl lê os
  estilos sempre de `xl/styles.xml` e o C# pelo relacionamento do pacote
  (iguais em arquivos do Excel); (2) a mensagem de arquivo corrompido ou
  que não é Excel é do C# (`Erro ao abrir o Excel '…': …`), não a do
  `zipfile`/openpyxl; (3) estilo de célula inexistente na coluna do código
  não dá o IndexError do openpyxl (o formato é tratado como vazio); (4)
  duração acima de ~29.000 anos (limite do `TimeSpan`) vira `#VALUE!`; (5)
  inteiro ≥ 2⁶³ com célula vazia na mesma coluna (só em XML escrito à mão: o
  Excel e o openpyxl gravam esses números como float) continua inteiro no C#;
  o pandas faz a coluna virar float64.
- **Fuso numa coluna e nenhuma data ISO na outra:** o Python quebra com o
  `TypeError` do pandas na subtração; o C# para no mesmo ponto, com a mesma
  mensagem (`Cannot subtract tz-naive and tz-aware datetime-like objects`,
  com ponto final quando o fuso está só em `Chegou_em`, como no pandas).
- **CSV com índice implícito** (primeira linha de dados com campo a mais,
  como um `;` sobrando no fim): o pandas usa os primeiros campos como índice
  e o motor Python usa os rótulos desse índice como posição da linha — em
  `_linhas_do_arquivo` (`int(i) + 2`) e no `fillna` de
  `_completar_com_formatos`. Lá isso dá `invalid literal for int()`,
  `cannot reindex on an axis with duplicate labels` ou números de linha
  errados nas mensagens. O C# descarta o índice (como o motor descarta os
  campos), numera as linhas pela posição e segue. Divergência intencional,
  decidida em 17/10/2026.
- **Base Bruta da exportação** (PR8): o Python relê o arquivo na hora de
  exportar; o C# usa a leitura do próprio processamento (a mesma tabela, sem
  releitura nem segunda cópia). Só difere se o arquivo mudar entre processar e
  exportar: aí o C# exporta o que foi processado.
- **Excel da exportação** (PR9): (1) texto que começa com `=` sai como texto
  (célula `inlineStr`, sem `<f>`); o openpyxl grava como fórmula, e o Excel
  executaria conteúdo vindo da base (`=HYPERLINK(...)`, por exemplo). Reaberto,
  o texto é o mesmo; (2) `\r` no texto sai como `&#xD;` e volta igual ao
  reabrir (`a\r\nb` continua `a\r\nb`). O Python no Windows grava `\r\r\n`
  (o arquivo temporário do openpyxl traduz `\n`) e, reaberto, `a\r\nb` vira
  `a\n\nb`; `\r` sozinho vira `\n`. Gravado cru, como no Python em Linux, o
  `\r` se perderia na leitura do XML; (3) base com data com fuso, aba grande demais e caractere de controle
  são recusados com mensagem clara antes de gravar ou no ponto do erro, sempre
  sem publicar nada; o Python falha no meio (`TypeError`,
  `IllegalCharacterError`) ou, no limite de linhas, grava um arquivo que o
  Excel não abre; (4) o temporário tem nome único (`*.{guid}.parcial`), não o
  `.parcial` fixo, para duas exportações ao mesmo alvo não se atropelarem;
  (5) só de contêiner: texto vazio (`Motivo_Inconsistencia` das linhas
  válidas) não gera célula; o openpyxl grava `<c t="inlineStr"/>`. Reabertos,
  os dois dão célula vazia.
- **Limitação conhecida, igual ao Python — fórmula no CSV:** os CSVs do ZIP
  levam o valor como veio da base, sem prefixo; texto que começa com `=`,
  `+`, `-` ou `@` pode ser interpretado como fórmula se o CSV for aberto
  direto no Excel. O Luna não altera o dado (o CSV é igual byte a byte ao do
  Python); para abrir no Excel, prefira o `.xlsx` ou importe o CSV como texto.

## Regras da migração

- Nenhuma mudança de metodologia: equivalência bit a bit com o contrato.
- Formatação e leitura de números sempre com `CultureInfo.InvariantCulture`.
- `Math.Round` sempre com `MidpointRounding` explícito.
- Sem interface (WPF) antes de o motor estar validado.
