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

## Build e testes

SDK fixado em `global.json` (10.0.401, aceita feature bands mais novas).

```powershell
dotnet test dotnet/Luna.sln
```

Os testes acham `tests/dados/contrato/` subindo a partir da pasta de saída e
conferem o SHA-256 de cada arquivo contra o `MANIFEST.json`.

## Diferenças de representação conhecidas

- **Precisão de data/hora:** `DateTimeOffset` guarda 100 ns (1 tick); o
  pandas guarda 1 ns. Dígitos abaixo de 100 ns não chegam iguais ao C# (o
  tratamento na leitura de texto será definido no PR5). As bases do Luna têm
  no máximo microssegundos, que os dois representam exatamente.
- **Faixa de duração:** o pandas guarda a diferença em int64 de
  nanossegundos (±292 anos) e dá `OverflowError` fora disso. O C# calcula em
  `Int128` e não estoura; dentro do int64 o resultado é o do pandas, bit a
  bit, inclusive o arredondamento acima de 2⁵³ ns.

## Regras da migração

- Nenhuma mudança de metodologia: equivalência bit a bit com o contrato.
- Formatação e leitura de números sempre com `CultureInfo.InvariantCulture`.
- `Math.Round` sempre com `MidpointRounding` explícito.
- Sem interface (WPF) antes de o motor estar validado.
