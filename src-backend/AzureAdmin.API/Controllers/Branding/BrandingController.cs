using AzureAdmin.API.Contracts;
using AzureAdmin.API.Services.Branding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AzureAdmin.API.Controllers.Branding;

[ApiController]
[AllowAnonymous]
[Route("api/branding")]
public sealed class BrandingController : ControllerBase
{
    private readonly IBrandingResolver _branding;

    public BrandingController(IBrandingResolver branding) => _branding = branding;

    [HttpGet]
    public async Task<ActionResult<BrandingDto>> Get(CancellationToken cancellationToken) =>
        Ok(await _branding.ResolveAsync(cancellationToken));
}
