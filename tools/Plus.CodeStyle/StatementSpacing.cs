using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Plus.CodeStyle;

public static class StatementSpacing
{
    // Insert trivia only: comments, directives, literals and executable tokens stay intact.
    public static string Format(string source)
    {
        var text = SourceText.From(source);
        var insertions = new SortedSet<int>();

        foreach (var symbols in new[] { Array.Empty<string>(), new[] { "DEBUG", "TRACE" } })
        {
            AddInsertions(text, symbols, insertions);
        }

        int firstNewline = source.IndexOf('\n');
        string newline = firstNewline > 0 && source[firstNewline - 1] == '\r' ? "\r\n" : "\n";

        return text.WithChanges(insertions.Select(position => new TextChange(new TextSpan(position, 0), newline))).ToString();
    }

    private static void AddInsertions(SourceText text, string[] symbols, SortedSet<int> insertions)
    {
        var tree = CSharpSyntaxTree.ParseText(text,
            new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols));

        if (tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidDataException("Cannot format a file with C# syntax errors.");
        }

        foreach (var node in tree.GetRoot().DescendantNodesAndSelf())
        {
            SyntaxList<StatementSyntax> statements = node switch
            {
                BlockSyntax block => block.Statements,
                SwitchSectionSyntax section => section.Statements,
                CompilationUnitSyntax unit => SyntaxFactory.List(unit.Members.OfType<GlobalStatementSyntax>()
                    .Select(statement => statement.Statement)),
                _ => default
            };

            for (int index = 1; index < statements.Count; index++)
            {
                var previous = statements[index - 1];
                var current = statements[index];

                if (current is not ReturnStatementSyntax && !IsControlFlow(previous) && !IsControlFlow(current))
                {
                    continue;
                }

                int firstLine = text.Lines.GetLineFromPosition(previous.Span.End).LineNumber;
                int lastLine = text.Lines.GetLineFromPosition(current.SpanStart).LineNumber;

                if (Enumerable.Range(firstLine + 1, Math.Max(0, lastLine - firstLine - 1))
                    .Any(line => string.IsNullOrWhiteSpace(text.Lines[line].ToString())))
                {
                    continue;
                }

                // Keep leading comments/directives attached to their statement.
                int position = current.GetLeadingTrivia()
                    .Where(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                    .Select(trivia => trivia.SpanStart)
                    .DefaultIfEmpty(current.SpanStart)
                    .First();
                int lineStart = text.Lines.GetLineFromPosition(position).Start;

                if (lineStart > previous.Span.End)
                {
                    insertions.Add(lineStart);
                }
            }
        }

    }

    private static bool IsControlFlow(StatementSyntax statement) => statement is
        IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax
        or WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax or TryStatementSyntax
        or UsingStatementSyntax or LockStatementSyntax;
}
