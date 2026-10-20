using System.Text;
using Luna.Application;
using Luna.Infrastructure.Csv;

namespace Luna.Integration.Tests;

/// <summary>
/// LeitorCsv contra o read_csv_safely do Python (pandas 2.3.3): os valores
/// esperados vêm do mesmo arquivo lido pelo Python.
/// </summary>
public sealed class LeitorCsvTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("luna_csv_").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private string Arquivo(string nome, byte[] conteudo)
    {
        string caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, conteudo);
        return caminho;
    }

    private string Arquivo(string nome, string texto) => Arquivo(nome, Encoding.UTF8.GetBytes(texto));

    private static string?[][] Linhas(TabelaBruta tabela)
        => tabela.Linhas.Select(l => l.Select(v => (string?)v).ToArray()).ToArray();

    private static readonly string[] Padrao = ["Cod_Cliente", "Chegou_em", "Finalizada_em"];

    [Fact]
    public void Marcadores_de_nulo_do_pandas_viram_nulo_e_os_outros_ficam()
    {
        var tabela = LeitorCsv.Ler(Arquivo("na.csv",
            "Cod_Cliente;Chegou_em;Finalizada_em\nNULL;2026-01-01 08:00;2026-01-01 08:10\nNA;x;y\n#N/A;x;y\n n/a;x;y\n;x;y\nNone;x;y\nnull ;x;y\n"));
        Assert.Equal(Padrao, tabela.Colunas);
        Assert.Equal(new string?[][]
        {
            [null, "2026-01-01 08:00", "2026-01-01 08:10"], [null, "x", "y"], [null, "x", "y"],
            [" n/a", "x", "y"], [null, "x", "y"], [null, "x", "y"], ["null ", "x", "y"],
        }, Linhas(tabela));
    }

    [Fact]
    public void Aspas_seguem_o_tokenizer_do_pandas()
    {
        var tabela = LeitorCsv.Ler(Arquivo("aspas.csv",
            "Cod_Cliente;Chegou_em;Finalizada_em\n\"00;1\";\"2026-01-01 08:00\";x\n\"a\"\"b\";x;y\nab\"c;x;y\n\"q\"r;x;y\n\"multi\nlinha\";x;y\n"));
        Assert.Equal(new string?[][]
        {
            ["00;1", "2026-01-01 08:00", "x"], ["a\"b", "x", "y"], ["ab\"c", "x", "y"], ["qr", "x", "y"], ["multi\nlinha", "x", "y"],
        }, Linhas(tabela));
    }

    [Fact]
    public void Linhas_vazias_e_so_com_espacos_sao_puladas()
    {
        var tabela = LeitorCsv.Ler(Arquivo("branco.csv", "Cod_Cliente;Chegou_em;Finalizada_em\n\nA;x;y\n   \nB;x;y\n\n"));
        Assert.Equal(new string?[][] { ["A", "x", "y"], ["B", "x", "y"] }, Linhas(tabela));
    }

    [Fact]
    public void Cabecalho_com_sem_nome_e_duplicatas_como_o_pandas()
    {
        var tabela = LeitorCsv.Ler(Arquivo("dup.csv", "Cliente;Cliente;;Cliente.1;Chegou_em;Finalizada_em\n1;2;3;4;x;y\n"));
        Assert.Equal(["Cliente", "Cliente.2", "Unnamed: 2", "Cliente.1", "Chegou_em", "Finalizada_em"], tabela.Colunas);
    }

    [Fact]
    public void Campos_a_menos_sao_completados_com_nulo()
    {
        var tabela = LeitorCsv.Ler(Arquivo("poucos.csv", "Cod_Cliente;Chegou_em;Finalizada_em\nA;x\nB\n"));
        Assert.Equal(new string?[][] { ["A", "x", null], ["B", null, null] }, Linhas(tabela));
    }

    [Fact]
    public void Primeira_linha_com_campo_a_mais_vira_indice_implicito()
    {
        var tabela = LeitorCsv.Ler(Arquivo("indice.csv", "Cod_Cliente;Chegou_em;Finalizada_em\nI;A;x;y\nJ;B;x;y\n"));
        Assert.Equal(Padrao, tabela.Colunas);
        Assert.Equal(new string?[][] { ["A", "x", "y"], ["B", "x", "y"] }, Linhas(tabela));
    }

    [Fact]
    public void Separador_virgula_e_fim_de_linha_cr()
    {
        Assert.Equal(new string?[][] { ["00123", "03/10/2026 08:00", "x"] },
            Linhas(LeitorCsv.Ler(Arquivo("virgula.csv", "Cod_Cliente,Chegou_em,Finalizada_em\n00123,\"03/10/2026 08:00\",x\n"))));
        Assert.Equal(new string?[][] { ["A", "x", "y"], ["B", "x", "y"] },
            Linhas(LeitorCsv.Ler(Arquivo("cr.csv", "Cod_Cliente;Chegou_em;Finalizada_em\rA;x;y\rB;x;y\r"))));
    }

    [Fact]
    public void Encodings_bom_latin1_e_cp1252()
    {
        Assert.Equal(Padrao, LeitorCsv.Ler(Arquivo("bom.csv", "\uFEFFCod_Cliente;Chegou_em;Finalizada_em\nA;x;y\n")).Colunas);
        Assert.Equal("João", LeitorCsv.Ler(Arquivo("latin1.csv",
            Encoding.Latin1.GetBytes("Cod_Cliente;Chegou_em;Finalizada_em\nJoão;x;y\n"))).Linhas[0][0]);
        // 0x80 não é UTF-8 válido: cai no latin1 (U+0080), como no Python.
        Assert.Equal("A\u0080", LeitorCsv.Ler(Arquivo("cp1252.csv",
            Encoding.Latin1.GetBytes("Cod_Cliente;Chegou_em;Finalizada_em\nA\u0080;x;y\n"))).Linhas[0][0]);
    }

    [Fact]
    public void Linha_completada_vira_a_referencia_da_seguinte()
    {
        // Sem completar a linha A, a linha B (3 campos) teria campo a mais.
        var tabela = LeitorCsv.Ler(Arquivo("encadeado.csv", "Cod_Cliente;Chegou_em;Finalizada_em\nA;x\nB;x;y\nC;x;y\n"));
        Assert.Equal(new string?[][] { ["A", "x", null], ["B", "x", "y"], ["C", "x", "y"] }, Linhas(tabela));
    }

    [Fact]
    public void Nome_real_tem_prioridade_sobre_o_unnamed_gerado()
    {
        var tabela = LeitorCsv.Ler(Arquivo("unnamed.csv", "a;;Unnamed: 1;b\n1;2;3;4\n"));
        Assert.Equal(["a", "Unnamed: 1.1", "Unnamed: 1", "b"], tabela.Colunas);
    }

    [Fact]
    public void Arquivo_com_cr_e_linha_comecando_pelo_separador()
    {
        var tabela = LeitorCsv.Ler(Arquivo("cr_sep.csv", "Cod_Cliente;Chegou_em;Finalizada_em\r;x;y\rB;x;y\r"));
        Assert.Equal(new string?[][] { [null, "x", "y"], ["B", "x", "y"] }, Linhas(tabela));
    }

    [Fact]
    public void Espaco_no_inicio_da_linha_com_dado_fica_no_campo()
    {
        var tabela = LeitorCsv.Ler(Arquivo("espaco.csv", "Cod_Cliente;Chegou_em;Finalizada_em\n  A;x;y\n\tB;x;y\n"));
        Assert.Equal(new string?[][] { ["  A", "x", "y"], ["\tB", "x", "y"] }, Linhas(tabela));
    }

    // Fim de linha só CR e linha começando por espaço/tab com dado: antes o
    // tokenizer voltava ao último '\n' (o começo do arquivo) e relia tudo sem
    // parar, consumindo memória até travar a máquina. O pandas dá o mesmo
    // resultado nos dois primeiros casos (read_csv_safely, pandas 2.3.3).
    [Theory]
    [InlineData("Cod_Cliente;Chegou_em;Finalizada_em\rA;x;y\r  B;x;y\rC;x;y\r")]
    [InlineData("Cod_Cliente;Chegou_em;Finalizada_em\rA;x;y\r  B;x;y\rC;x;y")]
    [InlineData("Cod_Cliente;Chegou_em;Finalizada_em\nA;x;y\r  B;x;y\rC;x;y\n")]
    public async Task Cr_e_linha_comecando_por_espaco_termina_e_mantem_o_espaco(string texto)
    {
        string caminho = Arquivo("cr_espaco.csv", texto);
        // Sem o limite de tempo, uma regressão trava o teste (e a máquina).
        var tabela = await Task.Run(() => LeitorCsv.Ler(caminho)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new string?[][] { ["A", "x", "y"], ["  B", "x", "y"], ["C", "x", "y"] }, Linhas(tabela));
    }

    // A leitura decodifica em blocos: caractere multibyte, CRLF, aspas e linha
    // de espaços cortados entre dois blocos não podem mudar nada.
    [Theory]
    [InlineData("utf-8")]
    [InlineData("latin1")]
    [InlineData("cp1252")]
    public void Qualquer_tamanho_de_bloco_da_o_mesmo_resultado(string encoding)
    {
        string[] textos =
        [
            "Cod_Cliente;Chegou_em;Finalizada_em\r\nJoão;\"a;\r\nb\";çã\r\n\r\n  x;y;z\r\n\"q\"\"t\";1;2",
            "Cod_Cliente;Chegou_em;Finalizada_em\rA;x;y\r  B;x;y\r\t\r;C;é\r",
            "a,b\n1,2\n   \n\"aberta,3\n",
            "Cód;Chegou_em;Finalizada_em\nA;B;C;D\nE;F\n",
            encoding == "utf-8" ? "Cod;X\n😀;\"𝔘😀\"\n€;x\n" : "Cod;X\n€;x\n",
        ];
        Encoding codificacao = encoding switch
        {
            "utf-8" => new UTF8Encoding(false, true),
            "latin1" => Encoding.Latin1,
            _ => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
        };
        foreach (string texto in textos)
        {
            byte[] bytes = codificacao.GetBytes(texto);
            foreach (char separador in ";,")
            {
                string esperado = Tokenizado(bytes, codificacao, separador, null);
                foreach (int bloco in new[] { 1, 2, 3, 5, 7, 64 })
                    Assert.Equal(esperado, Tokenizado(bytes, codificacao, separador, bloco));
            }
        }
    }

    private static string Tokenizado(byte[] bytes, Encoding codificacao, char separador, int? bloco)
    {
        try
        {
            var linhas = bloco is int b
                ? TokenizadorCsv.Tokenizar(bytes, 0, codificacao, separador, b)
                : TokenizadorCsv.Tokenizar(bytes, 0, codificacao, separador);
            return string.Join("\n", linhas.Select(l => string.Join("|", l.Select(c => $"[{c}]"))));
        }
        catch (ErroDeTokenizacaoException e)
        {
            return "erro: " + e.Message;
        }
    }

    [Fact]
    public void So_cabecalho_da_tabela_sem_linhas()
    {
        var tabela = LeitorCsv.Ler(Arquivo("so_header.csv", "Cod_Cliente;Chegou_em;Finalizada_em\n"));
        Assert.Equal(Padrao, tabela.Colunas);
        Assert.Empty(tabela.Linhas);
    }

    // Mensagens de read_csv_safely do Python para os mesmos arquivos.
    [Theory]
    [InlineData("uma_coluna.csv", "Cod_Cliente\nA\n",
        "Erro ao ler o CSV 'uma_coluna.csv'. Nenhum separador testado produziu mais de uma coluna. Tentativas: sep=';' encoding='utf-8-sig' -> 1 coluna ('Cod_Cliente') | sep=',' encoding='utf-8-sig' -> 1 coluna ('Cod_Cliente') | sep=';' encoding='latin1' -> 1 coluna ('Cod_Cliente') | sep=',' encoding='latin1' -> 1 coluna ('Cod_Cliente') | sep=';' encoding='cp1252' -> 1 coluna ('Cod_Cliente') | sep=',' encoding='cp1252' -> 1 coluna ('Cod_Cliente')")]
    [InlineData("muitos_campos.csv", "Cod_Cliente;Chegou_em;Finalizada_em\nA;x;y\nB;x;y;z\n",
        "Erro ao ler o CSV 'muitos_campos.csv'. Nenhum separador testado produziu mais de uma coluna. Tentativas: sep=',' encoding='utf-8-sig' -> 1 coluna ('Cod_Cliente;Chegou_em;Finalizada_em') | sep=',' encoding='latin1' -> 1 coluna ('Cod_Cliente;Chegou_em;Finalizada_em') | sep=',' encoding='cp1252' -> 1 coluna ('Cod_Cliente;Chegou_em;Finalizada_em')")]
    [InlineData("vazio.csv", "",
        "Erro ao ler o CSV 'vazio.csv'. Não foi possível interpretar o arquivo com os formatos testados. Último erro: No columns to parse from file")]
    [InlineData("vazio_linhas.csv", "\n\n  \n",
        "Erro ao ler o CSV 'vazio_linhas.csv'. Não foi possível interpretar o arquivo com os formatos testados. Último erro: No columns to parse from file")]
    [InlineData("tudo_erro.csv", "Cod_Cliente;Chegou_em\n\"aberto;x\n",
        "Erro ao ler o CSV 'tudo_erro.csv'. Não foi possível interpretar o arquivo com os formatos testados. Último erro: Error tokenizing data. C error: EOF inside string starting at row 1")]
    public void Mensagens_de_erro_iguais_as_do_python(string nome, string conteudo, string mensagem)
    {
        var erro = Assert.Throws<BaseInvalidaException>(() => LeitorCsv.Ler(Arquivo(nome, conteudo)));
        Assert.Equal(mensagem, erro.Message);
    }
}
