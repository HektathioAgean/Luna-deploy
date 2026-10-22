# Luna

Aplicativo para Windows que calcula os tempos operacionais de entrega por
cliente a partir de uma base de atendimentos em CSV ou Excel. Roda na própria
máquina: não instala nada, não precisa de administrador e não acessa a rede.

- Valida e padroniza a base e separa os registros inconsistentes.
- Calcula, por cliente, a mediana de atendimento, o tempo ideal (Q1) e o Q3.
- Mostra a janela de atendimento e um painel com gráficos por cliente.
- Exporta os resultados em Excel ou em ZIP de CSVs.

## Baixar

**[Baixar a versão mais recente](https://github.com/HektathioAgean/Luna-deploy/releases/latest)**

Na página da versão, em **Assets**:

| Arquivo | Para que serve |
|---|---|
| `Luna-<versão>-<commit>-win-x64.zip` | o aplicativo |
| `Luna-<versão>-<commit>-win-x64.zip.sha256` | o SHA-256 do ZIP, para conferir o download |
| `Luna-<versão>-<commit>-win-x64.MANIFESTO.txt` | opcional: tamanho e SHA-256 de cada arquivo do ZIP |

## Requisitos

- Windows 10 versão 2004 (build 19041) ou mais novo, ou Windows 11, 64 bits (x64).
- Cerca de 200 MB livres para a pasta extraída.
- 4 GB de memória.
- Não é preciso instalar .NET nem Python: tudo vem dentro do ZIP.

## Conferir o SHA-256

No PowerShell, na pasta onde o ZIP foi baixado:

```powershell
Get-FileHash .\Luna-*-win-x64.zip -Algorithm SHA256
```

O valor de `Hash` tem de ser igual ao código do arquivo `.sha256` (abra no
Bloco de Notas). Se for diferente, não abra o ZIP: apague e baixe de novo.

## Instalar e executar

1. Clique com o botão direito no ZIP > **Propriedades** > marque
   **Desbloquear** > **OK**.
2. Extraia o ZIP numa pasta local, fora do OneDrive, por exemplo
   `%LOCALAPPDATA%\Programs\Luna`.
3. Abra `Luna.exe`.

Para atualizar, feche o Luna, apague a pasta e extraia a versão nova. Para
remover, apague a pasta.

## SmartScreen e TI

O `Luna.exe` não é assinado digitalmente. O Windows SmartScreen ou a política
da empresa podem avisar ou bloquear a abertura. **No computador da empresa, não
contorne o aviso nem o bloqueio**: peça à TI a autorização do Luna, informando
o nome do ZIP, o SHA-256 e o link da versão. O resumo técnico para a TI está em
[Para a TI](docs/BAIXAR-E-INSTALAR.md#para-a-ti).

## Instruções detalhadas

Passo a passo completo: [Baixar e instalar o Luna](docs/BAIXAR-E-INSTALAR.md).
O `LEIA-ME.txt` dentro do ZIP traz as instruções da versão baixada.

## Suporte

Dúvidas ou problemas: abra uma
[issue](https://github.com/HektathioAgean/Luna-deploy/issues) com o nome do
ZIP e a mensagem exata que apareceu. Não anexe bases com dados da operação.

## Licença

Luna é distribuído sob a [Licença Apache 2.0](LICENSE). Os componentes de
terceiros incluídos no ZIP e as respectivas licenças estão em
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) (versão 1.0.0; cada ZIP traz
o seu).
