using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Xml;
using Luna.Application;
using Luna.Domain;

namespace Luna.Infrastructure.Exportacao;

/// <summary>
/// <c>gravar_excel</c> (src/engine.py) com o openpyxl 3.1.5 em modo write_only:
/// uma aba por saída, na ordem de SAIDAS; tabela vazia (sem linhas ou sem
/// colunas) deixa a aba vazia, sem cabeçalho; nulo vira célula ausente. Cada
/// célula como o <c>Cell._bind_value</c> e o <c>safe_string</c>: número com
/// <c>%.16g</c>, data como serial do Excel com o formato do tipo, texto inline
/// cortado em 32.767 caracteres, <c>#N/A</c> e afins como erro.
/// <para>
/// O XML é escrito em sequência direto no ZIP (uma linha por vez, nada da
/// planilha fica em memória).
/// </para>
/// <para>
/// Diferenças intencionais: texto que começa com <c>=</c> sai como texto (o
/// openpyxl grava como fórmula); <c>\r</c> no texto sai como <c>&amp;#xD;</c>
/// e volta igual ao reabrir; datas com fuso e abas acima do limite do Excel são
/// recusadas antes de gravar (ver <see cref="Validar"/>).
/// </para>
/// </summary>
internal static class GravadorXlsx
{
    /// <summary>Linhas por aba do Excel, cabeçalho incluído.</summary>
    public const int MaximoLinhas = 1_048_576;
    public const int MaximoColunas = 16_384;
    private const int MaximoTexto = 32_767;

    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPacote = "http://schemas.openxmlformats.org/package/2006/relationships";

    // cellXfs: 0 padrão; depois os formatos de TIME_FORMATS do openpyxl.
    private const int EstiloDataHora = 1, EstiloData = 2, EstiloHora = 3, EstiloDuracao = 4;

    private static readonly HashSet<string> CodigosDeErro = new(StringComparer.Ordinal)
    {
        "#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A",
    };

    private static readonly DateTime EpocaWindows = new(1899, 12, 30);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Recusa, antes de criar qualquer arquivo, o que o Excel não guarda: data
    /// com fuso (o openpyxl falharia no meio da gravação; o offset não é
    /// removido em silêncio) e aba acima de 1.048.576 linhas ou 16.384 colunas.
    /// </summary>
    /// <exception cref="ExportacaoRecusadaException"/>
    /// <param name="cancelamento">Conferido a cada 1.024 linhas na busca de datas com fuso.</param>
    public static void Validar(IReadOnlyList<TabelaSaida> saidas, CancellationToken cancelamento = default)
    {
        foreach (var tabela in saidas)
        {
            if (Vazia(tabela))
                continue;
            if (tabela.Colunas.Count > MaximoColunas)
            {
                throw new ExportacaoRecusadaException(string.Create(PtBr,
                    $"A aba '{tabela.Aba}' tem {tabela.Colunas.Count:N0} colunas; o Excel aceita até {MaximoColunas:N0}. " +
                    $"Exporte em ZIP/CSV."));
            }
            if (tabela.Quantidade > MaximoLinhas - 1)
            {
                throw new ExportacaoRecusadaException(string.Create(PtBr,
                    $"A aba '{tabela.Aba}' tem {tabela.Quantidade:N0} linhas; o Excel aceita até {MaximoLinhas - 1:N0} " +
                    $"linhas de dados por aba (mais o cabeçalho). Exporte em ZIP/CSV."));
            }
        }
        foreach (var tabela in saidas)
        {
            var comFuso = Enumerable.Range(0, tabela.Colunas.Count).Where(j => tabela.Colunas[j].Fuso is not null).ToList();
            if (comFuso.Count == 0 || Vazia(tabela))
                continue;
            int n = 0;
            foreach (var linha in tabela.Linhas())
            {
                if ((++n & 0x3FF) == 0)
                    cancelamento.ThrowIfCancellationRequested();
                foreach (int j in comFuso)
                {
                    if (linha[j] is DateTimeOffset data)
                    {
                        throw new ExportacaoRecusadaException(
                            $"A aba '{tabela.Aba}' tem data com fuso horário na coluna '{tabela.Colunas[j].Nome}' " +
                            $"({SaidaFuso(data.Offset)}). O Excel não guarda fuso e o Luna não remove o offset: " +
                            "exporte em ZIP/CSV, que mantém o horário com o fuso.");
                    }
                }
            }
        }
    }

    /// <param name="aoIniciarAba">Chamado com o índice (0 a 7) antes de gravar cada aba.</param>
    public static void Gravar(
        IReadOnlyList<TabelaSaida> saidas, Stream destino, CancellationToken cancelamento, Action<int>? aoIniciarAba = null)
    {
        using var zip = new ZipArchive(destino, ZipArchiveMode.Create, leaveOpen: true);
        Parte(zip, "[Content_Types].xml", TiposDeConteudo(saidas.Count));
        Parte(zip, "_rels/.rels",
            $"<Relationships xmlns=\"{NsPacote}\"><Relationship Id=\"rId1\" " +
            "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Parte(zip, "xl/workbook.xml", Workbook(saidas));
        Parte(zip, "xl/_rels/workbook.xml.rels", RelacoesWorkbook(saidas.Count));
        Parte(zip, "xl/styles.xml", Estilos);
        for (int k = 0; k < saidas.Count; k++)
        {
            aoIniciarAba?.Invoke(k);
            using var parte = zip.CreateEntry($"xl/worksheets/sheet{k + 1}.xml", CompressionLevel.Optimal).Open();
            using var xml = XmlWriter.Create(parte, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                // \r como &#xD;: reaberto, o texto volta igual. Cru, o leitor de
                // XML troca \r\n por \n (e o Python no Windows grava \r\r\n,
                // que volta como duas quebras).
                NewLineHandling = NewLineHandling.Entitize,
            });
            EscreverAba(xml, saidas[k], cancelamento);
        }
    }

    private static bool Vazia(TabelaSaida tabela) => tabela.Colunas.Count == 0 || tabela.Quantidade == 0;

    private static void EscreverAba(XmlWriter xml, TabelaSaida tabela, CancellationToken cancelamento)
    {
        xml.WriteStartDocument(true);
        xml.WriteStartElement("worksheet", NsMain);
        xml.WriteStartElement("sheetData", NsMain);
        if (!Vazia(tabela))
        {
            var letras = Enumerable.Range(0, tabela.Colunas.Count).Select(Letras).ToArray();
            var celula = new EscritorDeCelula(xml, tabela);
            celula.Linha(1, tabela.Colunas.Select(c => (object?)c.Nome).ToArray(), letras, cabecalho: true);
            int numero = 1;
            foreach (var linha in tabela.Linhas())
            {
                if ((numero & 0x3FF) == 0)
                    cancelamento.ThrowIfCancellationRequested();
                celula.Linha(++numero, linha, letras, cabecalho: false);
            }
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private sealed class EscritorDeCelula(XmlWriter xml, TabelaSaida tabela)
    {
        public void Linha(int numero, IReadOnlyList<object?> valores, string[] letras, bool cabecalho)
        {
            string r = numero.ToString(CultureInfo.InvariantCulture);
            xml.WriteStartElement("row", NsMain);
            xml.WriteAttributeString("r", r);
            for (int j = 0; j < valores.Count; j++)
            {
                if (valores[j] is { } valor)
                    Celula(letras[j] + r, valor, cabecalho ? TipoColuna.Objeto : tabela.Colunas[j].Tipo, numero, j);
            }
            xml.WriteEndElement();
        }

        private void Celula(string referencia, object valor, TipoColuna tipo, int linha, int coluna)
        {
            switch (valor)
            {
                case string texto:
                    Texto(referencia, Cortar(texto), linha, coluna);
                    break;
                case bool b:
                    Valor(referencia, "b", null, b ? "1" : "0");
                    break;
                case long n:
                    Valor(referencia, "n", null, G16(n));
                    break;
                case BigInteger g:
                    Valor(referencia, "n", null, G16(g >= 0 && g <= ulong.MaxValue ? (ulong)g : (double)g));
                    break;
                case double d:
                    Valor(referencia, "n", null, double.IsFinite(d) ? G16(d) : "");
                    break;
                case DateTime data:
                    Valor(referencia, "n", EstiloDataHora, G16(SerialExcel(data)));
                    break;
                case DateOnly dia:
                    Valor(referencia, "n", EstiloData, G16(SerialExcel(dia.ToDateTime(TimeOnly.MinValue))));
                    break;
                case TimeOnly hora:
                    Valor(referencia, "n", EstiloHora, G16(FracaoDoDia(hora.ToTimeSpan())));
                    break;
                case TimeSpan duracao:
                    // pd.Timedelta.total_seconds() divide nanossegundos; o timedelta do Python, microssegundos.
                    double segundos = tipo == TipoColuna.Duracao
                        ? duracao.Ticks * 100 / 1e9
                        : duracao.Ticks / 10 / 1e6;
                    Valor(referencia, "n", EstiloDuracao, G16(segundos / 86400));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Valor sem gravação no Excel na aba '{tabela.Aba}': {valor.GetType().Name}.");
            }
        }

        private void Texto(string referencia, string texto, int linha, int coluna)
        {
            if (texto.Length == 0)
                return;
            int ilegal = texto.AsSpan().IndexOfAny(Ilegais);
            if (ilegal >= 0)
            {
                throw new ExportacaoRecusadaException(
                    $"A aba '{tabela.Aba}', linha {linha}, coluna '{Nome(coluna)}' tem o caractere de controle " +
                    $"U+{(int)texto[ilegal]:X4}, que o Excel não aceita. Exporte em ZIP/CSV ou corrija a base.");
            }
            if (CodigosDeErro.Contains(texto))
            {
                Valor(referencia, "e", null, texto);
                return;
            }
            xml.WriteStartElement("c", NsMain);
            xml.WriteAttributeString("r", referencia);
            xml.WriteAttributeString("t", "inlineStr");
            xml.WriteStartElement("is", NsMain);
            xml.WriteStartElement("t", NsMain);
            if (TextoPython.EhEspaco(texto[0]) || TextoPython.EhEspaco(texto[^1]))
                xml.WriteAttributeString("xml", "space", null, "preserve");
            xml.WriteString(texto);
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        private string Nome(int coluna) => coluna < tabela.Colunas.Count ? tabela.Colunas[coluna].Nome : "";

        private void Valor(string referencia, string tipo, int? estilo, string texto)
        {
            xml.WriteStartElement("c", NsMain);
            xml.WriteAttributeString("r", referencia);
            if (estilo is int s)
                xml.WriteAttributeString("s", s.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("t", tipo);
            xml.WriteElementString("v", NsMain, texto);
            xml.WriteEndElement();
        }
    }

    // ILLEGAL_CHARACTERS_RE do openpyxl: [\x00-\x08], [\x0b-\x0c], [\x0e-\x1f].
    private static readonly System.Buffers.SearchValues<char> Ilegais = System.Buffers.SearchValues.Create(
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000B\u000C\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F");

    // check_string: value[:32767], em code points.
    private static string Cortar(string texto)
    {
        if (texto.Length <= MaximoTexto)
            return texto;
        int i = 0;
        for (int cp = 0; cp < MaximoTexto && i < texto.Length; cp++)
            i += char.IsSurrogatePair(texto, i) ? 2 : 1;
        return texto[..i];
    }

    /// <summary>
    /// to_excel do openpyxl (época 1899-12-30): dias inteiros desde a época,
    /// menos 1 até 1900-02-28 (o 29/02/1900 fictício), mais a fração do dia
    /// com a conta do <c>time_to_days</c> (só até o microssegundo).
    /// </summary>
    internal static double SerialExcel(DateTime data)
    {
        long dias = (data.Date - EpocaWindows).Days;
        if (dias > 0 && dias <= 60)
            dias--;
        return dias + FracaoDoDia(data.TimeOfDay);
    }

    private static double FracaoDoDia(TimeSpan hora)
    {
        long segundos = hora.Ticks / TimeSpan.TicksPerSecond;
        long micro = hora.Ticks % TimeSpan.TicksPerSecond / 10;
        return (segundos + micro / 1e6) / 86400;
    }

    /// <summary><c>"%.16g" % valor</c> do Python (safe_string do openpyxl).</summary>
    internal static string G16(double valor)
    {
        if (valor == 0)
            return double.IsNegative(valor) ? "-0" : "0";
        string e = valor.ToString("E15", CultureInfo.InvariantCulture); // -d.dddddddddddddddE+ddd
        int posE = e.IndexOf('E');
        int expoente = int.Parse(e[(posE + 1)..], CultureInfo.InvariantCulture);
        string sinal = e[0] == '-' ? "-" : "";
        string digitos = e[sinal.Length..posE].Replace(".", "").TrimEnd('0');
        if (expoente < -4 || expoente >= 16)
        {
            string mantissa = digitos.Length > 1 ? $"{digitos[0]}.{digitos[1..]}" : digitos;
            return $"{sinal}{mantissa}e{(expoente < 0 ? '-' : '+')}{Math.Abs(expoente):00}";
        }
        if (expoente < 0)
            return $"{sinal}0.{new string('0', -expoente - 1)}{digitos}";
        if (digitos.Length <= expoente + 1)
            return sinal + digitos.PadRight(expoente + 1, '0');
        return $"{sinal}{digitos[..(expoente + 1)]}.{digitos[(expoente + 1)..]}";
    }

    private static string G16(ulong valor) => G16((double)valor);

    private static string SaidaFuso(TimeSpan offset)
        => "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

    // 0 -> A, 25 -> Z, 26 -> AA.
    private static string Letras(int indice)
    {
        var sb = new StringBuilder();
        for (int n = indice + 1; n > 0; n = (n - 1) / 26)
            sb.Insert(0, (char)('A' + (n - 1) % 26));
        return sb.ToString();
    }

    private static void Parte(ZipArchive zip, string nome, string conteudo)
    {
        using var parte = zip.CreateEntry(nome, CompressionLevel.Optimal).Open();
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" + conteudo);
        parte.Write(bytes);
    }

    private static string TiposDeConteudo(int abas)
    {
        var sb = new StringBuilder("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        sb.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int k = 1; k <= abas; k++)
            sb.Append($"<Override PartName=\"/xl/worksheets/sheet{k}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        return sb.Append("</Types>").ToString();
    }

    private static string Workbook(IReadOnlyList<TabelaSaida> saidas)
    {
        var sb = new StringBuilder($"<workbook xmlns=\"{NsMain}\" xmlns:r=\"{NsRel}\"><sheets>");
        for (int k = 0; k < saidas.Count; k++)
        {
            sb.Append($"<sheet name=\"{System.Security.SecurityElement.Escape(saidas[k].Aba)}\" sheetId=\"{k + 1}\" r:id=\"rId{k + 1}\"/>");
        }
        return sb.Append("</sheets></workbook>").ToString();
    }

    private static string RelacoesWorkbook(int abas)
    {
        var sb = new StringBuilder($"<Relationships xmlns=\"{NsPacote}\">");
        for (int k = 1; k <= abas; k++)
        {
            sb.Append($"<Relationship Id=\"rId{k}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" " +
                $"Target=\"worksheets/sheet{k}.xml\"/>");
        }
        sb.Append($"<Relationship Id=\"rId{abas + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return sb.Append("</Relationships>").ToString();
    }

    // Formatos de TIME_FORMATS do openpyxl: datetime, date, time (embutido 21) e timedelta.
    private static readonly string Estilos =
        $"<styleSheet xmlns=\"{NsMain}\">" +
        "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd h:mm:ss\"/>" +
        "<numFmt numFmtId=\"165\" formatCode=\"yyyy-mm-dd\"/><numFmt numFmtId=\"166\" formatCode=\"[hh]:mm:ss\"/></numFmts>" +
        "<fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"5\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"21\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";
}
