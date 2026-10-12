using System.Globalization;

namespace Luna.Integration.Tests;

/// <summary>Arquivos do repositório usados como referência (tests/dados).</summary>
internal static class Repositorio
{
    private static readonly Lazy<string> Dados = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidato = Path.Combine(dir.FullName, "tests", "dados");
            if (File.Exists(Path.Combine(candidato, "contrato", "MANIFEST.json")))
                return candidato;
        }
        throw new DirectoryNotFoundException("tests/dados não encontrado acima da pasta dos testes.");
    });

    public static string Caminho(params string[] partes) => Path.Combine([Dados.Value, .. partes]);

    /// <summary>CSV com ';' gerado pelo pandas: cabeçalho + linhas de campos.</summary>
    public static (string[] Cabecalho, List<string[]> Linhas) LerCsv(string caminho)
    {
        var linhas = File.ReadAllLines(caminho).Select(l => l.TrimStart('\uFEFF').Split(';')).ToList();
        return (linhas[0], linhas.Skip(1).ToList());
    }

    public static double Double(string texto) => double.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// Compara uma tabela C# (campos na ordem) com o CSV do Python: mesmo
    /// cabeçalho, mesma ordem de linhas, inteiros e textos iguais, doubles bit a bit.
    /// </summary>
    public static void ConferirTabela<T>(
        string caminhoCsv, IReadOnlyList<T> obtido,
        IReadOnlyList<(string Coluna, Func<T, object?> Valor)> campos, string contexto)
    {
        var (cabecalho, linhas) = LerCsv(caminhoCsv);
        Assert.Equal(campos.Select(c => c.Coluna), cabecalho);
        Assert.Equal(linhas.Count, obtido.Count);
        for (int i = 0; i < obtido.Count; i++)
        {
            for (int j = 0; j < campos.Count; j++)
            {
                string esperado = linhas[i][j];
                string onde = $"{contexto} {linhas[i][0]} {campos[j].Coluna}";
                switch (campos[j].Valor(obtido[i]))
                {
                    case double d:
                        Assert.True(BitConverter.DoubleToInt64Bits(Double(esperado)) == BitConverter.DoubleToInt64Bits(d),
                            $"{onde}: esperado {esperado}, obtido {d:R}");
                        break;
                    case long n:
                        Assert.True(long.Parse(esperado, CultureInfo.InvariantCulture) == n, $"{onde}: esperado {esperado}, obtido {n}");
                        break;
                    case string s:
                        Assert.True(esperado == s, $"{onde}: esperado '{esperado}', obtido '{s}'");
                        break;
                    case null:
                        Assert.True(esperado.Length == 0, $"{onde}: esperado '{esperado}', obtido nulo");
                        break;
                }
            }
        }
    }
}
