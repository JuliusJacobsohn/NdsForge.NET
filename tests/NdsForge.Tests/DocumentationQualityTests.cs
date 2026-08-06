using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NdsForge.Tests;

public sealed partial class DocumentationQualityTests
{
    [Fact]
    public void EveryLibraryDeclarationHasMeaningfulXmlDocumentation()
    {
        string repositoryRoot = FindRepositoryRoot();
        string sourceRoot = Path.Combine(repositoryRoot, "src", "NdsForge");
        var failures = new List<string>();
        foreach (string path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                path: path,
                cancellationToken: cancellationToken);
            SyntaxNode root = tree.GetRoot(cancellationToken);
            foreach (MemberDeclarationSyntax declaration in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (!RequiresDocumentation(declaration))
                {
                    continue;
                }

                string? documentation = GetDocumentation(declaration);
                FileLinePositionSpan location = declaration.GetLocation().GetLineSpan();
                string displayPath = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
                string prefix = $"{displayPath}:{location.StartLinePosition.Line + 1} {Describe(declaration)}";
                if (documentation is null)
                {
                    failures.Add(prefix + " has no XML documentation.");
                }
                else if (!IsMeaningful(documentation))
                {
                    failures.Add(prefix + " has a tautological or uninformative summary.");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} documentation failures:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static bool RequiresDocumentation(MemberDeclarationSyntax declaration) => declaration switch
    {
        BaseTypeDeclarationSyntax => true,
        DelegateDeclarationSyntax => true,
        BaseMethodDeclarationSyntax => true,
        PropertyDeclarationSyntax => true,
        IndexerDeclarationSyntax => true,
        EventDeclarationSyntax => true,
        EventFieldDeclarationSyntax => true,
        FieldDeclarationSyntax field => !field.Modifiers.Any(SyntaxKind.ConstKeyword) ||
            !field.Parent!.IsKind(SyntaxKind.EnumDeclaration),
        EnumMemberDeclarationSyntax => true,
        _ => false,
    };

    private static string? GetDocumentation(MemberDeclarationSyntax declaration)
    {
        DocumentationCommentTriviaSyntax? comment = declaration.GetLeadingTrivia()
            .Select(static trivia => trivia.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .LastOrDefault();
        if (comment is null)
        {
            return null;
        }

        if (comment.Content.OfType<XmlEmptyElementSyntax>()
            .Any(static element => element.Name.LocalName.ValueText == "inheritdoc"))
        {
            return "Documentation inherited from the implemented contract.";
        }

        XmlElementSyntax? summary = comment.Content
            .OfType<XmlElementSyntax>()
            .FirstOrDefault(static element => element.StartTag.Name.LocalName.ValueText == "summary");
        return summary is null ? null : NormalizeWhitespace().Replace(summary.Content.ToFullString(), " ").Trim();
    }

    private static bool IsMeaningful(string summary)
    {
        string plainText = XmlMarkup().Replace(summary, string.Empty).Trim();
        return plainText.Length >= 24 && !Tautology().IsMatch(plainText);
    }

    private static string Describe(MemberDeclarationSyntax declaration) => declaration switch
    {
        BaseTypeDeclarationSyntax type => $"type '{type.Identifier.ValueText}'",
        DelegateDeclarationSyntax @delegate => $"delegate '{@delegate.Identifier.ValueText}'",
        BaseMethodDeclarationSyntax method => $"method '{method switch
        {
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => destructor.Identifier.ValueText,
            OperatorDeclarationSyntax @operator => @operator.OperatorToken.ValueText,
            ConversionOperatorDeclarationSyntax conversion => conversion.Type.ToString(),
            MethodDeclarationSyntax ordinary => ordinary.Identifier.ValueText,
            _ => "unknown",
        }}'",
        PropertyDeclarationSyntax property => $"property '{property.Identifier.ValueText}'",
        IndexerDeclarationSyntax => "indexer",
        EventDeclarationSyntax @event => $"event '{@event.Identifier.ValueText}'",
        EventFieldDeclarationSyntax field => $"event '{field.Declaration.Variables.First().Identifier.ValueText}'",
        FieldDeclarationSyntax field => $"field '{field.Declaration.Variables.First().Identifier.ValueText}'",
        EnumMemberDeclarationSyntax member => $"enum member '{member.Identifier.ValueText}'",
        _ => declaration.Kind().ToString(),
    };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NdsForge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the NdsForge repository root.");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex NormalizeWhitespace();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex XmlMarkup();

    [GeneratedRegex(
        @"^(?:Gets|Sets|Gets or sets|Represents|Provides|Creates|Initializes)\s+(?:the|a|an)\s+[\w -]+\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Tautology();
}
