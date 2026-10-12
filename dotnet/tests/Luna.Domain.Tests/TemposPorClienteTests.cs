using System.Globalization;

namespace Luna.Domain.Tests;

/// <summary>
/// Paridade de TemposPorCliente, Boxplot e KPIs com o motor Python:
/// tests/dados/contrato/tempos_por_cliente (5 conjuntos de parâmetros,
/// luna-py-ref-v1.1) e golden master da base sintética.
/// </summary>
public class TemposPorClienteTests
{
    public static TheoryData<string, int, int, int, double> Conjuntos => new()
    {
        { "p1_padrao", 10, 4, 600, 0.0 },
        { "p2_ajuste_10", 10, 4, 600, 10.0 },
        { "p3_ajuste_menos_20", 10, 4, 600, -20.0 },
        { "p4_n4", 4, 4, 600, 0.0 },
        { "p5_n5_min3", 5, 3, 900, 12.5 },
    };

    [Fact]
    public void Arquivos_tem_o_hash_registrado_no_manifest()
    {
        var hashes = Contrato.HashesDoManifest();
        var arquivos = Conjuntos.Select(c => $"tempos_por_cliente/esperado_{c[0]}.csv")
            .Append("tempos_por_cliente/entrada.csv");
        foreach (string arquivo in arquivos)
            Assert.Equal(hashes[arquivo], Contrato.Sha256(arquivo));
    }

    [Theory]
    [MemberData(nameof(Conjuntos))]
    public void Tempos_por_cliente_iguais_ao_motor_python(string nome, int n, int minimo, int padrao, double ajuste)
    {
        var obtido = TemposPorCliente.Calcular(EntradaDoContrato(), new ParametrosTempos(n, minimo, padrao, ajuste));
        ConferirTabela(Contrato.Caminho($"tempos_por_cliente/esperado_{nome}.csv"), obtido, nome);
    }

    // Valores de build_kpis do Python sobre o mesmo contrato (contagens fictícias 7/2/3/1).
    [Theory]
    [InlineData("p1_padrao", 780.0, "00:13:00", 660.0, "00:11:00", 45.0, "00:00:45", 3)]
    [InlineData("p2_ajuste_10", 858.0, "00:14:18", 726.0, "00:12:06", 49.0, "00:00:49", 3)]
    [InlineData("p3_ajuste_menos_20", 624.0, "00:10:24", 528.0, "00:08:48", 36.0, "00:00:36", 3)]
    [InlineData("p4_n4", 780.0, "00:13:00", 676.0, "00:11:16", 30.0, "00:00:30", 2)]
    [InlineData("p5_n5_min3", 878.0, "00:14:38", 810.0, "00:13:30", 50.0, "00:00:50", 2)]
    public void Kpis_iguais_ao_motor_python(string nome, double mediana, string medianaFmt,
        double ideal, string idealFmt, double gap, string gapFmt, long outliers)
    {
        var conjunto = Conjuntos.Single(c => (string)c[0] == nome);
        var parametros = new ParametrosTempos((int)conjunto[1], (int)conjunto[2], (int)conjunto[3], (double)conjunto[4]);
        var entrada = EntradaDoContrato();
        var kpis = Kpis.Calcular(7, entrada.Select(r => r.CodCliente).ToList(), 2, 3, 1,
            TemposPorCliente.Calcular(entrada, parametros));

        Assert.Equal(new Kpis(7, 101, 2, 3, 1, 15, mediana, medianaFmt, ideal, idealFmt, gap, gapFmt, outliers), kpis);
    }

    [Fact]
    public void Base_sintetica_ponta_a_ponta_igual_ao_golden_master()
    {
        // Classificação (PR2) + tempos por cliente sobre tests/dados/TESTE_data.csv,
        // com as datas ISO lidas aqui (a leitura de texto completa vem no PR5).
        var linhas = File.ReadAllLines(Path.Combine(Contrato.Caminho(".."), "TESTE_data.csv"))
            .Skip(1)
            .Select(l => l.Split(';'))
            .ToList();
        var regras = new RegrasOperacionais();
        var classificados = linhas
            .Select(c => Classificacao.Classificar(
                new Atendimento(c[0].Trim().Length == 0 ? null : c[0].Trim(), DataIso(c[1]), DataIso(c[2])), regras))
            .ToList();
        var processados = classificados
            .Where(c => c.Classe == Classe.Processado)
            .Select(c => new RegistroTempo(c.Origem.CodCliente!, c.Origem.ChegouEm!.Value, c.TempoSec!.Value))
            .ToList();
        var tempos = TemposPorCliente.Calcular(processados, new ParametrosTempos());

        ConferirTabela(Path.Combine(Contrato.Caminho(".."), "golden", "medianas_cliente.csv"), tempos, "golden");

        // Valores de executar() do Python sobre a mesma base.
        var kpis = Kpis.Calcular(linhas.Count, processados.Select(p => p.CodCliente).ToList(),
            classificados.Count(c => c.Classe == Classe.Inconsistente),
            classificados.Count(c => c.Classe == Classe.Expurgado),
            classificados.Count(c => c.Classe == Classe.Anomalia),
            tempos);
        Assert.Equal(new Kpis(76, 56, 10, 5, 5, 5, 850.0, "00:14:10", 800.0, "00:13:20", 25.0, "00:00:25", 1), kpis);
    }

    [Fact]
    public void Empate_em_chegou_em_nao_depende_da_ordem_de_entrada()
    {
        var instante = DateTimeOffset.UnixEpoch;
        var registros = new[] { 600.0, 1800.0, 600.0, 1800.0, 600.0, 600.0 }
            .Select(t => new RegistroTempo("C", instante, t)).ToList();
        var parametros = new ParametrosTempos(eventosPrevios: 4);

        var direta = TemposPorCliente.Calcular(registros, parametros);
        var invertida = TemposPorCliente.Calcular(Enumerable.Reverse(registros), parametros);
        Assert.Equal(direta, invertida);
        Assert.Equal(1200, direta[0].MedianaTempoSec);
    }

    [Fact]
    public void Percentual_usa_o_arredondamento_do_numpy()
    {
        // (4000 - 301) / 4000 * 100 = 92.475: numpy dá 92.48, round do Python dá
        // 92.47. Valores de calcular_medianas_por_cliente do Python (pandas 2.3.3).
        var registros = new[] { 301.0, 301.0, 4000.0, 4000.0, 4000.0 }
            .Select((t, i) => new RegistroTempo("C", DateTimeOffset.UnixEpoch.AddDays(i), t)).ToList();
        var linha = TemposPorCliente.Calcular(registros, new ParametrosTempos()).Single();

        Assert.Equal((4000L, 301L, 3699L), (linha.MedianaTempoSec, linha.TempoIdealQ1Sec, linha.DiferencaMedianaQ1Sec));
        Contrato.MesmoDouble(92.48, linha.DiferencaMedianaQ1Percentual, "percentual");
    }

    [Fact]
    public void Codigos_00123_e_123_sao_clientes_distintos()
    {
        var instante = DateTimeOffset.UnixEpoch;
        var registros = new[] { "00123", "123", " 123 " }
            .Select((c, i) => new RegistroTempo(c, instante.AddDays(i), 600)).ToList();
        var tempos = TemposPorCliente.Calcular(registros, new ParametrosTempos(minimoApontamentos: 1));
        Assert.Equal(["00123", "123"], tempos.Select(t => t.CodCliente));
        Assert.Equal([1L, 2L], tempos.Select(t => t.QtdApontamentos));
    }

    [Fact]
    public void Entrada_vazia_e_codigo_em_branco_nao_geram_linhas()
    {
        Assert.Empty(TemposPorCliente.Calcular([], new ParametrosTempos()));
        Assert.Empty(TemposPorCliente.Calcular(
            [new RegistroTempo("  ", DateTimeOffset.UnixEpoch, 600), new RegistroTempo("A", DateTimeOffset.UnixEpoch, double.NaN)],
            new ParametrosTempos()));
        Assert.Equal(0.0, Kpis.Calcular(0, [], 0, 0, 0, []).MedianaGlobalSeg);
    }

    [Fact]
    public void Parametros_validam_como_os_do_python()
    {
        Assert.Throws<ArgumentException>(() => new ParametrosTempos(eventosPrevios: 3, minimoApontamentos: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParametrosTempos(ajustePercentual: -20.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParametrosTempos(ajustePercentual: 100.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParametrosTempos(eventosPrevios: 0));
    }

    [Fact]
    public void Boxplot_sem_dispersao_usa_minimo_e_maximo_e_nao_corta()
    {
        var boxplot = Boxplot.Calcular([900.0, 900.0, 900.0]);
        Assert.True(boxplot.SemDispersao);
        Assert.Equal((900.0, 900.0), (boxplot.LimiteInferior, boxplot.LimiteSuperior));
        Assert.Equal(3, boxplot.Limpar([900.0, 900.0, 900.0]).Length);
    }

    private static List<RegistroTempo> EntradaDoContrato()
        // Cod_Cliente;Chegou_em;Tempo_Sec — na ordem do arquivo (importa para empates).
        => Contrato.Linhas("tempos_por_cliente/entrada.csv")
            .Select(c => new RegistroTempo(c[0], DataDoContrato(c[1]), Contrato.Double(c[2])))
            .ToList();

    /// <summary>
    /// Compara a tabela C# com o CSV do Python: mesmo cabeçalho e ordem de
    /// colunas, mesma ordem de linhas, inteiros e textos iguais, doubles bit a bit.
    /// </summary>
    private static void ConferirTabela(string caminhoCsv, IReadOnlyList<TempoCliente> obtido, string contexto)
    {
        var linhas = File.ReadAllLines(caminhoCsv).Select(l => l.TrimStart('﻿').Split(';')).ToList();
        Assert.Equal(TemposPorCliente.Campos.Select(c => c.Coluna), linhas[0]);
        Assert.Equal(linhas.Count - 1, obtido.Count);

        for (int i = 0; i < obtido.Count; i++)
        {
            string[] esperado = linhas[i + 1];
            for (int j = 0; j < TemposPorCliente.Campos.Count; j++)
            {
                var (coluna, valor) = TemposPorCliente.Campos[j];
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
