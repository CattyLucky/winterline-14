using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.ContentPack;
using Content.Shared.Dataset;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Localization;

[TestFixture]
public sealed class LocalizedDatasetPrototypeTest : GameTest
{
    private static readonly Regex LocEntryRegex = new(@"^\s*([A-Za-z0-9][A-Za-z0-9\-_.]*)\s*=",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Test]
    public async Task ValidProtoIdsTest()
    {
        var pair = Pair;

        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var localizationMan = server.ResolveDependency<ILocalizationManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        var protos = protoMan.EnumeratePrototypes<LocalizedDatasetPrototype>().OrderBy(p => p.ID);
        var localizedIds = LoadLocalizationIds(resources);

        Assert.Multiple(() =>
        {
            // Check each prototype
            foreach (var proto in protos)
            {
                var maxIndex = GetMaxIndexForPrefix(proto.Values.Prefix, localizedIds);
                Assert.That(maxIndex, Is.GreaterThan(0),
                    $"LocalizedDataset {proto.ID} with prefix \"{proto.Values.Prefix}\" has no matching localization entries.");

                // WL Change: compare with actual max suffix in localization instead of one-step +1 guessing.
                Assert.That(proto.Values.Count, Is.EqualTo(maxIndex),
                    $"LocalizedDataset {proto.ID} with prefix \"{proto.Values.Prefix}\" specifies {proto.Values.Count} entries, but highest localized ID is {proto.Values.Prefix}{maxIndex}.");

                // Check each value in the prototype
                foreach (var locId in proto.Values)
                {
                    // Make sure the localization manager has a string for the LocId
                    Assert.That(localizationMan.HasString(locId), $"LocalizedDataset {proto.ID} with prefix \"{proto.Values.Prefix}\" specifies {proto.Values.Count} entries, but no localized string was found matching {locId}!");
                }
            }
        });
    }

    private static HashSet<string> LoadLocalizationIds(IResourceManager resources)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in resources.ContentFindFiles(new ResPath("/Locale")))
        {
            if (!file.ToString().EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
                continue;

            using var reader = resources.ContentFileReadText(file);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var match = LocEntryRegex.Match(line);
                if (!match.Success)
                    continue;

                ids.Add(match.Groups[1].Value);
            }
        }

        return ids;
    }

    private static int GetMaxIndexForPrefix(string prefix, HashSet<string> localizedIds)
    {
        var max = 0;

        foreach (var id in localizedIds)
        {
            if (!id.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var suffix = id[prefix.Length..];
            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                continue;

            if (index > max)
                max = index;
        }

        return max;
    }
}
