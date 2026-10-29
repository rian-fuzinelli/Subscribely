using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Subscribely.Modules.Notifications.Email;

internal sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// Templates embutidos <c>Templates/{nome}.html</c> e <c>{nome}.txt</c> (1ª linha do texto: <c>Subject: ...</c>).
/// Placeholders <c>{{campo}}</c> são <b>sempre</b> HTML-encoded no HTML; placeholder sem valor é erro.
/// </summary>
internal sealed partial class TemplateRenderer
{
    private const string SubjectPrefix = "Subject:";

    private readonly ConcurrentDictionary<string, (string Subject, string Html, string Text)> _templates = new();

    public RenderedEmail Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var (subject, html, text) = _templates.GetOrAdd(template, Load);
        return new RenderedEmail(
            Fill(subject, values, encode: false),
            Fill(html, values, encode: true),
            Fill(text, values, encode: false));
    }

    private static string Fill(string content, IReadOnlyDictionary<string, string> values, bool encode) =>
        Placeholder().Replace(content, match =>
        {
            var name = match.Groups[1].Value;
            if (!values.TryGetValue(name, out var value))
            {
                throw new InvalidOperationException($"Missing value for placeholder '{name}'.");
            }

            return encode ? HtmlEncoder.Default.Encode(value) : value;
        });

    private static (string Subject, string Html, string Text) Load(string template)
    {
        var html = ReadResource($"{template}.html");
        var text = ReadResource($"{template}.txt");
        var firstLine = text.IndexOf('\n', StringComparison.Ordinal);
        var subjectLine = (firstLine < 0 ? text : text[..firstLine]).Trim();
        if (!subjectLine.StartsWith(SubjectPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Template {template}.txt must start with '{SubjectPrefix}'.");
        }

        return (subjectLine[SubjectPrefix.Length..].Trim(), html, text[(firstLine + 1)..].TrimStart('\r', '\n'));
    }

    private static string ReadResource(string fileName)
    {
        var assembly = typeof(TemplateRenderer).Assembly;
        var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith($".Templates.{fileName}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Email template '{fileName}' not found.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}

/// <summary>Formatação para o leitor brasileiro: <c>R$ 1.234,56</c> e horário de Brasília.</summary>
internal static class PtBr
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static string Money(long cents)
    {
        var sign = cents < 0 ? "-" : string.Empty;
        var abs = Math.Abs(cents);
        return string.Create(Culture, $"{sign}R$ {abs / 100:N0},{abs % 100:00}");
    }

    public static string DateTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, SaoPaulo).ToString("dd/MM/yyyy 'às' HH:mm", Culture);

    public static string Date(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, SaoPaulo).ToString("dd/MM/yyyy", Culture);
}
