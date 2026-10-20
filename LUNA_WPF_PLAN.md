# Luna em WPF: planejamento (Fase 3)

Estado: **planejamento, sem implementação**. Nada de `Luna.Desktop` antes de o
escopo abaixo ser decidido pelo usuário. Base: Fase 3 do
`LUNA_MIGRACAO_CSHARP_FASE0_PLAN.md` e a interface atual (`luna.py` +
`interface/*.js`), com o motor C# validado até o PR9 (leitura, processamento,
8 saídas e exportação iguais ao Python, inclusive numa base real de 133 mil
linhas).

## 1. O que a interface atual faz × o que já existe em C#

| Aba atual (`interface/`) | O que faz | Em C# hoje |
|---|---|---|
| Base (`aba_base.js`, `formulario.js`, `navegador.js`) | escolhe o arquivo (disco e rede), parâmetros, inicia, progresso por estágio, prévia da base padronizada | `ParametrosProcessamento` (mesmas validações), `ProcessarBase`, `EsquemaColunas`. **Falta:** progresso por estágio (o Python emite `leitura`, `padronizacao`, `validacao`, `transformacao`… ) |
| Validação (`aba_validacao.js`) | laudo do esquema (obrigatórias, opcionais, aliases, faltantes) | `EsquemaColunas.Analisar` → `RelatorioEsquema` |
| Processamento (`aba_processamento.js`) | 8 KPIs, resumo da execução, parâmetros usados | `Kpis`, `ResultadoProcessamento` |
| Resultados (`aba_resultados.js`) | as tabelas de saída, paginadas no servidor | `ResultadoProcessamento.Saidas` (`TabelaSaida.Linha(i)`: acesso por linha, serve à virtualização) |
| Painel do Cliente (`aba_cliente.js`) | busca de cliente, filtro por dia da semana, resumo recalculado, tabela de entregas com 2 linhas de resumo, 2 gráficos | **Não portado** (ver §2) |
| Exportação (`aba_exportacao.js`) | grava xlsx/zip numa pasta (sem progresso) | `Exportador.Exportar` com cancelamento. **Falta:** progresso |

## 2. O que falta portar antes da UI (Application, com paridade contra o Python)

O painel do cliente tem regra de negócio; ela entra num PR próprio, sem UI,
testado contra o motor (mesma disciplina dos PR1–PR9):

- `montar_dados_cliente`: recorte do cliente ordenado por `Chegou_em`,
  coluna de volume (`obter_coluna_volume`), `normalizar_volume_caixas`
  (`src/utils.py`), rótulos de data/hora, minutos de abertura e finalização,
  dia da semana (`DIAS_SEMANA_MAP`).
- `filtrar_dados_cliente_por_dia_semana` e `recalcular_resumo_cliente_filtrado`
  (reusa `TemposPorCliente.Calcular` sobre o recorte; o C# já tem a conta).
- `extrair_referencias_mediana`, `montar_tabela_cliente` (as 2 linhas de
  resumo), `clientes_disponiveis` (ordem de code point, `TextoPython.OrdemCodePoint`).
- Janela do cliente: `JanelasDeAtendimento.Calcular` já existe.
- Gráficos (`criar_grafico_cliente`, `criar_grafico_aberturas_cliente`): só os
  **dados** das séries (x, y, rótulos, faixa da janela, ticks) vão para a
  Application; o desenho fica na UI.

Também em Application, antes da UI: `IProgress<string>` com os mesmos estágios
do Python em `ProcessarBase` e progresso por aba no `Exportador`.

## 3. Arquitetura proposta para `Luna.Desktop`

- `dotnet/src/Luna.Desktop`: `net10.0-windows`, `UseWPF=true`, MVVM
  (CommunityToolkit.Mvvm, MIT, ou MVVM à mão; a decidir). Referencia
  Application e Infrastructure; nenhuma regra no XAML nem no code-behind.
- Processamento e exportação fora do thread da UI (`Task.Run`), com
  `IProgress<T>` e `CancellationToken`; botão Cancelar; estado
  ocioso/processando/concluído/erro.
- Tabelas grandes: `DataGrid` com virtualização, linhas buscadas de
  `TabelaSaida.Linha(i)` sob demanda (nada é copiado; o PR8 já foi desenhado
  para isso).
- Seleção de arquivo e pasta pelos diálogos do Windows (substitui o navegador
  de pastas da versão web; drives de rede continuam visíveis).
- Erros com a mensagem do motor e sem pilha; log local sem
  dados da base.
- Testes: ViewModels testáveis sem UI (xUnit); teste manual de fumaça descrito
  em cada PR.

## 4. Decisões de escopo para o usuário (antes de qualquer código)

1. **MVP:** que abas entram na primeira entrega e em que ordem? Proposta:
   Base + Validação + Processamento + Exportação primeiro (tudo já existe em
   C#); Resultados em seguida; Painel do Cliente por último (depende do §2).
2. **Gráficos:** biblioteca com licença compatível (candidatas: ScottPlot,
   OxyPlot, LiveCharts2; avaliar licença, desempenho com milhares de pontos e
   interação de zoom/arraste como o Plotly atual).
3. **MVVM:** CommunityToolkit.Mvvm ou implementação própria mínima.
4. **Publicação (Fase 4):** framework-dependent × self-contained; instalador;
   medir tamanho, abertura e memória numa máquina limpa.
5. **Convivência:** o Luna Python (porta 8791, beta em `D:\Documentos\Projetos\Luna-beta`)
   continua em uso até a substituição ser aprovada; o WPF não lê nem grava
   nada do Python.
6. **Visual:** manter a identidade da versão web (ícones de linha, abas
   laterais) ou o padrão do Windows (Fluent do WPF no .NET 9+)?

## 5. Fatiamento sugerido (PRs pequenos)

| PR | Conteúdo | Validação |
|---|---|---|
| PR10 | progresso por estágio em `ProcessarBase` e no `Exportador` | testes de ordem dos estágios; cancelamento |
| PR11 | painel do cliente em Application (§2), sem UI | paridade com `montar_painel_cliente` do Python (fixtures + fuzz) |
| PR12 | `Luna.Desktop` esqueleto: janela, abas Base/Validação/Processamento, processar com progresso e cancelamento | ViewModels testados; fumaça manual |
| PR13 | aba Resultados (8 tabelas virtualizadas) e Exportação | fumaça com a base real (133 mil linhas) |
| PR14 | Painel do Cliente com os gráficos escolhidos | comparação visual com a versão web |
| PR15 | publicação/instalador (Fase 4) | máquina limpa |

Cada PR: aberto para revisão, sem merge automático; Python, contrato e
matemática intocados.
