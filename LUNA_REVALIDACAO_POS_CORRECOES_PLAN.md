# LUNA — Revalidação pós-correções e preparação para migração C#

> **Modo obrigatório:** Claude Code **Plan Mode**. **Não modificar arquivos de produção**, não instalar dependências, não executar migrações nem realizar commits sem aprovação expressa. Testes somente de leitura ou em diretórios temporários isolados.

## Skills obrigatórias

1. Localize e carregue as skills **Caveman**, configurando/aplicando o modo **ULTRA**, e **`andrej-karpathy-skills:karpathy-guidelines`**.
2. Confirme no relatório quais skills foram encontradas, como foram ativadas e quais orientações relevantes foram aplicadas.
3. **Se alguma skill não estiver disponível, informe a limitação e peça instruções. Não finja que foi executada nem invente seu conteúdo.**

## Missão

Auditar o **estado atual do repositório Luna** após as correções matemáticas recentes. A versão atualmente presente no repositório é a única fonte de verdade para descrever a implementação; versões anteriores servem apenas para comparação quando estiverem efetivamente disponíveis. Não presumir que uma mudança foi implementada só porque foi solicitada em conversas anteriores.

Entregar um diagnóstico reproduzível, evidências e um plano de correção priorizado, preparando a futura migração do motor de cálculo para **C# / .NET 10 LTS / WPF**, com importação local de Excel/CSV, sem Google Drive como dependência de operação.

## Etapa 1 — Inventário e rastreabilidade

- Inspecione a árvore do projeto, dependências, configuração, entrypoints, módulos de importação, transformação, pipeline, engine, janelas, interface e exportação.
- Identifique alterações recentes usando `git status`, `git log`, `git diff` e histórico disponível, **sem alterar o repositório**. Se não houver histórico ou baseline, declare a impossibilidade de confirmar o que mudou.
- Produza mapa `entrada -> validação -> filtros -> seleção -> cálculo -> visualização -> exportação` com arquivo, função e linhas.
- Diferencie regra de negócio, parâmetro configurável, valor padrão, comportamento observado e hipótese.

## Etapa 2 — Auditoria matemática (prioridade máxima)

Verifique e documente a fórmula e a ordem efetiva das operações, citando arquivo, função e linhas:

1. **Parsing temporal:** `Chegou_em`, `Finalizada_em`, dia/mês, formatos Excel/CSV, timezone, datas ambíguas, meia-noite, tipos nulos e duração em segundos. Não assuma formato: detecte contratos da fonte e trate ambiguidades explicitamente.
2. **Identidade do atendimento:** cliente, tour/mapa, visitas repetidas, retornos, duplicatas e registros que não devem ser somados ou contados duas vezes.
3. **Elegibilidade:** tempos negativos, zero, abaixo do mínimo, acima do teto, registros incompletos; ordem dos filtros e motivo de exclusão rastreável.
4. **Amostragem:** ordenação temporal, últimos N atendimentos válidos, diferença entre selecionar N antes ou depois do expurgo, empates e clientes com poucos registros.
5. **Estatística:** mediana, Q1, Q3, IQR, multiplicador, limites inferior/superior, método de interpolação dos quantis, regras de remoção e recálculo; efeitos de amostras pequenas e valores repetidos.
6. **Tempo recomendado:** definição operacional de “tempo ideal” versus mediana, Q1, percentis e fallback; efeito do ajuste percentual, arredondamentos, limites e unidades.
7. **Variáveis operacionais:** quantidade de ajudantes, volume de caixas, dia da semana; verifique se são usadas, ignoradas ou apenas exibidas. Não introduza modelagem complexa sem evidência.
8. **Janelas:** algoritmo de menor arco circular, cobertura real, horários próximos à meia-noite, mínimo de observações, estabilidade e distinção entre janela de chegada e duração do atendimento.
9. **Consistência:** resultados idênticos entre motor, painel, agregações e exportações; nenhuma recalculação divergente na interface.

**Atenção:** não confunda expurgo operacional com outlier estatístico. Não declare Q1 como melhor estimador sem backtesting. Se as regras de negócio forem ambíguas, apresente alternativas e solicite decisão.

## Etapa 3 — Matriz de testes determinísticos

Calcule valores esperados independentemente do código e compare com a implementação. Registre resultado esperado, obtido, tolerância, status e causa da diferença. Inclua:

- Estável: `[12,13,12,14,13,12,13,14,12,13]` minutos.
- Outlier alto: `[12,13,12,14,13,12,13,14,12,90]`.
- Curto suspeito: `[2,12,13,14,12,13,14,13,12,13]`.
- Poucos registros: `[18,20,22]`.
- Alta dispersão: `[8,12,19,35,14,42,10,28,16,31]`.
- Mudança de regime (antigo -> recente): `[30,28,29,31,30,14,13,15,14,13]`.
- Todos iguais; lista vazia; único registro; valores exatamente nos limiares; valores negativos; nulos; duplicatas; retorno; mudança de dia; datas ambíguas `03/10/2026`; cliente com registros fora de ordem; alterações de N, min_n, ajuste e cobertura.
- Teste de unidade (segundos vs minutos), arredondamento, invariância à ordem quando apropriado e determinismo.

**Não modifique testes existentes para fazê-los passar.** Em Plan Mode, caso executar testes não seja permitido ou não seja seguro, forneça comandos exatos para execução posterior e marque os resultados como **não executados**, sem inventá-los.

## Etapa 4 — Validação com dados reais (se disponíveis e autorizados)

- Planeje ou execute somente leitura e sem expor dados sensíveis.
- Faça backtesting temporal por cliente: usar somente histórico anterior para prever atendimentos posteriores.
- Compare mediana, Q1 atual, mediana pós-limpeza e percentis operacionais, preservando a mesma amostra e critérios.
- Métricas: MAE, erro absoluto mediano, viés assinado, percentual de estouro do tempo planejado, cobertura por cliente e faixa de tamanho de amostra, impacto acumulado por rota.
- Segmente quando viável por volume e ajudantes. Relate tamanho de amostra e incerteza.
- Se não houver dados reais, **não conclua superioridade empírica de modelo**.

## Etapa 5 — Regressão funcional e distribuição

Verifique importação Excel/CSV, aliases de colunas, validação, filtros, cache, troca de cliente sem reprocessamento, visualizações, relatórios, arquivos ZIP/Excel/CSV, logs e tratamento de falhas. Identifique acoplamentos com Streamlit/Google Drive e bibliotecas específicas de Python que precisam ser substituídos na migração.

Não proponha reescrita imediata do motor sem **baseline numérico congelado**. Recomende criar fixtures de entrada e saída versionadas, testes golden master e contrato de dados para equivalência Python ↔ C#.

## Etapa 6 — Plano da arquitetura de longo prazo

Planeje, sem implementar, a divisão:

- `Luna.Domain`: regras puras, estatísticas, políticas de elegibilidade e janelas.
- `Luna.Application`: casos de uso de importar, validar, processar, consultar e exportar.
- `Luna.Infrastructure`: leitores Excel/CSV, persistência/configuração local, logs e exportadores.
- `Luna.Desktop`: WPF/MVVM, progresso, cancelamento e mensagens claras.
- `Luna.Domain.Tests`, `Luna.Integration.Tests`, `Luna.Regression.Tests`.

Defina critérios de equivalência numérica, contratos de arredondamento/quantis/datas, estratégia de publicação Windows e testes em máquinas sem ferramentas de desenvolvimento.

## Formato obrigatório da resposta

1. **Resumo executivo:** situação atual e decisão de prontidão (`APTO`, `APTO COM RESSALVAS` ou `NÃO APTO`), com justificativa.
2. **Skills:** confirmação verificável de Caveman ULTRA e Karpathy Guidelines; ausências.
3. **Mapa do cálculo atual:** sequência e fórmulas com referências `arquivo:linha`.
4. **Mudanças verificadas:** antes/depois com evidências de Git; caso indisponível, declarar.
5. **Achados:** tabela `ID | Severidade | Tipo (bug/risco/decisão) | Evidência | Impacto | Correção sugerida`.
6. **Matriz de testes:** esperado vs obtido, incluindo status `PASSOU`, `FALHOU`, `NÃO EXECUTADO`.
7. **Comparação estatística:** cálculos e limitações; não alegar ganhos sem dados.
8. **Plano de correções:** ordem, arquivos afetados, testes de aceitação e riscos.
9. **Plano de migração C#:** marcos, dependências e estratégia de equivalência.
10. **Perguntas de negócio pendentes:** apenas decisões que realmente exigem confirmação.

**Critério final:** priorize confiabilidade matemática, reprodutibilidade, compatibilidade com a operação e mudanças mínimas. Não faça alterações sem aprovação explícita.
