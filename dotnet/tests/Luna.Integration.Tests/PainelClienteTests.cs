using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Luna.Application;
using Luna.Domain;
using Luna.Infrastructure;

namespace Luna.Integration.Tests;

/// <summary>
/// Painel do cliente contra a referência Python: dados/painel/esperado.json,
/// gerado por dados/painel/gerar_painel.py com o montar_painel_cliente() do
/// motor. Compara o painel inteiro (resumo, janela, tabela e os dados dos dois
/// gráficos) e as funções isoladas que ele usa.
/// </summary>
public class PainelClienteTests
{
    private static readonly ProcessarBase Caso = new(new CarregadorDeArquivo());
    private static readonly string Dados = Path.Combine(AppContext.BaseDirectory, "dados");

    private static readonly Lazy<JsonElement> Esperado = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Dados, "painel", "esperado.json"))).RootElement);

    private static readonly Dictionary<string, ResultadoProcessamento> Resultados = [];

    public static TheoryData<int> Paineis => new(Enumerable.Range(0, Esperado.Value.GetProperty("paineis").GetArrayLength()));

    [Theory]
    [MemberData(nameof(Paineis))]
    public void Painel_igual_ao_python(int indice)
    {
        var caso = Esperado.Value.GetProperty("paineis")[indice];
        var resultado = Resultado(caso.GetProperty("base").GetString()!, caso.GetProperty("params"));
        var painel = PaineisDeCliente.Montar(resultado, caso.GetProperty("cliente").GetString()!,
            caso.GetProperty("dia").GetString()!, caso.GetProperty("rotulos").GetBoolean());

        var esperado = JsonNode.Parse(caso.GetProperty("painel").GetRawText())!.AsObject();
        var obtido = Canonico(painel);
        // Empates de minuto no gráfico de aberturas: o detalhe é comparado por grupo (ver PaineisDeCliente).
        NormalizarEmpates(esperado);
        NormalizarEmpates(obtido);
        string? onde = Diferenca(esperado, obtido, "");
        Assert.True(onde is null, $"{caso.GetProperty("base")} {caso.GetProperty("params")} cliente='{caso.GetProperty("cliente")}' dia='{caso.GetProperty("dia")}': {onde}");
    }

    [Fact]
    public void Clientes_disponiveis_iguais_ao_python()
    {
        foreach (var base_ in Esperado.Value.GetProperty("clientes_disponiveis").EnumerateObject())
        {
            var resultado = Resultado(base_.Name, JsonDocument.Parse("{}").RootElement);
            Assert.Equal(base_.Value.EnumerateArray().Select(e => e.GetString()), PaineisDeCliente.ClientesDisponiveis(resultado));
        }
    }

    [Fact]
    public void Numero_de_texto_como_o_python()
    {
        foreach (var caso in Esperado.Value.GetProperty("dominio").GetProperty("numero_de_texto").EnumerateArray())
        {
            string texto = caso[0].GetString()!;
            var esperado = caso[1].EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.True(esperado.SequenceEqual(Canon(Volumes.NumeroDeTexto(texto))), $"'{texto}': esperado [{string.Join(",", esperado)}]");
        }
    }

    [Fact]
    public void Formatar_numero_como_o_python()
    {
        foreach (var caso in Esperado.Value.GetProperty("dominio").GetProperty("formatar_numero").EnumerateArray())
        {
            double valor = double.Parse(caso[0].GetString()!, CultureInfo.InvariantCulture);
            Assert.Equal(caso[1].GetString(), Volumes.FormatarNumero(valor));
        }
        Assert.Equal("0", Volumes.FormatarNumero(null));
        Assert.Equal("0", Volumes.FormatarNumero(double.NaN));
    }

    [Fact]
    public void Media_como_o_pandas()
    {
        foreach (var caso in Esperado.Value.GetProperty("dominio").GetProperty("media").EnumerateArray())
        {
            var valores = caso.GetProperty("valores").EnumerateArray()
                .Select(v => double.Parse(v.GetString()!, CultureInfo.InvariantCulture)).ToList();
            double media = Volumes.SomaNumpy(valores) / valores.Count;
            Assert.Equal(caso.GetProperty("media").GetString(), TextoPython.ReprFloat(media));
        }
    }

    [Fact]
    public void Ordem_de_datetime64_como_o_pandas()
    {
        foreach (var caso in Esperado.Value.GetProperty("dominio").GetProperty("ordem_datetime64").EnumerateArray())
        {
            var chaves = caso.GetProperty("chaves").EnumerateArray().Select(c => c.GetInt64()).ToList();
            Assert.Equal(caso.GetProperty("ordem").EnumerateArray().Select(c => c.GetInt32()), OrdenacaoNumpy.ArgsortQuicksort(chaves));
        }
    }

    [Fact]
    public void Volume_numerico_invalido_e_vazio()
    {
        // Coluna de texto: vazio é nulo; inválido conta como zero real (fillna(0).eq(0) do motor).
        var resumo = Volumes.Normalizar(["10,5", null, "abc", "0", "1.234,5"], numerica: false);
        Assert.Equal([10.5, 0.0, 0.0, 0.0, 1234.5], resumo.Numeros);
        Assert.Equal((5, 1, 2, 1, 3), (resumo.QtdTotal, resumo.QtdNulosOrigem, resumo.QtdZerosReais, resumo.QtdInvalidosConvertidos, resumo.QtdValidos));
        Assert.Equal((10.5 + 0 + 1234.5) / 3, resumo.MediaValidos);
        // Coluna numérica: só o nulo sai.
        var numerica = Volumes.Normalizar([3L, null, 4.5, BigInteger.One], numerica: true);
        Assert.Equal([3.0, 0.0, 4.5, 1.0], numerica.Numeros);
        Assert.Equal(0, numerica.QtdInvalidosConvertidos);
    }

    [Fact]
    public void Painel_nao_muda_o_resultado()
    {
        var resultado = Caso.Executar(new ParametrosProcessamento(Repositorio.Caminho("TESTE_data.csv")));
        var antes = resultado.Saidas.Select(t => t.Linhas().Select(l => l.ToArray()).ToList()).ToList();
        foreach (string cliente in PaineisDeCliente.ClientesDisponiveis(resultado))
        {
            foreach (string dia in PaineisDeCliente.OpcoesDiaSemana)
                PaineisDeCliente.Montar(resultado, cliente, dia);
        }
        var depois = resultado.Saidas.Select(t => t.Linhas().Select(l => l.ToArray()).ToList()).ToList();
        Assert.Equal(antes.Count, depois.Count);
        for (int k = 0; k < antes.Count; k++)
            Assert.Equal(antes[k], depois[k]);
    }

    private static ResultadoProcessamento Resultado(string base_, JsonElement p)
    {
        string chave = base_ + p.GetRawText();
        lock (Resultados)
        {
            if (Resultados.TryGetValue(chave, out var r))
                return r;
            string caminho = base_.StartsWith("tests/dados/", StringComparison.Ordinal)
                ? Repositorio.Caminho(base_["tests/dados/".Length..].Split('/'))
                : Path.Combine([Dados, .. base_.Split('/')]);
            int I(string n, int d) => p.TryGetProperty(n, out var v) ? v.GetInt32() : d;
            r = Caso.Executar(new ParametrosProcessamento(caminho,
                eventosPrevios: I("eventos_previos", 10), minimoApontamentos: I("minimo_apontamentos", 4),
                ajustePercentual: p.TryGetProperty("ajuste_percentual", out var a) ? a.GetDouble() : 0.0,
                exibirJanelasEntrega: !p.TryGetProperty("exibir_janelas_entrega", out var x) || x.GetBoolean(),
                coberturaJanela: I("cobertura_janela", 80),
                baseJanela: p.TryGetProperty("base_janela", out var b) ? b.GetString()! : "Chegou_em"));
            Resultados[chave] = r;
            return r;
        }
    }

    // Mesma forma do painel_canonico() de gerar_painel.py.
    private static JsonObject Canonico(PainelCliente p)
    {
        var o = new JsonObject { ["cliente"] = p.Cliente, ["qtd_entregas"] = p.QtdEntregas, ["vazio"] = p.Vazio };
        if (p.DiaSemana is not null)
            o["dia_semana"] = p.DiaSemana;
        if (p.Resumo is not null)
            o["resumo"] = Objeto(ResumoCliente.Campos.Select(c => (c.Chave, (object?)c.Valor(p.Resumo))));
        if (p.Vazio)
            return o;
        o["janela"] = p.Janela is null ? null : Objeto(JanelasDeAtendimento.Campos.Select(c => (c.Coluna, c.Valor(p.Janela))));
        o["tabela"] = new JsonObject
        {
            ["colunas"] = new JsonArray(p.Tabela!.Colunas.Select(c => (JsonNode?)c).ToArray()),
            ["linhas"] = new JsonArray(p.Tabela.Linhas.Select(l => (JsonNode?)Lista(l)).ToArray()),
        };
        var g = p.Grafico!;
        o["grafico"] = new JsonObject
        {
            ["x"] = Lista(g.X.Cast<object?>()),
            ["marcas_x"] = Lista(g.MarcasX.Cast<object?>()),
            ["marcas_texto"] = Lista(g.MarcasTexto),
            ["faixa"] = Lista([g.FaixaInicio, g.FaixaFim]),
            ["volume"] = Lista(g.Volume.Cast<object?>()),
            ["volume_texto"] = Lista(g.VolumeTexto),
            ["volume_detalhe"] = new JsonArray(g.VolumeDetalhe.Select(d => (JsonNode?)Lista([d.Data, d.DataHora])).ToArray()),
            ["tempo"] = Lista(g.Tempo.Cast<object?>()),
            ["tempo_texto"] = g.TempoTexto is null ? null : Lista(g.TempoTexto),
            ["tempo_detalhe"] = new JsonArray(g.TempoDetalhe.Select(d => (JsonNode?)Lista([d.Data, d.DataHora, d.Tempo])).ToArray()),
            ["mediana"] = Referencia(g.Mediana),
            ["q1"] = Referencia(g.TempoIdealQ1),
        };
        var a = p.GraficoAberturas;
        o["grafico_aberturas"] = a is null ? null : new JsonObject
        {
            ["x"] = Lista(a.X.Cast<object?>()),
            ["y"] = Lista(a.Y.Cast<object?>()),
            ["texto"] = Lista(a.Texto),
            ["detalhe"] = new JsonArray(a.Detalhe.Select(d => (JsonNode?)Lista([d.DataHora, d.Tempo])).ToArray()),
            ["faixas"] = new JsonArray(a.Faixas.Select(f => (JsonNode?)Lista([f.Inicio, f.Fim])).ToArray()),
            ["linhas"] = Lista(a.Linhas.Cast<object?>()),
        };
        return o;
    }

    private static JsonObject? Referencia(LinhaDeReferencia? r)
        => r is null ? null : new JsonObject { ["nome"] = r.Nome, ["valor"] = CanonNo(r.Valor) };

    private static JsonObject Objeto(IEnumerable<(string Chave, object? Valor)> campos)
        => new(campos.Select(c => KeyValuePair.Create(c.Chave, (JsonNode?)CanonNo(c.Valor))));

    private static JsonArray Lista(IEnumerable<object?> valores) => new(valores.Select(v => (JsonNode?)CanonNo(v)).ToArray());

    private static JsonArray CanonNo(object? v) => new(Canon(v).Select(s => (JsonNode?)s).ToArray());

    private static List<string> Canon(object? v)
    {
        var ci = CultureInfo.InvariantCulture;
        return v switch
        {
            null => ["nulo"],
            bool b => ["bool", b ? "True" : "False"],
            int n => ["int", n.ToString(ci)],
            long n => ["int", n.ToString(ci)],
            BigInteger g => ["int", g.ToString(ci)],
            double d => double.IsNaN(d) ? ["nulo"] : ["float", TextoPython.ReprFloat(d)],
            string s => ["str", s],
            _ => [$"tipo inesperado {v.GetType().Name}"],
        };
    }

    // Ordena o detalhe dentro de cada grupo de minutos iguais (só ele pode variar nos empates).
    private static void NormalizarEmpates(JsonObject painel)
    {
        if (painel["grafico_aberturas"] is not JsonObject a)
            return;
        var x = a["x"]!.AsArray().Select(n => n!.ToJsonString()).ToList();
        var detalhe = a["detalhe"]!.AsArray().Select(n => n!.ToJsonString()).ToList();
        var normal = new List<string>(detalhe.Count);
        for (int i = 0; i < x.Count;)
        {
            int j = i;
            while (j < x.Count && x[j] == x[i])
                j++;
            normal.AddRange(detalhe.GetRange(i, j - i).Order(StringComparer.Ordinal));
            i = j;
        }
        a["detalhe"] = new JsonArray(normal.Select(s => JsonNode.Parse(s)).ToArray());
    }

    private static string? Diferenca(JsonNode? a, JsonNode? b, string caminho)
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var chave in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
            {
                if (!oa.ContainsKey(chave) || !ob.ContainsKey(chave))
                    return $"{caminho}/{chave}: só em {(oa.ContainsKey(chave) ? "Python" : "C#")}";
                if (Diferenca(oa[chave], ob[chave], $"{caminho}/{chave}") is { } d)
                    return d;
            }
            return null;
        }
        if (a is JsonArray la && b is JsonArray lb && !(la.Count > 0 && la[0] is JsonValue))
        {
            if (la.Count != lb.Count)
                return $"{caminho}: {la.Count} itens no Python, {lb.Count} no C#";
            for (int i = 0; i < la.Count; i++)
            {
                if (Diferenca(la[i], lb[i], $"{caminho}[{i}]") is { } d)
                    return d;
            }
            return null;
        }
        return JsonNode.DeepEquals(a, b) ? null : $"{caminho}: Python {a?.ToJsonString()} C# {b?.ToJsonString()}";
    }
}
