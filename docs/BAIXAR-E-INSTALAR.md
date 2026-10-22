# Baixar e instalar o Luna

Passo a passo para baixar, conferir e abrir o Luna no Windows. Não precisa de
administrador.

## Antes de começar

- Windows 10 versão 2004 (build 19041) ou mais novo, ou Windows 11, 64 bits.
  Para ver a sua versão: tecla Windows + R, digite `winver` e tecle Enter.
- Cerca de 200 MB livres e 4 GB de memória.

## 1. Baixar

1. Abra a [página da versão mais recente](https://github.com/HektathioAgean/Luna-deploy/releases/latest).
2. Em **Assets**, baixe o arquivo `.zip` e o arquivo `.zip.sha256` de mesmo
   nome.

## 2. Conferir o SHA-256

O SHA-256 é a impressão digital do arquivo. Se for igual ao publicado, o ZIP
chegou inteiro e é o mesmo da página da versão.

1. Abra o PowerShell na pasta do download. No Windows 11: clique com o botão
   direito num espaço vazio da pasta > **Abrir no Terminal**. No Windows 10:
   Shift + botão direito > **Abrir janela do PowerShell aqui**.
2. Rode:

   ```powershell
   Get-FileHash .\Luna-*-win-x64.zip -Algorithm SHA256
   ```

3. Abra o arquivo `.sha256` no Bloco de Notas. O código no início da linha tem
   de ser igual ao `Hash` mostrado pelo PowerShell.

Se forem diferentes, apague o ZIP e baixe de novo. Não abra um ZIP com SHA-256
diferente.

## 3. Desbloquear

Clique com o botão direito no ZIP > **Propriedades** > na aba **Geral**, marque
**Desbloquear** > **OK**. Sem isso, o Windows marca todos os arquivos extraídos
como vindos da internet. Se a opção não aparecer, o arquivo já está
desbloqueado. Se a política da empresa impedir, fale com a TI.

## 4. Extrair

1. Clique com o botão direito no ZIP > **Extrair tudo**.
2. Escolha uma pasta local, fora do OneDrive, por exemplo
   `%LOCALAPPDATA%\Programs\Luna` (isto é,
   `C:\Users\<seu usuário>\AppData\Local\Programs\Luna`).

## 5. Abrir

1. Na pasta extraída, abra `Luna.exe`.
2. Para ter um atalho: clique com o botão direito em `Luna.exe` > **Mostrar
   mais opções** (só no Windows 11) > **Enviar para** > **Área de trabalho
   (criar atalho)**.

Não mova nem apague os outros arquivos da pasta: o Luna precisa deles.

## 6. Se o Windows avisar ou bloquear

O `Luna.exe` não é assinado digitalmente. Por isso o Windows SmartScreen pode
mostrar um aviso, e a política da empresa (antivírus, AppLocker e similares)
pode impedir a abertura.

- No computador da empresa, **não contorne** o aviso nem o bloqueio.
- Peça à TI a autorização do Luna, com as informações da seção
  [Para a TI](#para-a-ti).

## 7. Atualizar ou remover

- **Atualizar:** feche o Luna, apague a pasta antiga e repita os passos 1 a 5
  com a versão nova.
- **Remover:** feche o Luna e apague a pasta (e o atalho, se criou um). Não há
  nada instalado no Windows.

## Para a TI

| Item | Situação |
|---|---|
| Distribuição | ZIP portátil para Windows x64, com o .NET incluído. Sem instalador. |
| Privilégios | Não precisa de administrador. |
| Rede | Não acessa a rede. |
| O que grava | Só os arquivos de exportação, na pasta escolhida pelo usuário; durante a gravação existe ali um arquivo `.parcial`, renomeado no fim. Não grava configurações, logs, temporários nem nada na pasta onde foi extraído, e lê a base sem copiá-la. |
| Assinatura | Executável não assinado digitalmente. |
| Integridade | SHA-256 do ZIP no arquivo `.sha256` publicado com a versão. O `MANIFESTO.txt` (dentro do ZIP e publicado ao lado dele) lista cada arquivo com tamanho e SHA-256. |
| Licenças | Apache 2.0 ([LICENSE](../LICENSE)). Terceiros em [THIRD-PARTY-NOTICES.txt](../THIRD-PARTY-NOTICES.txt), inclusive os termos da Microsoft para o .NET e o Windows SDK. |
| Homologação em computador corporativo | Pendente. |

Para pedir a autorização, informe o nome do ZIP, o SHA-256 e o link da página
da versão.
