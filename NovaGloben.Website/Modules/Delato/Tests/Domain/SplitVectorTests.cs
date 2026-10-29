using System.Globalization;
using System.Text.Json;
using Delato.Shared.Domain;
using Xunit;

namespace Delato.Tests.Domain;

/// <summary>
/// Runs every case in Tests/split-test-vectors.json (copied to test output as
/// 'split-test-vectors.json') through SplitCalculator.Compute and asserts the
/// per-member USD amounts match exactly.
/// </summary>
public class SplitVectorTests
{
    private sealed record VectorCase(
        string Name,
        decimal Amount,
        decimal FxRateToUsd,
        SplitMode Mode,
        List<SplitTargetInput> Targets,
        Dictionary<int, IReadOnlyList<int>> Entities,
        Dictionary<int, decimal> ExpectedUsd);

    private static readonly Lazy<Dictionary<string, VectorCase>> Cases = new(LoadCases);

    private static Dictionary<string, VectorCase> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "split-test-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new Dictionary<string, VectorCase>();

        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = c.GetProperty("name").GetString()!;
            var amount = ParseDec(c.GetProperty("amount").GetString()!);
            var fx = ParseDec(c.GetProperty("fxRateToUsd").GetString()!);
            var mode = Enum.Parse<SplitMode>(c.GetProperty("splitMode").GetString()!);

            var targets = new List<SplitTargetInput>();
            foreach (var t in c.GetProperty("targets").EnumerateArray())
            {
                var type = Enum.Parse<TargetType>(t.GetProperty("type").GetString()!);
                var id = t.GetProperty("id").GetInt32();
                decimal? value = t.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                    ? ParseDec(v.GetString()!)
                    : null;
                targets.Add(new SplitTargetInput(type, id, value));
            }

            var entities = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var e in c.GetProperty("entities").EnumerateObject())
            {
                entities[int.Parse(e.Name, CultureInfo.InvariantCulture)] =
                    e.Value.EnumerateArray().Select(x => x.GetInt32()).ToList();
            }

            var expected = new Dictionary<int, decimal>();
            foreach (var e in c.GetProperty("expectedUsd").EnumerateObject())
                expected[int.Parse(e.Name, CultureInfo.InvariantCulture)] = ParseDec(e.Value.GetString()!);

            result[name] = new VectorCase(name, amount, fx, mode, targets, entities, expected);
        }
        return result;
    }

    private static decimal ParseDec(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Value.Keys) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Vector_ProducesExactPerMemberUsdAmounts(string caseName)
    {
        var c = Cases.Value[caseName];

        var shares = SplitCalculator.Compute(c.Amount, c.FxRateToUsd, c.Mode, c.Targets, c.Entities);

        var actual = shares.ToDictionary(s => s.MemberId, s => s.AmountUsd);
        Assert.Equal(c.ExpectedUsd.Count, actual.Count);
        foreach (var (memberId, expectedUsd) in c.ExpectedUsd)
        {
            Assert.True(actual.TryGetValue(memberId, out var actualUsd),
                $"{caseName}: member {memberId} missing from computed shares");
            Assert.True(expectedUsd == actualUsd,
                $"{caseName}: member {memberId} expected {expectedUsd} but got {actualUsd}");
        }
    }

    [Fact]
    public void VectorFile_HasCases()
    {
        Assert.NotEmpty(Cases.Value);
    }
}
