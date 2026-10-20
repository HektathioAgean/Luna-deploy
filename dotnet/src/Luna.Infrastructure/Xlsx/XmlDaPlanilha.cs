using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;

namespace Luna.Infrastructure.Xlsx;

/// <summary>
/// Leitura sequencial (System.Xml) das partes grandes do pacote: a planilha e
/// as strings compartilhadas. O OpenXmlReader do SDK alocava ~1 KB por célula;
/// aqui só os textos lidos viram objetos. Como o SDK, aceita o namespace
/// principal transitional e o strict, mantém espaços como estão e proíbe DTD.
/// </summary>
internal static class XmlDaPlanilha
{
    private const string Principal = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string PrincipalStrict = "http://purl.oclc.org/ooxml/spreadsheetml/main";

    private static readonly XmlReaderSettings Configuracao = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CloseInput = true,
    };

    public static XmlReader Abrir(OpenXmlPart parte)
        => XmlReader.Create(parte.GetStream(FileMode.Open, FileAccess.Read), Configuracao);

    /// <summary>Elemento atual é <paramref name="nome"/> no namespace principal.</summary>
    public static bool E(XmlReader leitor, string nome)
        => leitor.NodeType == XmlNodeType.Element && leitor.LocalName == nome
            && (leitor.NamespaceURI == Principal || leitor.NamespaceURI == PrincipalStrict);

    /// <summary>
    /// Avança até o próximo filho direto (elemento) do elemento de profundidade
    /// <paramref name="pai"/>; false ao chegar no fim dele. Quem trata o filho
    /// para no último nó dele e chama Read; quem ignora chama Skip.
    /// </summary>
    public static bool ProximoFilho(XmlReader leitor, int pai)
    {
        while (!leitor.EOF)
        {
            if (leitor.NodeType == XmlNodeType.EndElement && leitor.Depth == pai)
                return false;
            if (leitor.NodeType == XmlNodeType.Element && leitor.Depth == pai + 1)
                return true;
            leitor.Read();
        }
        return false;
    }

    /// <summary>InnerText do elemento atual; o leitor para no fim dele.</summary>
    public static string Texto(XmlReader leitor)
    {
        if (leitor.IsEmptyElement)
            return "";
        int profundidade = leitor.Depth;
        string? unico = null;
        StringBuilder? partes = null;
        while (leitor.Read() && !(leitor.NodeType == XmlNodeType.EndElement && leitor.Depth == profundidade))
        {
            if (leitor.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                if (unico is null)
                    unico = leitor.Value;
                else
                    (partes ??= new StringBuilder(unico)).Append(leitor.Value);
            }
        }
        return partes?.ToString() ?? unico ?? "";
    }

    /// <summary>
    /// Text.content do openpyxl para &lt;si&gt; ou &lt;is&gt;: o primeiro &lt;t&gt;
    /// direto e o primeiro &lt;t&gt; de cada &lt;r&gt;, nessa ordem (sem a
    /// fonética). O leitor para no fim do elemento.
    /// </summary>
    public static string Conteudo(XmlReader leitor)
    {
        if (leitor.IsEmptyElement)
            return "";
        int profundidade = leitor.Depth;
        string? puro = null;
        List<string>? trechos = null;
        leitor.Read();
        while (ProximoFilho(leitor, profundidade))
        {
            if (puro is null && E(leitor, "t"))
            {
                puro = Texto(leitor);
                leitor.Read();
            }
            else if (E(leitor, "r"))
            {
                if (PrimeiroT(leitor) is string texto)
                    (trechos ??= []).Add(texto);
                leitor.Read();
            }
            else
            {
                leitor.Skip();
            }
        }
        return trechos is null ? puro ?? "" : (puro ?? "") + string.Concat(trechos);
    }

    private static string? PrimeiroT(XmlReader leitor)
    {
        if (leitor.IsEmptyElement)
            return null;
        int profundidade = leitor.Depth;
        string? texto = null;
        leitor.Read();
        while (ProximoFilho(leitor, profundidade))
        {
            if (texto is null && E(leitor, "t"))
            {
                texto = Texto(leitor);
                leitor.Read();
            }
            else
            {
                leitor.Skip();
            }
        }
        return texto;
    }
}
