using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Luna.Domain.Tests;

/// <summary>
/// Leitura do contrato matemático gerado pelo motor Python
/// (tests/dados/contrato, tag luna-py-ref-v1.1). Formato: ';', UTF-8, LF,
/// ponto decimal, float na forma mais curta que volta ao mesmo double.
/// </summary>
internal static class Contrato
{
    private static readonly Lazy<string> Pasta = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidato = Path.Combine(dir.FullName, "tests", "dados", "contrato");
            if (File.Exists(Path.Combine(candidato, "MANIFEST.json")))
                return candidato;
        }
        throw new DirectoryNotFoundException("tests/dados/contrato não encontrado acima da pasta dos testes.");
    });

    public static string Caminho(string relativo) => Path.Combine(Pasta.Value, relativo);

    /// <summary>Linhas de dados (sem o cabeçalho), já separadas por ';'.</summary>
    public static IEnumerable<string[]> Linhas(string relativo)
        => File.ReadAllText(Caminho(relativo), Encoding.UTF8)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(linha => linha.Split(';'));

    public static double Double(string texto)
        => double.Parse(texto, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static double[] Valores(string texto)
        => texto.Split('|').Select(Double).ToArray();

    public static IReadOnlyDictionary<string, string> HashesDoManifest()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Caminho("MANIFEST.json"), Encoding.UTF8));
        return json.RootElement.GetProperty("sha256").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    public static string Sha256(string relativo)
        => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Caminho(relativo))));

    /// <summary>Igualdade bit a bit: distingue 0.0 de -0.0 e não aceita 1 ulp.</summary>
    public static void MesmoDouble(double esperado, double obtido, string contexto)
    {
        if (BitConverter.DoubleToInt64Bits(esperado) != BitConverter.DoubleToInt64Bits(obtido))
        {
            throw new Xunit.Sdk.XunitException(string.Create(CultureInfo.InvariantCulture,
                $"{contexto}: esperado {esperado:R}, obtido {obtido:R}"));
        }
    }
}
