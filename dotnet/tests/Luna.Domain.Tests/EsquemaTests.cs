namespace Luna.Domain.Tests;

/// <summary>Aliases e esquema de colunas contra src/schema.py (valores do Python).</summary>
public class EsquemaTests
{
    // normalize_text | slugify_column | get_standard_name do Python.
    [Theory]
    [InlineData("Código do Cliente", "codigo do cliente", "codigo_do_cliente", "Cod_Cliente")]
    [InlineData("  CHEGOU EM ", "chegou em", "chegou_em", "Chegou_em")]
    [InlineData("Data/Hora Chegada", "data/hora chegada", "data_hora_chegada", "Chegou_em")]
    [InlineData("\uFEFFcod_cliente", "cod_cliente", "cod_cliente", "Cod_Cliente")]
    [InlineData("Data-Finalização", "data-finalizacao", "data_finalizacao", "Finalizada_em")]
    [InlineData("Finished At", "finished at", "finished_at", "Finalizada_em")]
    [InlineData("customer code", "customer code", "customer_code", "Cod_Cliente")]
    [InlineData("Veículo", "veiculo", "veiculo", "Veiculo")]
    [InlineData("lng", "lng", "lng", "Longitude")]
    [InlineData("ID do Tour", "id do tour", "id_do_tour", "tour_display_id")]
    [InlineData("Dentro do Raio", "dentro do raio", "dentro_do_raio", "within_radius")]
    [InlineData("Coluna Extra", "coluna extra", "coluna_extra", "Coluna Extra")]
    [InlineData("\u0130stanbul \u00DCn\u00EFcode", "istanbul unicode", "istanbul_unicode", "\u0130stanbul \u00DCn\u00EFcode")]
    [InlineData("Stra\u00DFe", "stra\u00DFe", "strae", "Stra\u00DFe")]
    [InlineData("data\u00A0chegada", "data chegada", "data_chegada", "Chegou_em")]
    [InlineData("data\u001Cchegada", "data chegada", "data_chegada", "Chegou_em")]
    [InlineData("Cliente", "cliente", "cliente", "Cod_Cliente")]
    public void Normalizacao_slug_e_alias_iguais_ao_python(string coluna, string normalizado, string slug, string padrao)
    {
        Assert.Equal(normalizado, EsquemaColunas.NormalizarTexto(coluna));
        Assert.Equal(slug, EsquemaColunas.Slug(coluna));
        Assert.Equal(padrao, EsquemaColunas.NomePadrao(coluna));
    }

    [Fact]
    public void Sigma_final_difere_no_texto_mas_nao_no_nome_padrao()
    {
        // lower() do Python faz o sigma final (\u03C2); o ToLowerInvariant do .NET
        // não (\u03C3). O nome padrão é o mesmo: nenhum alias alcançável tem letra
        // fora do ASCII depois da normalização.
        string coluna = "\u03A3\u039F\u03A6\u039F\u03A3";
        Assert.Equal("\u03C3\u03BF\u03C6\u03BF\u03C3", EsquemaColunas.NormalizarTexto(coluna));  // Python: ...\u03C2
        Assert.Equal("", EsquemaColunas.Slug(coluna));
        Assert.Equal(coluna, EsquemaColunas.NomePadrao(coluna));
        Assert.All(EsquemaColunas.Aliases.Keys.Where(k => k == EsquemaColunas.NormalizarTexto(k)),
            k => Assert.True(k.All(c => c < 128), k));
    }

    [Fact]
    public void Minusculas_seguem_o_python_3_10()
    {
        // \u0130: mapeamento completo do Python (i + U+0307), que muda o alias.
        Assert.Equal("i\u0307d do tour", TextoPython.Minusculas("\u0130D do Tour"));
        Assert.Equal("tour_display_id", EsquemaColunas.NomePadrao("\u0130D do Tour"));
        // Casings do Unicode 14 ficam como no Python 3.10 (Unicode 13).
        Assert.Equal("\u2C2F\uA7C0", TextoPython.Minusculas("\u2C2F\uA7C0"));
        string vithkuqi = char.ConvertFromUtf32(0x10570);
        Assert.Equal(vithkuqi, TextoPython.Minusculas(vithkuqi));
        // O resto segue o ToLowerInvariant.
        Assert.Equal("ç\u03B1\u0434", TextoPython.Minusculas("Ç\u0391\u0414"));
    }

    [Fact]
    public void Padronizar_e_analisar_iguais_ao_python()
    {
        string[] colunas = ["Cliente", "Chegou em", "Finalizada_em", "cod_cliente", "Motorista", "Extra", "CLIENTE"];
        var padronizadas = EsquemaColunas.Padronizar(colunas);
        Assert.Equal(["Cod_Cliente", "Chegou_em", "Finalizada_em", "Cod_Cliente__dup1", "Motorista", "Extra", "Cod_Cliente__dup2"],
            padronizadas);

        var relatorio = EsquemaColunas.Analisar(padronizadas);
        Assert.True(relatorio.Valido);
        Assert.Equal(["Cod_Cliente", "Chegou_em", "Finalizada_em"], relatorio.ObrigatoriasEncontradas);
        Assert.Empty(relatorio.ObrigatoriasFaltando);
        Assert.Equal(["Motorista"], relatorio.OpcionaisEncontradas);
        Assert.Equal(["Cod_Cliente__dup1", "Extra", "Cod_Cliente__dup2"], relatorio.ColunasDesconhecidas);
        Assert.Equal(["Cod_Cliente"], relatorio.ColunasDuplicadas);
    }

    // Nome original repetido (o Excel não renomeia como o read_csv): o
    // rename_map do Python é por nome original, então o último sufixo vale
    // para todas as cópias. Esperados de standardize_columns/analyze_schema.
    [Fact]
    public void Nome_original_repetido_recebe_o_ultimo_sufixo_em_todas_as_copias()
    {
        Assert.Equal(["Motorista__dup1", "Cod_Cliente", "Chegou_em", "Motorista__dup1", "Finalizada_em"],
            EsquemaColunas.Padronizar(["Motorista", "cod_cliente", "arrived_at", "Motorista", "finalizada em"]));

        var padronizadas = EsquemaColunas.Padronizar(["cod_cliente", "Cod_Cliente", "cod_cliente", "Chegou_em", "Finalizada_em"]);
        Assert.Equal(["Cod_Cliente__dup2", "Cod_Cliente__dup1", "Cod_Cliente__dup2", "Chegou_em", "Finalizada_em"], padronizadas);
        var relatorio = EsquemaColunas.Analisar(padronizadas);
        Assert.True(relatorio.Valido);
        Assert.Equal(["Cod_Cliente__dup2", "Cod_Cliente__dup1", "Cod_Cliente__dup2"], relatorio.ColunasDesconhecidas);
        Assert.Equal(["Cod_Cliente"], relatorio.ColunasDuplicadas);
    }

    [Fact]
    public void Esquema_sem_obrigatorias_e_invalido()
    {
        var relatorio = EsquemaColunas.Analisar(EsquemaColunas.Padronizar(["Cliente", "Outra"]));
        Assert.False(relatorio.Valido);
        Assert.Equal(["Chegou_em", "Finalizada_em"], relatorio.ObrigatoriasFaltando);
        Assert.Equal(["Outra"], relatorio.ColunasDesconhecidas);
    }

    [Fact]
    public void Strip_do_python_remove_separadores_que_o_trim_mantem()
    {
        Assert.Equal("123", TextoPython.Strip("\u001C123\u001F"));
        Assert.Equal("\u001C123\u001F", "\u001C123\u001F".Trim());
    }
}
