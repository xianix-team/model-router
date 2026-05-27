using LlmModelProxy.Core.Routing;

namespace LlmModelProxy.Core.Configuration;

/// <summary>Root configuration section bound from "Proxy" in appsettings.</summary>
public sealed class ProxyOptions
{
    public const string Section = "Proxy";

    public PipelineOptions Pipeline { get; set; } = new();
    public ProviderRoutingOptions ProviderRouting { get; set; } = new();
    public LoggingOptions Logging { get; set; } = new();
}

public sealed class PipelineOptions
{
    /// <summary>
    /// Overrides the Anthropic body tool_choice when tools are present.
    /// Values: "required" | "auto" | "none" | "" (use body value).
    /// </summary>
    public string ToolChoice { get; set; } = "required";

    /// <summary>Inject the autonomous-agent mandate into every system prompt.</summary>
    public bool InjectAgentHint { get; set; } = true;

    /// <summary>Extra text appended after the agent hint.</summary>
    public string SystemAppend { get; set; } = string.Empty;
}

public sealed class ProviderRoutingOptions
{
    public List<RoutingRule> Rules { get; set; } = [];
}

public sealed class LoggingOptions
{
    public string FilePath { get; set; } = "proxy-debug.log";
    public bool Enabled { get; set; } = true;
}
