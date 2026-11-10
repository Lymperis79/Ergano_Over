namespace ErganiManager.ErganiApi.Models;

public class ErganiCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    /// <summary>Must be loaded from the selected company's ErganiBaseUrl setting.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>"01"=External, "02"=ΕΡΓΑΝΗ, "03"=ΕΦΚΑ</summary>
    public string Usertype { get; set; } = "01";
}

public class ErganiApiException : Exception
{
    public int? HttpStatusCode { get; }
    public string? ResponseBody { get; }

    public ErganiApiException(string message, int? httpStatusCode = null, string? responseBody = null, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatusCode = httpStatusCode;
        ResponseBody = responseBody;
    }
}

public class ErganiAuthenticationException : ErganiApiException
{
    public ErganiAuthenticationException(string message, int? httpStatusCode = null, string? responseBody = null)
        : base(message, httpStatusCode, responseBody)
    {
    }
}
