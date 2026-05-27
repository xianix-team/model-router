using Microsoft.AspNetCore.Mvc;

namespace LlmModelProxy.Api.Controllers;

/// <summary>
/// Minimal <c>GET /v1/models</c> implementation so Claude CLI's model-check
/// call succeeds without hitting the real Anthropic API.
/// Returns a static list that includes the most common Claude model IDs.
/// </summary>
[ApiController]
[Route("v1")]
public sealed class ModelsController : ControllerBase
{
    private static readonly object[] KnownModels =
    [
        Model("claude-opus-4-6"),
        Model("claude-sonnet-4-6"),
        Model("claude-haiku-4-5-20251001"),
        Model("claude-3-5-sonnet-20241022"),
        Model("claude-3-5-haiku-20241022"),
        Model("claude-3-opus-20240229"),
    ];

    [HttpGet("models")]
    public IActionResult GetModels() => Ok(new
    {
        data = KnownModels,
        object_ = "list"
    });

    private static object Model(string id) => new
    {
        id,
        @object = "model",
        created = 1_700_000_000,
        owned_by = "anthropic"
    };
}
