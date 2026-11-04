using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Text;

namespace ErganiManager.Android;

public sealed class MobileApiClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly ConcurrentDictionary<string, IPAddress[]> ResolvedHostCache =
        new(StringComparer.OrdinalIgnoreCase);

    public MobileApiClient(string baseAddress)
    {
        if (!Uri.TryCreate(baseAddress.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Enter a valid HTTPS address for the mobile API, for example https://ThinkPad-p16s.local:5001.");

        var cookies = new CookieContainer();
        var handler = new SocketsHttpHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            ConnectCallback = ConnectToHostAsync
        };
        _http = new HttpClient(handler) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(30) };
    }

    // Android's normal DNS resolver does not consistently resolve .local names.
    // Resolve those names with an mDNS A-record query, then connect to the
    // resolved address while keeping the original URI hostname. HttpClient
    // therefore still validates the TLS certificate against the DNS hostname.
    private static async ValueTask<Stream> ConnectToHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        IPAddress[] addresses = [];
        var isLocalHostname = host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        var hasCachedAddress = false;
        if (isLocalHostname &&
            ResolvedHostCache.TryGetValue(host, out var cachedAddresses) &&
            cachedAddresses is not null)
        {
            addresses = cachedAddresses;
            hasCachedAddress = true;
        }

        if (hasCachedAddress)
        {
            // Reuse the address that already passed DNS/mDNS and TCP connection
            // during the health check. Do not issue a fresh multicast lookup
            // for every API request on the same HttpClient.
        }
        else if (IPAddress.TryParse(host, out var literalAddress))
        {
            addresses = [literalAddress];
        }
        else if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            // Do not let Android's native .local resolver consume the complete
            // HttpClient timeout. Some Android/Wi-Fi combinations leave this
            // lookup pending even though the browser can resolve the same name.
            string nativeDnsDiagnostic;
            using (var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                dnsTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    addresses = await Dns.GetHostAddressesAsync(host, dnsTimeout.Token);
                    nativeDnsDiagnostic = addresses.Length == 0
                        ? "Android/.NET DNS returned no addresses."
                        : "Android/.NET DNS returned: " + string.Join(", ", addresses.Select(a => a.ToString()));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    addresses = [];
                    nativeDnsDiagnostic = "Android/.NET DNS timed out after 3 seconds.";
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    addresses = [];
                    nativeDnsDiagnostic = "Android/.NET DNS failed: " + ex.GetBaseException().GetType().Name +
                        ": " + ex.GetBaseException().Message;
                }
            }

            // Fall back promptly to multicast DNS if the native resolver did
            // not return an address. Keep the original URI hostname for TLS.
            if (addresses.Length == 0)
            {
                try
                {
                    addresses = await MdnsResolver.ResolveIpv4Async(host, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new HttpRequestException(
                        nativeDnsDiagnostic + " Custom mDNS also failed: " +
                        ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message, ex);
                }
            }
        }
        else
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var client = new TcpClient(address.AddressFamily);
            try
            {
                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(address, context.DnsEndPoint.Port, connectTimeout.Token);
                if (isLocalHostname)
                    ResolvedHostCache[host] = [address];
                return client.GetStream();
            }
            catch (Exception ex)
            {
                client.Dispose();
                lastError = ex is OperationCanceledException && !cancellationToken.IsCancellationRequested
                    ? new TimeoutException($"TCP connection to {address}:{context.DnsEndPoint.Port} timed out after 5 seconds.", ex)
                    : ex;
                if (isLocalHostname && hasCachedAddress)
                    ResolvedHostCache.TryRemove(host, out _);
                // A timeout created for this individual address is not a
                // cancellation of the whole HTTP request. Continue to the next
                // resolved address and report a connection-specific error.
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                    throw;
            }
        }

        throw new HttpRequestException(
            $"Could not connect to {host}:{context.DnsEndPoint.Port}. " +
            (lastError?.Message ?? "No usable IP address was found."));
    }

    public async Task CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/mobile/health", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"The API server responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}".Trim());
        }
    }

    public async Task<MobileLoginResponse> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/mobile/login", new { username, password }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("The API was reached, but the username or password was rejected.");
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable) throw new InvalidOperationException("The API was reached, but the server database is unavailable. Mobile login requires an online connection.");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Login endpoint responded with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {body}".Trim());
        }
        return await response.Content.ReadFromJsonAsync<MobileLoginResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty login response.");
    }

    public async Task<List<MobileCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/mobile/companies", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Companies returned HTTP {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<List<MobileCompany>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<MobileCompany> SelectCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/mobile/company/select", new { companyId }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Company selection returned HTTP {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<MobileCompany>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty company selection response.");
    }

    public async Task<List<MobileEmployee>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/mobile/employees", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<MobileEmployee>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<List<MobileTodayEmployeeStatus>> GetTodayStatusAsync(DateOnly date, int graceMinutes, CancellationToken cancellationToken = default)
    {
        var url = "api/mobile/today?date=" + date.ToString("yyyy-MM-dd") + "&graceMinutes=" + graceMinutes;
        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException("Daily Status returned HTTP " + (int)response.StatusCode + ": " + body);
        }
        return await response.Content.ReadFromJsonAsync<List<MobileTodayEmployeeStatus>>(cancellationToken: cancellationToken) ?? [];
    }


    public async Task<List<MobileScheduleDay>> GetScheduleMonthAsync(
        int employeeId, int year, int month, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"api/mobile/schedules/{employeeId}/{year}/{month}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Work calendar returned HTTP {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<List<MobileScheduleDay>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task SaveScheduleDayAsync(
        int employeeId, DateOnly scheduleDate, int workType,
        TimeOnly? startTime, TimeOnly? endTime, string? comments,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/mobile/schedules/day",
            new { employeeId, scheduleDate, workType, startTime, endTime, comments }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Saving work calendar day returned HTTP {(int)response.StatusCode}: {body}");
        }
    }

    public async Task<List<MobileWorkCardHistoryItem>> GetWorkCardHistoryAsync(
        DateOnly fromDate, DateOnly toDate, int? employeeId,
        CancellationToken cancellationToken = default)
    {
        var url = "api/mobile/workcards/history?fromDate=" + fromDate.ToString("yyyy-MM-dd") +
                  "&toDate=" + toDate.ToString("yyyy-MM-dd");
        if (employeeId.HasValue) url += "&employeeId=" + employeeId.Value;

        using var response = await _http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Work cards returned HTTP {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<List<MobileWorkCardHistoryItem>>(
            cancellationToken: cancellationToken) ?? [];
    }

    public async Task<MobileWorkCardScanResult> ScanWorkCardAsync(string scanValue, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/mobile/workcards/scan",
            new { scanValue }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Work card scan returned HTTP {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<MobileWorkCardScanResult>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned an empty scan response.");
    }

    public async Task<List<MobileNotification>> GetNotificationsAsync(long afterId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("api/mobile/notifications?afterId=" + afterId, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Notifications returned HTTP {(int)response.StatusCode}: {body}");
        }
        return await response.Content.ReadFromJsonAsync<List<MobileNotification>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task MarkNotificationReadAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync($"api/mobile/notifications/{notificationId}/read", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync("api/mobile/logout", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}

internal static class MdnsResolver
{
    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private const int MdnsPort = 5353;

    public static async Task<IPAddress[]> ResolveIpv4Async(string host, CancellationToken cancellationToken)
    {
        var normalizedHost = host.TrimEnd('.');
        var wifi = global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.WifiService)
            as global::Android.Net.Wifi.WifiManager;
        using var multicastLock = wifi?.CreateMulticastLock("ErganiMobileApi.mDNS");
        multicastLock?.SetReferenceCounted(false);
        multicastLock?.Acquire();

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            // RFC 6762 requires mDNS packets to use IP TTL 255. Responders may
            // silently discard queries sent with the operating system default.
            udp.Ttl = 255;
            udp.EnableBroadcast = true;

            var query = CreateAQuery(normalizedHost);
            var destination = new IPEndPoint(MulticastAddress, MdnsPort);

            // QU asks the mDNS responder to return the answer directly to this
            // ephemeral UDP port, avoiding a dependency on binding port 5353.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            // Repeat the query once: Wi-Fi multicast can be delayed while the
            // Android multicast lock and radio are becoming active.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await udp.SendAsync(query, query.Length, destination);
                using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptTimeout.CancelAfter(TimeSpan.FromMilliseconds(2200));

                while (true)
                {
                    UdpReceiveResult packet;
                    try
                    {
                        packet = await udp.ReceiveAsync(attemptTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var addresses = ParseARecords(packet.Buffer, normalizedHost);
                    if (addresses.Length > 0)
                        return addresses;
                }
            }

            throw new HttpRequestException(
                $"Could not resolve '{normalizedHost}' using mDNS after two attempts. " +
                "Check that the phone and PC are on the same Wi-Fi network and that UDP 5353 multicast is not blocked.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException(
                $"Could not resolve '{normalizedHost}' using mDNS. Check that the phone and PC are on the same Wi-Fi network and that UDP 5353 multicast is not blocked.");
        }
        finally
        {
            if (multicastLock?.IsHeld == true)
                multicastLock.Release();
        }
    }

    private static byte[] CreateAQuery(string host)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[2]); // mDNS transaction ID is zero.
        writer.Write((byte)0);
        writer.Write((byte)0); // Standard query flags.
        writer.Write((byte)0);
        writer.Write((byte)1); // One question.
        writer.Write(new byte[6]); // No answer/authority/additional records.

        foreach (var label in host.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63)
                throw new ArgumentException("Invalid mDNS hostname.", nameof(host));
            writer.Write((byte)bytes.Length);
            writer.Write(bytes);
        }

        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)1); // A record.
        writer.Write((byte)0x80);
        writer.Write((byte)1); // IN class with QU bit set.
        return stream.ToArray();
    }

    private static IPAddress[] ParseARecords(byte[] packet, string expectedHost)
    {
        if (packet.Length < 12 || (packet[2] & 0x80) == 0)
            return [];

        var questionCount = ReadUInt16(packet, 4);
        var answerCount = ReadUInt16(packet, 6);
        var authorityCount = ReadUInt16(packet, 8);
        var additionalCount = ReadUInt16(packet, 10);
        var offset = 12;

        for (var i = 0; i < questionCount; i++)
        {
            _ = ReadDnsName(packet, ref offset);
            offset += 4;
            if (offset > packet.Length) return [];
        }

        var addresses = new List<IPAddress>();
        var recordCount = answerCount + authorityCount + additionalCount;
        for (var i = 0; i < recordCount; i++)
        {
            var name = ReadDnsName(packet, ref offset);
            if (offset + 10 > packet.Length) return [];

            var type = ReadUInt16(packet, offset);
            var recordClass = (ushort)(ReadUInt16(packet, offset + 2) & 0x7fff);
            var dataLength = ReadUInt16(packet, offset + 8);
            offset += 10;

            if (offset + dataLength > packet.Length) return [];
            if (name.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) &&
                type == 1 && recordClass == 1 && dataLength == 4)
            {
                addresses.Add(new IPAddress(packet.AsSpan(offset, 4)));
            }

            offset += dataLength;
        }

        return addresses.Distinct().ToArray();
    }

    private static string ReadDnsName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var position = offset;
        var jumped = false;
        var nextOffset = offset;
        var jumps = 0;

        while (position < packet.Length)
        {
            var length = packet[position];
            if (length == 0)
            {
                if (!jumped) nextOffset = position + 1;
                break;
            }

            if ((length & 0xc0) == 0xc0)
            {
                if (position + 1 >= packet.Length || ++jumps > 16)
                    throw new InvalidDataException("Invalid compressed mDNS name.");
                var pointer = ((length & 0x3f) << 8) | packet[position + 1];
                if (!jumped) nextOffset = position + 2;
                position = pointer;
                jumped = true;
                continue;
            }

            if ((length & 0xc0) != 0 || position + 1 + length > packet.Length)
                throw new InvalidDataException("Invalid mDNS name.");

            labels.Add(Encoding.ASCII.GetString(packet, position + 1, length));
            position += 1 + length;
        }

        offset = nextOffset;
        return string.Join(".", labels);
    }

    private static ushort ReadUInt16(byte[] packet, int offset)
        => (ushort)((packet[offset] << 8) | packet[offset + 1]);
}


public sealed class MobileScheduleDay
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int BranchId { get; set; }
    public DateOnly ScheduleDate { get; set; }
    public int WorkType { get; set; }
    public int Source { get; set; }
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }
    public List<MobileSchedulePeriod> WorkingPeriods { get; set; } = new();
    public bool SubmittedToErgani { get; set; }
    public bool HasPendingChange { get; set; }
    public string? Protocol { get; set; }
    public string? Comments { get; set; }
    public DateTime? ActualArrival { get; set; }
    public DateTime? ActualDeparture { get; set; }
    public string WorkTypeText => WorkType switch
    {
        0 => "Office", 1 => "Work from home", 2 => "Rest day", 3 => "Absent", _ => "Unknown"
    };
    public string PlannedTimeText => WorkingPeriods.Count > 0
        ? string.Join(" · ", WorkingPeriods.OrderBy(x => x.PeriodNumber)
            .Select(x => x.StartTime.ToString("HH:mm") + "–" + x.EndTime.ToString("HH:mm")))
        : StartTime.HasValue && EndTime.HasValue
            ? StartTime.Value.ToString("HH:mm") + "–" + EndTime.Value.ToString("HH:mm")
            : "No planned hours";
    public string ActualTimeText =>
        "Actual: " + (ActualArrival?.ToString("HH:mm") ?? "—") +
        " → " + (ActualDeparture?.ToString("HH:mm") ?? "—");
    public string SubmissionText => SubmittedToErgani
        ? "Sent to Ergani" + (string.IsNullOrWhiteSpace(Protocol) ? "" : " · " + Protocol)
        : HasPendingChange ? "Pending change" : "Not submitted";
}
public sealed class MobileSchedulePeriod
{
    public int PeriodNumber { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

public sealed record MobileLoginResponse(string Username, string Role, int? CompanyId, string? CompanyName, int? BranchId, string? BranchName);
public sealed record MobileCompany(int Id, string Name, string TaxId);
public sealed record MobileEmployee(int Id, int BranchId, string FirstName, string LastName, string? FatherName, string TaxId, bool IsActive)
{
    public string DisplayName =>
        $"{FirstName} {LastName}" +
        (string.IsNullOrWhiteSpace(FatherName) ? "" : $" {FatherName}") +
        (string.IsNullOrWhiteSpace(TaxId) ? "" : $" · ΑΦΜ: {TaxId}");
}


public sealed class MobileTodayEmployeeStatus
{
    public int EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int ScheduleKind { get; set; }
    public TimeOnly? PlannedStart { get; set; }
    public TimeOnly? PlannedEnd { get; set; }
    public bool IsHome { get; set; }
    public string? LeaveName { get; set; }
    public TimeOnly? ArrivalTime { get; set; }
    public TimeOnly? DepartureTime { get; set; }
    public int Presence { get; set; }
    public int Level { get; set; }
    public bool IsOfficialHoliday { get; set; }
    public List<MobileTodayIssue> Issues { get; set; } = new();

    public string PresenceText => Presence switch
    {
        1 => "Working", 2 => "Left", 3 => "On leave", 4 => "Not arrived",
        5 => "Expected", 6 => "Not working", _ => "No status"
    };
    public string ScheduleText => ScheduleKind switch
    {
        1 => IsHome ? "Work · Home" : "Work", 2 => "Rest day", 3 => "Absent",
        4 => LeaveName ?? "Leave", _ => IsOfficialHoliday ? "Official holiday" : "No schedule"
    };
    public string PlannedHours => (PlannedStart?.ToString("HH:mm") ?? "—") + " – " + (PlannedEnd?.ToString("HH:mm") ?? "—");
    public string ClockingText => "In: " + (ArrivalTime?.ToString("HH:mm") ?? "—") + "   Out: " + (DepartureTime?.ToString("HH:mm") ?? "—");
    public string LevelText => Level switch { 2 => "Error", 1 => "Warning", _ => "OK" };
    public string IssueSummary => Issues.Count == 0 ? "No issues" : Issues.Count + " issue(s)";
}
public sealed class MobileTodayIssue
{
    public int Code { get; set; }
    public int Level { get; set; }
    public string? Detail { get; set; }
}

public sealed class MobileNotification
{
    public long Id { get; set; }
    public int EventCode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsRead { get; set; }
}

public sealed class MobileWorkCardScanResult
{
    public string EmployeeName { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string Movement { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Protocol { get; set; }
}
