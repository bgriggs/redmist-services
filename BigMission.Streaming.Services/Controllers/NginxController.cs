using BigMission.Streaming.Services.Clients;
using BigMission.Streaming.Services.Models;
using BigMission.Streaming.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BigMission.Streaming.Services.Controllers;

[ApiController]
[Route("[controller]/[action]")]
[Authorize]
public class NginxController : ControllerBase
{
    private readonly NginxClient nginxClient;

    private ILogger Logger { get; }

    public NginxController(ILoggerFactory loggerFactory, NginxClient nginxClient)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.nginxClient = nginxClient;
    }

    [HttpGet]
    [ProducesResponseType<List<NginxInfo>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<NginxInfo>>> GetNginxServers()
    {
        var servers = await nginxClient.GetAllServerInfo();
        await nginxClient.UpdateNginxServiceStatus();
        return servers.Select(i => i.info).ToList();
    }

    [HttpPost]
    [Authorize(Roles = "administrator,contributor")]
    [ProducesResponseType<NginxInfo>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NginxInfo?>> UpdateStreams(string hostName, List<NginxStreamPush> streams)
    {
        var info = await nginxClient.UpdateServerStreams(hostName, streams);
        return info;
    }

    [HttpGet]
    [ProducesResponseType<List<NginxStatus>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<NginxStatus>>> GetNginxServerStatus()
    {
        return await nginxClient.GetNginxStatus();
    }
}
