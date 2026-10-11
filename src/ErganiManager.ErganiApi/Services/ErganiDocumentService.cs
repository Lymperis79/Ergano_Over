using ErganiManager.Core.Interfaces;
using ErganiManager.Data;
using ErganiManager.ErganiApi.Models;

namespace ErganiManager.ErganiApi.Services;

public sealed class ErganiDocumentService : IErganiDocumentService
{
    private readonly IErganiClient _client;
    private readonly IConnectionStateService _connectionState;
    private readonly ICredentialProtector _protector;

    public ErganiDocumentService(IErganiClient client, IConnectionStateService connectionState, ICredentialProtector protector)
    { _client = client; _connectionState = connectionState; _protector = protector; }

    public async Task<byte[]> DownloadPdfAsync(int companyId, string submissionCode, string protocol, DateOnly submittedDate, CancellationToken ct = default)
    {
        await using var db = new AppDbContext(_connectionState.GetDbOptions());
        var company = await db.Companies.FindAsync(new object[] { companyId }, ct) ?? throw new InvalidOperationException("Company not found.");
        var credentials = new ErganiCredentials { Username = company.ErganiUsername, Password = _protector.Unprotect(company.ErganiPasswordEncrypted), Usertype = company.ErganiUsertype, BaseUrl = company.ErganiBaseUrl };
        var result = await _client.GetDocumentAsync(credentials, submissionCode, protocol, submittedDate, ct);
        if (!result.Success || result.PdfBytes == null) throw new InvalidOperationException(result.ErrorMessage ?? "Could not download the Ergani PDF.");
        return result.PdfBytes;
    }
}
