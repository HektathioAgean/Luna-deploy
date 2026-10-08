from __future__ import annotations

"""
============================================================
LUNA - ANALISE DE TEMPOS OPERACIONAIS (APLICATIVO LOCAL)
============================================================
Servidor HTTP da biblioteca padrao que serve a interface de interface/ e
expoe o pipeline de src/pipeline.py como rotas JSON.

  - Roda so em 127.0.0.1. A tela mostra cliente, motorista, veiculo e
    coordenadas: nada disso vai para a LAN.
  - Um processamento por vez (ferramenta mono-usuario).
  - A base e escolhida por caminho, em qualquer lugar do disco ou da rede.
  - Os graficos sao montados em Python (plotly) e enviados como JSON; o
    navegador so chama Plotly.newPlot.

Uso:
  py luna.py                      # abre a interface no navegador
  py luna.py --porta 8782
  py luna.py --sem-navegador
============================================================
"""

import argparse
import json
import math
import string
import sys
import threading
import time
import webbrowser
from dataclasses import dataclass, field
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse

RAIZ = Path(__file__).resolve().parent
if str(RAIZ) not in sys.path:
    sys.path.insert(0, str(RAIZ))

import pandas as pd  # noqa: E402

from config import APP_NAME, APP_TITLE, THEME  # noqa: E402
from src.pipeline import (  # noqa: E402
    BASES_JANELA,
    FORMATOS_EXPORTACAO,
    Parametros,
    clientes_disponiveis,
    executar,
    exportar,
    montar_painel_cliente,
)
from src.schema import (  # noqa: E402
    get_aliases_dataframe,
    get_schema_dataframe,
    suggest_missing_columns,
)
from src.utils import OPCOES_DIA_SEMANA  # noqa: E402

HOST = "127.0.0.1"   # local so. base operacional nunca vai p/ LAN
PORTA = 8781         # 8780 e do Mechanicus, 8770 do hub_ui

INTERFACE = RAIZ / "interface"

TIPOS_MIME = {
    ".html": "text/html",
    ".css": "text/css",
    ".js": "text/javascript",
    ".svg": "image/svg+xml",
}

EXTENSOES_BASE = {".csv", ".xlsx", ".xlsm"}

# Tabelas que a aba Resultados e a aba Base oferecem, e as colunas de cada uma
# quando a tela nao mostra todas. A lista de janelas_entrega reproduz a selecao
# que a aba Resultados fazia no Streamlit.
TABELAS = (
    "base_padronizada",
    "processados",
    "inconsistencias",
    "expurgados",
    "anomalias",
    "medianas",
    "base_detalhada",
    "janelas_entrega",
)

COLUNAS_EXIBICAO = {
    "janelas_entrega": [
        "Cod_Cliente",
        "Qtd_Apontamentos",
        "Janela_Inicio",
        "Janela_Fim",
        "Janela_Largura_Min",
        "Cobertura_Alvo",
        "Cobertura_Real",
        "Cruza_MeiaNoite",
        "Periodo_Predominante",
        "Comercial",
        "Base_Janela",
    ],
}

# Tabelas fixas, que nao dependem de processamento.
TABELAS_ESTATICAS = {
    "schema": get_schema_dataframe,
    "aliases": get_aliases_dataframe,
}

TAMANHO_PAGINA_PADRAO = 100
TAMANHO_PAGINA_MAXIMO = 1000


# ============================================================
# ESTADO  (1 processamento por vez)
# ============================================================
@dataclass
class Execucao:
    parametros: Parametros
    estagios: list[str] = field(default_factory=list)
    resultado: dict | None = None
    erro: str | None = None
    terminada: bool = False
    inicio: float = field(default_factory=time.monotonic)
    fim: float = 0.0

    @property
    def rodando(self) -> bool:
        return not self.terminada

    @property
    def segundos(self) -> float:
        return (self.fim or time.monotonic()) - self.inicio


EXECUCAO: Execucao | None = None
SERVIDOR: ThreadingHTTPServer | None = None
_TRAVA = threading.Lock()


def _trabalhar(execucao: Execucao) -> None:
    try:
        execucao.resultado = executar(
            execucao.parametros,
            progresso=execucao.estagios.append,
        )
    except Exception as exc:  # noqa: BLE001 - a tela precisa ver qualquer falha
        execucao.erro = f"{type(exc).__name__}: {exc}"
        execucao.estagios.append("erro")
    finally:
        execucao.fim = time.monotonic()
        execucao.terminada = True


def iniciar(parametros: Parametros) -> tuple[bool, str]:
    global EXECUCAO
    with _TRAVA:
        if EXECUCAO is not None and EXECUCAO.rodando:
            return False, "Ja existe um processamento em andamento."
        execucao = Execucao(parametros=parametros)
        EXECUCAO = execucao
        threading.Thread(target=_trabalhar, args=(execucao,), daemon=True).start()
        return True, "iniciado"


def resultado_atual() -> dict:
    """Resultado pronto, ou erro explicando por que nao ha um."""
    execucao = EXECUCAO
    if execucao is None:
        raise ValueError("Nenhuma base foi processada ainda.")
    if execucao.rodando:
        raise ValueError("O processamento ainda esta em andamento.")
    if execucao.erro is not None:
        raise ValueError(execucao.erro)
    assert execucao.resultado is not None
    return execucao.resultado


# ============================================================
# SERIALIZACAO
# ============================================================
def _valor_json(valor):
    """Converte um valor de celula para algo que o json aceita."""
    if valor is None or valor is pd.NaT:
        return None
    if isinstance(valor, float):
        return None if math.isnan(valor) or math.isinf(valor) else valor
    if isinstance(valor, (bool, int, str)):
        return valor
    if isinstance(valor, (pd.Timestamp,)):
        return valor.strftime("%Y-%m-%d %H:%M:%S")
    try:
        if pd.isna(valor):
            return None
    except (TypeError, ValueError):
        pass
    if hasattr(valor, "item"):          # escalares do numpy
        return _valor_json(valor.item())
    return str(valor)


def tabela_json(
    df: pd.DataFrame,
    pagina: int = 1,
    tamanho: int = TAMANHO_PAGINA_PADRAO,
    colunas: list[str] | None = None,
) -> dict:
    """Pagina a tabela no servidor: a tela nunca recebe a base inteira."""
    if df is None or df.empty:
        return {"total": 0, "pagina": 1, "paginas": 0, "tamanho": tamanho,
                "colunas": [], "linhas": []}

    if colunas:
        presentes = [c for c in colunas if c in df.columns]
        df = df[presentes]

    tamanho = max(1, min(int(tamanho), TAMANHO_PAGINA_MAXIMO))
    total = int(len(df))
    paginas = max(1, math.ceil(total / tamanho))
    pagina = max(1, min(int(pagina), paginas))
    inicio = (pagina - 1) * tamanho

    recorte = df.iloc[inicio:inicio + tamanho]
    nomes = [str(c) for c in recorte.columns]

    linhas = [
        [_valor_json(valor) for valor in registro]
        for registro in recorte.itertuples(index=False, name=None)
    ]

    return {
        "total": total,
        "pagina": pagina,
        "paginas": paginas,
        "tamanho": tamanho,
        "primeira_linha": inicio + 1,
        "colunas": nomes,
        "linhas": linhas,
    }


def figura_json(figura) -> dict | None:
    """plotly -> dict pronto para Plotly.newPlot. None quando nao ha figura."""
    if figura is None:
        return None
    return json.loads(figura.to_json())


# ============================================================
# ROTAS DE DADOS
# ============================================================
def dados_estado() -> dict:
    execucao = EXECUCAO
    if execucao is None:
        return {
            "app": APP_NAME,
            "titulo": APP_TITLE,
            "rodando": False,
            "processado": False,
            "erro": None,
            "estagios": [],
            "parametros": None,
        }
    return {
        "app": APP_NAME,
        "titulo": APP_TITLE,
        "rodando": execucao.rodando,
        "processado": execucao.terminada and execucao.erro is None,
        "erro": execucao.erro,
        "estagios": list(execucao.estagios),
        "segundos": round(execucao.segundos, 1),
        "parametros": execucao.parametros.como_dict(),
    }


def dados_opcoes() -> dict:
    """Tudo que a tela precisa para montar o formulario, numa chamada."""
    return {
        "app": APP_NAME,
        "titulo": APP_TITLE,
        "tema": THEME,
        "porta": PORTA,
        "padrao": _parametros_padrao(),
        "bases_janela": list(BASES_JANELA),
        "dias_semana": list(OPCOES_DIA_SEMANA),
        "formatos_exportacao": sorted(FORMATOS_EXPORTACAO),
        "extensoes_base": sorted(EXTENSOES_BASE),
        "tabelas": list(TABELAS),
    }


def _parametros_padrao() -> dict:
    """Valores padrao do formulario, lidos da propria dataclass."""
    padrao = Parametros(caminho="__padrao__").como_dict()
    padrao["caminho"] = ""
    return padrao


def dados_procurar(caminho: str) -> dict:
    """
    Lista pastas e bases de um diretorio.

    Navega qualquer lugar que o usuario ja alcanca pelo Explorer, inclusive os
    drives de rede: e a razao de existir desta rota. O servidor so escuta em
    127.0.0.1, entao o alcance aqui e o mesmo da conta que abriu o aplicativo.
    """
    if not caminho.strip():
        return {
            "caminho": "",
            "pai": None,
            "drives": _drives(),
            "pastas": [],
            "arquivos": [],
        }

    alvo = Path(caminho).expanduser()
    try:
        alvo = alvo.resolve()
    except OSError as exc:
        raise ValueError(f"Caminho invalido: {exc}") from exc

    if alvo.is_file():
        alvo = alvo.parent

    if not alvo.is_dir():
        raise ValueError(f"Pasta nao encontrada: {alvo}")

    pastas: list[dict] = []
    arquivos: list[dict] = []

    try:
        entradas = sorted(alvo.iterdir(), key=lambda p: p.name.lower())
    except PermissionError as exc:
        raise ValueError(f"Sem permissao de leitura em {alvo}") from exc

    for entrada in entradas:
        try:
            if entrada.is_dir():
                pastas.append({"nome": entrada.name, "caminho": str(entrada)})
            elif entrada.suffix.lower() in EXTENSOES_BASE:
                info = entrada.stat()
                arquivos.append({
                    "nome": entrada.name,
                    "caminho": str(entrada),
                    "bytes": int(info.st_size),
                    "medida": _medida(info.st_size),
                    "modificado": time.strftime(
                        "%d/%m/%Y %H:%M", time.localtime(info.st_mtime)
                    ),
                })
        except OSError:
            continue        # entrada que o SO nao deixa consultar: segue a lista

    pai = str(alvo.parent) if alvo.parent != alvo else None

    return {
        "caminho": str(alvo),
        "pai": pai,
        "drives": _drives(),
        "pastas": pastas,
        "arquivos": arquivos,
    }


def _medida(tamanho: int) -> str:
    """Tamanho legivel. As bases vao de alguns KB a centenas de MB."""
    if tamanho >= 1024 ** 3:
        return f"{tamanho / 1024 ** 3:.2f} GB"
    if tamanho >= 1024 ** 2:
        return f"{tamanho / 1024 ** 2:.1f} MB"
    if tamanho >= 1024:
        return f"{tamanho / 1024:.0f} KB"
    return f"{tamanho} B"


def _drives() -> list[str]:
    """Letras de unidade montadas, para o atalho de navegacao."""
    return [
        f"{letra}:\\"
        for letra in string.ascii_uppercase
        if Path(f"{letra}:\\").exists()
    ]


def dados_resultado() -> dict:
    """O que as abas Base, Validacao e Processamento mostram."""
    resultado = resultado_atual()
    validacao = resultado["relatorio_validacao"]
    parametros: Parametros = resultado["parametros"]

    contagens = {nome: int(len(resultado[nome])) for nome in TABELAS}

    dados = {
        "caminho": resultado["caminho"],
        "nome_base": Path(resultado["caminho"]).name,
        "linhas_brutas": resultado["linhas_brutas"],
        "parametros": parametros.como_dict(),
        "kpis": resultado["kpis"],
        "contagens": contagens,
        "validacao": {
            "is_valid": validacao["is_valid"],
            "total_columns": validacao["total_columns"],
            "total_required": validacao["total_required"],
            "total_optional": validacao["total_optional"],
            "required_found": validacao["required_found"],
            "required_missing": validacao["required_missing"],
            "optional_found": validacao["optional_found"],
            "unknown_columns": validacao["unknown_columns"],
            "duplicate_standardized_columns": validacao["duplicate_standardized_columns"],
        },
    }

    if not validacao["is_valid"]:
        dados["sugestoes"] = suggest_missing_columns(resultado["schema_report"])

    return dados


def dados_tabela(nome: str, pagina: int, tamanho: int) -> dict:
    if nome in TABELAS_ESTATICAS:
        return tabela_json(TABELAS_ESTATICAS[nome](), pagina, tamanho)

    # Checa o nome antes de exigir processamento: um nome errado tem de dizer
    # que o nome esta errado, nao que falta processar.
    if nome not in TABELAS and nome != "mapeamento":
        raise ValueError(f"Tabela desconhecida: {nome}")

    resultado = resultado_atual()

    if nome == "mapeamento":
        preview = pd.DataFrame(resultado["relatorio_validacao"]["mapping_preview"])
        return tabela_json(preview, pagina, tamanho)

    return tabela_json(
        resultado[nome], pagina, tamanho, colunas=COLUNAS_EXIBICAO.get(nome)
    )


def dados_clientes() -> dict:
    resultado = resultado_atual()
    return {"clientes": clientes_disponiveis(resultado)}


def dados_cliente(cliente: str, dia_semana: str, rotulos: bool) -> dict:
    resultado = resultado_atual()

    if not cliente.strip():
        raise ValueError("Nenhum cliente informado.")

    painel = montar_painel_cliente(
        resultado,
        cliente=cliente,
        dia_semana=dia_semana or "TODOS",
        mostrar_rotulos_tempo=rotulos,
    )

    tabela = painel.pop("tabela", None)
    painel["tabela"] = tabela_json(tabela, pagina=1, tamanho=TAMANHO_PAGINA_MAXIMO) \
        if tabela is not None else None
    painel["grafico"] = figura_json(painel.get("grafico"))
    painel["grafico_aberturas"] = figura_json(painel.get("grafico_aberturas"))

    janela = painel.get("janela")
    if janela is not None:
        painel["janela"] = {k: _valor_json(v) for k, v in janela.items()}

    return painel


def acao_exportar(corpo: dict) -> dict:
    resultado = resultado_atual()
    formato = str(corpo.get("formato", "xlsx"))
    destino = str(corpo.get("destino", "")).strip()

    if not destino:
        raise ValueError("Nenhuma pasta de destino informada.")

    alvo = exportar(resultado, formato=formato, destino=destino)
    return {"ok": True, "caminho": str(alvo)}


# ============================================================
# ARQUIVOS ESTATICOS
# ============================================================
def arquivo_de_interface(relativo: str) -> Path | None:
    """Resolve um caminho dentro de interface/. Fora dela ou de TIPOS_MIME: None."""
    alvo = (INTERFACE / relativo.lstrip("/")).resolve()
    if alvo.suffix not in TIPOS_MIME or INTERFACE not in alvo.parents:
        return None
    return alvo if alvo.is_file() else None


def plotly_js() -> Path:
    """
    O plotly.min.js do proprio pacote plotly instalado.

    Servir daqui, e nao de uma copia em interface/vendor/, garante que o
    JavaScript que desenha e da mesma versao do Python que monta a figura, e
    evita 4,6 MB versionados. Nenhum CDN: a rede corporativa tem inspecao TLS
    e o aplicativo precisa funcionar offline.
    """
    import plotly

    caminho = Path(plotly.__file__).parent / "package_data" / "plotly.min.js"
    if not caminho.is_file():
        raise FileNotFoundError(
            f"plotly.min.js nao encontrado em {caminho}. "
            "Reinstale o plotly:  py -m pip install -r requirements.txt"
        )
    return caminho


# ============================================================
# SERVIDOR
# ============================================================
class Handler(BaseHTTPRequestHandler):
    server_version = "Luna"

    def log_message(self, format: str, *args) -> None:  # noqa: A002 - assinatura da stdlib
        pass                                            # silencia o log de acesso

    # -------- helpers --------
    def _responder(self, corpo: bytes, tipo: str, codigo: int = 200) -> None:
        self.send_response(codigo)
        self.send_header("Content-Type", tipo)
        self.send_header("Content-Length", str(len(corpo)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(corpo)

    def _json(self, dados: dict, codigo: int = 200) -> None:
        self._responder(
            json.dumps(dados, ensure_ascii=False, allow_nan=False).encode("utf-8"),
            "application/json; charset=utf-8", codigo,
        )

    def _erro(self, mensagem: str, codigo: int = 400) -> None:
        self._json({"ok": False, "erro": mensagem}, codigo)

    def _corpo(self) -> dict:
        tamanho = int(self.headers.get("Content-Length") or 0)
        if not tamanho:
            return {}
        try:
            dados = json.loads(self.rfile.read(tamanho).decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            return {}
        return dados if isinstance(dados, dict) else {}

    def _estatico(self, relativo: str) -> None:
        alvo = arquivo_de_interface(relativo)
        if alvo is None:
            self._responder(b"nao encontrado", "text/plain; charset=utf-8", 404)
            return
        self._responder(alvo.read_bytes(), f"{TIPOS_MIME[alvo.suffix]}; charset=utf-8")

    # -------- rotas --------
    def do_GET(self) -> None:                    # noqa: N802 - assinatura da stdlib
        rota = urlparse(self.path)
        caminho = rota.path
        query = parse_qs(rota.query)

        def q(nome: str, padrao: str = "") -> str:
            return query.get(nome, [padrao])[0]

        if caminho in ("/", "/index.html"):
            self._estatico("index.html")
            return
        if caminho == "/vendor/plotly.min.js":
            try:
                self._responder(
                    plotly_js().read_bytes(), "text/javascript; charset=utf-8"
                )
            except FileNotFoundError as exc:
                self._responder(str(exc).encode("utf-8"),
                                "text/plain; charset=utf-8", 404)
            return
        if caminho.startswith("/interface/"):
            self._estatico(caminho[len("/interface/"):])
            return

        if caminho == "/api/progresso":
            self._progresso()
            return

        try:
            if caminho == "/api/estado":
                self._json(dados_estado())
            elif caminho == "/api/opcoes":
                self._json(dados_opcoes())
            elif caminho == "/api/procurar":
                self._json(dados_procurar(q("caminho")))
            elif caminho == "/api/resultado":
                self._json(dados_resultado())
            elif caminho == "/api/clientes":
                self._json(dados_clientes())
            elif caminho == "/api/tabela":
                self._json(dados_tabela(
                    q("nome"),
                    int(q("pagina", "1") or 1),
                    int(q("tamanho", str(TAMANHO_PAGINA_PADRAO)) or TAMANHO_PAGINA_PADRAO),
                ))
            elif caminho == "/api/cliente":
                self._json(dados_cliente(
                    q("cod"),
                    q("dia", "TODOS"),
                    q("rotulos", "1") != "0",
                ))
            else:
                self._responder(b"nao encontrado", "text/plain; charset=utf-8", 404)
        # Caminho inexistente ou parametro fora da faixa e erro de quem pediu,
        # nao do servidor: 400, com a mensagem original na tela.
        except (ValueError, FileNotFoundError, PermissionError) as exc:
            self._erro(str(exc))
        except Exception as exc:  # noqa: BLE001 - a tela precisa ver qualquer falha
            self._erro(f"{type(exc).__name__}: {exc}", 500)

    def do_POST(self) -> None:                   # noqa: N802 - assinatura da stdlib
        caminho = urlparse(self.path).path

        try:
            if caminho == "/api/processar":
                parametros = Parametros.de_dict(self._corpo())
                ok, msg = iniciar(parametros)
                self._json({"ok": ok, "msg": msg}, 200 if ok else 409)
            elif caminho == "/api/exportar":
                self._json(acao_exportar(self._corpo()))
            elif caminho == "/api/encerrar":
                self._json({"ok": True, "msg": "Luna encerrada."})
                if SERVIDOR is not None:
                    # shutdown() espera o serve_forever sair; nao pode rodar
                    # nesta thread.
                    threading.Thread(target=SERVIDOR.shutdown, daemon=True).start()
            else:
                self._responder(b"nao encontrado", "text/plain; charset=utf-8", 404)
        except (ValueError, FileNotFoundError, PermissionError) as exc:
            self._erro(str(exc))
        except Exception as exc:  # noqa: BLE001 - a tela precisa ver qualquer falha
            self._erro(f"{type(exc).__name__}: {exc}", 500)

    def _progresso(self) -> None:
        """SSE: manda cada estagio novo do processamento ate ele terminar."""
        # A resposta nao tem Content-Length. Sem fechar a conexao no fim, o
        # cliente fica esperando mais dados depois do evento 'fim'.
        self.close_connection = True
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream; charset=utf-8")
        self.send_header("Cache-Control", "no-cache")
        self.send_header("Connection", "close")
        self.end_headers()

        # Prende a execucao AGORA: se outra comecar, este stream nao pode passar
        # a ler os estagios da nova com um indice velho.
        execucao = EXECUCAO
        indice = 0
        try:
            while execucao is not None:
                if indice < len(execucao.estagios):
                    for estagio in execucao.estagios[indice:len(execucao.estagios)]:
                        dado = json.dumps(
                            {"estagio": estagio,
                             "segundos": round(execucao.segundos, 1)},
                            ensure_ascii=False,
                        )
                        self.wfile.write(f"event: estagio\ndata: {dado}\n\n".encode("utf-8"))
                    indice = len(execucao.estagios)
                    self.wfile.flush()
                elif execucao.terminada:
                    break
                else:
                    time.sleep(0.12)

            fim = json.dumps({
                "erro": execucao.erro if execucao is not None else "sem execucao",
                "segundos": round(execucao.segundos, 1) if execucao is not None else 0,
            }, ensure_ascii=False)
            self.wfile.write(f"event: fim\ndata: {fim}\n\n".encode("utf-8"))
            self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, OSError):
            pass    # navegador fechou a aba; nada a fazer


# ============================================================
# MAIN
# ============================================================
def criar_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="Luna - analise de tempos operacionais (aplicativo local)."
    )
    p.add_argument("--porta", type=int, default=PORTA, metavar="N")
    p.add_argument("--sem-navegador", action="store_true",
                   help="Nao abre o navegador sozinho.")
    return p


def main() -> int:
    args = criar_parser().parse_args()

    # Num console de verdade o Python ja escreve Unicode direto. Redirecionado
    # (`py luna.py > saida.txt`), ele cai no encoding da locale e quebra o
    # acento do banner. Declarar UTF-8 resolve nos dois casos.
    for fluxo in (sys.stdout, sys.stderr):
        if hasattr(fluxo, "reconfigure"):
            fluxo.reconfigure(encoding="utf-8")

    global SERVIDOR
    try:
        servidor = ThreadingHTTPServer((HOST, args.porta), Handler)
    except OSError as exc:
        print(f"[FALHA] Nao consegui abrir {HOST}:{args.porta} - {exc}", file=sys.stderr)
        print("        Tente outra porta:  py luna.py --porta 8782", file=sys.stderr)
        return 1

    SERVIDOR = servidor
    url = f"http://{HOST}:{args.porta}/"
    print("=" * 60)
    print(f"{APP_TITLE}")
    print("=" * 60)
    print(f"  Interface   : {url}")
    print(f"  Interpretador: Python {sys.version.split()[0]}")
    print("  Encerrar    : Ctrl+C")
    print("=" * 60)

    if not args.sem_navegador:
        threading.Timer(0.6, lambda: webbrowser.open(url)).start()

    try:
        servidor.serve_forever()
    except KeyboardInterrupt:
        print("\nEncerrando...")
    finally:
        servidor.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
