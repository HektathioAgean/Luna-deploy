using System.Globalization;
using Luna.Application;
using Luna.Domain;
using Luna.Infrastructure;

namespace Luna.Integration.Tests;

/// <summary>
/// Fluxo ponta a ponta (arquivo → leitura → esquema → transformação → tempos
/// → KPIs → janelas) contra o motor Python: golden master e executar().
/// </summary>
public class ProcessarBaseTests
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static string BaseSintetica => Repositorio.Caminho("TESTE_data.csv");

    [Fact]
    public void Base_sintetica_igual_ao_golden_master()
    {
        var resultado = Caso.Executar(new ParametrosProcessamento(BaseSintetica));

        Assert.True(resultado.Esquema.Valido);
        Repositorio.ConferirTabela(Repositorio.Caminho("golden", "medianas_cliente.csv"),
            resultado.TemposPorCliente,
            TemposPorCliente.Campos.Select(c => (c.Coluna, (Func<TempoCliente, object?>)c.Valor)).ToList(), "medianas");
        Repositorio.ConferirTabela(Repositorio.Caminho("golden", "janelas_atendimento.csv"),
            resultado.Janelas, JanelasDeAtendimento.Campos, "janelas");

        var (_, inconsistencias) = Repositorio.LerCsv(Repositorio.Caminho("golden", "inconsistencias.csv"));
        Assert.Equal(inconsistencias.Select(l => l[16]), resultado.Inconsistencias.Select(r => r.MotivoInconsistencia));
        Assert.Equal(inconsistencias.Select(l => l[0]), resultado.Inconsistencias.Select(r => r.Origem.CodCliente ?? ""));

        Assert.Equal(new Kpis(76, 56, 10, 5, 5, 5, 850.0, "00:14:10", 800.0, "00:13:20", 25.0, "00:00:25", 1), resultado.Kpis);
        Assert.Empty(resultado.Avisos);
    }

    // executar() do Python sobre a mesma base com outros parâmetros.
    [Fact]
    public void Ajuste_cobertura_e_janela_pela_finalizacao_iguais_ao_python()
    {
        var r = Caso.Executar(new ParametrosProcessamento(BaseSintetica,
            ajustePercentual: 10.0, coberturaJanela: 50, baseJanela: "Finalizada_em"));

        Assert.Equal((935.0, 880.0, 27.0, 1L),
            (r.Kpis!.MedianaGlobalSeg, r.Kpis.TempoIdealQ1GlobalSeg, r.Kpis.GapMedianaQ1GlobalSeg, r.Kpis.OutliersBoxplot));
        Assert.Equal(
        [
            ("C002", 660L, 660L, 660L), ("C003", 874L, 847L, 891L), ("C004", 935L, 880L, 935L),
            ("C001", 1138L, 1065L, 1212L), ("C009", 1265L, 1238L, 1292L),
        ], r.TemposPorCliente.Select(t => (t.CodCliente, t.MedianaTempoSec, t.TempoIdealQ1Sec, t.TempoQ3Sec)));
        Assert.Equal(
        [
            ("C001", "09:25", "09:30", "50.0%"), ("C003", "11:42", "11:43", "53.3%"), ("C004", "01:19", "01:19", "50.0%"),
            ("C009", "14:03", "14:04", "50.0%"), ("C002", "14:15", "14:15", "100.0%"),
        ], r.Janelas.Select(j => (j.CodCliente, j.JanelaInicio, j.JanelaFim, j.CoberturaReal)));
        Assert.All(r.Janelas, j => Assert.Equal("Finalizada_em", j.BaseJanela));
    }

    [Fact]
    public void Regras_operacionais_e_n_diferentes_iguais_ao_python()
    {
        var r = Caso.Executar(new ParametrosProcessamento(BaseSintetica,
            eventosPrevios: 4, minimoApontamentos: 3, tempoPadraoPoucosApontamentos: 900,
            tempoMinExpurgo: 400, tempoMaxAnomalia: 1800));

        Assert.Equal((76L, 55L, 10L, 5L, 6L, 5L), (r.Kpis!.LinhasBrutas, r.Kpis.LinhasValidas, r.Kpis.Inconsistencias,
            r.Kpis.Expurgados, r.Kpis.Anomalias, r.Kpis.ClientesUnicos));
        Assert.Equal((900.0, 900.0, 7.0, 0L),
            (r.Kpis.MedianaGlobalSeg, r.Kpis.TempoIdealQ1GlobalSeg, r.Kpis.GapMedianaQ1GlobalSeg, r.Kpis.OutliersBoxplot));
        Assert.Equal(
        [
            ("C003", 815L, 808L, 822L, "mediana_ultimos_n"), ("C004", 850L, 850L, 850L, "mediana_ultimos_n"),
            ("C002", 900L, 900L, 900L, "mediana_total"), ("C001", 1125L, 1102L, 1148L, "mediana_ultimos_n"),
            ("C009", 1170L, 1155L, 1185L, "mediana_ultimos_n"),
        ], r.TemposPorCliente.Select(t => (t.CodCliente, t.MedianaTempoSec, t.TempoIdealQ1Sec, t.TempoQ3Sec, t.MetodoAplicado)));
    }

    public static TheoryData<string> CasosDoContrato =>
        ["br", "us", "divergente", "uma_linha_us", "fuso_homogeneo", "fuso_z"];

    [Theory]
    [MemberData(nameof(CasosDoContrato))]
    public void Contrato_de_transformacao_lido_pelo_leitor_csv(string caso)
    {
        // A entrada do contrato passa pelo leitor (vazio e "null" viram nulo,
        // como no read_csv) e precisa dar o mesmo esperado do motor.
        var tabela = new CarregadorDeArquivo().Carregar(Repositorio.Caminho("contrato", "transformacao", $"{caso}_entrada.csv"));
        var registros = tabela.Linhas.Select(l => new RegistroBruto((string?)l[0], l[1], l[2])).ToList();
        var obtido = Transformacao.Transformar(registros, new RegrasOperacionais()).Registros;

        var (_, esperado) = Repositorio.LerCsv(Repositorio.Caminho("contrato", "transformacao", $"{caso}_esperado.csv"));
        Assert.Equal(esperado.Count, obtido.Count);
        for (int i = 0; i < obtido.Count; i++)
        {
            Assert.Equal(esperado[i][1], obtido[i].Origem.CodCliente ?? "");
            Assert.Equal(esperado[i][4], obtido[i].TempoSec?.ToString("R", CultureInfo.InvariantCulture) is { } t
                && !t.Contains('.') && !t.Contains('E') ? t + ".0" : obtido[i].TempoSec?.ToString("R", CultureInfo.InvariantCulture) ?? "");
            Assert.Equal(esperado[i][5], obtido[i].MotivoInconsistencia);
            Assert.Equal(esperado[i][6], obtido[i].Classe.ToString().ToLowerInvariant());
        }
    }

    [Fact]
    public void Esquema_invalido_devolve_so_o_relatorio()
    {
        string pasta = Directory.CreateTempSubdirectory("luna_esq_").FullName;
        try
        {
            string caminho = Path.Combine(pasta, "sem_obrig.csv");
            File.WriteAllText(caminho, "Cliente;Outra\nA;1\n");
            var r = Caso.Executar(new ParametrosProcessamento(caminho));
            Assert.False(r.Esquema.Valido);
            Assert.Equal(["Chegou_em", "Finalizada_em"], r.Esquema.ObrigatoriasFaltando);
            Assert.Equal(1, r.LinhasBrutas);
            Assert.Null(r.Kpis);
            Assert.Empty(r.TemposPorCliente);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Base_so_com_cabecalho_e_vazia()
    {
        string pasta = Directory.CreateTempSubdirectory("luna_vazia_").FullName;
        try
        {
            string caminho = Path.Combine(pasta, "so_header.csv");
            File.WriteAllText(caminho, "Cod_Cliente;Chegou_em;Finalizada_em\n");
            var erro = Assert.Throws<BaseInvalidaException>(() => Caso.Executar(new ParametrosProcessamento(caminho)));
            Assert.Equal("A base 'so_header.csv' esta vazia.", erro.Message);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public void Erros_de_carregamento_com_as_mensagens_do_python()
    {
        var carregador = new CarregadorDeArquivo();
        var naoExiste = Assert.Throws<FileNotFoundException>(() => carregador.Carregar("tests/dados/nao_existe.csv"));
        Assert.Equal(@"Arquivo não encontrado: tests\dados\nao_existe.csv", naoExiste.Message);

        string pasta = Repositorio.Caminho("golden");
        Assert.Equal($"O caminho informado não é um arquivo: {pasta}",
            Assert.Throws<BaseInvalidaException>(() => carregador.Carregar(pasta)).Message);

        string manifest = Repositorio.Caminho("contrato", "MANIFEST.json");
        Assert.Equal("Formato não suportado: '.json'. Esperado .csv, .xlsx ou .xlsm.",
            Assert.Throws<BaseInvalidaException>(() => carregador.Carregar(manifest)).Message);
    }

    [Theory]
    [InlineData("  ", "O caminho da base nao foi informado.")]
    public void Parametros_validam_o_caminho(string caminho, string mensagem)
        => Assert.Equal(mensagem, Assert.Throws<ArgumentException>(() => new ParametrosProcessamento(caminho)).Message);

    [Fact]
    public void Parametros_validam_como_os_do_python()
    {
        string Erro(Func<ParametrosProcessamento> criar) => Assert.Throws<ArgumentException>(() => criar()).Message;
        Assert.Equal("base_janela invalida: 'Outra'. Valores aceitos: Chegou_em, Finalizada_em.",
            Erro(() => new ParametrosProcessamento("x.csv", baseJanela: "Outra")));
        Assert.Equal("eventos_previos precisa ser no minimo 1.", Erro(() => new ParametrosProcessamento("x.csv", eventosPrevios: 0)));
        Assert.Equal("cobertura_janela precisa estar entre 1 e 100.", Erro(() => new ParametrosProcessamento("x.csv", coberturaJanela: 101)));
        Assert.Equal("ajuste_percentual precisa estar entre -20 e 100.", Erro(() => new ParametrosProcessamento("x.csv", ajustePercentual: -21)));
        Assert.Equal("minimo_apontamentos nao pode ser maior que eventos_previos.",
            Erro(() => new ParametrosProcessamento("x.csv", eventosPrevios: 3, minimoApontamentos: 4)));
        Assert.Equal("tempo_min_expurgo precisa ser menor que tempo_max_anomalia.",
            Erro(() => new ParametrosProcessamento("x.csv", tempoMinExpurgo: 500, tempoMaxAnomalia: 500)));
    }
}
