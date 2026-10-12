# LUNA — Plano de migração progressiva Python → C#/.NET 10

> **Modo:** Claude Code **PLAN MODE** — diagnóstico e planejamento somente.
> **Escopo:** repositório Luna clonado e versão efetivamente disponível no momento da execução.
> **Objetivo:** obter um plano implementável, testável e auditável para um Luna Desktop Windows distribuível às revendas, sem dependência de Python.

## 0. Instruções obrigatórias e limites

1. **Ative e utilize as skills Caveman em modo ULTRA** e **Karpathy Guidelines**. O identificador observado anteriormente para esta última foi `anthropic-skills:karpathy-guidelines`; procure o identificador realmente disponível no ambiente. **Não afirme que uma skill foi utilizada se não conseguir carregá-la.** Informe nome resolvido, situação e efeito prático na execução. Se ausente, relate o bloqueio e siga apenas com instruções que estejam de fato disponíveis; não invente uma instalação.
2. **Não edite, mova, exclua ou crie arquivos neste PLAN MODE.** Não execute comandos que modifiquem código, dependências, banco de dados ou Git. Comandos de inspeção e testes que escrevam caches ou artefatos temporários no repositório precisam de redirecionamento externo ou devem ser omitidos/justificados. Não faça `git commit`, `push`, `checkout`, `reset`, `clean` ou instalações globais.
3. Inspecione `git status --short`, `git rev-parse HEAD`, branch atual, árvore de diretórios, testes e documentação. A última referência relatada foi `main @ 25aaf55`; **não presuma que continua atual**.
4. Não exponha `secrets.toml`, chaves de serviço, tokens, credenciais ou bases de clientes nos logs, relatórios ou código gerado. A versão distribuída deverá usar **importação local**, sem Google Drive ou credenciais incorporadas.
5. Preserve o funcionamento Python existente durante a migração. O C# será introduzido em projetos paralelos, com critérios objetivos para substituição futura.
6. **Não otimize, simplifique ou mude as regras matemáticas sem demonstrar equivalência com o contrato de referência.** Se um detalhe do contrato não estiver confirmado no código, investigue e marque como pendente.

## 1. Situação conhecida — verificar novamente no repositório

Relatório anterior do projeto (não assumir que já foi implementado):

- Suíte Python: **50 testes passando** no commit `25aaf55`.
- F1 (convenção de datas) e F3 (desempate na ordenação dos últimos N) identificados como resolvidos.
- **F10 aprovado:** preservar `Cod_Cliente` como texto ao importar Excel, sem fundir `00123` e `123` quando distintos na origem; cuidado com números do Excel exibidos com zeros por formatação, que exigem contrato explícito.
- **F5b aprovado:** rejeitar mistura ambígua de horários UTC (`Z`) com horários sem fuso, fornecendo diagnóstico legível.
- F2 (duplicatas/retornos) sem regra de negócio aprovada; não deduplicar silenciosamente.
- F6 (serial do Excel dentro de CSV): rejeitar com mensagem explícita por enquanto.
- F7/F8 (qual estatística usar como tempo ideal): **manter Q1 limpo provisoriamente**, sem alegar superioridade sobre mediana antes de backtesting real.
- F9 (janelas): manter a regra atual de histórico completo até haver decisão de negócio.
- F4: comportamento de **quantil tipo 7** e arredondamento **half-even** deve compor o contrato matemático C#.

**Gate 0:** verificar se F10 e F5b realmente foram implementados e testados no HEAD. Se pendentes, indicar correção Python como pré-requisito para congelar os resultados de referência. Não usá-los como se já estivessem concluídos.

## 2. Inventário obrigatório

Inspecione o código efetivo e mapeie as responsabilidades atuais. Na versão anteriormente enviada existiam, entre outros:

- `src/data_loader.py`: leitura Excel/CSV;
- `src/schema.py`: nomes, colunas e aliases;
- `src/data_transformer.py`: interpretação de datas, validações e duração;
- `src/pipeline.py`: orquestração;
- `src/engine.py`: mediana, quartis, regras estatísticas e seleção recente;
- `src/window_calculator.py` ou equivalente: janelas circulares, se existir no HEAD;
- `luna.py` e interface atual;
- `tests/` e `tests/dados/golden/`, se presentes no HEAD.

Para cada entrada/saída, documente contratos, formatos, tipos, colunas e condições de erro; identifique dependências de Streamlit/UI misturadas ao cálculo.

## 3. Arquitetura-alvo

Proponha inicialmente esta organização, adaptando somente com justificativa:

```text
Luna/
├── src/                       # aplicação Python legada (mantida temporariamente)
├── dotnet/
│   ├── Luna.sln
│   └── src/
│       ├── Luna.Domain/       # regras de negócio e funções matemáticas puras
│       ├── Luna.Application/  # casos de uso e orquestração
│       ├── Luna.Infrastructure/ # Excel/CSV, armazenamento, logs e exportações
│       └── Luna.Desktop/      # WPF, .NET 10, MVVM, Windows
├── tests/                     # testes Python existentes e golden masters
└── dotnet/tests/
    ├── Luna.Domain.Tests/
    ├── Luna.Integration.Tests/
    └── Luna.Regression.Tests/
```

- Target preferencial: `.NET 10 LTS`; Domain/Application/Infrastructure em `net10.0` e WPF em `net10.0-windows` (`UseWPF=true`).
- **Não criar WPF na primeira entrega**. Começar por `Luna.Domain` e testes em C#; depois importação e integração; interface por último.
- Não utilizar Python embutido, processo Python auxiliar, API remota ou navegador/Streamlit na versão desktop final, salvo decisão explicitamente aprovada.
- Definir DTOs tipados e imutáveis quando apropriado, separando entrada bruta, registros validados, descartados e resultados consolidados.
- Priorizar cálculo determinístico e consumo de memória controlado para planilhas grandes.
- Bibliotecas candidatas, sujeitas a verificação de licenças e adequação: ClosedXML (XLSX), CsvHelper (CSV), xUnit (testes), Serilog (logs), CommunityToolkit.Mvvm (MVVM). Não instalá-las nesta fase.
- Não adicionar banco de dados, cloud, telemetria ou login sem necessidade concreta.

## 4. Contrato matemático a congelar antes de portar

Levantar a sequência **exata no HEAD**, não apenas a sequência conceitual:

1. Importação preservando identidade do cliente; normalização dos códigos sem conversão indevida.
2. Resolução de aliases e tipos; tratamento de Excel nativo e CSV.
3. Interpretação de `Chegou_em` e `Finalizada_em` de forma conjunta quando necessário; data brasileira, ISO, fuso explícito e ausência de fuso; rejeição de ambiguidades.
4. `Tempo_Sec` derivado da diferença real de timestamps; negativos, nulos, datas faltantes, meia-noite, segundos fracionários e serial Excel.
5. Expurgo de durações **< 300 s** e anomalia **> 21.600 s**, com inclusão dos limites somente se isso corresponder à implementação atual.
6. Agrupamento por cliente, ordenação determinística pelos critérios realmente usados e seleção dos últimos **N=10** (configurável).
7. Aplicação de Q1, Q3, IQR=Q3-Q1 e limiares IQR com **fator 1,5**; confirmar em qual universo a limpeza é aplicada.
8. Cálculo da mediana, Q1 limpo e Q3 limpo; confirmar o comportamento para amostras vazias/pequenas e fallback (**600 s**, mínimo de amostras **4**, se atuais).
9. Cálculo dos quantis por interpolação linear **Hyndman–Fan tipo 7**; documentar a expressão e casos de borda.
10. **Ordem exata de arredondamento e ajuste percentual**. Em C#, o `Math.Round` deve receber `MidpointRounding.ToEven` quando o contrato exigir half-even; não assumir defaults.
11. Janelas circulares (horas em segundos desde meia-noite), cobertura histórica e semântica do resultado; confirmar regra atual de cobertura (histórico completo, percentual padrão e mínimo).
12. Exportação: nomes de colunas, unidades, precisão, ordenação, vazios e interpretação de números/datas.

**Condição essencial:** não confundir `Q1 limpo`, `mediana` e `tempo planejado`. Registrar origem e significado de cada indicador.

## 5. Golden master e paridade Python ↔ C#

Antes de migrar cálculos:

- Localizar o golden master atual e os testes que o geram; auditar se contêm dados sensíveis.
- Se os fixtures existentes não cobrirem bordas, **planejar** casos adicionais sintéticos e determinísticos para posterior implementação.
- Fixar versões, parâmetros, esquema e resultados esperados; registrar também hashes de entradas sintéticas para rastreabilidade.
- Criar uma matriz de casos obrigatórios: `00123` vs `123`; datas `03/10/2026`; ISO e BR cruzados; `Z` misturado sem fuso; meia-noite; 299/300/21600/21601 segundos; inválidos; duplicatas sem remoção; últimos N com empate; n=0/1/3/4/10; quartis com interpolação; half-even em `.5`; ajuste 0/10/-20%; outlier baixo/alto; janela atravessando meia-noite e horários dispersos.
- Executar a **mesma entrada e configuração** nos dois motores e comparar: linhas válidas/inválidas, classificações, clientes, amostras escolhidas, medianas, quartis, tempos ajustados e janelas.
- Preferir **igualdade exata nos campos inteiros/strings**; para valores internos de ponto flutuante definir tolerância explícita e justificada; resultados arredondados/exportados devem corresponder ao contrato.
- Registrar causas de diferenças (parser, cultura, timezone, tipos, ordenação, interpolação, arredondamento, precisão ou bugs), sem mascarar discrepâncias ajustando expectativas.
- Não afirmar equivalência com base somente em `dotnet build` ou em testes C# internos.

## 6. Plano de execução — solicitar aprovação por fases

### Fase 0 — congelar referência

Verificar HEAD, F10/F5b, executar/planejar suíte Python de modo seguro, inventariar entradas/saídas, contrato matemático, fixtures e golden master. Propor um commit/tag de referência **para aprovação posterior**, sem executá-lo em Plan Mode.

### Fase 1 — motor matemático independente

Criar, **somente após aprovação**, solução .NET, `Luna.Domain` e `Luna.Domain.Tests`. Migrar funções puras pequenas, começando por quantis tipo 7, arredondamento, duração e classificação, depois seleção por recência, cálculo por cliente e janela circular. Validar a cada função contra golden master. Sem UI, sem importação Excel nesta fase.

### Fase 2 — importação e pipeline

Implementar `Luna.Application` e `Luna.Infrastructure`, leitores de XLSX/CSV, validações explícitas, cancelamento e processamento de lotes. Preservar zeros à esquerda de acordo com o contrato aprovado. Incluir logs locais sem dados sensíveis. Validar paridade ponta a ponta.

### Fase 3 — aplicação Windows

Criar `Luna.Desktop` em WPF/MVVM: seleção do arquivo local, pré-validação, parâmetros, progresso e cancelamento, resultados por cliente, janela, filtros e exportações. Não bloquear o thread da UI em processamentos longos. Não exigir acesso a Drive, rede, Python ou terminal.

### Fase 4 — entrega para revendas

Comparar instalação **framework-dependent** (pacote menor, exige runtime) com **self-contained** (sem instalação prévia do .NET, tamanho maior); medir tamanho/tempo de abertura/uso de memória. Preferir publicação em pasta com instalador ao `single-file` até testes demonstrarem benefício. Avaliar MSIX ou instalador corporativo conforme permissões dos PCs. Planejar versionamento, desinstalação, logs, atualização, manual e teste em máquina Windows limpa.

## 7. Critérios de aceitação mínimos

- Código Python preservado durante a transição.
- F10/F5b verificados e integrados à referência; se pendentes, bloqueio identificado.
- Todas as regressões Python passam no baseline; todos os testes de domínio C# e comparativos passam antes de UI.
- Nenhuma colisão de identificadores `Cod_Cliente` e nenhuma conversão silenciosa de datas/fusos.
- Zero divergência não justificada em saídas finais para os casos sintéticos/golden.
- Sem necessidade de Python na máquina da revenda no produto final.
- Importação, cálculo e exportação completamente locais.
- Erros apresentados de forma compreensível ao usuário; logs sem dados pessoais desnecessários ou credenciais.
- Compatibilidade com arquivos e saídas do Luna atual documentada.
- Medições concretas de tamanho, inicialização e processamento antes de afirmar ganho de desempenho.

## 8. Formato obrigatório da resposta no PLAN MODE

Entregue, nesta ordem:

A. **Estado real do repositório**: branch, commit, status, presença/ausência de F10/F5b, testes e fixtures; sinalize divergências com `25aaf55`.

B. **Mapa de equivalência**: para cada função Python, destino C#, inputs, outputs, regra e dificuldade.

C. **Contrato matemático congelável**: fórmulas, arredondamentos, cultura, timezone, limites e pendências.

D. **Plano técnico por PRs pequenos**: arquivos a criar, dependências, ordem de implementação, testes e critérios de aceite. Sugerir PR1 para scaffold + quantil/arredondamento, PR2 para duração/expurgos, PR3 para agregações/janelas etc., adaptando ao repo real.

E. **Riscos e bloqueios**: incompatibilidades, dados sensíveis, interpretações ambíguas, F10/F5b pendentes e limitações de ambiente Windows/.NET SDK.

F. **Primeira implementação proposta**: escopo exato da primeira PR, comandos de build/test previstos, sem executar alterações.

G. **Perguntas indispensáveis**: somente decisões que impedem o trabalho; explicitar valores padrão provisórios quando legítimos.

**Pare no plano e aguarde aprovação expressa antes de alterar o repositório.**
