"""
============================================================
LUNA - LAUNCHER SEM CONSOLE
============================================================
Abre o Luna como um aplicativo: sem terminal, sem VSCode.

  - Roda com pythonw.exe (o atalho da area de trabalho aponta para ca).
  - Se o Luna ja estiver no ar, so abre a janela de novo.
  - A janela abre no modo app do Edge/Chrome; sem eles, no navegador padrao.
  - Saida e erros do servidor vao para logs/luna_app.log.
  - Para fechar: botao "Encerrar" na propria interface.

Criar o atalho:  powershell -ExecutionPolicy Bypass -File ferramentas\\criar_atalho.ps1
============================================================
"""

from __future__ import annotations

import os
import subprocess
import sys
import threading
import traceback
import urllib.request
import webbrowser
from pathlib import Path

RAIZ = Path(__file__).resolve().parent
LOG = RAIZ / "logs" / "luna_app.log"

NAVEGADORES_APP = (
    r"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
    r"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
    r"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
    r"%LocalAppData%\Google\Chrome\Application\chrome.exe",
)


def luna_no_ar(url: str) -> bool:
    try:
        with urllib.request.urlopen(url + "api/estado", timeout=1.5) as resposta:
            return resposta.status == 200
    except OSError:
        return False


def abrir_janela(url: str) -> None:
    for modelo in NAVEGADORES_APP:
        exe = Path(os.path.expandvars(modelo))
        if exe.is_file():
            subprocess.Popen([str(exe), f"--app={url}", "--window-size=1500,950"])
            return
    webbrowser.open(url)


def main() -> int:
    os.chdir(RAIZ)
    if str(RAIZ) not in sys.path:
        sys.path.insert(0, str(RAIZ))

    import luna

    url = f"http://{luna.HOST}:{luna.PORTA}/"
    if luna_no_ar(url):
        abrir_janela(url)
        return 0

    # pythonw nao tem console: sem isto, print() e tracebacks somem.
    # So abre depois do teste acima para nao zerar o log da instancia que ja roda.
    LOG.parent.mkdir(exist_ok=True)
    saida = open(LOG, "w", encoding="utf-8", buffering=1)
    sys.stdout = sys.stderr = saida

    sys.argv = [sys.argv[0], "--sem-navegador"]
    threading.Timer(0.6, abrir_janela, args=(url,)).start()
    try:
        return luna.main()
    except Exception:
        traceback.print_exc()
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
