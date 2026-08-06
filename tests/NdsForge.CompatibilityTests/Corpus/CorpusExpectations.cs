using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NdsForge.CompatibilityTests.Corpus;

/// <summary>Loads committed expectations and resolves private fixtures by content, making local filenames irrelevant.</summary>
internal static class CorpusExpectations
{
    /// <summary>Defines the environment switch that makes an absent or partial legal-dump corpus a test failure instead of a skip.</summary>
    private const string RequireVariable = "NDSFORGE_REQUIRE_CORPUS";
    /// <summary>Defines the user-supplied root searched recursively for candidate Nintendo DS images.</summary>
    private const string RootVariable = "NDSFORGE_CORPUS";
    /// <summary>Supports enum names in the checked-in, human-reviewable JSON rather than brittle numeric values.</summary>
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    /// <summary>Avoids repeatedly hashing multi-gigabyte fixture trees across theory discovery and execution.</summary>
    private static readonly Lazy<Dictionary<string, string>> FixturesByHash = new(DiscoverFixtures);

    /// <summary>Returns every expectation as independent theory data so one bad dump does not hide the remaining cases.</summary>
    public static TheoryData<CorpusExpectationIndexEntry> Cases
    {
        get
        {
            var data = new TheoryData<CorpusExpectationIndexEntry>();
            foreach (CorpusExpectationIndexEntry entry in ReadIndex().Cases)
            {
                data.Add(entry);
            }

            return data;
        }
    }

    /// <summary>Reads and schema-checks one payload-free document selected by the trusted public index.</summary>
    public static CorpusExpectation Read(CorpusExpectationIndexEntry entry)
    {
        string path = Path.Combine(GetExpectationRoot(), entry.ExpectationFile);
        CorpusExpectation expectation = Deserialize<CorpusExpectation>(path);
        Assert.Equal(1, expectation.SchemaVersion);
        Assert.Equal(entry.RomSha256, expectation.Rom.Sha256, ignoreCase: true);
        return expectation;
    }

    /// <summary>Finds the exact dump by SHA-256, skipping only when the developer has not opted into mandatory corpus coverage.</summary>
    public static string Resolve(CorpusExpectationIndexEntry entry)
    {
        if (FixturesByHash.Value.TryGetValue(entry.RomSha256, out string? path))
        {
            return path;
        }

        string message = $"Missing corpus image {entry.Name} ({entry.RomSha256}). " +
            $"Set {RootVariable} to a directory containing the exact legally dumped image.";
        if (IsRequired())
        {
            Assert.Fail(message);
        }

        Assert.Skip(message);
        throw new InvalidOperationException("Assert.Skip does not return.");
    }

    /// <summary>Reads the copied test-data index from the build output, independent of the process working directory.</summary>
    private static CorpusExpectationIndex ReadIndex()
    {
        CorpusExpectationIndex index = Deserialize<CorpusExpectationIndex>(Path.Combine(GetExpectationRoot(), "index.json"));
        Assert.Equal(1, index.SchemaVersion);
        Assert.Equal(57, index.Cases.Count);
        Assert.Equal(index.Cases.Count, index.Cases.Select(static item => item.RomSha256).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        return index;
    }

    /// <summary>Locates expectation content copied by MSBuild while remaining compatible with test-host shadow directories.</summary>
    private static string GetExpectationRoot() => Path.Combine(AppContext.BaseDirectory, "Corpus", "Expectations");

    /// <summary>Deserializes a required oracle document and turns truncation or schema drift into an immediate test failure.</summary>
    private static T Deserialize<T>(string path) where T : class
    {
        Assert.True(File.Exists(path), $"Corpus expectation is missing from test output: {path}");
        T? value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        return value ?? throw new InvalidDataException($"Corpus expectation is empty: {path}");
    }

    /// <summary>Indexes every recursively discovered .nds file once, rejecting two different paths with identical payloads as harmless duplicates.</summary>
    private static Dictionary<string, string> DiscoverFixtures()
    {
        string? root = Environment.GetEnvironmentVariable(RootVariable);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = FindPrivateCorpusFromRepository();
        }

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(root, "*.nds", SearchOption.AllDirectories))
        {
            using FileStream stream = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            result.TryAdd(hash, path);
        }

        return result;
    }

    /// <summary>Auto-discovers the ignored maintainer corpus when tests run from this repository checkout.</summary>
    private static string? FindPrivateCorpusFromRepository()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "fixtures", "private", "nds-corpus", "library");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>Interprets common CI truth values without treating an arbitrary nonempty value as authorization to require private assets.</summary>
    private static bool IsRequired() => Environment.GetEnvironmentVariable(RequireVariable) is string value &&
        (value.Equals("1", StringComparison.Ordinal) || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>Configures the public JSON dialect shared with the corpus publishing tool.</summary>
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
