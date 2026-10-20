using AzureAdmin.API.Contracts;
using AzureAdmin.API.Services.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.Settings;

[ApiController]
[Authorize]
[Route("api/settings/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly WebhookSubscriptionService _subscriptions;
    private readonly OutboundWebhookPublisher _publisher;

    public WebhooksController(WebhookSubscriptionService subscriptions, OutboundWebhookPublisher publisher)
    {
        _subscriptions = subscriptions;
        _publisher = publisher;
    }

    [HttpGet]
    public async Task<ActionResult<WebhookSettingsDto>> List(CancellationToken cancellationToken) =>
        Ok(await _subscriptions.GetSettingsAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<WebhookSubscriptionDto>> Create(
        [FromBody] CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _subscriptions.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WebhookSubscriptionDto>> Update(
        Guid id,
        [FromBody] UpdateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _subscriptions.UpdateAsync(id, request, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var ok = await _subscriptions.DeleteAsync(id, cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<WebhookTestResultDto>> Test(Guid id, CancellationToken cancellationToken)
    {
        var result = await _publisher.SendTestAsync(id, cancellationToken);
        if (!result.Ok && result.Error == "Destination was not found.")
            return NotFound(new { message = result.Error });
        return Ok(new WebhookTestResultDto(result.Ok, result.Error));
    }
}
