using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AIGarage.Mobile.Services;

namespace AIGarage.Mobile.Tests;

internal static class LocalTlsCheck
{
    public static async Task VerifyUntrustedCertificateRejectedAsync()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using X509Certificate2 generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Windows Schannel requires an imported temporary key container. Disposal removes it.
        // This imports a private key for the server only; it never installs a trusted certificate.
        string temporaryPassword = Guid.NewGuid().ToString();
        using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12, temporaryPassword), temporaryPassword, X509KeyStorageFlags.DefaultKeySet);
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        bool accepted = false; Exception? endpointFailure = null;
        Task endpoint = Task.Run(async () =>
        {
            using TcpClient socket = await listener.AcceptTcpClientAsync(timeout.Token);
            accepted = true;
            using SslStream stream = new(socket.GetStream());
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, timeout.Token);
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException) { endpointFailure = exception; }
        }, timeout.Token);

        GarageMqttService service = new();
        Exception? failure = null;
        try { await service.ConnectAsync(TestSession.Settings with { Host = "127.0.0.1", Port = port }, TestSession.Credentials, timeout.Token); }
        catch (Exception exception) { failure = exception; }
        finally { await service.DisconnectAsync(); listener.Stop(); }
        await endpoint;
        if (!accepted) throw new InvalidOperationException("TLS endpoint was never reached.");
        if (failure is null || !ContainsAuthenticationFailure(failure))
            throw new InvalidOperationException("Expected a real certificate validation failure, received: " + failure + "; server: " + endpointFailure);
        if (service.IsConnected) throw new InvalidOperationException("Untrusted TLS connection remained usable.");
    }

    private static bool ContainsAuthenticationFailure(Exception exception) => exception is AuthenticationException ||
        exception is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsAuthenticationFailure) ||
        exception.InnerException is not null && ContainsAuthenticationFailure(exception.InnerException);
}
