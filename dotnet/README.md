# Luna em C#/.NET 10 (migração em andamento)

Porta do motor Python (`src/`) para C#, em PRs pequenos, cada um validado
contra o contrato matemático gerado pelo Python em `tests/dados/contrato/`
(tag `luna-py-ref-v1.1`). O Python continua sendo o Luna em uso até a
substituição ser aprovada.

| Projeto | Conteúdo | Dependências |
|---|---|---|
| `src/Luna.Domain` | regras e funções matemáticas puras | nenhuma (só BCL) |
| `src/Luna.Application` | caso de uso `ProcessarBase`, `ParametrosProcessamento`, porta `ILeitorDeBase` | Domain |
| `src/Luna.Infrastructure` | `CarregadorDeArquivo`, leitura de CSV e de XLSX/XLSM | Application, DocumentFormat.OpenXml 3.5.1 (MIT) |
| `tests/Luna.Domain.Tests` | testes de unidade e paridade com o contrato | xUnit |
| `tests/Luna.Integration.Tests` | leitura de arquivo e fluxo ponta a ponta contra o Python | xUnit |

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

**Memória (ressalva da revisão do PR6):** o CSV ainda é materializado por
inteiro: o arquivo é lido em memória (`File.ReadAllBytes`), decodificado
para uma `string` e tokenizado numa `TabelaBruta` com todas as linhas antes
do processamento. Não há medição de pico de memória nem de tempo em base
grande; **benchmark obrigatório antes da distribuição**.

PR6b — leitura de `.xlsx`/`.xlsm` (`LeitorXlsx`) com DocumentFormat.OpenXml
3.5.1 (MIT), em streaming pelo `OpenXmlReader`: a planilha é lida uma linha
por vez (`LoadCurrentElement` só da `<row>`) e as strings compartilhadas
também; `workbook.xml` e `styles.xml`, pequenos, vão pelo DOM. Reproduz o
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
  coordenada por célula. Otimizar só com novo benchmark.
- Medido com uma base sintética; benchmark com base real continua
  obrigatório antes da distribuição.

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
```

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
  o C# lê o arquivo inteiro. Só muda num caso patológico: arquivo com fim de
  linha só CR e uma linha que começa com espaço/tab seguido de dado (o C do
  pandas volta até o último `
` do bloco). A mensagem de "Último erro" para
  falha de decodificação UTF-8 também não é reproduzida (só aparece se as
  seis tentativas falharem, e a última é cp1252, cuja mensagem é a do Python).
- **Excel, diferenças conhecidas com o openpyxl:** (1) o openpyxl lê os
  estilos sempre de `xl/styles.xml` e o C# pelo relacionamento do pacote
  (iguais em arquivos do Excel); (2) a mensagem de arquivo corrompido ou
  que não é Excel é do C# (`Erro ao abrir o Excel '…': …`), não a do
  `zipfile`/openpyxl; (3) estilo de célula inexistente na coluna do código
  não dá o IndexError do openpyxl (o formato é tratado como vazio); (4)
  duração acima de ~29.000 anos (limite do `TimeSpan`) vira `#VALUE!`.
- **Fuso numa coluna e nenhuma data ISO na outra:** o Python quebra com o
  `TypeError` do pandas na subtração; o C# para no mesmo ponto, com a mesma
  mensagem (`Cannot subtract tz-naive and tz-aware datetime-like objects`).

## Regras da migração

- Nenhuma mudança de metodologia: equivalência bit a bit com o contrato.
- Formatação e leitura de números sempre com `CultureInfo.InvariantCulture`.
- `Math.Round` sempre com `MidpointRounding` explícito.
- Sem interface (WPF) antes de o motor estar validado.
