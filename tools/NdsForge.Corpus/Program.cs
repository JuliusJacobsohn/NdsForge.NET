namespace NdsForge.Corpus;

/// <summary>Dispatches private-corpus preparation without placing ROM paths or payloads in the public repository.</summary>
internal static class Program
{
    /// <summary>Runs the requested catalog or oracle workflow and reports actionable failures to standard error.</summary>
    /// <param name="args">Command followed by its explicit private input and output paths.</param>
    /// <returns>Zero after complete output, two for usage errors, or one after an operational failure.</returns>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["catalog", string incoming, string catalogLibrary, string catalog])
            {
                await CorpusCatalogBuilder.BuildAsync(incoming, catalogLibrary, catalog).ConfigureAwait(false);
                return 0;
            }

            if (args is ["oracle", string oracleLibrary, string oracle, string ndstool])
            {
                await CorpusOracleGenerator.GenerateAsync(oracleLibrary, oracle, ndstool).ConfigureAwait(false);
                return 0;
            }

            if (args is ["merge", string additional, string mergeLibrary, string mergeCatalog])
            {
                await CorpusCatalogBuilder.MergeAsync(additional, mergeLibrary, mergeCatalog).ConfigureAwait(false);
                return 0;
            }

            if (args is ["expectations", string expectationLibrary, string privateOracle, string publicExpectations])
            {
                await CorpusExpectationPublisher.PublishAsync(
                    expectationLibrary,
                    privateOracle,
                    publicExpectations).ConfigureAwait(false);
                return 0;
            }

            if (args is ["expectations", string refreshedLibrary, string refreshedOracle, string refreshedExpectations, string expectationCatalog])
            {
                await CorpusExpectationPublisher.PublishAsync(
                    refreshedLibrary,
                    refreshedOracle,
                    refreshedExpectations,
                    expectationCatalog).ConfigureAwait(false);
                return 0;
            }

            if (args is ["refresh", string refreshLibrary, string refreshCatalog])
            {
                await CorpusCatalogBuilder.RefreshAsync(refreshLibrary, refreshCatalog).ConfigureAwait(false);
                return 0;
            }

            await Console.Error.WriteLineAsync("Usage:").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  NdsForge.Corpus catalog <incoming> <library> <catalog.json>").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  NdsForge.Corpus merge <additional-root> <library> <catalog.json>").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  NdsForge.Corpus oracle <library> <oracle> <ndstool.exe>").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  NdsForge.Corpus expectations <library> <private-oracle> <public-directory>").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  NdsForge.Corpus expectations <library> <private-oracle> <public-directory> <catalog.json>").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  NdsForge.Corpus refresh <library> <catalog.json>").ConfigureAwait(false);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
