using Microsoft.CodeAnalysis.CSharp;
using Plus.CodeStyle;
using Xunit;

namespace Plus.Tests;

public class CodeStyleTests
{
    [Fact]
    public void SeparatesConditionsAndReturnsWithoutChangingTokens()
    {
        const string source = """
            class Example
            {
                int Run(bool first, bool second)
                {
                    if (first)
                    {
                        return 1;
                    }
                    // Keep this comment with the second condition.
                    if (second)
                    {
                        return 2;
                    }
                    var value = 3;
                    // Explain the result.
                    return value;
                }
            }
            """;

        string formatted = StatementSpacing.Format(source);

        Assert.Contains("}\n\n        // Keep this comment", formatted);
        Assert.Contains("}\n\n        var value", formatted);
        Assert.Contains("var value = 3;\n\n        // Explain the result.\n        return value;", formatted);
        Assert.DoesNotContain("{\n\n            return", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void PreservesExistingBlankLinesElseChainsAndCrLf()
    {
        const string source = "class Example\r\n{\r\n    int Run(bool flag)\r\n    {\r\n        if (flag)\r\n        {\r\n            return 1;\r\n        }\r\n        else\r\n        {\r\n            return 2;\r\n        }\r\n\r\n        return 3;\r\n    }\r\n}";

        Assert.Equal(source, StatementSpacing.Format(source));
    }

    [Fact]
    public void HandlesSwitchSectionsAndKeepsDirectivesAndStringsIntact()
    {
        const string source = """
            class Example
            {
                string Run(int value)
                {
                    switch (value)
                    {
                        case 1:
                            value++;
                            return "if (x) { return y; }";
                        default:
                            value--;
            #if DEBUG
                            value++;
            #endif
                            return "fallback";
                    }
                }
            }
            """;

        string formatted = StatementSpacing.Format(source);

        Assert.Contains("value++;\n\n                return", formatted);
        Assert.Contains("value--;\n\n#if DEBUG", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void FormatsTopLevelStatements()
    {
        const string source = "if (true)\n{\n    Work();\n}\nif (false)\n{\n    Work();\n}\nWork();\nreturn;\n";

        string formatted = StatementSpacing.Format(source);

        Assert.Contains("}\n\nif (false)", formatted);
        Assert.Contains("Work();\n\nreturn;", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void FormatsBothDebugAndReleaseBranches()
    {
        const string source = "class Example\n{\n    void Run()\n    {\n#if DEBUG\n        Work();\n        return;\n#else\n        Work();\n        return;\n#endif\n    }\n}\n";

        string formatted = StatementSpacing.Format(source);

        Assert.Equal(2, formatted.Split("Work();\n\n        return;").Length - 1);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void ExpandsControlFlowButPreservesCompactPropertiesAndInitializers()
    {
        const string source = "class Example\n{\n    string Name { get; }\n    void Run()\n    {\n        if (true) { Work(); return; }\n        try { Work(); } catch { throw; }\n        var value = new { Name = \"compact\" };\n    }\n}\n";

        string formatted = StatementSpacing.Format(source);

        Assert.Contains("string Name { get; }", formatted);
        Assert.Contains("new { Name = \"compact\" }", formatted);
        Assert.DoesNotContain("if (true) { Work();", formatted);
        Assert.DoesNotContain("try { Work();", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void ExpandsNonemptyDeclarationsAndKeepsEmptyBodiesCompact()
    {
        const string source = "class Example { string Name { get; } Example() { Work(); } void Run() { Work(); return; } void Empty() { } }";

        string formatted = StatementSpacing.Format(source);

        Assert.Equal("record Value(int Number);", StatementSpacing.Format("record Value(int Number);"));
        Assert.DoesNotContain("{\n\n", formatted);
        Assert.Equal("class Empty { }", StatementSpacing.Format("class Empty {\n\n    }"));
        Assert.Equal("class Empty { }", StatementSpacing.Format("class Empty {\r\n\r\n}"));
        Assert.Contains("class Example {\n", formatted);
        Assert.Contains("string Name { get; }", formatted);
        Assert.Contains("Example() {\n", formatted);
        Assert.Contains("void Run() {\n", formatted);
        Assert.Contains("void Empty() { }", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void CompactsEmptyDefinitionsWithoutChangingCommentsOrControlFlow()
    {
        const string source = "interface Example\r\n{\r\n    void Configure()\r\n    {\r\n    }\r\n}\r\nclass Empty\r\n{\r\n}\r\nclass Other\r\n{\r\n    Other()\r\n    {\r\n    }\r\n    void Run()\r\n    {\r\n        void Local()\r\n        {\r\n        }\r\n        if (true) { }\r\n    }\r\n}\r\n";

        string formatted = StatementSpacing.Format(source);

        Assert.Contains("void Configure() { }", formatted);
        Assert.Contains("class Empty { }", formatted);
        Assert.Contains("Other() { }", formatted);
        Assert.Contains("void Local() { }", formatted);
        Assert.Contains("if (true) { }", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));

        const string commented = "class Example\n{\n    void Empty() // Keep this comment.\n    {\n    }\n    void Explained()\n    {\n        // Intentionally empty.\n    }\n}\n";
        Assert.Equal(commented, StatementSpacing.Format(commented));
        const string directive = "class Example\n{\n    void Empty()\n    {\n#if DEBUG\n#endif\n    }\n}\n";
        Assert.Equal(directive, StatementSpacing.Format(directive));
    }

    [Fact]
    public void KeepsEmptyBodySeparateFromMultilineConstructorInitializer()
    {
        const string source = "class Example\n{\n    Example() : this(new[] {\n        1,\n        2\n    })\n    {\n    }\n    Example(int[] values) { }\n}\n";

        string formatted = StatementSpacing.Format(source);

        Assert.Contains("})\n    { }", formatted);
        Assert.Equal(Tokens(source), Tokens(formatted));
        Assert.Equal(formatted, StatementSpacing.Format(formatted));
    }

    [Fact]
    public void RejectsInvalidSyntax()
    {
        Assert.Throws<InvalidDataException>(() => StatementSpacing.Format("class Example {"));
    }

    private static string[] Tokens(string source) => CSharpSyntaxTree.ParseText(source)
        .GetRoot().DescendantTokens().Select(token => token.Text).ToArray();
}
