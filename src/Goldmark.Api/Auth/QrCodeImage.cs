using System.Globalization;
using Net.Codecrete.QrCodeGenerator;

namespace Goldmark.Api.Auth;

/// <summary>
/// The authenticator QR code as an SVG image (DEC-038 3A): one path, black on white, with no script,
/// style or external reference, served only to the person enrolling and never cached. The CSP allows
/// no <c>data:</c> images, so the SPA loads it from Goldmark like any image.
/// </summary>
internal static class QrCodeImage
{
    private const int Border = 4; // the quiet zone the QR specification requires, in modules

    public static string Svg(string text)
    {
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var size = (qr.Size + (Border * 2)).ToString(CultureInfo.InvariantCulture);
        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" width="{size}" height="{size}" shape-rendering="crispEdges">""" +
            $"""<rect width="100%" height="100%" fill="#ffffff"/><path d="{qr.ToGraphicsPath(Border)}" fill="#000000"/></svg>""";
    }
}
