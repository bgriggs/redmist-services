using BigMission.Streaming.Services.Clients;
using BigMission.Streaming.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BigMission.Streaming.Services.Controllers;

[ApiController]
[Route("[controller]/[action]")]
[Authorize]
public class ObsController : ControllerBase
{
    private readonly ObsClient obsClient;

    public ObsController(ObsClient obsClient)
    {
        this.obsClient = obsClient;
    }

    [HttpGet]
    [ProducesResponseType<string[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<string[]>> GetScenes(string hostName)
    {
        return await obsClient.GetScenes(hostName);
    }

    [HttpPost]
    [Authorize(Roles = "administrator,contributor")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> SetProgramScene(string hostName, string scene)
    {
        return await obsClient.SetProgramScene(hostName, scene);
    }

    [HttpPost]
    [Authorize(Roles = "administrator,contributor")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> StartStreaming(string hostName)
    {
        return await obsClient.StartStreaming(hostName);
    }

    [HttpPost]
    [Authorize(Roles = "administrator,contributor")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> StopStreaming(string hostName)
    {
        return await obsClient.StopStreaming(hostName);
    }

    [HttpGet]
    [ProducesResponseType<ObsStatus[]>(StatusCodes.Status200OK)]
    public ActionResult<ObsStatus[]> GetAllStatus()
    {
        // May not be required at 1 per second status rate
        return Array.Empty<ObsStatus>();
    }
}
