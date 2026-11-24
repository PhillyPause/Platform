using System.Net;
using System.Text;

namespace ManyHands.Data.Email;

/// <summary>
/// The HTML half of an email that asks for one click: the logo, a few short
/// paragraphs and one big button, sent alongside the plain text so a client
/// that shows no HTML loses nothing. Tables and inline styles only, because
/// that is what Gmail, Outlook and Apple Mail render alike. Every value is
/// encoded here: a display name is whatever somebody typed.
/// </summary>
internal static class EmailHtml
{
    private const string Ink = "#15110C";
    private const string Cream = "#FBF8F1";
    private const string Red = "#AE2012";
    private const string Rule = "#CFC7B6";
    private const string Muted = "#6A6254";
    private const string Sans = "Arial, Helvetica, sans-serif";
    private const string Serif = "Georgia, 'Times New Roman', serif";

    /// <summary>The PNG's own size is twice this, so it stays sharp on high-density screens.</summary>
    private const int LogoWidth = 140;
    private const int LogoHeight = 149;

    public static string Page(string subject, string preheader, string logoUrl, params string[] blocks)
    {
        var body = new StringBuilder();
        foreach (var block in blocks) body.Append(block);

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="light">
            <meta name="supported-color-schemes" content="light">
            <title>{E(subject)}</title>
            </head>
            <body style="margin:0;padding:0;background-color:{Cream};">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all;">{E(preheader)}</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{Cream};">
            <tr><td align="center" style="padding:32px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:520px;background-color:#FFFFFF;border:1px solid {Rule};border-radius:8px;">
            <tr><td align="center" style="padding:32px 32px 8px;">
            <img src="{E(logoUrl)}" width="{LogoWidth}" height="{LogoHeight}" alt="{E(Core.Organisation.Name)}" style="display:block;border:0;outline:none;width:{LogoWidth}px;height:auto;">
            </td></tr>
            <tr><td style="padding:16px 32px 32px;font-family:{Sans};font-size:16px;line-height:1.5;color:{Ink};">
            {body}
            </td></tr>
            </table>
            <p style="margin:16px 0 0;font-family:{Sans};font-size:12px;line-height:1.5;color:{Muted};">{E(Core.Organisation.Name)} &middot; Philadelphia</p>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    public static string Greeting(string text) =>
        $"""<p style="margin:0 0 16px;font-family:{Serif};font-size:22px;line-height:1.3;color:{Ink};">{E(text)}</p>""";

    public static string Paragraph(string text) =>
        $"""<p style="margin:0 0 16px;">{E(text)}</p>""";

    public static string Quiet(string text) =>
        $"""<p style="margin:0 0 16px;font-size:14px;color:{Muted};">{E(text)}</p>""";

    /// <summary>
    /// Drawn with borders, not padding: Outlook on Windows ignores padding on a
    /// link, so a padded button there shrinks to its words.
    /// </summary>
    public static string Button(string text, string url) =>
        $"""
        <table role="presentation" cellpadding="0" cellspacing="0" border="0" align="center" style="margin:8px auto 24px;">
        <tr><td align="center" bgcolor="{Red}" style="border-radius:6px;">
        <a href="{E(url)}" target="_blank" style="display:inline-block;background-color:{Red};border-top:14px solid {Red};border-bottom:14px solid {Red};border-left:32px solid {Red};border-right:32px solid {Red};border-radius:6px;font-family:{Sans};font-size:18px;font-weight:bold;line-height:1.2;color:#FFFFFF;text-decoration:none;">{E(text)}</a>
        </td></tr>
        </table>
        """;

    /// <summary>The link written out, for a client that will not follow the button.</summary>
    public static string Fallback(string url) =>
        $"""
        <p style="margin:0 0 16px;font-size:13px;color:{Muted};">Button not working? Copy this link into your browser:<br>
        <a href="{E(url)}" target="_blank" style="color:{Muted};word-break:break-all;">{E(url)}</a></p>
        """;

    /// <summary>Small print with one link in it, under a rule.</summary>
    public static string Footnote(string before, string linkText, string url, string after) =>
        $"""
        <p style="margin:24px 0 0;padding-top:16px;border-top:1px solid {Rule};font-size:13px;color:{Muted};">{E(before)}<a href="{E(url)}" target="_blank" style="color:{Muted};">{E(linkText)}</a>{E(after)}</p>
        """;

    /// <summary>Encodes text and attribute values alike; it covers the double quote too.</summary>
    private static string E(string text) => WebUtility.HtmlEncode(text);
}
