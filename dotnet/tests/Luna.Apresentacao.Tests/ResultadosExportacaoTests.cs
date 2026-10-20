using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Luna.Application;
using Luna.Apresentacao;
using Luna.Infrastructure;
using Luna.Infrastructure.Exportacao;

namespace Luna.Apresentacao.Tests;

/// <summary>A gravação do motor com os tipos da interface, como na composição do Luna.Desktop.</summary>
internal static class Exportacoes
{
    public static readonly ExecutorDeExportacao DoMotor = (resultado, formato, alvo, progresso, cancelamento) =>
    {
        try
        {
            return Exportador.Exportar(resultado, formato, alvo, cancelamento,
                new Repasse(p => progresso.Report(new PassoDeGravacao((EtapaDeGravacao)(int)p.Etapa, p.Aba, p.TotalAbas, p.Nome, p.Linhas))));
        }
        catch (ExportacaoRecusadaException e)
        {
            throw new GravacaoRecusadaException(e.Message, e);
        }
    };

    private sealed class Repasse(Action<ProgressoExportacao> aoAvisar) : IProgress<ProgressoExportacao>
    {
        public void Report(ProgressoExportacao value) => aoAvisar(value);
    }
}

/// <summary>Diálogos da exportação com respostas fixas, contando as perguntas.</summary>
internal sealed class Dialogos(string? pasta = null, bool substituir = true) : IDialogosDeExportacao
{
    public List<string?> PastasIniciais { get; } = [];
    public List<string> Perguntas { get; } = [];

    public string? EscolherPasta(string? pastaInicial)
    {
        PastasIniciais.Add(pastaInicial);
        return pasta;
    }

    public bool ConfirmarSubstituicao(string caminho)
    {
        Perguntas.Add(caminho);
        return substituir;
    }
}

/// <summary>
/// Seções Resultados e Exportação: abas, linhas virtuais sem materialização,
/// texto das células igual à tela web (dados/resultados/esperado.json),
/// gravação real em pasta temporária, confirmação, cancelamento, erros e a
/// exclusão entre processar e exportar.
/// </summary>
public sealed class ResultadosExportacaoTests : IDisposable
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static readonly ExecutorDeProcessamento Real = (p, g, t) => Caso.Executar(p, g, t);
    private readonly string _pasta = Directory.CreateTempSubdirectory("luna_pr13_").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private sealed class Imediato : IDespachante
    {
        public void Executar(Action acao) => acao();
    }

    private sealed class SemEscolha : IEscolhaDeArquivo
    {
        public string? EscolherBase(string? caminhoAtual) => null;
    }

    private static PrincipalViewModel Principal(
        ExecutorDeProcessamento? processar = null, ExecutorDeExportacao? exportar = null, Dialogos? dialogos = null)
        => new(processar ?? Real, exportar ?? Exportacoes.DoMotor, new Imediato(), new SemEscolha(), dialogos ?? new Dialogos());

    private static string Teste => Repositorio.Caminho("TESTE_data.csv");

    private static async Task<PrincipalViewModel> Processado(string caminho, Dialogos? dialogos = null,
        ExecutorDeExportacao? exportar = null, bool janelas = true)
    {
        var p = Principal(exportar: exportar, dialogos: dialogos);
        p.Base.Caminho = caminho;
        p.Base.ExibirJanelasEntrega = janelas;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.Equal(EstadoExecucao.Concluido, p.Base.Estado);
        return p;
    }

    // ---------- Resultados ----------

    [Fact]
    public async Task Oito_abas_na_ordem_do_motor_com_quantidade_em_pt_br()
    {
        var p = await Processado(Teste);
        var r = p.Resultados;
        Assert.True(r.TemResultado);
        Assert.Equal("TESTE_data.csv", r.NomeBase);
        var saidas = p.Base.Resultado!.Saidas;
        Assert.Equal(
            ["Base Bruta (76)", $"Base Validada ({saidas[1].Quantidade})", $"Base Detalhada ({saidas[2].Quantidade})",
             "Inconsistências (10)", "Expurgados (5)", "Anomalias (5)", "Medianas Cliente (5)", "Janelas Atendimento (5)"],
            r.Tabelas.Select(t => t.Titulo));
        Assert.All(r.Tabelas.Zip(saidas), x => Assert.Equal(x.Second.Quantidade, x.First.Linhas.Count));
        Assert.Same(r.Tabelas[0], r.TabelaAtual);
        Assert.All(r.Tabelas, t => Assert.False(string.IsNullOrEmpty(t.Nota)));
    }

    [Fact]
    public void Quantidade_com_separador_de_milhar()
    {
        var linhas = new LinhasVirtuais([new ColunaSaida("A", TipoColuna.Inteiro)], 1_234_567, i => [(long)i]);
        Assert.Equal("Base Bruta (1.234.567)", new TabelaDeResultado("Base Bruta", "", linhas).Titulo);
    }

    [Fact]
    public async Task Janelas_desligadas_somem_dos_resultados_mas_vao_para_o_arquivo()
    {
        var p = await Processado(Teste, janelas: false);
        Assert.Equal(7, p.Resultados.Tabelas.Count);
        Assert.DoesNotContain(p.Resultados.Tabelas, t => t.Rotulo == "Janelas Atendimento");
        Assert.Equal(8, p.Exportacao.Conteudo.Count);
        Assert.Equal(new Indicador("Janelas Atendimento", "0 linhas"), p.Exportacao.Conteudo[7]);
    }

    [Fact]
    public async Task Esquema_invalido_so_a_base_bruta_tem_linhas()
    {
        string caminho = Arquivo("sem_obrig.csv", "Cliente;Outra\nA;1\nB;2\n");
        var p = await Processado(caminho);
        Assert.Equal(8, p.Resultados.Tabelas.Count);
        Assert.Equal(2, p.Resultados.Tabelas[0].Linhas.Count);
        Assert.Equal(["A", "1"], p.Resultados.Tabelas[0].Linhas[0].Celulas);
        Assert.All(p.Resultados.Tabelas.Skip(1), t => Assert.True(t.Vazia, t.Rotulo));
    }

    [Fact]
    public async Task Linhas_virtuais_iguais_a_linha_da_tabela()
    {
        var p = await Processado(Teste);
        var saidas = p.Base.Resultado!.Saidas;
        foreach (var (tabela, saida) in p.Resultados.Tabelas.Zip(saidas))
        {
            var linhas = tabela.Linhas;
            Assert.Equal(saida.Quantidade, linhas.Count);
            Assert.Equal(saida.Quantidade, ((System.Collections.IList)linhas).Count);
            for (int i = 0; i < linhas.Count; i++)
            {
                var esperado = saida.Linha(i).Select((v, j) => FormatoCelula.Texto(saida.Colunas[j], v));
                Assert.Equal(esperado, linhas[i].Celulas);
                Assert.Equal(i, linhas[i].Indice);
            }
        }
    }

    [Fact]
    public void Linhas_virtuais_so_montam_o_que_foi_pedido()
    {
        int chamadas = 0;
        var linhas = new LinhasVirtuais([new ColunaSaida("N", TipoColuna.Inteiro), new ColunaSaida("T", TipoColuna.Objeto)],
            5_000_000, i => { chamadas++; return [(long)i, $"t{i}"]; });
        System.Collections.IList lista = linhas;

        Assert.Equal(5_000_000, lista.Count);
        Assert.Equal(0, chamadas);

        // Uma "tela" de 40 linhas no começo e outra no fim.
        for (int i = 0; i < 40; i++)
            Assert.Equal(["" + i, "t" + i], linhas[i].Celulas);
        var ultima = (LinhaVirtual)lista[4_999_999]!;
        Assert.Equal(["4999999", "t4999999"], ultima.Celulas);
        Assert.Equal(41, chamadas);
        Assert.Equal(41, linhas.LinhasMontadas);

        // A grade pede de novo as linhas visíveis: vêm da memória recente, sem montar.
        for (int i = 0; i < 40; i++)
            _ = lista[i];
        Assert.Equal(41, chamadas);

        // IndexOf e Contains não percorrem a lista nem montam linha.
        Assert.Equal(4_999_999, lista.IndexOf(ultima));
        Assert.True(lista.Contains(ultima));
        Assert.Equal(-1, lista.IndexOf("outra coisa"));
        var deOutraLista = new LinhasVirtuais(linhas.Colunas, 1, _ => [0L, "x"])[0];
        Assert.Equal(-1, lista.IndexOf(deOutraLista));
        Assert.Equal(41, chamadas);

        // Fora da memória recente, a linha é montada de novo, mas continua sendo a mesma para a grade.
        var primeira = linhas[0];
        for (int i = 1000; i < 1300; i++)
            _ = lista[i];
        var denovo = linhas[0];
        Assert.NotSame(primeira, denovo);
        Assert.Equal(linhas.LinhasMontadas, chamadas);
        Assert.True(primeira.Equals(denovo));
        Assert.Equal(primeira.GetHashCode(), denovo.GetHashCode());
        Assert.Equal(0, lista.IndexOf(primeira));
        Assert.False(primeira.Equals(linhas[1]));

        Assert.True(lista.IsReadOnly);
        Assert.True(lista.IsFixedSize);
        Assert.Throws<NotSupportedException>(() => lista.Add(null));
        Assert.Throws<NotSupportedException>(() => lista.RemoveAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => lista[5_000_000]);
        Assert.Throws<ArgumentOutOfRangeException>(() => lista[-1]);
    }

    [Fact]
    public async Task Processar_de_novo_solta_as_tabelas()
    {
        var segundo = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        int chamadas = 0;
        var p = Principal(processar: (par, g, t) =>
        {
            if (++chamadas == 2)
            {
                segundo.SetResult();
                libera.Task.Wait(t);
            }
            return Caso.Executar(par, g, t);
        });
        p.Base.Caminho = Teste;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.Equal(8, p.Resultados.Tabelas.Count);

        var execucao = p.Base.ProcessarCommand.ExecuteAsync(null);
        await segundo.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(p.Resultados.TemResultado);
        Assert.Empty(p.Resultados.Tabelas);
        Assert.Null(p.Resultados.TabelaAtual);
        libera.SetResult();
        await execucao;
        Assert.Equal(8, p.Resultados.Tabelas.Count);
    }

    // ---------- Texto das células (referência Python + tabela.js) ----------

    private static readonly Lazy<JsonElement> Referencia = new(() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "dados", "resultados", "esperado.json"))).RootElement);

    public static TheoryData<int> Casos => new(Enumerable.Range(0, Referencia.Value.GetProperty("casos").GetArrayLength()));

    [Theory]
    [MemberData(nameof(Casos))]
    public void Texto_das_celulas_igual_ao_da_tela_web(int indice)
    {
        var caso = Referencia.Value.GetProperty("casos")[indice];
        var parametros = Parametros(caso);
        if (caso.TryGetProperty("erro", out _))
        {
            Assert.ThrowsAny<Exception>(() => Caso.Executar(parametros));
            return;
        }
        var saidas = Caso.Executar(parametros).Saidas;
        var tabelas = caso.GetProperty("tabelas").EnumerateArray().ToList();
        Assert.Equal(saidas.Count, tabelas.Count);
        for (int k = 0; k < saidas.Count; k++)
        {
            var linhas = new LinhasVirtuais(saidas[k]);
            var esperadas = tabelas[k].EnumerateArray().ToList();
            Assert.Equal(esperadas.Count, linhas.Count);
            for (int i = 0; i < linhas.Count; i++)
            {
                var esperado = esperadas[i].EnumerateArray().Select(c => c.GetString()!).ToList();
                var obtido = linhas[i].Celulas;
                for (int j = 0; j < esperado.Count; j++)
                {
                    Assert.True(esperado[j] == obtido[j],
                        $"{caso.GetProperty("base")} [{saidas[k].Aba}] linha {i} {saidas[k].Colunas[j].Nome} ({saidas[k].Colunas[j].Dtype}): " +
                        $"esperado '{esperado[j]}', obtido '{obtido[j]}' de {saidas[k].Linha(i)[j]?.GetType().Name}");
                }
            }
        }
    }

    [Fact]
    public void Numeros_como_o_javascript_mostra()
    {
        foreach (var par in Referencia.Value.GetProperty("numeros").EnumerateArray())
        {
            string valor = par[0].GetString()!, esperado = par[1].GetString()!;
            string obtido = valor.Contains('.') || valor.Contains('e')
                ? FormatoCelula.Numero(double.Parse(valor, CultureInfo.InvariantCulture))
                : FormatoCelula.Texto(new ColunaSaida("x", TipoColuna.InteiroSemSinal), BigInteger.Parse(valor, CultureInfo.InvariantCulture));
            Assert.True(esperado == obtido, $"{valor}: esperado '{esperado}', obtido '{obtido}'");
        }
        var inteiro = new ColunaSaida("x", TipoColuna.Inteiro);
        Assert.Equal("9007199254740992", FormatoCelula.Texto(inteiro, 9007199254740993L));
        Assert.Equal("", FormatoCelula.Texto(new ColunaSaida("x", TipoColuna.Real), double.NaN));
        Assert.Equal("", FormatoCelula.Texto(new ColunaSaida("x", TipoColuna.Real), double.PositiveInfinity));
    }

    [Fact]
    public void Datas_e_duracoes_como_o_str_do_python()
    {
        var objeto = new ColunaSaida("x", TipoColuna.Objeto);
        var data = new ColunaSaida("x", TipoColuna.DataHora);
        var comFuso = new ColunaSaida("x", TipoColuna.DataHora, TimeSpan.FromHours(5.5));
        var duracao = new ColunaSaida("x", TipoColuna.Duracao);
        var d = new DateTime(2020, 1, 1, 0, 0, 0).AddTicks(70);
        // Referências de str() no Python 3.10 / pandas 2.3.3 (gerar_resultados.py cobre os casos das bases).
        Assert.Equal("2020-01-01 00:00:00.000007", FormatoCelula.Texto(objeto, d));
        Assert.Equal("2020-01-01 00:00:00", FormatoCelula.Texto(data, d));
        Assert.Equal("2020-01-01 00:00:00+05:30",
            FormatoCelula.Texto(objeto, new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.FromHours(5.5))));
        Assert.Equal("2020-01-01 08:00:00",
            FormatoCelula.Texto(comFuso, new DateTimeOffset(2020, 1, 1, 8, 0, 0, TimeSpan.FromHours(5.5))));
        Assert.Equal("08:20:00.000005", FormatoCelula.Texto(objeto, new TimeOnly(8, 20).Add(TimeSpan.FromTicks(50))));
        Assert.Equal("2026-01-05", FormatoCelula.Texto(objeto, new DateOnly(2026, 1, 5)));
        Assert.Equal("-1 days +23:00:00", FormatoCelula.Texto(duracao, TimeSpan.FromHours(-1)));
        Assert.Equal("0 days 00:00:00.500000", FormatoCelula.Texto(duracao, TimeSpan.FromMilliseconds(500)));
        Assert.Equal("0 days 00:00:00.000000100", FormatoCelula.Texto(duracao, TimeSpan.FromTicks(1)));
        Assert.Equal("-2 days +22:58:58.500000", FormatoCelula.Texto(duracao, TimeSpan.FromTicks(-90061500000 * 10)));
        Assert.Equal("-1 day, 0:00:05.000003",
            FormatoCelula.Texto(objeto, TimeSpan.FromDays(-1) + TimeSpan.FromSeconds(5) + TimeSpan.FromTicks(30)));
        Assert.Equal("1 day, 0:00:00", FormatoCelula.Texto(objeto, TimeSpan.FromDays(1)));
        Assert.Equal("0:00:59", FormatoCelula.Texto(objeto, TimeSpan.FromSeconds(59)));
        Assert.Equal("-3 days, 0:00:00", FormatoCelula.Texto(objeto, TimeSpan.FromDays(-3)));
        Assert.Equal("200000 days, 0:00:00", FormatoCelula.Texto(objeto, TimeSpan.FromDays(200000)));
    }

    // ---------- Exportação ----------

    [Fact]
    public async Task Gravar_so_com_resultado_pasta_e_formato()
    {
        var p = Principal();
        var e = p.Exportacao;
        Assert.False(e.TemResultado);
        Assert.False(e.GravarCommand.CanExecute("xlsx"));
        e.Pasta = _pasta;
        Assert.False(e.GravarCommand.CanExecute("xlsx"));
        Assert.Null(e.AlvoExcel);

        p.Base.Caminho = Teste;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.True(e.GravarCommand.CanExecute("xlsx"));
        Assert.True(e.GravarCommand.CanExecute("zip"));
        Assert.False(e.GravarCommand.CanExecute("pdf"));
        Assert.False(e.GravarCommand.CanExecute(null));
        Assert.False(e.GravarCancelCommand.CanExecute(null));
        e.Pasta = "  ";
        Assert.False(e.GravarCommand.CanExecute("zip"));
        Assert.Null(e.AlvoZip);
    }

    [Theory]
    [InlineData("xlsx", "Excel")]
    [InlineData("zip", "ZIP")]
    public async Task Grava_na_pasta_com_o_nome_da_base(string formato, string nome)
    {
        var dialogos = new Dialogos();
        var p = await Processado(Teste, dialogos);
        var e = p.Exportacao;
        e.Pasta = _pasta + "  ";
        string alvo = Path.Combine(_pasta, $"TESTE_data_luna_resultado.{formato}");
        Assert.Equal(alvo, formato == "xlsx" ? e.AlvoExcel : e.AlvoZip);

        await e.GravarCommand.ExecuteAsync(formato);

        Assert.Equal(EstadoExportacao.Concluido, e.Estado);
        Assert.Equal(alvo, e.CaminhoGravado);
        Assert.True(File.Exists(alvo));
        Assert.Empty(Directory.GetFiles(_pasta, "*.parcial"));
        Assert.Empty(dialogos.Perguntas);
        Assert.StartsWith($"{nome} gravado em ", e.TextoEstado);
        Assert.EndsWith(alvo, e.TextoEstado);
        Assert.True(e.Ativa);
        Assert.Null(e.Passo);
        Assert.Null(e.MensagemDeErro);
        Assert.False(p.Base.Bloqueado);
        // O mesmo caminho que o motor escolhe quando recebe só a pasta.
        Assert.Equal(alvo, Exportador.Exportar(p.Base.Resultado!, formato, _pasta));
    }

    [Fact]
    public async Task Avisos_de_progresso_na_ordem()
    {
        var p = await Processado(Teste);
        var e = p.Exportacao;
        e.Pasta = _pasta;
        var passos = new List<string?>();
        e.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(ExportacaoViewModel.Passo))
                passos.Add(e.Passo);
        };
        await e.GravarCommand.ExecuteAsync("xlsx");

        var saidas = p.Base.Resultado!.Saidas;
        var abas = saidas.Select((t, k) => $"Aba {k + 1}/8: {t.Aba} ({t.Quantidade} linhas)");
        Assert.Equal(["Conferindo o conteúdo para o Excel…", .. abas, "Publicando o arquivo…", null], passos);

        passos.Clear();
        await e.GravarCommand.ExecuteAsync("zip");
        Assert.Equal("Arquivo 1/8: base_bruta.csv (76 linhas)", passos[0]);
        Assert.Equal("Publicando o arquivo…", passos[^2]);
    }

    [Fact]
    public async Task Arquivo_existente_pede_confirmacao_e_recusa_nao_toca_em_nada()
    {
        string alvo = Path.Combine(_pasta, "TESTE_data_luna_resultado.zip");
        File.WriteAllText(alvo, "anterior");
        int gravacoes = 0;
        var dialogos = new Dialogos(substituir: false);
        var p = await Processado(Teste, dialogos, (r, f, a, g, t) => { gravacoes++; return Exportacoes.DoMotor(r, f, a, g, t); });
        var e = p.Exportacao;
        e.Pasta = _pasta;

        await e.GravarCommand.ExecuteAsync("zip");
        Assert.Equal([alvo], dialogos.Perguntas);
        Assert.Equal(0, gravacoes);
        Assert.Equal(EstadoExportacao.Ocioso, e.Estado);
        Assert.Equal("anterior", File.ReadAllText(alvo));

        // Sem arquivo no destino do Excel: grava sem perguntar.
        await e.GravarCommand.ExecuteAsync("xlsx");
        Assert.Single(dialogos.Perguntas);
        Assert.Equal(1, gravacoes);
    }

    [Fact]
    public async Task Confirmada_a_substituicao_o_arquivo_e_trocado()
    {
        string alvo = Path.Combine(_pasta, "TESTE_data_luna_resultado.zip");
        File.WriteAllText(alvo, "anterior");
        var dialogos = new Dialogos(substituir: true);
        var p = await Processado(Teste, dialogos);
        p.Exportacao.Pasta = _pasta;
        await p.Exportacao.GravarCommand.ExecuteAsync("zip");
        Assert.Equal([alvo], dialogos.Perguntas);
        Assert.Equal(EstadoExportacao.Concluido, p.Exportacao.Estado);
        Assert.NotEqual("anterior", File.ReadAllText(alvo));
    }

    [Theory]
    [InlineData("xlsx")]
    [InlineData("zip")]
    public async Task Cancelar_no_meio_preserva_o_anterior_e_nao_deixa_temporario(string formato)
    {
        string alvo = Path.Combine(_pasta, $"TESTE_data_luna_resultado.{formato}");
        File.WriteAllText(alvo, "anterior");
        ExportacaoViewModel? e = null;
        // Cancela quando a terceira aba começa, como o botão Cancelar no meio da gravação.
        var p = await Processado(Teste, exportar: (r, f, a, g, t) => Exportacoes.DoMotor(r, f, a, new Progresso(passo =>
        {
            g.Report(passo);
            if (passo is { Etapa: EtapaDeGravacao.Gravacao, Aba: 3 })
                e!.GravarCancelCommand.Execute(null);
        }), t));
        e = p.Exportacao;
        e.Pasta = _pasta;

        await e.GravarCommand.ExecuteAsync(formato);

        Assert.Equal(EstadoExportacao.Cancelado, e.Estado);
        Assert.Equal("Exportação cancelada. Nenhum arquivo foi alterado.", e.TextoEstado);
        Assert.Null(e.MensagemDeErro);
        Assert.Null(e.CaminhoGravado);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Equal([alvo], Directory.GetFiles(_pasta));
        Assert.False(p.Base.Bloqueado);
        Assert.True(e.GravarCommand.CanExecute(formato));
    }

    private sealed class Progresso(Action<PassoDeGravacao> aoAvisar) : IProgress<PassoDeGravacao>
    {
        public void Report(PassoDeGravacao value) => aoAvisar(value);
    }

    [Fact]
    public async Task Excel_recusado_por_fuso_mostra_a_mensagem_do_motor_e_o_zip_grava()
    {
        var p = await Processado(Repositorio.Caminho("contrato", "transformacao", "fuso_homogeneo_entrada.csv"));
        var e = p.Exportacao;
        e.Pasta = _pasta;

        await e.GravarCommand.ExecuteAsync("xlsx");
        Assert.Equal(EstadoExportacao.Erro, e.Estado);
        Assert.Contains("fuso", e.MensagemDeErro);
        Assert.Contains("ZIP", e.MensagemDeErro);
        Assert.DoesNotContain("   at ", e.MensagemDeErro);
        Assert.Empty(Directory.GetFiles(_pasta));

        await e.GravarCommand.ExecuteAsync("zip");
        Assert.Equal(EstadoExportacao.Concluido, e.Estado);
        Assert.Null(e.MensagemDeErro);
    }

    [Fact]
    public async Task Alvo_aberto_em_outro_programa_da_erro_legivel_e_preserva_o_arquivo()
    {
        string alvo = Path.Combine(_pasta, "TESTE_data_luna_resultado.xlsx");
        File.WriteAllText(alvo, "anterior");
        var p = await Processado(Teste, new Dialogos(substituir: true));
        var e = p.Exportacao;
        e.Pasta = _pasta;
        using (new FileStream(alvo, FileMode.Open, FileAccess.Read, FileShare.None))
            await e.GravarCommand.ExecuteAsync("xlsx");

        Assert.Equal(EstadoExportacao.Erro, e.Estado);
        Assert.StartsWith("Não foi possível gravar o arquivo. Ele pode estar aberto em outro programa", e.MensagemDeErro);
        Assert.DoesNotContain("   at ", e.MensagemDeErro);
        Assert.Equal("anterior", File.ReadAllText(alvo));
        Assert.Equal([alvo], Directory.GetFiles(_pasta));
    }

    [Fact]
    public async Task Pasta_inexistente_nao_chama_o_motor()
    {
        int gravacoes = 0;
        var p = await Processado(Teste, exportar: (_, _, a, _, _) => { gravacoes++; return a; });
        var e = p.Exportacao;
        e.Pasta = Path.Combine(_pasta, "nao_existe");
        await e.GravarCommand.ExecuteAsync("zip");
        Assert.Equal(0, gravacoes);
        Assert.Equal(EstadoExportacao.Erro, e.Estado);
        Assert.Equal($"A pasta de destino não existe: {e.Pasta}", e.MensagemDeErro);
    }

    [Fact]
    public async Task Erro_inesperado_nao_mostra_detalhe_interno()
    {
        var p = await Processado(Teste, exportar: (_, _, _, _, _) => throw new InvalidOperationException("Foo.cs:linha 42"));
        p.Exportacao.Pasta = _pasta;
        await p.Exportacao.GravarCommand.ExecuteAsync("zip");
        Assert.Equal("Erro inesperado ao gravar a exportação (InvalidOperationException). O arquivo anterior não foi alterado.",
            p.Exportacao.MensagemDeErro);
    }

    [Fact]
    public async Task Durante_a_exportacao_nao_processa_nem_chamando_o_comando_direto()
    {
        var comecou = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        int processamentos = 0, gravacoes = 0;
        var p = Principal(
            processar: (par, g, t) => { processamentos++; return Caso.Executar(par, g, t); },
            exportar: (r, f, a, g, t) =>
            {
                gravacoes++;
                g.Report(new PassoDeGravacao(EtapaDeGravacao.Gravacao, 1, 8, "Base Bruta", 76));
                comecou.SetResult();
                libera.Task.Wait(t);
                return a;
            });
        p.Base.Caminho = Teste;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        p.Exportacao.Pasta = _pasta;

        var gravacao = p.Exportacao.GravarCommand.ExecuteAsync("zip");
        await comecou.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(p.Exportacao.EmExecucao);
        Assert.True(p.Base.Bloqueado);
        Assert.False(p.Base.ProcessarCommand.CanExecute(null));
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.Equal(1, processamentos);
        Assert.NotNull(p.Base.Resultado);
        Assert.Equal(EstadoExecucao.Concluido, p.Base.Estado);

        // Nem uma segunda gravação ao mesmo tempo.
        Assert.False(p.Exportacao.GravarCommand.CanExecute("xlsx"));
        await p.Exportacao.GravarCommand.ExecuteAsync("xlsx");
        Assert.Equal(1, gravacoes);

        Assert.Equal("Arquivo 1/8: Base Bruta (76 linhas)", p.Exportacao.Passo);
        libera.SetResult();
        await gravacao;
        Assert.Equal(EstadoExportacao.Concluido, p.Exportacao.Estado);
        Assert.False(p.Base.Bloqueado);
        Assert.True(p.Base.ProcessarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Durante_o_processamento_nao_exporta_nem_chamando_o_comando_direto()
    {
        var comecou = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        int chamadas = 0, gravacoes = 0;
        var p = Principal(
            processar: (par, g, t) =>
            {
                if (++chamadas == 2)
                {
                    comecou.SetResult();
                    libera.Task.Wait(t);
                }
                return Caso.Executar(par, g, t);
            },
            exportar: (_, _, a, _, _) => { gravacoes++; return a; });
        p.Base.Caminho = Teste;
        await p.Base.ProcessarCommand.ExecuteAsync(null);
        p.Exportacao.Pasta = _pasta;
        Assert.True(p.Exportacao.GravarCommand.CanExecute("zip"));

        var execucao = p.Base.ProcessarCommand.ExecuteAsync(null);
        await comecou.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(p.Exportacao.GravarCommand.CanExecute("zip"));
        await p.Exportacao.GravarCommand.ExecuteAsync("zip");
        Assert.Equal(0, gravacoes);
        Assert.Equal(EstadoExportacao.Ocioso, p.Exportacao.Estado);
        libera.SetResult();
        await execucao;
        Assert.True(p.Exportacao.GravarCommand.CanExecute("zip"));
    }

    [Fact]
    public async Task Processar_de_novo_descarta_o_estado_da_exportacao()
    {
        var p = await Processado(Teste);
        var e = p.Exportacao;
        e.Pasta = _pasta;
        await e.GravarCommand.ExecuteAsync("zip");
        Assert.Equal(EstadoExportacao.Concluido, e.Estado);

        await p.Base.ProcessarCommand.ExecuteAsync(null);
        Assert.Equal(EstadoExportacao.Ocioso, e.Estado);
        Assert.False(e.Ativa);
        Assert.Null(e.CaminhoGravado);
        Assert.Null(e.MensagemDeErro);
        Assert.Equal("", e.TextoEstado);
        Assert.Equal(_pasta, e.Pasta);
    }

    [Fact]
    public async Task Aviso_de_uma_gravacao_nao_altera_a_seguinte_nem_o_processamento()
    {
        IProgress<PassoDeGravacao>? primeira = null;
        var segunda = new TaskCompletionSource();
        var libera = new TaskCompletionSource();
        var p = await Processado(Teste, exportar: (r, f, a, g, t) =>
        {
            if (primeira is null)
            {
                primeira = g;
                return a;
            }
            g.Report(new PassoDeGravacao(EtapaDeGravacao.Gravacao, 1, 8, "atual", 1));
            segunda.SetResult();
            libera.Task.Wait(t);
            return a;
        });
        var e = p.Exportacao;
        e.Pasta = _pasta;
        string estadoBase = p.Base.TextoEstado;
        var estagios = p.Base.Estagios.Select(x => (x.Estagio, x.Iniciado)).ToList();

        await e.GravarCommand.ExecuteAsync("zip");
        // Aviso atrasado da primeira gravação: depois de ela terminar e durante a seguinte.
        primeira!.Report(new PassoDeGravacao(EtapaDeGravacao.Gravacao, 2, 8, "atrasado", 1));
        Assert.Null(e.Passo);
        Assert.Equal(EstadoExportacao.Concluido, e.Estado);

        var corrente = e.GravarCommand.ExecuteAsync("zip");
        await segunda.Task;
        primeira.Report(new PassoDeGravacao(EtapaDeGravacao.Gravacao, 5, 8, "atrasado", 1));
        Assert.Equal("Arquivo 1/8: atual (1 linhas)", e.Passo);
        libera.SetResult();
        await corrente;

        Assert.Equal(estadoBase, p.Base.TextoEstado);
        Assert.Equal(estagios, p.Base.Estagios.Select(x => (x.Estagio, x.Iniciado)));
    }

    [Fact]
    public async Task Procurar_pasta_abre_na_pasta_da_base_e_preenche()
    {
        var dialogos = new Dialogos(pasta: @"C:\saida");
        var p = await Processado(Teste, dialogos);
        p.Exportacao.ProcurarPastaCommand.Execute(null);
        Assert.Equal(Path.GetDirectoryName(Teste), dialogos.PastasIniciais[0]);
        Assert.Equal(@"C:\saida", p.Exportacao.Pasta);
        p.Exportacao.ProcurarPastaCommand.Execute(null);
        Assert.Equal(@"C:\saida", dialogos.PastasIniciais[1]);
    }

    [Theory]
    [InlineData(typeof(OperationCanceledException), "Exportação cancelada. Nenhum arquivo foi alterado.")]
    [InlineData(typeof(DirectoryNotFoundException), "A pasta de destino não existe ou não está acessível.")]
    [InlineData(typeof(IOException), "Não foi possível gravar o arquivo.")]
    [InlineData(typeof(UnauthorizedAccessException), "Não foi possível gravar o arquivo.")]
    [InlineData(typeof(GravacaoRecusadaException), "msg")]
    public void Mensagens_de_erro_da_exportacao(Type tipo, string inicio)
    {
        var erro = tipo == typeof(GravacaoRecusadaException)
            ? new GravacaoRecusadaException("msg")
            : (Exception)Activator.CreateInstance(tipo, "msg")!;
        Assert.StartsWith(inicio, MensagemDeErro.ParaExportacao(erro));
        Assert.StartsWith("Não foi possível ler o arquivo.", MensagemDeErro.Para(new IOException("x")));
    }

    private static ParametrosProcessamento Parametros(JsonElement caso)
    {
        var p = caso.GetProperty("params");
        int I(string n, int d) => p.TryGetProperty(n, out var v) ? v.GetInt32() : d;
        return new ParametrosProcessamento(Repositorio.Base(caso.GetProperty("base").GetString()!),
            tempoMinExpurgo: I("tempo_min_expurgo", 300), tempoMaxAnomalia: I("tempo_max_anomalia", 21600),
            eventosPrevios: I("eventos_previos", 10), minimoApontamentos: I("minimo_apontamentos", 4),
            ajustePercentual: p.TryGetProperty("ajuste_percentual", out var a) ? a.GetDouble() : 0.0,
            exibirJanelasEntrega: !p.TryGetProperty("exibir_janelas_entrega", out var x) || x.GetBoolean(),
            baseJanela: p.TryGetProperty("base_janela", out var b) ? b.GetString()! : "Chegou_em");
    }

    private string Arquivo(string nome, string conteudo)
    {
        string caminho = Path.Combine(_pasta, nome);
        File.WriteAllText(caminho, conteudo, new UTF8Encoding(false));
        return caminho;
    }
}
