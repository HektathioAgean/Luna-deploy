# Luna

Aplicativo **local** para análise de tempos operacionais de entrega. Roda na
própria máquina, em `http://127.0.0.1:8781`, e lê a base de onde ela estiver —
disco local ou drive de rede. Nada é enviado para fora da estação.

O que ele entrega:

- validação e padronização da base de entrada
- expurgo de registros inconsistentes e separação de anomalias
- mediana de atendimento, tempo ideal Q1 e Q3 por cliente (boxplot/IQR); a
  mediana em segundos (`Mediana_Tempo_Sec`) é o tempo que o roteirizador usa
- janela de atendimento por cliente, com leitura circular do dia
- painel por cliente, com gráficos e filtro por dia da semana
- exportação consolidada em Excel ou ZIP de CSVs

---

## Executar

Abra o PowerShell na pasta `Luna-deply-main`:

```powershell
py -m pip install -r requirements.txt
py luna.py
```

A interface abre no navegador em `http://127.0.0.1:8781`. Outras opções:

```powershell
py luna.py --porta 8782     # outra porta
py luna.py --sem-navegador  # não abre o navegador sozinho
```

### Atalho na área de trabalho (sem terminal)

```powershell
powershell -ExecutionPolicy Bypass -File ferramentas\criar_atalho.ps1
```

O script cria `Luna.lnk` na área de trabalho. O atalho roda `Luna.pyw` com o
`pythonw.exe` do `.venv`, sem console e sem VSCode. A janela abre em modo app do
Edge ou do Chrome; sem eles, no navegador padrão. Se o Luna já estiver no ar, o
atalho só reabre a janela. Fechar a janela não para o servidor: use o botão
**Encerrar** na barra superior. A saída do servidor fica em
`logs/luna_app.log`. Rode o script de novo se mover a pasta ou recriar o
`.venv`.

### Conferir o pipeline por linha de comando

```powershell
py ferramentas\conferir_pipeline.py --base "V:\...\base.csv"
py ferramentas\conferir_pipeline.py --base "V:\...\base.csv" --saida "C:\temp"
```

Imprime validação, os dez KPIs, o tamanho de cada tabela e o tempo de cada
estágio. É por aqui que se compara um resultado contra outro sem abrir a tela.

---

## A base de entrada

A base é escolhida pelo **caminho do arquivo**, no campo da aba Base ou pelo
botão **Procurar**, que navega pelas pastas e pelos drives montados (`C:`, `V:`,
`G:`, `N:`, `P:`). Não existe mais a convenção `{UNIDADE}_data.csv` como único
caminho: ela continua funcionando como atalho em `src/data_loader.py`, mas
qualquer `.csv`, `.xlsx` ou `.xlsm` serve.

Para CSV, a leitura testa seis combinações de separador e encoding
(`;` e `,` × `utf-8-sig`, `latin1`, `cp1252`) e valida cada tentativa pelo
número de colunas obtidas — separador errado em `pd.read_csv` não levanta erro,
devolve uma coluna só.

O CSV é lido todo como texto, para que um código de cliente como `00123` não
vire o número `123`.

No Excel, a coluna do código do cliente (reconhecida pelos mesmos apelidos da
padronização) também sai como texto: `00123` e `123` são clientes diferentes.
Regra para célula **numérica**: se o formato da célula for só zeros (`00000`),
os zeros à esquerda são recuperados (`123` com formato `00000` vira `00123`);
com qualquer outro formato, fica o número sem zeros (`123`). Quem precisa de
zeros num código guardado como número deve formatar a coluna como `00000` ou
gravá-la como texto. As demais colunas ficam com o tipo da célula.

As datas de `Chegou_em` e `Finalizada_em` são aceitas em ISO
(`2026-03-02 09:15:00`) e em `dd/mm/aaaa`, com ou sem segundos, inclusive
misturadas na mesma coluna. A convenção `dd/mm` ou `mm/dd` é decidida uma vez
para as duas colunas juntas: vence a que gera mais atendimentos com duração
entre zero e um dia, e `dd/mm` vence o empate. Uma linha que só seria válida
na convenção perdedora vai para inconsistências como `data_formato_divergente`,
em vez de virar a coluna inteira.

Datas que não dá para ler sem adivinhar **interrompem o processamento** com a
coluna e as linhas do arquivo na mensagem, em vez de serem convertidas em
silêncio:

- fusos diferentes na mesma coluna (`-03:00` e `-02:00`);
- horário com fuso (`Z`, `-03:00`) misturado com horário sem fuso, na mesma
  coluna ou entre `Chegou_em` e `Finalizada_em`. Coluna homogênea é aceita: só
  `Z`, só um offset (a hora local é mantida) ou só sem fuso;
- número serial do Excel (`46083,375`) no lugar da data, comum em CSV exportado
  de planilha. Exporte as datas como texto ou use o `.xlsx` original.

Na escolha dos últimos N atendimentos, chegadas empatadas em `Chegou_em` são
desempatadas pelo maior `Tempo_Sec`, então o resultado não depende da ordem das
linhas no arquivo.

### Diretrizes provisórias do método

Decididas na revalidação de 12/10/2026, até haver backtesting com dados reais:

- **Tempo ideal Q1** é uma **referência estatística** (1º quartil depois da
  limpeza por IQR), não um tempo operacional validado. O tempo de planejamento
  continua sendo a mediana.
- **Duplicatas** não são removidas automaticamente enquanto não houver uma
  chave única de atendimento definida; uma linha repetida conta duas vezes.
- **Janelas de atendimento** usam o histórico completo do cliente, não só os
  últimos N, até que o efeito da recência seja avaliado.
- **Contrato matemático** (vale também para a futura versão em C#): quantil
  linear (tipo 7), arredondamento *half-even* para segundo inteiro antes do
  ajuste percentual, e ajuste calculado como `valor + valor * p / 100`. As
  saídas em `tests/dados/golden/` são a referência de equivalência.

### Contrato de referência para a versão em C#

`tests/dados/contrato/` guarda entradas sintéticas e as saídas exatas do motor
Python: quantis, os três arredondamentos (inteiro e 2 casas do numpy, `round`
do Python com 1 e 2 casas — algoritmos diferentes, ex.: `2.675` dá `2.68` no
numpy e `2.67` no Python), ajuste percentual, formatação, tempos por cliente em
cinco conjuntos de parâmetros, transformação de datas e motivos, erros de
importação com a mensagem exata e janelas em três coberturas. O
`MANIFEST.json` registra versões, parâmetros e o SHA-256 de cada arquivo.

- Regerar: `python tests/gerar_contrato.py` (só com justificativa no commit).
- `tests/test_contrato.py` falha se o motor, os arquivos, os hashes ou as
  versões de `pandas`/`numpy`/`openpyxl`/`plotly` mudarem.
- Versões exatas do ambiente de referência: `requirements-lock.txt`
  (Python 3.10.11).

Colunas obrigatórias: `Cod_Cliente`, `Chegou_em`, `Finalizada_em`. As opcionais
e os ~70 apelidos reconhecidos estão em `src/schema.py` e aparecem nas tabelas
da aba Validação.

---

## Arquitetura

Servidor HTTP da biblioteca padrão, interface em HTML/CSS e módulos ES nativos.
**Não há framework web, bundler nem passo de build**: editar um arquivo de
`interface/` e recarregar a página basta.

```text
Luna-deply-main/
├── luna.py                 servidor HTTP, rotas JSON e SSE de progresso
├── Luna.pyw                launcher sem console (alvo do atalho)
├── config.py               caminhos, título, tema
├── ferramentas/
│   ├── criar_atalho.ps1    atalho na área de trabalho
│   └── conferir_pipeline.py  execução por linha de comando
├── interface/              a tela, servida por luna.py
│   ├── index.html          casca: topbar, sidebar, workspace
│   ├── app.css             tokens vindos de config.py THEME
│   ├── app.js              boot, topbar, sidebar, troca de seção
│   ├── state.js            estado único + assinar/avisar
│   ├── api.js              as rotas e o SSE
│   ├── componentes.js      peças de tela (métrica, aviso, campo, abas)
│   ├── navegador.js        navegador de pastas e bases
│   ├── formulario.js       caminho da base + parâmetros
│   ├── tabela.js           tabela com paginação do servidor
│   ├── grafico.js          Plotly.newPlot sobre o JSON do Python
│   └── aba_*.js            uma por seção da sidebar
├── logs/                   criada pelo launcher
└── src/
    ├── pipeline.py         o pipeline inteiro, mais o cache e a exportação
    ├── data_loader.py      leitura por caminho e por convenção de nome
    ├── data_transformer.py tipagem, Tempo_Sec e inconsistências
    ├── engine.py           boxplot/IQR, medianas por cliente, KPIs, export
    ├── schema.py           schema oficial, apelidos, padronização de colunas
    └── utils.py            formatação, janela circular, volume de caixas
```

Três decisões que explicam o resto:

**Os gráficos são montados em Python.** `criar_grafico_cliente` e
`criar_grafico_aberturas_cliente`, em `src/pipeline.py`, devolvem figuras
`plotly.graph_objects`. A rota serializa com `fig.to_json()` e o navegador só
chama `Plotly.newPlot`. Nenhuma regra de gráfico vive no JavaScript.

**O `plotly.min.js` vem do pacote plotly instalado**, servido pela rota
`/vendor/plotly.min.js`. Sem CDN — a rede corporativa tem inspeção TLS e o
aplicativo precisa abrir offline — e sem 4,6 MB versionados. Como é o mesmo
pacote que monta a figura, as duas pontas nunca ficam em versões diferentes.

**As tabelas são paginadas no servidor.** `GET /api/tabela` devolve uma página
por vez, então abrir a base detalhada de 300 mil linhas não manda 300 mil linhas
para o navegador.

### Rotas

| Rota | O que faz |
|---|---|
| `GET /api/opcoes` | tema, porta, valores padrão do formulário |
| `GET /api/estado` | se há processamento rodando, estágios, erro |
| `GET /api/procurar?caminho=` | pastas, bases e drives de um diretório |
| `POST /api/processar` | dispara o pipeline numa thread e volta na hora |
| `GET /api/progresso` | SSE com cada estágio do pipeline |
| `GET /api/resultado` | KPIs, validação, contagens das tabelas |
| `GET /api/tabela?nome=&pagina=&tamanho=` | uma página de uma tabela |
| `GET /api/clientes` | lista de clientes do processamento |
| `GET /api/cliente?cod=&dia=` | resumo, janela, tabela e as duas figuras |
| `POST /api/exportar` | grava xlsx ou zip na pasta informada |
| `POST /api/encerrar` | desliga o servidor local |

Um processamento por vez: um segundo `POST /api/processar` enquanto o primeiro
roda devolve HTTP 409.

---

## Desempenho medido

Em base sintética de **370.000 linhas e 13.562 clientes** (43 MB em disco
local), nesta estação (i5-1334U, 16 GB):

| Estágio | Tempo |
|---|---|
| Leitura do CSV | 0,8 s |
| Padronização de colunas | 0,1 s |
| Validação do schema | 0,0 s |
| `transform_base` | 4,7 s |
| Regras operacionais | 0,2 s |
| `calcular_medianas_por_cliente` | **1,1 s** |
| KPIs | 0,0 s |
| Base detalhada | 0,3 s |
| `calcular_janelas_entrega` | 3,4 s |
| **Processamento completo** | **10,6 s** |

`calcular_medianas_por_cliente` era um laço Python sobre os 13,5 mil clientes e
levava **43 s**; a versão vetorizada faz o mesmo em **1,1 s** (40× mais
rápido), com resultado idêntico — a equivalência foi conferida linha por linha
em cinco jogos de parâmetros, incluindo ajuste percentual e bases com empate em
`Chegou_em`.

Exportação da mesma base:

| Formato | Tempo | Arquivo |
|---|---|---|
| ZIP de CSVs | 38 s | 60 MB |
| Excel (8 abas, ~15 milhões de células) | mais de 6 min (não medido até o fim) | — |

Pico de memória do processo, do início do processamento até o meio da
exportação do Excel: **~540 MB**, constante. A gravação usa o modo `write_only`
do openpyxl, que despeja as células em arquivos temporários conforme escreve.
A versão anterior montava a planilha inteira em `BytesIO` e passava de
**4,8 GB** sem terminar.

Duas armadilhas de precisão que a vetorização tem de respeitar, e respeita:

- a mediana é **arredondada para segundo inteiro antes** do ajuste percentual;
  ajustar o valor cru e arredondar depois move o resultado em 1 segundo;
- o ajuste usa a forma `valor + valor * (p/100)`, não `valor * (1 + p/100)`:
  as duas não dão o mesmo float.

O que a tela recebe, na mesma base de 13.562 clientes:

| Rota | Tempo | Payload |
|---|---|---|
| `GET /api/resultado` | 0,00 s | 2 KB |
| `GET /api/clientes` (13.562 códigos) | 0,14 s | 146 KB |
| `GET /api/tabela` — qualquer tabela, 100 linhas | 0,01 s | 10–35 KB |
| `GET /api/tabela base_detalhada` — 1.000 linhas | 0,05 s | 345 KB |
| `GET /api/cliente` — pior caso, 3.628 entregas | 0,68 s | 867 KB |

É a paginação no servidor que sustenta isso: a aba Resultados abre a base
detalhada de 326 mil linhas mandando 35 KB para o navegador.

Leitura de rede é o gargalo real quando a base vem do file server: o
compartilhamento SMB do `V:` entrega ~10,6 MB/s medidos, ou ~20 s por arquivo de 200 MB na
primeira leitura. A segunda leitura do mesmo arquivo cai para ~3 s, pelo cache
do sistema operacional — vantagem que só existe rodando na máquina.

---

## Limitações conhecidas

- **Não há autenticação, e por isso o servidor escuta só em `127.0.0.1`.** A
  tela mostra código de cliente, motorista, veículo, transportadora e
  coordenadas. Não troque o host por `0.0.0.0` sem antes construir controle de
  acesso.
- **A suíte de testes cobre o cálculo, não a tela.** `tests/` trava os tempos
  de referência, a leitura de datas, os parâmetros e a janela; rode com
  `.venv\Scripts\python.exe -m pytest tests -q` (pytest vem de
  `requirements-dev.txt`). A interface continua verificada à mão.
- **A exportação em Excel de uma base grande é lenta**, por natureza do formato:
  são ~15 milhões de células em oito abas, e passa de 6 minutos. O ZIP de CSVs
  leva 38 s e carrega os mesmos dados. A gravação é feita em disco, em lotes,
  então o custo é de tempo e não de memória.
- **A exportação roda dentro da requisição HTTP, sem barra de progresso.** A
  tela avisa e espera; a janela não pode ser fechada no meio.
- **Se o processo for morto durante a gravação do Excel**, o openpyxl deixa os
  arquivos de rascunho em `%TEMP%\openpyxl.*` — podem passar de 900 MB. A
  limpeza só acontece no encerramento normal; apague-os na mão se isso
  acontecer.
- **`transform_base` é o estágio mais caro** depois do porte (4,7 s em 370 mil
  linhas), concentrado nas colunas derivadas por `dt.strftime`. Não foi tocado:
  dá para melhorar, mas não é mais o gargalo.

---

## O que a saída do Streamlit rendeu

O porte está fechado: `app.py`, `streamlit_app.py`, `.devcontainer/`,
`.github/` e `.streamlit/` foram removidos, e as 30 dependências que existiam
só por causa do Streamlit saíram do `.venv`.

| | Streamlit | UI própria |
|---|---|---|
| RSS ao abrir | 124 MB | **70 MB** |
| Tempo de import | 1,18 s | **0,45 s** |
| Módulos carregados | 1.240 | **636** |
| `site-packages` | 389 MB | **191 MB** |
| Pacotes instalados | 41 | **12** |
| Dependências declaradas | 5 | **4** |

Saíram do `.venv`: `streamlit`, `altair`, `pydeck`, `pyarrow`, `protobuf`,
`watchdog`, `jsonschema`, `jsonschema-specifications`, `referencing`,
`rpds-py`, `attrs`, `pillow`, `jinja2`, `markupsafe`, `click`, `toml`,
`requests`, `urllib3`, `certifi`, `charset-normalizer`, `idna`, `starlette`,
`uvicorn`, `anyio`, `h11`, `httptools`, `websockets`, `itsdangerous`,
`python-multipart`, `typing-extensions`.

Ficaram, como manda o `requirements.txt`: `pandas`, `numpy`, `openpyxl`,
`plotly` e o que eles puxam (`narwhals`, `packaging`, `python-dateutil`,
`pytz`, `six`, `tzdata`, `et_xmlfile`).

Para refazer um `.venv` do zero:

```powershell
py -m venv .venv
.venv\Scripts\python.exe -m pip install -r requirements.txt
```

Tudo o que foi removido está versionado em git e volta com
`git checkout HEAD -- <arquivo>`.

### Código morto que saiu junto

- `src/utils.py` — `preparar_dataframe_streamlit`, que existia só para
  contornar a serialização Arrow do `st.dataframe`.
- `src/engine.py` — `exportar_excel`, `exportar_zip_csv` e
  `_df_para_csv_bytes`, as versões que montavam o arquivo em memória para o
  `st.download_button`. Ficaram `gravar_excel` e `gravar_zip_csv`.
- `config.py` — `LAYOUT`, que era o parâmetro `layout=` do
  `st.set_page_config`.

Continuam no `config.py`, sem uso no aplicativo, e são decisão sua manter ou
não: `initialize_directories`, `ASSETS_DIR`, `EXPORT_DIR` e `AVAILABLE_UNITS`.
Fazem par com `load_unit_file` em `src/data_loader.py`, o atalho da convenção
`{UNIDADE}_data.csv`, que também não é mais chamado por nenhuma tela.
