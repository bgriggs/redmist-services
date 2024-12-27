﻿using System.Text;
using System.Text.Json;

namespace BigMission.Shared.Auth;

/// <summary>
/// Utilities for Keycloak based authentication for services.
/// </summary>
public class KeycloakServiceToken
{
    const string URL = "{0}/realms/{1}/protocol/openid-connect/token";
    const string GRANTREQUEST = "grant_type=client_credentials&client_id={0}&client_secret={1}";
    const string CONTENTTYPE = "application/x-www-form-urlencoded";

    /// <summary>
    /// Gets a token for a service given it's name and secret. The client has to be previously configured in keycloak.
    /// </summary>
    /// <param name="authUrl">server path, e.g. https://sunnywood.redmist.racing/dev/auth</param>
    /// <param name="realm">redmist</param>
    /// <param name="clientName">service-client</param>
    /// <param name="clientSecret">from keycloak</param>
    /// <returns></returns>
    public static async Task<string?> RequestClientToken(string authUrl, string realm, string clientName, string clientSecret)
    {
        var url = string.Format(URL, authUrl, realm);
        var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        var content = string.Format(GRANTREQUEST, clientName, clientSecret);
        request.Content = new StringContent(content, Encoding.UTF8, CONTENTTYPE);
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var jsonObj = JsonSerializer.Deserialize<dynamic>(json);
        return jsonObj?.GetProperty("access_token").GetString();
    }
}
