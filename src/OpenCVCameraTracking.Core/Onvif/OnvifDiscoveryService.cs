using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;
using System.Xml;
using OpenCVCameraTracking.Core.Logging;

namespace OpenCVCameraTracking.Core.Onvif;

public sealed record OnvifProfileInfo(
    string Token,
    string Name,
    string? StreamUri,
    string? PtzConfigurationToken);

public sealed record OnvifDeviceInfo(
    string DeviceUri,
    string? Name,
    string? Manufacturer,
    string? Model,
    bool SupportsProfileT,
    bool SupportsPtz,
    IReadOnlyList<OnvifProfileInfo> Profiles);

public sealed record OnvifPresetInfo(string Token, string Name);

public sealed class OnvifDiscoveryService
{
    private static readonly IPEndPoint DiscoveryEndpoint = new(IPAddress.Parse("239.255.255.250"), 3702);
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Tds = "http://www.onvif.org/ver10/device/wsdl";
    private static readonly XNamespace Trt = "http://www.onvif.org/ver10/media/wsdl";
    private static readonly XNamespace Tt = "http://www.onvif.org/ver10/schema";

    public async Task<IReadOnlyList<OnvifDeviceInfo>> DiscoverAsync(
        TimeSpan timeout,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var addresses = await DiscoverAddressesAsync(timeout, cancellationToken).ConfigureAwait(false);
        var devices = new List<OnvifDeviceInfo>();
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                devices.Add(await ReadDeviceAsync(address, credential, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppLogger.Warn($"ONVIF device metadata unavailable: {address} ({exception.Message})");
                devices.Add(new OnvifDeviceInfo(address, null, null, null, false, false, []));
            }
        }

        return devices;
    }

    public async Task<IReadOnlyList<string>> DiscoverAddressesAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.ReceiveTimeout = 250;
        var probe = BuildProbe();
        await client.SendAsync(probe, probe.Length, DiscoveryEndpoint).ConfigureAwait(false);

        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCancellation.CancelAfter(timeout);
        while (!deadlineCancellation.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await client.ReceiveAsync(deadlineCancellation.Token).ConfigureAwait(false);
                var document = XDocument.Parse(Encoding.UTF8.GetString(result.Buffer));
                foreach (var value in document.Descendants().Where(element => element.Name.LocalName == "XAddrs")
                             .SelectMany(element => element.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
                {
                    if (Uri.TryCreate(value, UriKind.Absolute, out _))
                    {
                        addresses.Add(value.TrimEnd('/'));
                    }
                }
            }
            catch (SocketException)
            {
                // ReceiveTimeout is expected while waiting for additional probes.
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested) throw;
                break;
            }
            catch (XmlException)
            {
                // Ignore malformed discovery packets from unrelated WS-Discovery services.
            }
        }

        return addresses.ToArray();
    }

    public Task<IReadOnlyList<OnvifProfileInfo>> GetProfilesAsync(
        string deviceUri,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default) =>
        ReadProfilesAsync(deviceUri, credential, cancellationToken);

    public Task<bool> SupportsProfileTAsync(
        string deviceUri,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default) =>
        ReadCapabilitiesAsync(deviceUri, credential, cancellationToken);

    public async Task<bool> AbsoluteMoveAsync(
        string deviceUri,
        string profileToken,
        float x,
        float y,
        float zoom,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var body = $"<tptz:AbsoluteMove><tptz:ProfileToken>{XmlEscape(profileToken)}</tptz:ProfileToken><tptz:Position><tt:PanTilt x=\"{x:0.###}\" y=\"{y:0.###}\"/><tt:Zoom x=\"{zoom:0.###}\"/></tptz:Position></tptz:AbsoluteMove>";
        var endpoint = (await ResolveServiceUriAsync(deviceUri, "PTZ", credential, cancellationToken).ConfigureAwait(false))?.ToString() ?? deviceUri;
        await SendSoapAsync(endpoint, "http://www.onvif.org/ver20/ptz/wsdl/AbsoluteMove", body, credential, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> GotoPresetAsync(
        string deviceUri,
        string profileToken,
        string presetToken,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var body = $"<tptz:GotoPreset><tptz:ProfileToken>{XmlEscape(profileToken)}</tptz:ProfileToken><tptz:PresetToken>{XmlEscape(presetToken)}</tptz:PresetToken></tptz:GotoPreset>";
        var endpoint = (await ResolveServiceUriAsync(deviceUri, "PTZ", credential, cancellationToken).ConfigureAwait(false))?.ToString() ?? deviceUri;
        await SendSoapAsync(endpoint, "http://www.onvif.org/ver20/ptz/wsdl/GotoPreset", body, credential, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<OnvifPresetInfo>> GetPresetsAsync(
        string deviceUri,
        string profileToken,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var body = $"<tptz:GetPresets><tptz:ProfileToken>{XmlEscape(profileToken)}</tptz:ProfileToken></tptz:GetPresets>";
        var endpoint = (await ResolveServiceUriAsync(deviceUri, "PTZ", credential, cancellationToken).ConfigureAwait(false))?.ToString() ?? deviceUri;
        var document = await SendSoapAsync(endpoint, "http://www.onvif.org/ver20/ptz/wsdl/GetPresets", body, credential, cancellationToken).ConfigureAwait(false);
        return document.Descendants().Where(element => element.Name.LocalName == "Preset")
            .Select(element => new OnvifPresetInfo(
                (string?)element.Attribute("token") ?? string.Empty,
                Value(element, "Name") ?? (string?)element.Attribute("token") ?? string.Empty))
            .Where(preset => !string.IsNullOrWhiteSpace(preset.Token))
            .ToArray();
    }

    public async Task<bool> ContinuousMoveAsync(
        string deviceUri,
        string profileToken,
        float pan,
        float tilt,
        float zoom,
        TimeSpan duration,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var seconds = Math.Max(0.1, duration.TotalSeconds);
        var body = $"<tptz:ContinuousMove><tptz:ProfileToken>{XmlEscape(profileToken)}</tptz:ProfileToken><tptz:Velocity><tt:PanTilt x=\"{pan:0.###}\" y=\"{tilt:0.###}\"/><tt:Zoom x=\"{zoom:0.###}\"/></tptz:Velocity><tptz:Timeout>PT{seconds:0.###}S</tptz:Timeout></tptz:ContinuousMove>";
        var endpoint = (await ResolveServiceUriAsync(deviceUri, "PTZ", credential, cancellationToken).ConfigureAwait(false))?.ToString() ?? deviceUri;
        await SendSoapAsync(endpoint, "http://www.onvif.org/ver20/ptz/wsdl/ContinuousMove", body, credential, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> StopAsync(
        string deviceUri,
        string profileToken,
        NetworkCredential? credential = null,
        CancellationToken cancellationToken = default)
    {
        var body = $"<tptz:Stop><tptz:ProfileToken>{XmlEscape(profileToken)}</tptz:ProfileToken><tptz:PanTilt>true</tptz:PanTilt><tptz:Zoom>true</tptz:Zoom></tptz:Stop>";
        var endpoint = (await ResolveServiceUriAsync(deviceUri, "PTZ", credential, cancellationToken).ConfigureAwait(false))?.ToString() ?? deviceUri;
        await SendSoapAsync(endpoint, "http://www.onvif.org/ver20/ptz/wsdl/Stop", body, credential, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<OnvifDeviceInfo> ReadDeviceAsync(string deviceUri, NetworkCredential? credential, CancellationToken cancellationToken)
    {
        var info = await SendSoapAsync(deviceUri, "http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation", "<tds:GetDeviceInformation/>", credential, cancellationToken).ConfigureAwait(false);
        var capabilities = await ReadCapabilitiesAsync(deviceUri, credential, cancellationToken).ConfigureAwait(false);
        var profiles = await ReadProfilesAsync(deviceUri, credential, cancellationToken).ConfigureAwait(false);
        var device = new OnvifDeviceInfo(
            deviceUri,
            Value(info, "Model") ?? Value(info, "DeviceName"),
            Value(info, "Manufacturer"),
            Value(info, "Model"),
            capabilities,
            profiles.Any(profile => !string.IsNullOrWhiteSpace(profile.PtzConfigurationToken)),
            profiles);
        return device;
    }

    private async Task<IReadOnlyList<OnvifProfileInfo>> ReadProfilesAsync(string deviceUri, NetworkCredential? credential, CancellationToken cancellationToken)
    {
        var mediaUri = await ResolveServiceUriAsync(deviceUri, "Media", credential, cancellationToken).ConfigureAwait(false);
        var mediaEndpoint = mediaUri?.ToString() ?? deviceUri;
        var document = await SendSoapAsync(mediaEndpoint, "http://www.onvif.org/ver10/media/wsdl/GetProfiles", "<trt:GetProfiles/>", credential, cancellationToken).ConfigureAwait(false);
        var profiles = new List<OnvifProfileInfo>();
        foreach (var profile in document.Descendants().Where(element => element.Name.LocalName == "Profiles"))
        {
            var token = (string?)profile.Attribute("token");
            if (string.IsNullOrWhiteSpace(token)) continue;
            var name = Value(profile, "Name") ?? token;
            string? streamUri = null;
            try
            {
                var body = $"<trt:GetStreamUri><trt:StreamSetup><tt:Stream>RTP-Unicast</tt:Stream><tt:Transport><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trt:StreamSetup><trt:ProfileToken>{XmlEscape(token)}</trt:ProfileToken></trt:GetStreamUri>";
                var uriDocument = await SendSoapAsync(mediaEndpoint, "http://www.onvif.org/ver10/media/wsdl/GetStreamUri", body, credential, cancellationToken).ConfigureAwait(false);
                streamUri = Value(uriDocument, "Uri");
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                AppLogger.Warn($"ONVIF stream URI unavailable for profile {token}: {exception.Message}");
            }

            profiles.Add(new OnvifProfileInfo(token, name, streamUri, Value(profile, "PTZConfiguration", "token")));
        }
        return profiles;
    }

    private async Task<bool> ReadCapabilitiesAsync(string deviceUri, NetworkCredential? credential, CancellationToken cancellationToken)
    {
        var document = await SendSoapAsync(deviceUri, "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", "<tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities>", credential, cancellationToken).ConfigureAwait(false);
        return document.Descendants().Any(element => element.Name.LocalName is "Media2" or "Analytics2");
    }

    private async Task<Uri?> ResolveServiceUriAsync(string deviceUri, string serviceName, NetworkCredential? credential, CancellationToken cancellationToken)
    {
        try
        {
            var document = await SendSoapAsync(deviceUri, "http://www.onvif.org/ver10/device/wsdl/GetServices", "<tds:GetServices><tds:IncludeCapability>true</tds:IncludeCapability></tds:GetServices>", credential, cancellationToken).ConfigureAwait(false);
            var service = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Service" &&
                element.Descendants().Any(child => child.Name.LocalName == "Namespace" && child.Value.Contains(serviceName, StringComparison.OrdinalIgnoreCase)));
            return Uri.TryCreate(Value(service, "XAddr"), UriKind.Absolute, out var uri) ? uri : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<XDocument> SendSoapAsync(
        string endpoint,
        string action,
        string body,
        NetworkCredential? credential,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { Credentials = credential, PreAuthenticate = credential is not null };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var envelope = $"<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\" xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\" xmlns:tptz=\"http://www.onvif.org/ver20/ptz/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><s:Header/><s:Body>{body}</s:Body></s:Envelope>";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", action);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ONVIF HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        return XDocument.Parse(text, LoadOptions.PreserveWhitespace);
    }

    private static byte[] BuildProbe()
    {
        var messageId = $"uuid:{Guid.NewGuid()}";
        var probe = $"<e:Envelope xmlns:e=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:w=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\"><e:Header><w:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action><w:MessageID>{messageId}</w:MessageID><w:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To></e:Header><e:Body><d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe></e:Body></e:Envelope>";
        return Encoding.UTF8.GetBytes(probe);
    }

    private static string? Value(XContainer? container, params string[] names)
    {
        if (container is null || names.Length == 0) return null;
        var element = container.Descendants().FirstOrDefault(candidate => candidate.Name.LocalName == names[0]);
        if (element is null) return null;
        if (names.Length == 1) return element.Value.Trim();
        return element.Attribute(names[1])?.Value ?? element.Value.Trim();
    }

    private static string XmlEscape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
