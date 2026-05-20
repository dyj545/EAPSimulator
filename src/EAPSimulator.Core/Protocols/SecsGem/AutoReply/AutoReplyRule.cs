using Newtonsoft.Json;

namespace EAPSimulator.Core.Protocols.SecsGem.AutoReply;

/// <summary>
/// A quick-reply rule: when trigger (S,F) is received and conditions match,
/// reply with the referenced template. Simple 1-to-1 mapping.
/// </summary>
public class AutoReplyRule
{
    [JsonProperty("triggerStream")]
    public byte TriggerStream { get; set; }

    [JsonProperty("triggerFunction")]
    public byte TriggerFunction { get; set; }

    /// <summary>
    /// Additional field conditions that must ALL match.
    /// Empty = match any message with this (S,F).
    /// </summary>
    [JsonProperty("conditions")]
    public List<FieldCondition> Conditions { get; set; } = [];

    [JsonProperty("replyStream")]
    public byte ReplyStream { get; set; }

    [JsonProperty("replyFunction")]
    public byte ReplyFunction { get; set; }

    /// <summary>
    /// Name of the reply template in the message template file.
    /// Used to look up the actual message body from the loaded templates.
    /// </summary>
    [JsonProperty("replyTemplateName")]
    public string ReplyTemplateName { get; set; } = "";

    [JsonProperty("description")]
    public string Description { get; set; } = "";

    [JsonProperty("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public string TriggerMessageId => $"S{TriggerStream}F{TriggerFunction}";

    [JsonIgnore]
    public string ReplyMessageId => $"S{ReplyStream}F{ReplyFunction}";

    [JsonIgnore]
    public string TriggerDisplay
    {
        get
        {
            if (Conditions.Count == 0) return TriggerMessageId;
            var conds = string.Join(" & ", Conditions.Select(c => c.DisplayText));
            return $"{TriggerMessageId} where {conds}";
        }
    }

    public override string ToString() =>
        $"{(Enabled ? "✓" : "✗")} {TriggerDisplay} → {ReplyMessageId} ({Description})";
}
