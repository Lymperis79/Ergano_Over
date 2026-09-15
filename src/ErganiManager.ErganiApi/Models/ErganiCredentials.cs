namespace ErganiManager.ErganiApi.Models;

public class ErganiCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://trialeservices.yeka.gr/WebServicesAPI/api";
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
