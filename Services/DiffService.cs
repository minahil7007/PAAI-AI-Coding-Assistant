using PAAI.Models;
using System.IO;


namespace PAAI.Services;

public class DiffService
{
    public DiffResult GenerateDiff(string filePath, string originalCode, string fixedCode, string errorContext)
    {
        var result = new DiffResult
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            OriginalCode = originalCode,
            FixedCode = fixedCode,
            ErrorContext = errorContext
        };

        var originalLines = originalCode.Split('\n');
        var fixedLines = fixedCode.Split('\n');

        // Simple line by line diff
        var maxLines = Math.Max(originalLines.Length, fixedLines.Length);

        for (int i = 0; i < maxLines; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i].TrimEnd() : null;
            var fixedLine = i < fixedLines.Length ? fixedLines[i].TrimEnd() : null;

            if (origLine == null)
            {
                // New line added
                result.Lines.Add(new DiffLine
                {
                    Content = fixedLine!,
                    Type = DiffLineType.Added
                });
            }
            else if (fixedLine == null)
            {
                // Line removed
                result.Lines.Add(new DiffLine
                {
                    Content = origLine,
                    Type = DiffLineType.Removed
                });
            }
            else if (origLine == fixedLine)
            {
                // Unchanged
                result.Lines.Add(new DiffLine
                {
                    Content = origLine,
                    Type = DiffLineType.Unchanged
                });
            }
            else
            {
                // Changed — show old removed, new added
                result.Lines.Add(new DiffLine
                {
                    Content = origLine,
                    Type = DiffLineType.Removed
                });
                result.Lines.Add(new DiffLine
                {
                    Content = fixedLine,
                    Type = DiffLineType.Added
                });
            }
        }

        return result;
    }
}