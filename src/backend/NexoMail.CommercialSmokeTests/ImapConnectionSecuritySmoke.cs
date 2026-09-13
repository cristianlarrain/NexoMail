using System.Net;
using NexoMail.Infrastructure.Imap;

internal static class ImapConnectionSecuritySmoke
{
    public static void Run()
    {
        RequirePublic("74.208.114.73");
        RequirePublic("74.208.114.78");
        RequirePublic("::ffff:74.208.114.73");

        RequireBlocked("127.0.0.1");
        RequireBlocked("10.0.0.1");
        RequireBlocked("169.254.1.1");
        RequireBlocked("192.168.1.1");
        RequireBlocked("224.0.0.1");
        RequireBlocked("::1");
        RequireBlocked("fe80::1");
        RequireBlocked("ff02::1");
    }

    private static void RequirePublic(string value)
    {
        if (!MailHostAddressPolicy.IsPublic(IPAddress.Parse(value)))
            throw new InvalidOperationException($"La IP pública {value} fue bloqueada incorrectamente.");
    }

    private static void RequireBlocked(string value)
    {
        if (MailHostAddressPolicy.IsPublic(IPAddress.Parse(value)))
            throw new InvalidOperationException($"La IP no pública {value} fue permitida incorrectamente.");
    }
}
