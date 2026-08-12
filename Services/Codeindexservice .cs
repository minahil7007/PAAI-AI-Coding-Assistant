using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PAAI.Services;

public class SymbolInfo
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Kind { get; set; } = ""; // class, method, etc.
}

public class CodeIndexService
{
    // Symbol naam -> kahan defined hai (class/method name -> file)
    private readonly Dictionary<string, SymbolInfo> _index = new();

    // File -> woh kin dusre symbols ko use karta hai
    private readonly Dictionary<string, HashSet<string>> _fileDependencies = new();

    // ==================== INDEXING ====================

    public void IndexProject(string folder)
    {
        try
        {
            var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

            foreach (var file in files)
            {
                try { IndexFile(file, File.ReadAllText(file)); }
                catch { /* is file ko skip karo, agle pe jao */ }
            }
        }
        catch { }
    }

    public void IndexFile(string filePath, string code)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            // Is file mein defined classes record karo
            foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                _index[cls.Identifier.Text] = new SymbolInfo
                { Name = cls.Identifier.Text, FilePath = filePath, Kind = "class" };

            // Is file mein defined methods record karo
            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                _index[method.Identifier.Text] = new SymbolInfo
                { Name = method.Identifier.Text, FilePath = filePath, Kind = "method" };

            // Is file mein jo naam use ho rahe hain (calls, references) record karo
            var used = new HashSet<string>();
            foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
                used.Add(id.Identifier.Text);

            _fileDependencies[filePath] = used;
        }
        catch { /* parse fail hua (invalid syntax ho sakta hai) — is file ko skip karo */ }
    }

    // ==================== QUERYING ====================

    // File X ke liye related files nikalo (jo actually reference ho rahe hain)
    public List<string> GetRelatedFiles(string filePath, int maxResults = 3)
    {
        if (!_fileDependencies.TryGetValue(filePath, out var used))
            return new();

        var related = new HashSet<string>();
        foreach (var name in used)
        {
            if (_index.TryGetValue(name, out var symbol) && symbol.FilePath != filePath)
                related.Add(symbol.FilePath);
        }
        return related.Take(maxResults).ToList();
    }

    // Roslyn se free/instant syntax errors nikalo — Claude API call se pehle
    public List<string> GetSyntaxErrors(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        return tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
            .ToList();
    }

    // Bade files ke liye — sirf us function ki boundaries nikalo jahan error hai
    public (string methodCode, int startLine, int endLine)? GetContainingMethod(string code, int errorLine)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var span = method.GetLocation().GetLineSpan();
                var start = span.StartLinePosition.Line;
                var end = span.EndLinePosition.Line;

                if (errorLine >= start && errorLine <= end)
                    return (method.ToFullString(), start, end);
            }
        }
        catch { }
        return null;
    }
}