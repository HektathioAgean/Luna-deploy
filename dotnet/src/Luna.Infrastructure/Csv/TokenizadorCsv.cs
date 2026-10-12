namespace Luna.Infrastructure.Csv;

/// <summary>Falha do tokenizer; a mensagem segue a do ParserError do pandas.</summary>
internal sealed class ErroDeTokenizacaoException(string mensagem) : Exception(mensagem);

/// <summary>
/// Porta do tokenizer C do pandas 2.3.3 (pandas/_libs/src/parser/tokenizer.c,
/// tokenize_bytes, end_line e parser_handle_eof) com os padrões de
/// <c>pd.read_csv</c> que o motor usa: aspas duplas com <c>""</c> escapando
/// <c>"</c>, sem caractere de escape, sem comentário, linhas vazias e só com
/// espaço/tab puladas, fim de linha LF, CRLF ou CR.
/// <para>
/// Fim de linha (end_line): a linha de cabeçalho e a primeira de dados não
/// são conferidas; a partir da segunda linha de dados, campo a mais que a
/// linha anterior é erro e campo a menos é completado com vazio.
/// </para>
/// </summary>
internal static class TokenizadorCsv
{
    private enum Estado
    {
        InicioRegistro,
        InicioCampo,
        EmCampo,
        EmCampoComAspas,
        AspasEmCampoComAspas,
        ComerCrLf,
        ComerCrLfSemLinha,
        LinhaDeEspacos,
    }

    private const char Aspas = '"';

    private static bool EspacoOuTab(char c) => c is ' ' or '\t';

    /// <summary>Linhas aceitas (a primeira é o cabeçalho), cada uma com seus campos.</summary>
    public static List<List<string>> Tokenizar(string texto, char separador)
    {
        var linhas = new List<List<string>>();
        var campos = new List<string>();
        var campo = new System.Text.StringBuilder();
        long linhasDoArquivo = 0;
        var estado = Estado.InicioRegistro;

        void FecharCampo()
        {
            campos.Add(campo.ToString());
            campo.Clear();
        }

        void FecharLinha()
        {
            int quantidade = campos.Count;
            int esperado = linhas.Count > 0 ? linhas[^1].Count : -1;
            // Cabeçalho e primeira linha de dados ficam fora da conferência.
            if (linhas.Count > 1 && quantidade > esperado)
            {
                linhasDoArquivo++;
                throw new ErroDeTokenizacaoException(
                    $"Error tokenizing data. C error: Expected {esperado} fields in line {linhasDoArquivo}, saw {quantidade}\n");
            }
            if (linhas.Count >= 1)
            {
                while (campos.Count < esperado)
                    campos.Add("");
            }
            linhasDoArquivo++;
            linhas.Add(campos);
            campos = [];
        }

        for (int i = 0; i < texto.Length; i++)
        {
            char c = texto[i];
            switch (estado)
            {
                case Estado.LinhaDeEspacos:
                    if (c == '\n')
                    {
                        linhasDoArquivo++;
                        estado = Estado.InicioRegistro;
                    }
                    else if (c == '\r')
                    {
                        linhasDoArquivo++;
                        estado = Estado.ComerCrLfSemLinha;
                    }
                    else if (!(EspacoOuTab(c) && c != separador))
                    {
                        // Não era linha em branco: volta ao começo da linha
                        // (depois do último '\n', como o C) e relê como campo.
                        int inicio = texto.LastIndexOf('\n', i - 1) + 1;
                        i = inicio - 1;
                        estado = Estado.InicioCampo;
                    }
                    break;

                case Estado.InicioRegistro:
                    if (c == '\n')
                    {
                        linhasDoArquivo++;
                        break;
                    }
                    if (c == '\r')
                    {
                        linhasDoArquivo++;
                        estado = Estado.ComerCrLfSemLinha;
                        break;
                    }
                    if (EspacoOuTab(c) && c != separador)
                    {
                        estado = Estado.LinhaDeEspacos;
                        break;
                    }
                    estado = Estado.InicioCampo;
                    goto case Estado.InicioCampo;

                case Estado.InicioCampo:
                    if (c == '\n')
                    {
                        FecharCampo();
                        FecharLinha();
                        estado = Estado.InicioRegistro;
                    }
                    else if (c == '\r')
                    {
                        FecharCampo();
                        estado = Estado.ComerCrLf;
                    }
                    else if (c == Aspas)
                    {
                        estado = Estado.EmCampoComAspas;
                    }
                    else if (c == separador)
                    {
                        FecharCampo();
                    }
                    else
                    {
                        campo.Append(c);
                        estado = Estado.EmCampo;
                    }
                    break;

                case Estado.EmCampo:
                    if (c == '\n')
                    {
                        FecharCampo();
                        FecharLinha();
                        estado = Estado.InicioRegistro;
                    }
                    else if (c == '\r')
                    {
                        FecharCampo();
                        estado = Estado.ComerCrLf;
                    }
                    else if (c == separador)
                    {
                        FecharCampo();
                        estado = Estado.InicioCampo;
                    }
                    else
                    {
                        campo.Append(c);
                    }
                    break;

                case Estado.EmCampoComAspas:
                    if (c == Aspas)
                        estado = Estado.AspasEmCampoComAspas;
                    else
                        campo.Append(c);
                    break;

                case Estado.AspasEmCampoComAspas:
                    if (c == Aspas)
                    {
                        campo.Append(c);
                        estado = Estado.EmCampoComAspas;
                    }
                    else if (c == separador)
                    {
                        FecharCampo();
                        estado = Estado.InicioCampo;
                    }
                    else if (c == '\n')
                    {
                        FecharCampo();
                        FecharLinha();
                        estado = Estado.InicioRegistro;
                    }
                    else if (c == '\r')
                    {
                        FecharCampo();
                        estado = Estado.ComerCrLf;
                    }
                    else
                    {
                        campo.Append(c);
                        estado = Estado.EmCampo;
                    }
                    break;

                case Estado.ComerCrLf:
                    if (c == '\n')
                    {
                        FecharLinha();
                        estado = Estado.InicioRegistro;
                    }
                    else if (c == separador)
                    {
                        // Arquivo separado por CR: a linha nova começa com campo vazio.
                        FecharLinha();
                        FecharCampo();
                        estado = Estado.InicioCampo;
                    }
                    else
                    {
                        FecharLinha();
                        estado = Estado.InicioRegistro;
                        i--;
                    }
                    break;

                case Estado.ComerCrLfSemLinha:
                    estado = Estado.InicioRegistro;
                    if (c != '\n' && c != separador)
                        i--;
                    break;
            }
        }

        // parser_handle_eof
        switch (estado)
        {
            case Estado.InicioRegistro:
            case Estado.LinhaDeEspacos:
            case Estado.ComerCrLfSemLinha:
                return linhas;
            case Estado.EmCampoComAspas:
                throw new ErroDeTokenizacaoException(
                    $"Error tokenizing data. C error: EOF inside string starting at row {linhasDoArquivo}");
            case Estado.EmCampo:
            case Estado.InicioCampo:
            case Estado.AspasEmCampoComAspas:
                FecharCampo();
                break;
        }
        FecharLinha();
        return linhas;
    }
}
