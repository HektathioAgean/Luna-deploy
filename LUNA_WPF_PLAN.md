# Luna em WPF: planejamento (Fase 3)

Estado: **escopo aprovado em 20/10/2026 (§4)**; implementação em PRs
pequenos (§5). **MVP concluído** (PR13 mergeado em 20/10, `ef8c800`);
protótipo ScottPlot 5 aprovado em 20/10; PR14 mergeado em 21/10 (`2816e7f`). Base: Fase 3 do
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
  (CommunityToolkit.Mvvm, MIT; §4). Referencia
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

## 4. Decisões de escopo (aprovadas na revisão do PR #29, 20/10/2026)

1. **MVP:** Base, Validação, Processamento, Resultados e Exportação. O Painel
   do Cliente vem na entrega seguinte. Nenhuma funcionalidade da versão atual
   é retirada: o que não está no MVP continua no Luna Python até chegar ao WPF.
2. **Gráficos:** ScottPlot 5 para WPF (licença MIT). Antes do PR14, um
   protótipo valida: dois eixos Y, zoom, deslocamento, barras, dispersão e a
   faixa da janela circular (inclusive a que cruza a meia-noite). Nenhuma
   dependência gráfica entra antes disso. **Protótipo aprovado em 20/10/2026**
   (`luna-fuzz-harness/desktop/scottplot/RELATORIO.md`, 150 verificações OK):
   `ScottPlot.WPF` 5.1.59; zoom e deslocamento só no eixo X, eixos Y fixos a
   partir de zero; TFM `net10.0-windows10.0.19041.0` para os assets modernos
   do `SkiaSharp.Views.WPF` (sem suprimir NU1701); mediana `#00866A` só no
   tema claro.
3. **Arquitetura:** WPF .NET 10 com CommunityToolkit.Mvvm; XAML só para
   apresentação; regras de negócio em Domain/Application, fora da UI.
4. **Publicação:** self-contained win-x64, primeiro portátil em pasta/ZIP:
   sem Python, sem servidor local, sem administrador. Instalador por usuário
   numa etapa posterior.
5. **Convivência:** o aplicativo Python (porta 8791, beta em
   `D:\Documentos\Projetos\Luna-beta`) continua independente até a
   homologação e a aprovação da substituição; o WPF não lê nem grava nada do
   Python.
6. **Visual:** tema Fluent nativo do WPF, seguindo o tema do sistema
   (claro/escuro), com a identidade do Luna; navegação lateral e interface
   objetiva.

## 5. Fatiamento sugerido (PRs pequenos)

| PR | Conteúdo | Validação |
|---|---|---|
| PR10 | progresso por estágio em `ProcessarBase` e no `Exportador`, cancelamento (mergeado, #30) | testes de ordem dos estágios contra o Python; cancelamento |
| PR11 | painel do cliente em Application (§2), sem UI (mergeado, #31) | paridade com `montar_painel_cliente` do Python (fixtures + fuzz) |
| PR12 | `Luna.Desktop` esqueleto: janela, abas Base/Validação/Processamento, processar com progresso e cancelamento (mergeado, #32) | ViewModels testados; fumaça por UI Automation nos temas claro e escuro |
| PR13 | aba Resultados (8 tabelas virtualizadas) e Exportação; fecha o MVP (mergeado, #33) | fumaça com a base real (133 mil linhas) |
| — | protótipo ScottPlot 5 (§4.2), fora do produto (aprovado em 20/10) | dois eixos, zoom, deslocamento, barras, dispersão, janela circular |
| PR14 | Painel do Cliente com ScottPlot 5 (mergeado, #34) | comparação visual com a versão web |
| PR15a | leitura do XLSX sem cópia temporária em `%TEMP%` (mergeado, #36) | diferencial C# × Python; `%TEMP%` vigiado com controle positivo |
| PR15 | publicação self-contained win-x64 portátil (em revisão); instalador por usuário depois | ZIP conferido, smoke nos dois temas a partir do ZIP, pasta sem escrita; homologação no PC corporativo com conta padrão pendente (`LUNA_HOMOLOGACAO_DESKTOP.md`) |

Cada PR: aberto para revisão, sem merge automático; Python, contrato e
matemática intocados.
