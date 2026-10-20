using System.Globalization;
using System.Text;
using Luna.Application;

namespace Luna.Infrastructure.Csv;

/// <summary>
/// <c>read_csv_safely</c> (src/data_loader.py): tenta seis combinações de
/// separador e encoding e fica com a primeira que produz mais de uma coluna.
/// Cada tentativa segue o <c>pd.read_csv(..., dtype=str)</c> do pandas 2.3.3:
/// tudo como texto, marcadores de nulo do pandas viram nulo, cabeçalho com
/// <c>Unnamed: i</c> e duplicatas <c>.N</c>, índice implícito quando a
/// primeira linha de dados tem campos a mais.
/// </summary>
public static class LeitorCsv
{
    private static readonly (char Separador, string Encoding)[] Tentativas =
    [
        (';', "utf-8-sig"), (',', "utf-8-sig"),
        (';', "latin1"), (',', "latin1"),
        (';', "cp1252"), (',', "cp1252"),
    ];

    /// <summary>STR_NA_VALUES do pandas: com dtype=str, estes textos (exatos) viram nulo.</summary>
    private static readonly HashSet<string> MarcadoresDeNulo = new(StringComparer.Ordinal)
    {
        "", "#N/A", "#N/A N/A", "#NA", "-1.#IND", "-1.#QNAN", "-NaN", "-nan", "1.#IND", "1.#QNAN",
        "<NA>", "N/A", "NA", "NULL", "NaN", "None", "n/a", "nan", "null",
    };

    // Bytes sem caractere no cp1252 do Python (o decodificador estrito falha).
    private static readonly byte[] IndefinidosCp1252 = [0x81, 0x8D, 0x8F, 0x90, 0x9D];

    /// <exception cref="BaseInvalidaException">Nenhuma tentativa produziu mais de uma coluna.</exception>
    public static TabelaBruta Ler(string caminho, CancellationToken cancelamento = default)
    {
        byte[] bytes = File.ReadAllBytes(caminho);
        string nome = Path.GetFileName(caminho);
        string? ultimoErro = null;
        var umaColuna = new List<string>();

        foreach (var (separador, encoding) in Tentativas)
        {
            // Cada tentativa lê o arquivo todo (0,2 s em 300 mil linhas): confere entre elas.
            cancelamento.ThrowIfCancellationRequested();
            TabelaBruta tabela;
            try
            {
                var (codificacao, inicio) = Validar(bytes, encoding);
                tabela = Montar(TokenizadorCsv.Tokenizar(bytes, inicio, codificacao, separador));
            }
            catch (Exception e) when (e is ErroDeTokenizacaoException or DecoderFallbackException or ErroDeDecodificacaoException)
            {
                ultimoErro = e.Message;
                continue;
            }

            if (tabela.Colunas.Count > 1)
                return tabela;

            string primeira = tabela.Colunas.Count > 0 ? tabela.Colunas[0] : "";
            umaColuna.Add($"sep='{separador}' encoding='{encoding}' -> 1 coluna ('{PrimeirosCodePoints(primeira, 60)}')");
        }

        if (umaColuna.Count > 0)
        {
            throw new BaseInvalidaException(
                $"Erro ao ler o CSV '{nome}'. Nenhum separador testado produziu mais de uma coluna. " +
                $"Tentativas: {string.Join(" | ", umaColuna)}");
        }
        throw new BaseInvalidaException(
            $"Erro ao ler o CSV '{nome}'. Não foi possível interpretar o arquivo com os formatos testados. " +
            $"Último erro: {ultimoErro}");
    }

    private sealed class ErroDeDecodificacaoException(string mensagem) : Exception(mensagem);

    /// <summary>
    /// Decodificação estrita, como a do Python para cada encoding: falha aqui,
    /// antes de tokenizar, com a mesma exceção de decodificar tudo de uma vez.
    /// Devolve o encoding e onde o texto começa (depois do BOM no utf-8-sig).
    /// </summary>
    private static (Encoding Codificacao, int Inicio) Validar(byte[] bytes, string encoding)
    {
        switch (encoding)
        {
            case "utf-8-sig":
                bool temBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                int inicio = temBom ? 3 : 0;
                var utf8 = new UTF8Encoding(false, true);
                utf8.GetCharCount(bytes, inicio, bytes.Length - inicio);
                return (utf8, inicio);
            case "latin1":
                return (Encoding.Latin1, 0);
            case "cp1252":
                int indefinido = Array.FindIndex(bytes, b => Array.IndexOf(IndefinidosCp1252, b) >= 0);
                if (indefinido >= 0)
                {
                    throw new ErroDeDecodificacaoException(string.Create(CultureInfo.InvariantCulture,
                        $"'charmap' codec can't decode byte 0x{bytes[indefinido]:x2} in position {indefinido}: character maps to <undefined>"));
                }
                return (CodePagesEncodingProvider.Instance.GetEncoding(1252)!, 0);
            default:
                throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null);
        }
    }

    /// <summary>
    /// Cabeçalho (parsers.pyx, _get_header) e linhas de dados. Sem nenhuma
    /// linha, o pandas dá EmptyDataError.
    /// </summary>
    private static TabelaBruta Montar(List<string[]> linhas)
    {
        if (linhas.Count == 0)
            throw new ErroDeTokenizacaoException("No columns to parse from file");

        var cabecalho = NomesDoCabecalho(linhas[0]);
        // Índice implícito: a primeira linha de dados com campos a mais faz
        // dos primeiros campos o índice do DataFrame (descartado pelo motor).
        int camposDoIndice = linhas.Count > 1 ? Math.Max(0, linhas[1].Length - cabecalho.Count) : 0;

        var dados = new List<IReadOnlyList<object?>>(linhas.Count - 1);
        for (int i = 1; i < linhas.Count; i++)
        {
            string?[] linha = linhas[i];
            linhas[i] = null!;
            // Caso comum: a própria linha vira o registro, com os nulos no lugar.
            if (camposDoIndice == 0 && linha.Length == cabecalho.Count)
            {
                for (int j = 0; j < linha.Length; j++)
                {
                    if (MarcadoresDeNulo.Contains(linha[j]!))
                        linha[j] = null;
                }
                dados.Add(linha);
                continue;
            }
            var valores = new object?[cabecalho.Count];
            for (int j = 0; j < cabecalho.Count; j++)
            {
                int indice = camposDoIndice + j;
                string? campo = indice < linha.Length ? linha[indice] : null;
                valores[j] = campo is null || MarcadoresDeNulo.Contains(campo) ? null : campo;
            }
            dados.Add(valores);
        }
        return new TabelaBruta(cabecalho, dados);
    }

    /// <summary>
    /// Nome vazio vira <c>Unnamed: i</c>; duplicatas ganham <c>.1</c>, <c>.2</c>...,
    /// pulando nomes que já existem no cabeçalho, com os nomes regulares antes
    /// dos sem nome.
    /// </summary>
    private static List<string> NomesDoCabecalho(IReadOnlyList<string> campos)
    {
        var nomes = new List<string>(campos.Count);
        var semNome = new List<int>();
        for (int i = 0; i < campos.Count; i++)
        {
            if (campos[i].Length == 0)
            {
                nomes.Add(string.Create(CultureInfo.InvariantCulture, $"Unnamed: {i}"));
                semNome.Add(i);
            }
            else
            {
                nomes.Add(campos[i]);
            }
        }

        var ordem = Enumerable.Range(0, nomes.Count).Where(i => !semNome.Contains(i)).Concat(semNome);
        var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (int i in ordem)
        {
            string coluna = nomes[i];
            string original = coluna;
            int vezes = contagem.GetValueOrDefault(coluna);
            if (vezes > 0)
            {
                while (vezes > 0)
                {
                    contagem[original] = vezes + 1;
                    coluna = string.Create(CultureInfo.InvariantCulture, $"{original}.{vezes}");
                    if (nomes.Contains(coluna))
                        vezes++;
                    else
                        vezes = contagem.GetValueOrDefault(coluna);
                }
            }
            nomes[i] = coluna;
            contagem[coluna] = vezes + 1;
        }
        return nomes;
    }

    // texto[:n] do Python: n code points, não n unidades UTF-16.
    private static string PrimeirosCodePoints(string texto, int n)
    {
        int corte = 0, contados = 0;
        for (int i = 0; i < texto.Length && contados < n; contados++)
            i = corte = i + (char.IsSurrogatePair(texto, i) ? 2 : 1);
        return texto[..corte];
    }
}
