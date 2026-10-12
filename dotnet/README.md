# Luna em C#/.NET 10 (migração em andamento)

Porta do motor Python (`src/`) para C#, em PRs pequenos, cada um validado
contra o contrato matemático gerado pelo Python em `tests/dados/contrato/`
(tag `luna-py-ref-v1.1`). O Python continua sendo o Luna em uso até a
substituição ser aprovada.

| Projeto | Conteúdo | Dependências |
|---|---|---|
| `src/Luna.Domain` | regras e funções matemáticas puras | nenhuma (só BCL) |
| `tests/Luna.Domain.Tests` | testes de unidade e paridade com o contrato | xUnit |

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

## Divergências conhecidas com o Python (decisão pendente ou fora do escopo)

- **`now` e `today`:** o pandas troca esses textos pela data e hora da
  máquina (inclusive no `strptime`). O C# não os lê (ficam como data
  inválida): replicar tornaria o resultado dependente do relógio. Decisão
  de negócio pendente.
- **Sigma final:** `str.lower()` do Python faz `Σ` final virar `ς`; o C#
  dá `σ`. Afeta só o texto normalizado do cabeçalho, nunca o alias (todos
  ASCII).
- **Versão do Unicode:** NFKD e categorias vêm do .NET (Unicode mais novo
  que o 13 do Python 3.10). Só caracteres criados depois do Unicode 13
  podem normalizar diferente num cabeçalho.
- **Célula numérica do Excel em coluna de data ou de código:** fora do PR5
  (a importação de arquivos é do PR6). O domínio recebe texto,
  `DateTime` nativo ou nulo.
- **Fuso numa coluna e nenhuma data ISO na outra:** o Python quebra com o
  `TypeError` do pandas na subtração; o C# para no mesmo ponto, com a mesma
  mensagem (`Cannot subtract tz-naive and tz-aware datetime-like objects`).

## Regras da migração

- Nenhuma mudança de metodologia: equivalência bit a bit com o contrato.
- Formatação e leitura de números sempre com `CultureInfo.InvariantCulture`.
- `Math.Round` sempre com `MidpointRounding` explícito.
- Sem interface (WPF) antes de o motor estar validado.
