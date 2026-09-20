using System;
using System.Text.Json.Serialization;

namespace ErganiManager.ErganiApi.Models;

public class ErganiAuthRequest
{
    [JsonPropertyName("Username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("Password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("Usertype")]
    public string Usertype { get; set; } = ErganiEndpoints.UsertypeErgani;
}

public class ErganiAuthResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("accessTokenExpired")]
    public int AccessTokenExpiredSeconds { get; set; }

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshTokenExpired")]
    public DateTimeOffset RefreshTokenExpired { get; set; }
}

public class ErganiRefreshRequest
{
    [JsonPropertyName("AccessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("RefreshToken")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Cached JWT state for one set of Ergani credentials.
/// Reused across calls until the access token is near expiry.
/// </summary>
public class ErganiTokenCache
{
    public string AccessToken   { get; set; } = string.Empty;
    public string RefreshToken  { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RefreshExpiresAt { get; set; }

    public bool IsAccessTokenValid =>
        !string.IsNullOrEmpty(AccessToken) &&
        DateTimeOffset.UtcNow < ExpiresAt.AddSeconds(-60); // 60s safety margin

    public bool IsRefreshTokenValid =>
        !string.IsNullOrEmpty(RefreshToken) &&
        DateTimeOffset.UtcNow < RefreshExpiresAt.AddMinutes(-1);
}

// ── Data retrieval models ─────────────────────────────────────────────────

public class ErganiSubmissionType
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public class ErganiServiceDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<ErganiServiceParameter> Parameters { get; set; } = new();
}

public class ErganiServiceParameter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("maxLength")]
    public int MaxLength { get; set; }
}

public class ErganiExecuteServiceRequest
{
    [JsonPropertyName("ServiceCode")]
    public string ServiceCode { get; set; } = string.Empty;

    [JsonPropertyName("Parameters")]
    public List<ErganiServiceParameterValue> Parameters { get; set; } = new();
}

public class ErganiServiceParameterValue
{
    [JsonPropertyName("ParameterName")]
    public string ParameterName { get; set; } = string.Empty;

    [JsonPropertyName("ParameterValue")]
    public string ParameterValue { get; set; } = string.Empty;
}
