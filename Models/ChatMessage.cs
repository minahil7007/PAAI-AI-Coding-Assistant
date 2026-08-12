namespace PAAI.Models;

public class ChatMessage
{
    public string Text { get; set; } = "";
    public bool IsUser { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
}