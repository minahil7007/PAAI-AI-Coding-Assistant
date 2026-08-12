namespace PAAI.Models;

public class DiffResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string OriginalCode { get; set; } = "";
    public string FixedCode { get; set; } = "";
    public string ErrorContext { get; set; } = "";
    public List<DiffLine> Lines { get; set; } = new();
}

public class DiffLine
{
    public string Content { get; set; } = "";
    public DiffLineType Type { get; set; }
}

public enum DiffLineType
{
    Unchanged,
    Added,
    Removed
}