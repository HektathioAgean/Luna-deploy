using System.Globalization;

namespace Luna.Domain.Tests;

/// <summary>
/// Paridade da janela circular com o motor Python:
/// tests/dados/contrato/janelas (coberturas 50, 80 e 100%, luna-py-ref-v1.1)
/// e golden master de janelas da base sintética.
/// </summary>
public class JanelasTests
{
    public static TheoryData<int> Coberturas => [50, 80, 100];

    [Fact]
    public void Arquivos_tem_o_hash_registrado_no_manifest()
    {
        var hashes = Contrato.HashesDoManifest();
        foreach (string arquivo in new[] { 50, 80, 100 }.Select(c => $"janelas/esperado_cobertura_{c}.csv").Append("janelas/entrada.csv"))
            Assert.Equal(hashes[arquivo], Contrato.Sha256(arquivo));
    }

    [Theory]
    [MemberData(nameof(Coberturas))]
    public void Janelas_iguais_ao_motor_python(int coberturaPercentual)
    {
        // Cod_Cliente;Chegou_em — único atendimento, dispersos, com segundos,
        // atravessando a meia-noite, empate, comercial e noturno.
        var entrada = Contrato.Linhas("janelas/entrada.csv")
            .Select(c => new RegistroHorario(c[0], DataDoContrato(c[1])))
            .ToList();

        var obtido = JanelasDeAtendimento.Calcular(entrada, coberturaPercentual / 100.0);

        ConferirTabela(Contrato.Caminho($"janelas/esperado_cobertura_{coberturaPercentual}.csv"), obtido,
            $"cobertura {coberturaPercentual}");
    }

    [Fact]
    public void Base_sintetica_ponta_a_ponta_igual_ao_golden_master()
    {
        // Classificação (PR2) sobre TESTE_data.csv; janelas sobre os processados.
        var regras = new RegrasOperacionais();
        var processados = File.ReadAllLines(Path.Combine(Contrato.Caminho(".."), "TESTE_data.csv"))
            .Skip(1)
            .Select(l => l.Split(';'))
            .Select(c => Classificacao.Classificar(
                new Atendimento(c[0].Trim().Length == 0 ? null : c[0].Trim(), DataIso(c[1]), DataIso(c[2])), regras))
            .Where(c => c.Classe == Classe.Processado)
            .Select(c => new RegistroHorario(c.Origem.CodCliente!, c.Origem.ChegouEm))
            .ToList();

        var obtido = JanelasDeAtendimento.Calcular(processados, 80 / 100.0);

        ConferirTabela(Path.Combine(Contrato.Caminho(".."), "golden", "janelas_atendimento.csv"), obtido, "golden");
    }

    [Theory]
    [InlineData(0, 0, 0, 0.0)]
    [InlineData(8, 0, 30, 480.5)]
    [InlineData(23, 59, 59, 1439.9833333333333)]
    public void Minutos_desde_meia_noite_ignora_fracao_de_segundo(int h, int m, int s, double esperado)
    {
        var instante = new DateTimeOffset(2026, 1, 1, h, m, s, 999, TimeSpan.FromHours(-3));
        Contrato.MesmoDouble(esperado, Horario.MinutosDesdeMeiaNoite(instante), $"{h}:{m}:{s}");
    }

    [Theory]
    [InlineData(0.5, "00:00")]      // empate para o par
    [InlineData(1.5, "00:02")]
    [InlineData(1439.5, "00:00")]   // 1440 dá a volta no dia
    [InlineData(-30.0, "23:30")]    // resto do Python
    [InlineData(double.NaN, "")]
    public void Formatar_hhmm_segue_o_python(double minutos, string esperado)
        => Assert.Equal(esperado, Horario.FormatarHhMm(minutos));

    [Theory]
    [InlineData(419.0, "Noturno", "Nao")]
    [InlineData(420.0, "Diurno", "Sim")]
    [InlineData(719.99, "Diurno", "Sim")]
    [InlineData(720.0, "Vespertino", "Sim")]
    [InlineData(1020.0, "Noturno", "Sim")]
    [InlineData(1080.0, "Noturno", "Nao")]
    [InlineData(1860.0, "Diurno", "Sim")]   // 1860 % 1440 = 420
    public void Periodo_e_horario_comercial_nas_bordas(double minutos, string periodo, string comercial)
    {
        Assert.Equal(periodo, Horario.ClassificarPeriodo(minutos));
        Assert.Equal(comercial, Horario.ClassificarComercial(minutos));
    }

    [Fact]
    public void Janela_que_atravessa_a_meia_noite()
    {
        var janela = JanelaCircular.Calcular([1380, 1410, 30, 60], 0.8);
        Assert.Equal(new JanelaCircular(1380, 60, 120, 1.0, true), janela);
    }

    [Fact]
    public void Primeira_menor_largura_vence_o_empate()
    {
        var janela = JanelaCircular.Calcular([60, 120, 180, 240], 0.5);
        Assert.Equal((60.0, 120.0), (janela.InicioMin!.Value, janela.FimMin!.Value));
    }

    [Fact]
    public void Cobertura_usa_o_teto_em_double_como_o_python()
    {
        // 25 * 0.28 = 7.000000000000001 em double: o teto dá 8 pontos, não 7.
        // Valores de calcular_janela_circular_minima do Python.
        double[] minutos = Enumerable.Range(0, 25).Select(i => i * 10.0).ToArray();
        var janela = JanelaCircular.Calcular(minutos, 0.28);
        Assert.Equal(new JanelaCircular(0.0, 70.0, 70.0, 0.32, false), janela);
    }

    [Fact]
    public void Tolerancia_inclui_o_ponto_da_borda_que_o_double_deixaria_de_fora()
    {
        // Atravessa a meia-noite: o fim estendido (m + 1440) e a conta circular
        // (m - início) % 1440 diferem em 1 ulp; sem a tolerância de 1e-9 um
        // ponto da borda sairia e a cobertura cairia para 0,875. Valores de
        // calcular_janela_circular_minima do Python.
        double[] minutos =
        [
            1056.8333333333333, 621.7333333333333, 698.5166666666667, 1131.4833333333333,
            173.08333333333334, 510.73333333333335, 1264.05, 1424.3166666666666,
        ];
        var janela = JanelaCircular.Calcular(minutos, 1.0);
        Assert.Equal(new JanelaCircular(1056.8333333333333, 698.5166666666664, 1081.6833333333332, 1.0, true), janela);
    }

    [Fact]
    public void Janela_pela_finalizacao_igual_ao_motor_python()
    {
        // Mesmos atendimentos lidos por Finalizada_em e por Chegou_em: a base
        // muda a janela e o campo Base_Janela. Valores de
        // calcular_janelas_entrega(..., 0.8, usar_coluna) do Python.
        DateTimeOffset D(string texto) => DateTimeOffset.ParseExact(texto, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        (string Cliente, string Chegou, string Finalizada)[] atendimentos =
        [
            ("F1", "2026-01-01 23:40:00", "2026-01-02 00:10:00"),
            ("F1", "2026-01-02 23:50:00", "2026-01-03 00:20:30"),
            ("F1", "2026-01-03 08:00:00", "2026-01-03 08:30:00"),
            ("F2", "2026-01-01 10:00:00", "2026-01-01 10:45:00"),
            ("F2", "2026-01-02 10:30:00", "2026-01-02 11:00:00"),
        ];

        var pelaFinalizacao = JanelasDeAtendimento.Calcular(
            atendimentos.Select(a => new RegistroHorario(a.Cliente, D(a.Finalizada))), 0.8, "Finalizada_em");
        Assert.Equal(
        [
            new JanelaCliente("F1", 3, 10.0, 510.0, "00:10", "08:30", 500.0, "80%", "100.0%", 1.0,
                "Nao", "Noturno", "Nao", "Finalizada_em"),
            new JanelaCliente("F2", 2, 645.0, 660.0, "10:45", "11:00", 15.0, "80%", "100.0%", 1.0,
                "Nao", "Diurno", "Sim", "Finalizada_em"),
        ], pelaFinalizacao);

        var pelaChegada = JanelasDeAtendimento.Calcular(
            atendimentos.Select(a => new RegistroHorario(a.Cliente, D(a.Chegou))), 0.8, "Chegou_em");
        Assert.Equal(
        [
            new JanelaCliente("F1", 3, 1420.0, 480.0, "23:40", "08:00", 500.0, "80%", "100.0%", 1.0,
                "Sim", "Noturno", "Nao", "Chegou_em"),
            new JanelaCliente("F2", 2, 600.0, 630.0, "10:00", "10:30", 30.0, "80%", "100.0%", 1.0,
                "Nao", "Diurno", "Sim", "Chegou_em"),
        ], pelaChegada);
    }

    // Valores de calcular_janela_circular_minima do Python.
    [Theory]
    [InlineData(new[] { 600.0, 600.0, 600.0, 600.0 }, 0.8, 1.0)]
    [InlineData(new[] { 600.0, 600.0, 600.0, 900.0 }, 0.5, 0.75)]
    [InlineData(new[] { 600.0, 600.0, 900.0, 900.0 }, 0.5, 0.5)]
    public void Horarios_repetidos_dao_largura_zero_e_contam_todos_os_iguais(
        double[] minutos, double cobertura, double coberturaReal)
    {
        var janela = JanelaCircular.Calcular(minutos, cobertura);
        Assert.Equal(new JanelaCircular(600.0, 600.0, 0.0, coberturaReal, false), janela);
    }

    [Fact]
    public void Unico_horario_e_entradas_vazias()
    {
        Assert.Equal(new JanelaCircular(620, 620, 0.0, 1.0, false), JanelaCircular.Calcular([620], 0.8));
        Assert.Equal(new JanelaCircular(null, null, null, 0.0, false), JanelaCircular.Calcular([double.NaN], 0.8));
        Assert.Empty(JanelasDeAtendimento.Calcular(
            [new RegistroHorario("  ", DateTimeOffset.UnixEpoch), new RegistroHorario("A", null)], 0.8));
    }

    [Fact]
    public void Parametros_invalidos_sao_rejeitados()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JanelasDeAtendimento.Calcular([], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => JanelasDeAtendimento.Calcular([], 1.01));
        Assert.Throws<ArgumentException>(() => JanelasDeAtendimento.Calcular([], 0.8, "Outra"));
    }

    private static void ConferirTabela(string caminhoCsv, IReadOnlyList<JanelaCliente> obtido, string contexto)
    {
        var linhas = File.ReadAllLines(caminhoCsv).Select(l => l.Split(';')).ToList();
        Assert.Equal(JanelasDeAtendimento.Campos.Select(c => c.Coluna), linhas[0]);
        Assert.Equal(linhas.Count - 1, obtido.Count);

        for (int i = 0; i < obtido.Count; i++)
        {
            string[] esperado = linhas[i + 1];
            for (int j = 0; j < JanelasDeAtendimento.Campos.Count; j++)
            {
                var (coluna, valor) = JanelasDeAtendimento.Campos[j];
                string onde = $"{contexto} {esperado[0]} {coluna}";
                switch (valor(obtido[i]))
                {
                    case double d:
                        Contrato.MesmoDouble(Contrato.Double(esperado[j]), d, onde);
                        break;
                    case long n:
                        Assert.True(long.Parse(esperado[j], CultureInfo.InvariantCulture) == n,
                            $"{onde}: esperado {esperado[j]}, obtido {n}");
                        break;
                    case string s:
                        Assert.True(esperado[j] == s, $"{onde}: esperado '{esperado[j]}', obtido '{s}'");
                        break;
                    case null:
                        Assert.True(esperado[j].Length == 0, $"{onde}: esperado '{esperado[j]}', obtido nulo");
                        break;
                }
            }
        }
    }

    private static DateTimeOffset DataDoContrato(string texto)
        => DateTimeOffset.ParseExact(texto, "yyyy-MM-dd'T'HH:mm:ss.ffffff", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);

    private static DateTimeOffset? DataIso(string texto)
        => DateTimeOffset.TryParseExact(texto, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var data) ? data : null;
}
