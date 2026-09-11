namespace SchoolManager.Core;

/// <summary>Eine zu versendende E-Mail, unabhängig von der Bedienoberfläche.</summary>
public sealed class OutgoingEmail
{
    public List<string> To { get; } = [];
    public List<string> Cc { get; } = [];
    public List<string> Bcc { get; } = [];
    public List<string> Attachments { get; } = [];

    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";

    /// <summary>Nachrichtentext als HTML statt als reiner Text versenden.</summary>
    public bool IsHtml { get; set; }
}
