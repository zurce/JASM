using System.Net;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.UI.Text;

namespace GIMI_ModManager.WinUI.Helpers.Xaml;

/// <summary>
/// Renders a small, safe subset of HTML into a <see cref="RichTextBlock"/> as inline text
/// (bold/italic/underline/strikethrough, headings, paragraphs, line breaks, hyperlinks,
/// bullet lists, code). Unknown or unsafe markup (scripts, styles, embedded objects) is
/// stripped; text content is HTML-decoded and never executes scripts. Safe for untrusted
/// GameBanana description HTML.
/// </summary>
public static class HtmlToRichText
{
    /// <summary>Attached property: set the HTML text to render into a <see cref="RichTextBlock"/>.</summary>
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.RegisterAttached(
        "Html", typeof(string), typeof(HtmlToRichText),
        new PropertyMetadata(null, OnHtmlChanged));

    public static string? GetHtml(DependencyObject obj) => (string?)obj.GetValue(HtmlProperty);
    public static void SetHtml(DependencyObject obj, string? value) => obj.SetValue(HtmlProperty, value);

    private static void OnHtmlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBlock richTextBlock)
            RenderInto(richTextBlock, e.NewValue as string);
    }

    /// <summary>Renders <paramref name="html"/> into <paramref name="richTextBlock"/>, replacing its content.</summary>
    public static void RenderInto(RichTextBlock richTextBlock, string? html)
    {
        richTextBlock.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(html))
            return;

        try
        {
            var paragraph = new Paragraph();
            foreach (var inline in Parse(html))
                paragraph.Inlines.Add(inline);
            richTextBlock.Blocks.Add(paragraph);
        }
        catch (Exception)
        {
            // Malformed HTML must never crash the pane; fall back to escaped plain text.
            richTextBlock.Blocks.Add(new Paragraph { Inlines = { new Run { Text = WebUtility.HtmlDecode(html) } } });
        }
    }

    /// <summary>
    /// Strips HTML tags and decodes entities, returning plain text with whitespace collapsed.
    /// Used for compact/preview surfaces (e.g. the mod table Notes column) where full HTML
    /// rendering would be too large.
    /// </summary>
    public static string StripHtml(string? html, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        try
        {
            var sb = new StringBuilder(html.Length);
            var i = 0;
            var n = html.Length;
            while (i < n)
            {
                var c = html[i];
                if (c == '<')
                {
                    var gt = html.IndexOf('>', i);
                    if (gt == -1)
                    {
                        sb.Append(c);
                        i++;
                        continue;
                    }
                    var inner = html.Substring(i + 1, gt - i - 1).TrimStart();
                    // Replace block tags with a space separator so words don't run together.
                    var name = inner.StartsWith('/') ? inner[1..].TrimStart() : inner;
                    var tagName = name.Length > 0 ? name.Split(' ', 2)[0].ToLowerInvariant() : string.Empty;
                    if (tagName is "br" or "p" or "div" or "li" or "ul" or "ol" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "tr" or "hr")
                        sb.Append(' ');
                    i = gt + 1;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }

            var decoded = WebUtility.HtmlDecode(sb.ToString());
            var collapsed = string.Join(' ', decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return collapsed.Length > maxLength ? collapsed[..maxLength].TrimEnd() + "…" : collapsed;
        }
        catch (Exception)
        {
            var decoded2 = WebUtility.HtmlDecode(html).Trim();
            return decoded2.Length > maxLength ? decoded2[..maxLength].TrimEnd() + "…" : decoded2;
        }
    }

    /// <summary>Parses <paramref name="html"/> into a flat list of <see cref="Inline"/> elements.</summary>
    public static IReadOnlyList<Inline> Parse(string html)
    {
        var result = new List<Inline>();
        var ctxStack = new Stack<Ctx>();
        Ctx ctx = new();

        void PopToRoot()
        {
            ctx = ctxStack.Count > 0 ? ctxStack.Peek() : new Ctx();
        }

        var tokens = Tokenize(html);
        foreach (var token in tokens)
        {
            if (!token.IsTag)
            {
                if (token.Text.Length == 0)
                    continue;
                result.Add(Run(token.Text, ctx));
                continue;
            }

            var name = token.TagName;
            var closing = token.IsClosing;

            switch (name)
            {
                case "b":
                case "strong":
                    if (closing) { if (ctxStack.Count > 0) ctxStack.Pop(); PopToRoot(); }
                    else { ctxStack.Push(ctx = Push(ctx, b: true)); }
                    break;
                case "i":
                case "em":
                    if (closing) { if (ctxStack.Count > 0) ctxStack.Pop(); PopToRoot(); }
                    else { ctxStack.Push(ctx = Push(ctx, i: true)); }
                    break;
                case "u":
                    if (closing) { if (ctxStack.Count > 0) ctxStack.Pop(); PopToRoot(); }
                    else { ctxStack.Push(ctx = Push(ctx, u: true)); }
                    break;
                case "s":
                case "strike":
                case "del":
                    if (closing) { if (ctxStack.Count > 0) ctxStack.Pop(); PopToRoot(); }
                    else { ctxStack.Push(ctx = Push(ctx, s: true)); }
                    break;
                case "br":
                    if (!closing)
                        result.Add(new LineBreak());
                    break;
                case "p":
                case "div":
                case "section":
                case "article":
                case "blockquote":
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    if (closing)
                        result.Add(new LineBreak());
                    else if (result.Count > 0)
                        result.Add(new LineBreak());
                    break;
                case "ul":
                case "ol":
                    if (!closing)
                        PushList(ctx);
                    break;
                case "li":
                    if (!closing)
                        result.Add(Run("•\u00A0", ctx));
                    else
                        result.Add(new LineBreak());
                    break;
                case "a":
                    if (!closing)
                    {
                        var href = GetAttribute(token.Attributes, "href");
                        if (Uri.TryCreate(href, UriKind.Absolute, out var uri))
                        {
                            var link = new Hyperlink { NavigateUri = uri };
                            link.Inlines.Add(new Run { Text = WebUtility.HtmlDecode(uri.ToString()) });
                            result.Add(link);
                        }
                    }
                    break;
                case "code":
                    if (!closing)
                        result.Add(Run("`", ctx));
                    break;
                case "img":
                    // Images are not rendered (avoid remote-resource/layout issues).
                    if (!closing)
                        result.Add(Run("[image]", ctx));
                    break;
                default:
                    // Unknown tags: ignore the tag, keep rendering inner text.
                    break;
            }
        }

        return result;
    }

    /// <summary>Inline formatting context.</summary>
    private struct Ctx
    {
        public bool B;
        public bool I;
        public bool U;
        public bool S;
        public bool Bullet;
    }

    private static Ctx Push(Ctx source, bool b = false, bool i = false, bool u = false, bool s = false, bool bullet = false)
    {
        return new Ctx
        {
            B = source.B || b,
            I = source.I || i,
            U = source.U || u,
            S = source.S || s,
            Bullet = bullet || source.Bullet
        };
    }

    private static Ctx PushList(Ctx source) => source with { Bullet = true };

    private static Run Run(string text, Ctx ctx)
    {
        var decorations = TextDecorations.None;
        if (ctx.U) decorations |= TextDecorations.Underline;
        if (ctx.S) decorations |= TextDecorations.Strikethrough;

        return new Run
        {
            Text = WebUtility.HtmlDecode(text),
            FontWeight = ctx.B ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = ctx.I ? FontStyle.Italic : FontStyle.Normal,
            TextDecorations = decorations
        };
    }

    #region Tokenization

    private struct Token
    {
        public string Text;
        public bool IsTag;
        public bool IsClosing;
        public string TagName;
        public string Attributes;
    }

    /// <summary>Small, tag-focused HTML tokenizer. Strips comments/CDATA; handles quoted attributes.</summary>
    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        var text = new StringBuilder();
        var i = 0;
        var n = html.Length;

        void Flush()
        {
            if (text.Length == 0)
                return;
            tokens.Add(TokenForText(text.ToString()));
            text.Clear();
        }

        while (i < n)
        {
            var c = html[i];
            if (c == '<')
            {
                var gt = FindClosingBracket(html, i);
                if (gt == -1)
                {
                    text.Append('<');
                    i++;
                    continue;
                }

                var inner = html.Substring(i + 1, gt - i - 1);
                var trimmed = inner.TrimStart();

                if (trimmed.StartsWith("!--", StringComparison.Ordinal))
                {
                    var end = html.IndexOf("-->", gt, StringComparison.Ordinal);
                    i = end == -1 ? n : end + 3;
                    continue;
                }

                Flush();

                var isClosing = trimmed.StartsWith('/');
                if (isClosing)
                    trimmed = trimmed[1..].TrimStart();

                tokens.Add(TokenForTag(trimmed, isClosing));
                i = gt + 1;
            }
            else
            {
                text.Append(c);
                i++;
            }
        }

        Flush();
        return tokens;
    }

    /// <summary>Finds the '&gt;' that closes a tag, respecting quoted attribute values.</summary>
    private static int FindClosingBracket(string html, int start)
    {
        var quote = '\0';
        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }
            if (c == '>')
                return i;
        }
        return -1;
    }

    private static Token TokenForText(string text) => new() { Text = text, IsTag = false };

    private static Token TokenForTag(string tag, bool isClosing)
    {
        var ws = IndexOfWhitespace(tag);
        var tagName = ws == -1 ? tag : tag[..ws];
        var hasSelfClose = tag.EndsWith('/');
        if (hasSelfClose && ws == -1)
            tagName = tag[..^1];

        var attributes = ws == -1
            ? string.Empty
            : (hasSelfClose ? tag.Substring(ws, tag.Length - ws - 1) : tag[(ws + 1)..]);

        return new Token
        {
            Text = tag,
            IsTag = true,
            IsClosing = isClosing,
            TagName = tagName.ToLowerInvariant(),
            Attributes = attributes
        };
    }

    private static int IndexOfWhitespace(string s)
    {
        for (var i = 0; i < s.Length; i++)
            if (char.IsWhiteSpace(s[i]))
                return i;
        return -1;
    }

    /// <summary>Extracts a quoted/unquoted attribute value by name (simple parser).</summary>
    private static string GetAttribute(string attributes, string name)
    {
        var search = name + "=";
        var idx = attributes.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx == -1)
        {
            // handle attributes like `name = "value"`
            var nameOnly = attributes.IndexOf(name, StringComparison.OrdinalIgnoreCase);
            if (nameOnly == -1)
                return string.Empty;
            idx = nameOnly;
        }

        var eq = attributes.IndexOf('=', idx + name.Length);
        if (eq == -1)
            return string.Empty;

        var slice = attributes[(eq + 1)..].TrimStart();
        if (slice.Length == 0)
            return string.Empty;

        var q = slice[0];
        if (q is '"' or '\'')
        {
            var end = slice.IndexOf(q, 1);
            return end == -1 ? slice[1..] : slice[1..end];
        }

        var ws = IndexOfWhitespace(slice);
        return ws == -1 ? slice : slice[..ws];
    }

    #endregion
}