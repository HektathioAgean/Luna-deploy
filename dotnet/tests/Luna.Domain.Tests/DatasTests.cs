using System.Globalization;
using Luna.Domain.Datas;

namespace Luna.Domain.Tests;

/// <summary>
/// Interpretação de datas, código do cliente e transformação completa contra
/// o motor Python: tests/dados/contrato/transformacao (luna-py-ref-v1.1), as
/// mensagens de erro esperadas e baterias de formatos conferidas no pandas 2.3.3.
/// </summary>
public class DatasTests
{
    private static readonly RegrasOperacionais Padrao = new();

    public static TheoryData<string> CasosDoContrato =>
        ["br", "us", "divergente", "uma_linha_us", "fuso_homogeneo", "fuso_z"];

    [Theory]
    [MemberData(nameof(CasosDoContrato))]
    public void Transformacao_do_texto_bruto_igual_ao_motor_python(string caso)
    {
        Assert.Equal(Contrato.HashesDoManifest()[$"transformacao/{caso}_entrada.csv"],
            Contrato.Sha256($"transformacao/{caso}_entrada.csv"));

        var entrada = Contrato.Linhas($"transformacao/{caso}_entrada.csv")
            .Select(c => new RegistroBruto(c[0], c[1], c[2]))
            .ToList();
        var esperado = Contrato.Linhas($"transformacao/{caso}_esperado.csv").ToList();

        var obtido = Transformacao.Transformar(entrada, Padrao);

        Assert.Equal(esperado.Count, obtido.Count);
        for (int i = 0; i < obtido.Count; i++)
        {
            // linha;Cod_Cliente;Chegou_em;Finalizada_em;Tempo_Sec;Motivo_Inconsistencia;Classe
            string[] e = esperado[i];
            var o = obtido[i];
            string onde = $"{caso} linha {e[0]}";
            Assert.True(e[1] == (o.Origem.CodCliente ?? ""), $"{onde}: cliente '{o.Origem.CodCliente}', esperado '{e[1]}'");
            ConferirData(e[2], o.Origem.ChegouEm, $"{onde} Chegou_em");
            ConferirData(e[3], o.Origem.FinalizadaEm, $"{onde} Finalizada_em");
            if (e[4].Length == 0)
                Assert.Null(o.TempoSec);
            else
                Contrato.MesmoDouble(Contrato.Double(e[4]), o.TempoSec!.Value, $"{onde} Tempo_Sec");
            Assert.True(e[5] == o.MotivoInconsistencia, $"{onde}: motivo '{o.MotivoInconsistencia}', esperado '{e[5]}'");
            Assert.True(e[6] == o.Classe.ToString().ToLowerInvariant(), $"{onde}: classe {o.Classe}, esperado {e[6]}");
        }
    }

    [Fact]
    public void Erros_de_importacao_com_a_mensagem_exata_do_motor()
    {
        var esperados = Contrato.Linhas("transformacao/erros_esperados.csv").ToList();
        Assert.Equal(5, esperados.Count);
        foreach (var (caso, mensagem) in esperados.Select(c => (c[0], c[1])))
        {
            var entrada = Contrato.Linhas($"transformacao/erro_{caso}_entrada.csv")
                .Select(c => new RegistroBruto(c[0], c[1], c[2]))
                .ToList();
            var erro = Assert.Throws<FormatoDeDataException>(() => Transformacao.Transformar(entrada, Padrao));
            Assert.True(mensagem == erro.Message, $"{caso}:\n esperado: {mensagem}\n obtido:   {erro.Message}");
        }
    }

    [Fact]
    public void Base_sintetica_do_texto_bruto_igual_ao_python()
    {
        var registros = File.ReadAllLines(Path.Combine(Contrato.Caminho(".."), "TESTE_data.csv"))
            .Skip(1)
            .Select(l => l.TrimStart('\uFEFF').Split(';'))
            .Select(c => new RegistroBruto(c[0], c[1], c[2]))
            .ToList();
        var classificados = Transformacao.Transformar(registros, Padrao);

        // Golden de inconsistências: mesmo motivo e tempo, na ordem do arquivo.
        var golden = File.ReadAllLines(Path.Combine(Contrato.Caminho(".."), "golden", "inconsistencias.csv"))
            .Skip(1).Select(l => l.Split(';')).ToList();
        var inconsistentes = classificados.Where(c => c.Classe == Classe.Inconsistente).ToList();
        Assert.Equal(golden.Select(g => g[16]), inconsistentes.Select(c => c.MotivoInconsistencia));

        var processados = classificados.Where(c => c.Classe == Classe.Processado)
            .Select(c => new RegistroTempo(c.Origem.CodCliente!, c.Origem.ChegouEm!.Value, c.TempoSec!.Value))
            .ToList();
        var kpis = Kpis.Calcular(registros.Count, processados.Select(p => p.CodCliente).ToList(),
            inconsistentes.Count,
            classificados.Count(c => c.Classe == Classe.Expurgado),
            classificados.Count(c => c.Classe == Classe.Anomalia),
            TemposPorCliente.Calcular(processados, new ParametrosTempos()));
        // Valores de executar() do Python.
        Assert.Equal(new Kpis(76, 56, 10, 5, 5, 5, 850.0, "00:14:10", 800.0, "00:13:20", 25.0, "00:00:25", 1), kpis);
    }

    // pd.to_datetime(pd.Series([texto], dtype=object), format="ISO8601", errors="coerce")
    // no pandas 2.3.3; "" = NaT. Fração abaixo de 100 ns é truncada no C# (1 tick).
    [Theory]
    [InlineData("2026-03-02", "2026-03-02T00:00:00.0000000")]
    [InlineData("2026-03-02 09:15", "2026-03-02T09:15:00.0000000")]
    [InlineData("2026-03-02T09:15", "2026-03-02T09:15:00.0000000")]
    [InlineData("2026-03-02 09:15:30", "2026-03-02T09:15:30.0000000")]
    [InlineData("2026-03-02T09:15:30.5", "2026-03-02T09:15:30.5000000")]
    [InlineData("2026-03-02T09:15:30.123456789", "2026-03-02T09:15:30.1234567")]
    [InlineData("2026-03-02T09:15:30,5", "")]
    [InlineData("2026-03-02 9:15", "2026-03-02T09:15:00.0000000")]
    [InlineData("2026-3-2", "2026-03-02T00:00:00.0000000")]
    [InlineData("2026-03-02T09", "2026-03-02T09:00:00.0000000")]
    [InlineData("2026-03", "2026-03-01T00:00:00.0000000")]
    [InlineData("2026", "2026-01-01T00:00:00.0000000")]
    [InlineData("20260302", "2026-03-02T00:00:00.0000000")]
    [InlineData("20260302T091530", "2026-03-02T09:15:30.0000000")]
    [InlineData("2026-03-02T0915", "2026-03-02T09:15:00.0000000")]
    [InlineData("2026-03-02t09:15", "")]
    [InlineData("2026-03-02  09:15", "")]
    [InlineData(" 2026-03-02", "2026-03-02T00:00:00.0000000")]
    [InlineData("\t2026-03-02", "2026-03-02T00:00:00.0000000")]
    [InlineData("2026-03-02 ", "")]
    [InlineData("2026-03-02\t09:15", "")]
    [InlineData("2026-03-02 09:15:30 ", "2026-03-02T09:15:30.0000000")]
    [InlineData("2026-03-02T09:15Z", "2026-03-02T09:15:00.0000000+00:00")]
    [InlineData("2026-03-02 09:15:30Z", "2026-03-02T09:15:30.0000000+00:00")]
    [InlineData("2026-03-02T09:15:30z", "")]
    [InlineData("2026-03-02T09:15:30+0300", "2026-03-02T09:15:30.0000000+03:00")]
    [InlineData("2026-03-02T09:15:30-03", "2026-03-02T09:15:30.0000000-03:00")]
    [InlineData("2026-03-02T09:15:30 -03:00", "2026-03-02T09:15:30.0000000-03:00")]
    [InlineData("2026-03-02T09:15-03 ", "")]
    [InlineData("2026-03-02T09:15:30+03:00:00", "")]
    [InlineData("2026-03-02T24:00:00", "")]
    [InlineData("2026-02-30", "")]
    [InlineData("2026-03-02T09:15:60", "")]
    [InlineData("2026-W10-1", "")]
    [InlineData("2026-061", "")]
    [InlineData("2026/03/02", "2026-03-02T00:00:00.0000000")]
    [InlineData("+2026-03-02", "")]
    [InlineData("2026-03-02T09:15:30.", "2026-03-02T09:15:30.0000000")]
    [InlineData("2026-03-02T09:15:30.1234567890", "2026-03-02T09:15:30.1234567")]
    [InlineData("2026-03-02T09:15:30.123456789123456789", "2026-03-02T09:15:30.1234567")]
    [InlineData("2026-03-02T09:15:30.1234567891234567891", "")]
    [InlineData("0099-01-01", "")]
    [InlineData("2026-13-01", "")]
    [InlineData("1677-09-21 00:12:44", "1677-09-21T00:12:44.0000000")]
    [InlineData("1677-09-21 00:12:43", "")]
    [InlineData("2262-04-11 23:47:16", "2262-04-11T23:47:16.0000000")]
    [InlineData("2262-04-11 23:47:17", "")]
    [InlineData("2262-04-11T23:47:16-01:00", "2262-04-11T23:47:16.0000000-01:00")]
    [InlineData("\u0662\u0660\u0662\u0666-03-02", "")]
    [InlineData("2026-03-02\u00A009:15", "")]
    [InlineData("\u00A02026-03-02", "")]
    public void Parser_iso_igual_ao_do_pandas(string texto, string esperado)
    {
        bool leu = ParserIso8601.TentarLer(texto, out DataLida data);
        Assert.Equal(esperado.Length > 0, leu);
        if (leu)
            Assert.Equal(esperado, Formatar(data));
    }

    // pd.to_datetime(texto, format=f) com f em FORMATOS_DATA_BR, na ordem; "" = NaT.
    [Theory]
    [InlineData("3/10/2026", "2026-10-03T00:00:00.0000000")]
    [InlineData("03/10/2026 8:05", "2026-10-03T08:05:00.0000000")]
    [InlineData("03/10/2026 08:05:7", "2026-10-03T08:05:07.0000000")]
    [InlineData("03/10/26", "")]
    [InlineData("03/10/2026 08:05:07.5", "")]
    [InlineData("03/10/2026T08:05", "")]
    [InlineData("03/10/2026  08:05", "2026-10-03T08:05:00.0000000")]
    [InlineData("003/10/2026", "")]
    [InlineData("03/10/2026 24:00", "")]
    [InlineData("31/02/2026", "")]
    [InlineData("03/10/02026", "")]
    [InlineData("03/10/2026 08:05:07 ", "")]
    [InlineData("3/1/2026 1:2:3", "2026-01-03T01:02:03.0000000")]
    [InlineData("03/10/2026 08:05 PM", "")]
    [InlineData("\u0660\u0663/10/2026", "")]
    [InlineData("03-10-2026", "")]
    [InlineData("03/10/2026 08:05:07.123", "")]
    [InlineData("03/10/2026 08:5", "2026-10-03T08:05:00.0000000")]
    [InlineData("03/10/0099", "")]
    [InlineData("29/02/2025", "")]
    [InlineData("03/10/2026 23:59:60", "2026-10-04T00:00:00.0000000")]
    public void Parser_dd_mm_igual_ao_strptime_do_pandas(string texto, string esperado)
    {
        DataLida? lida = null;
        foreach (var formato in ParserDataBarra.FormatosBr)
        {
            if (ParserDataBarra.TentarLer(texto, formato, out DataLida d))
            {
                lida = d;
                break;
            }
        }
        Assert.Equal(esperado, lida is { } valor ? Formatar(valor) : "");
    }

    // pd.to_datetime(texto, format=f) com f em FORMATOS_DATA_US: o %d aceita
    // " 5" (espaço + dígito), que no mm/dd aparece no meio do texto.
    [Theory]
    [InlineData("03/ 5/2026", "2026-03-05T00:00:00.0000000")]
    [InlineData("03/ 5/2026 08:00", "2026-03-05T08:00:00.0000000")]
    [InlineData("03/  5/2026", "")]
    public void Parser_mm_dd_aceita_dia_com_espaco_como_o_strptime(string texto, string esperado)
    {
        DataLida? lida = null;
        foreach (var formato in ParserDataBarra.FormatosUs)
        {
            if (ParserDataBarra.TentarLer(texto, formato, out DataLida d))
            {
                lida = d;
                break;
            }
        }
        Assert.Equal(esperado, lida is { } valor ? Formatar(valor) : "");
    }

    // pd.to_numeric(texto) entre 20.000 e 80.000 (texto já sem espaços nas pontas).
    [Theory]
    [InlineData("46083", true)]
    [InlineData("46083.", true)]
    [InlineData("46083.375", true)]
    [InlineData("46083,375", true)]
    [InlineData("+46083", true)]
    [InlineData("4.6083e4", true)]
    [InlineData("46083E0", true)]
    [InlineData(" 46083 ", true)]
    [InlineData("-46083", false)]
    [InlineData("46 083", false)]
    [InlineData("inf", false)]
    [InlineData("nan", false)]
    [InlineData("0x10", false)]
    [InlineData("46083d", false)]
    [InlineData("1_000", false)]
    [InlineData("\u0664\u0666\u0660\u0668\u0663", false)]
    [InlineData("19999", false)]
    [InlineData("80000", true)]
    public void Serial_do_excel_reconhecido_como_o_to_numeric(string texto, bool serial)
    {
        void Converter() => InterpretacaoDatas.ConverterPar([texto], ["2026-03-02 09:10:00"]);
        if (serial)
            Assert.Contains("serial do Excel", Assert.Throws<FormatoDeDataException>(Converter).Message);
        else
            Converter();
    }

    [Theory]
    [InlineData("03/10/2026 08:00")]
    [InlineData("lixo")]
    public void Fuso_numa_coluna_e_nenhuma_data_iso_na_outra_falha_como_o_python(string chegou)
    {
        // O motor Python quebra com o TypeError do pandas na subtração.
        var erro = Assert.Throws<FormatoDeDataException>(
            () => InterpretacaoDatas.ConverterPar([chegou], ["2026-10-03T11:20:00Z"]));
        Assert.Equal("Cannot subtract tz-naive and tz-aware datetime-like objects", erro.Message);
    }

    [Fact]
    public void Com_e_sem_fuso_que_o_regex_nao_ve_sao_rejeitados_pelo_iso()
    {
        // "20260302" e "2026" não parecem data para o regex, mas o ISO lê sem fuso.
        foreach (string semFuso in new[] { "20260302", "2026" })
        {
            var erro = Assert.Throws<FormatoDeDataException>(() => InterpretacaoDatas.ConverterPar(
                ["2026-03-02T09:00Z", semFuso], ["2026-03-02T09:10Z", "2026-03-02T09:10Z"]));
            Assert.Equal("A coluna Chegou_em mistura fusos horarios diferentes. Padronize o fuso da base antes de processar.",
                erro.Message);
        }
    }

    [Fact]
    public void Offsets_iguais_escritos_de_formas_diferentes_sao_o_mesmo_fuso()
    {
        // Z e +00:00 numa coluna, -03:00 e -0300 na outra: um fuso por coluna,
        // como no pandas. Offsets diferentes entre as colunas são aceitos.
        var datas = InterpretacaoDatas.ConverterPar(
            ["2026-03-02T09:00Z", "2026-03-02T10:00+00:00"],
            ["2026-03-02T06:30-03:00", "2026-03-02T07:30-0300"]);
        Assert.All(datas.ChegouEm, d => Assert.Equal(TimeSpan.Zero, d!.Value.Offset));
        Assert.All(datas.FinalizadaEm, d => Assert.Equal(TimeSpan.FromHours(-3), d!.Value.Offset));
        Assert.Equal(1800.0, Classificacao.TempoEmSegundos(datas.ChegouEm[0], datas.FinalizadaEm[0]));
    }

    [Fact]
    public void Data_nativa_do_excel_misturada_com_texto_com_fuso_e_rejeitada()
    {
        var erro = Assert.Throws<FormatoDeDataException>(() => InterpretacaoDatas.ConverterPar(
            [new DateTime(2026, 3, 2, 9, 0, 0), "2026-03-02T10:00Z", "2026-03-02T11:00Z"],
            ["2026-03-02T09:10Z", "2026-03-02T10:10Z", "2026-03-02T11:10Z"]));
        Assert.StartsWith("A coluna Chegou_em mistura horarios com fuso (Z ou +-hh:mm, 2 linhas) e sem fuso (1 linhas). Linhas sem fuso: 2.",
            erro.Message);
    }

    [Fact]
    public void Coluna_so_de_datas_nativas_passa_direto()
    {
        var datas = InterpretacaoDatas.ConverterPar(
            [new DateTime(2026, 3, 2, 9, 0, 0), null],
            [new DateTime(2026, 3, 2, 9, 10, 0), new DateTime(2026, 3, 2, 9, 10, 0)]);
        Assert.Equal(600.0, Classificacao.TempoEmSegundos(datas.ChegouEm[0], datas.FinalizadaEm[0]));
        Assert.Null(datas.ChegouEm[1]);
    }

    [Fact]
    public void Now_e_today_ficam_nulos_divergencia_registrada()
    {
        // O pandas troca "now"/"today" pelo relógio da máquina; o C# não lê.
        var datas = InterpretacaoDatas.ConverterPar(["now", " today "], ["2026-03-02 09:10", "2026-03-02 09:10"]);
        Assert.All(datas.ChegouEm, d => Assert.Null(d));
    }

    // Valores de transform_base do Python (Cod_Cliente depois da normalização).
    [Theory]
    [InlineData("00123", "00123")]
    [InlineData(" 123 ", "123")]
    [InlineData("null", null)]
    [InlineData("NULL", "NULL")]
    [InlineData("None", null)]
    [InlineData("nan", null)]
    [InlineData("NaN", "NaN")]
    [InlineData("<NA>", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\u001C123\u001F", "123")]
    [InlineData("\u00A0A\u00A0", "A")]
    [InlineData(null, null)]
    public void Codigo_do_cliente_normalizado_como_o_python(string? bruto, string? esperado)
        => Assert.Equal(esperado, CodigoCliente.Normalizar(bruto));

    [Fact]
    public void Codigos_ordenados_por_code_point_como_o_python()
    {
        // Python: "\uFFFD" < "\U0001F600"; o ordinal UTF-16 do .NET inverte.
        string fffd = "\uFFFD", emoji = char.ConvertFromUtf32(0x1F600);
        Assert.True(string.CompareOrdinal(emoji, fffd) < 0);
        Assert.True(TextoPython.OrdemCodePoint.Compare(fffd, emoji) < 0);

        var registros = new[] { emoji, fffd, "b", "a" }
            .Select(c => new RegistroTempo(c, DateTimeOffset.UnixEpoch, 600)).ToList();
        var tempos = TemposPorCliente.Calcular(registros, new ParametrosTempos(minimoApontamentos: 1));
        Assert.Equal(["a", "b", fffd, emoji], tempos.Select(t => t.CodCliente));
    }

    private static string Formatar(DataLida data)
    {
        var dto = data.ParaDateTimeOffset();
        string texto = dto.DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
        return data.OffsetMinutos is null ? texto : texto + dto.ToString("zzz", CultureInfo.InvariantCulture);
    }

    private static void ConferirData(string esperado, DateTimeOffset? obtido, string contexto)
    {
        if (esperado.Length == 0)
        {
            Assert.True(obtido is null, $"{contexto}: esperado nulo, obtido {obtido:o}");
            return;
        }
        string[] formatos = ["yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", "yyyy-MM-dd'T'HH:mm:ss.ffffff"];
        var data = DateTimeOffset.ParseExact(esperado, formatos, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        Assert.True(obtido is not null && data.EqualsExact(obtido.Value), $"{contexto}: esperado {data:o}, obtido {obtido:o}");
    }
}
