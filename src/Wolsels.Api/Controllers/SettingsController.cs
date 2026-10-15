using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wolsels.Api.Contracts;
using Wolsels.Api.Services;

namespace Wolsels.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(IOptions<BlendOptions> blendOptions) : ControllerBase
{
    [HttpGet]
    public SettingsDto Get() => new(blendOptions.Value.MaxComponents);
}
