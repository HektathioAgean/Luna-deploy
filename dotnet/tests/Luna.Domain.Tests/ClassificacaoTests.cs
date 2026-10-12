using System.Globalization;

namespace Luna.Domain.Tests;

/// <summary>
/// Paridade da duração, dos motivos e das regras operacionais com o motor
/// Python: tests/dados/contrato/transformacao (luna-py-ref-v1.1) e o golden
/// master de inconsistências.
/// <para>
/// A interpretação das datas em texto ainda não foi portada (PR5): a entrada
/// aqui são as datas já convertidas pelo Python, como estão no esperado. O
/// indicador de formato divergente também vem do esperado; o teste confere
/// a posição dele na ordem dos motivos.
/// </para>
/// </summary>
public class ClassificacaoTests
{
    private static readonly RegrasOperacionais Padrao = new();

    public static TheoryData<string> CasosDoContrato =>
        ["br", "us", "divergente", "uma_linha_us", "fuso_homogeneo", "fuso_z"];

    [Theory]
    [MemberData(nameof(CasosDoContrato))]
    public void Esperado_tem_o_hash_registrado_no_manifest(string caso)
    {
        string arquivo = $"transformacao/{caso}_esperado.csv";
        Assert.Equal(Contrato.HashesDoManifest()[arquivo], Contrato.Sha256(arquivo));
    }

    [Theory]
    [MemberData(nameof(CasosDoContrato))]
    public void Classificacao_igual_ao_motor_python(string caso)
    {
        int linhas = 0;
        // linha;Cod_Cliente;Chegou_em;Finalizada_em;Tempo_Sec;Motivo_Inconsistencia;Classe
        foreach (var c in Contrato.Linhas($"transformacao/{caso}_esperado.csv"))
        {
            var atendimento = new Atendimento(
                CodCliente: c[1].Length == 0 ? null : c[1],
                ChegouEm: DataDoContrato(c[2]),
                FinalizadaEm: DataDoContrato(c[3]),
                FormatoDivergente: c[5].Split(" | ").Contains(Classificacao.MotivoFormatoDivergente));

            var obtido = Classificacao.Classificar(atendimento, Padrao);

            string contexto = $"{caso} linha {c[0]}";
            ConferirTempo(c[4], obtido.TempoSec, contexto);
            Assert.True(c[5] == obtido.MotivoInconsistencia, $"{contexto}: motivo '{obtido.MotivoInconsistencia}', esperado '{c[5]}'");
            Assert.True(c[6] == NomeDaClasse(obtido.Classe), $"{contexto}: classe {obtido.Classe}, esperado {c[6]}");
            linhas++;
        }
        Assert.True(linhas > 0, caso);
    }

    [Fact]
    public void Golden_master_de_inconsistencias_igual_ao_motor_python()
    {
        // Cod_Cliente;Chegou_em;Finalizada_em;...;Tempo_Sec (6);...;Motivo_Inconsistencia (16)
        var linhas = File.ReadAllLines(Path.Combine(Contrato.Caminho(".."), "golden", "inconsistencias.csv"))
            .Skip(1)
            .Select(l => l.Split(';'))
            .ToList();
        Assert.Equal(10, linhas.Count);

        foreach (var c in linhas)
        {
            var atendimento = new Atendimento(
                c[0].Length == 0 ? null : c[0], DataDoGolden(c[1]), DataDoGolden(c[2]));
            var obtido = Classificacao.Classificar(atendimento, Padrao);

            ConferirTempo(c[6], obtido.TempoSec, $"golden {c[0]} {c[1]}");
            Assert.Equal(c[16], obtido.MotivoInconsistencia);
            Assert.Equal(Classe.Inconsistente, obtido.Classe);
        }
    }

    [Theory]
    [InlineData(299.999999, Classe.Expurgado)]
    [InlineData(300.0, Classe.Processado)]
    [InlineData(21600.0, Classe.Processado)]
    [InlineData(21600.000001, Classe.Anomalia)]
    [InlineData(0.0, Classe.Expurgado)]
    public void Limites_de_300_e_21600_segundos_ficam_no_calculo(double tempo, Classe esperada)
        => Assert.Equal(esperada, Padrao.Classificar(tempo));

    [Fact]
    public void Fracao_de_segundo_e_preservada()
    {
        var inicio = new DateTimeOffset(2026, 10, 5, 10, 0, 0, 250, TimeSpan.Zero);
        var fim = new DateTimeOffset(2026, 10, 5, 10, 10, 0, 750, TimeSpan.Zero);
        Contrato.MesmoDouble(600.5, Classificacao.TempoEmSegundos(inicio, fim)!.Value, "600,5 s");
        Contrato.MesmoDouble(0.000001,
            Classificacao.TempoEmSegundos(inicio, inicio.AddTicks(10))!.Value, "1 microssegundo");
    }

    [Fact]
    public void Datas_com_fusos_diferentes_comparam_o_instante()
    {
        var inicio = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.FromHours(-3));
        var fim = new DateTimeOffset(2026, 10, 3, 11, 20, 0, TimeSpan.Zero);
        Assert.Equal(1200.0, Classificacao.TempoEmSegundos(inicio, fim));
    }

    [Fact]
    public void Todos_os_motivos_saem_na_ordem_do_motor()
    {
        var semNada = new Atendimento(null, null, null, FormatoDivergente: true);
        Assert.Equal("data_formato_divergente | datetime_invalido | cliente_vazio | tempo_nulo",
            Classificacao.Motivo(semNada, null));

        var negativo = new Atendimento("", DateTimeOffset.UnixEpoch.AddHours(1), DateTimeOffset.UnixEpoch);
        Assert.Equal("cliente_vazio | tempo_negativo",
            Classificacao.Classificar(negativo, Padrao).MotivoInconsistencia);
    }

    [Fact]
    public void Tempo_zero_e_valido_e_vai_para_expurgo()
    {
        var atendimento = new Atendimento("A", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var obtido = Classificacao.Classificar(atendimento, Padrao);
        Assert.Equal("", obtido.MotivoInconsistencia);
        Assert.Equal(Classe.Expurgado, obtido.Classe);
    }

    [Fact]
    public void Regras_validam_como_os_parametros_do_python()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegrasOperacionais(0, 100));
        Assert.Throws<ArgumentException>(() => new RegrasOperacionais(300, 300));
        var regras = new RegrasOperacionais(60, 120);
        Assert.Equal(Classe.Anomalia, regras.Classificar(121));
    }

    private static DateTimeOffset? DataDoContrato(string texto)
    {
        if (texto.Length == 0)
            return null;
        string[] formatos = ["yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", "yyyy-MM-dd'T'HH:mm:ss.ffffff"];
        return DateTimeOffset.ParseExact(texto, formatos, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
    }

    private static DateTimeOffset? DataDoGolden(string texto)
        => texto.Length == 0
            ? null
            : DateTimeOffset.ParseExact(texto, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);

    private static void ConferirTempo(string esperado, double? obtido, string contexto)
    {
        if (esperado.Length == 0)
        {
            Assert.True(obtido is null, $"{contexto}: tempo {obtido}, esperado nulo");
            return;
        }
        Assert.True(obtido is not null, $"{contexto}: tempo nulo, esperado {esperado}");
        Contrato.MesmoDouble(Contrato.Double(esperado), obtido!.Value, contexto);
    }

    private static string NomeDaClasse(Classe classe) => classe switch
    {
        Classe.Processado => "processado",
        Classe.Expurgado => "expurgado",
        Classe.Anomalia => "anomalia",
        Classe.Inconsistente => "inconsistente",
        _ => throw new ArgumentOutOfRangeException(nameof(classe)),
    };
}
