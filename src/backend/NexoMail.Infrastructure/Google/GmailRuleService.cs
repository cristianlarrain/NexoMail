using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailRuleService(
    IHttpClientFactory httpClientFactory,
    NexoMailDbContext database,
    ITokenProtector tokenProtector,
    IOptions<GmailOptions> options) : IMailRuleProvider
{
    private static readonly Regex EmailPattern = new(
        @"(?<![\w.-])[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}(?![\w.-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GmailOperatorPattern = new(
        @"(?:^|\s)(?:from|to|cc|bcc|subject|label|in|is|has|larger|smaller|after|before|newer|older|filename):",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> SystemLabelIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "INBOX", "SPAM", "TRASH", "UNREAD", "STARRED", "IMPORTANT", "SENT", "DRAFT",
        "CATEGORY_PERSONAL", "CATEGORY_SOCIAL", "CATEGORY_PROMOTIONS", "CATEGORY_UPDATES", "CATEGORY_FORUMS"
    };

    public MailProviderType ProviderType => MailProviderType.Gmail;

    public async Task<IReadOnlyList<MailRuleDefinition>> ListAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        var labels = await GetLabelMapAsync(client, cancellationToken);
        using var response = await client.GetAsync("users/me/settings/filters", cancellationToken);
        await EnsureRulePermissionAsync(response, cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken);
        var result = new List<MailRuleDefinition>();
        if (document is null || !document.RootElement.TryGetProperty("filter", out var filters) || filters.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var filter in filters.EnumerateArray())
        {
            var id = filter.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var query = filter.TryGetProperty("criteria", out var criteria) && criteria.ValueKind == JsonValueKind.Object
                ? BuildCriteriaQuery(criteria)
                : string.Empty;
            if (!filter.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object) continue;

            var definition = ReadSupportedAction(id, query, action, labels);
            if (definition is not null) result.Add(definition);
        }

        return result;
    }

    public async Task<IReadOnlyList<MailRuleDestination>> GetDestinationsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(accountId, cancellationToken);
        var labels = await GetLabelMapAsync(client, cancellationToken);
        return labels
            .Where(pair => !SystemLabelIds.Contains(pair.Key))
            .Select(pair => new MailRuleDestination(pair.Key, pair.Value))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<MailRuleCreateResult> CreateAsync(MailRuleCreateRequest request, CancellationToken cancellationToken)
    {
        var normalizedQuery = NormalizeIncomingQuery(request.Query);
        if (normalizedQuery.Length is < 1 or > 500)
            throw new InvalidOperationException("La condición de la regla debe tener entre 1 y 500 caracteres.");

        var client = await CreateClientAsync(request.AccountId, cancellationToken);
        var labels = await GetLabelMapAsync(client, cancellationToken);
        var destination = await ResolveDestinationAsync(
            client,
            request.Action,
            request.DestinationId,
            request.DestinationName,
            labels,
            cancellationToken);
        var actionPayload = BuildActionPayload(request.Action, destination?.Id);

        using (var listResponse = await client.GetAsync("users/me/settings/filters", cancellationToken))
        {
            await EnsureRulePermissionAsync(listResponse, cancellationToken);
            using var list = await ReadJsonAsync(listResponse, cancellationToken);
            if (list is not null && list.RootElement.TryGetProperty("filter", out var filters) && filters.ValueKind == JsonValueKind.Array)
            {
                foreach (var filter in filters.EnumerateArray())
                {
                    var criteriaQuery = filter.TryGetProperty("criteria", out var criteria) && criteria.ValueKind == JsonValueKind.Object
                        ? BuildCriteriaQuery(criteria)
                        : string.Empty;
                    if (!string.Equals(criteriaQuery.Trim(), normalizedQuery, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!filter.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object) continue;

                    var existingId = filter.TryGetProperty("id", out var existingIdElement) ? existingIdElement.GetString() ?? string.Empty : string.Empty;
                    var existing = ReadSupportedAction(existingId, criteriaQuery, action, labels);
                    if (existing is null || existing.Action != request.Action) continue;
                    if (request.Action == MailRuleActionType.MoveToFolder
                        && !string.Equals(existing.DestinationId, destination?.Id, StringComparison.OrdinalIgnoreCase)) continue;

                    return new MailRuleCreateResult(existing, false);
                }
            }
        }

        using var response = await client.PostAsJsonAsync("users/me/settings/filters", new
        {
            criteria = new { query = normalizedQuery },
            action = actionPayload
        }, cancellationToken);
        await EnsureRulePermissionAsync(response, cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken)
            ?? throw new InvalidOperationException("Gmail creó la regla, pero no devolvió un identificador válido.");
        var id = document.RootElement.TryGetProperty("id", out var createdIdElement) ? createdIdElement.GetString() ?? string.Empty : string.Empty;
        var definition = new MailRuleDefinition(id, normalizedQuery, request.Action, destination?.Id, destination?.DisplayName);
        return new MailRuleCreateResult(definition, true);
    }

    public async Task<bool> RemoveAsync(Guid accountId, string ruleId, CancellationToken cancellationToken)
    {
        var normalizedId = ruleId.Trim();
        if (normalizedId.Length is < 1 or > 300)
            throw new InvalidOperationException("La regla indicada no es válida.");

        var client = await CreateClientAsync(accountId, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"users/me/settings/filters/{Uri.EscapeDataString(normalizedId)}");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureRulePermissionAsync(response, cancellationToken);
        return true;
    }

    private static MailRuleDefinition? ReadSupportedAction(
        string id,
        string query,
        JsonElement action,
        IReadOnlyDictionary<string, string> labels)
    {
        var addLabels = ReadStringArray(action, "addLabelIds");
        var removeLabels = ReadStringArray(action, "removeLabelIds");

        if (addLabels.Contains("TRASH", StringComparer.OrdinalIgnoreCase))
            return new MailRuleDefinition(id, query, MailRuleActionType.Trash);

        var destinationId = addLabels.FirstOrDefault(label => !SystemLabelIds.Contains(label));
        if (!string.IsNullOrWhiteSpace(destinationId))
        {
            var destinationName = labels.TryGetValue(destinationId, out var name) ? name : destinationId;
            return new MailRuleDefinition(id, query, MailRuleActionType.MoveToFolder, destinationId, destinationName);
        }

        if (removeLabels.Contains("INBOX", StringComparer.OrdinalIgnoreCase))
            return new MailRuleDefinition(id, query, MailRuleActionType.Archive);

        if (removeLabels.Contains("UNREAD", StringComparer.OrdinalIgnoreCase))
            return new MailRuleDefinition(id, query, MailRuleActionType.MarkRead);

        return null;
    }

    private static Dictionary<string, string[]> BuildActionPayload(MailRuleActionType action, string? destinationId)
    {
        return action switch
        {
            MailRuleActionType.Trash => new Dictionary<string, string[]> { ["addLabelIds"] = ["TRASH"] },
            MailRuleActionType.Archive => new Dictionary<string, string[]> { ["removeLabelIds"] = ["INBOX"] },
            MailRuleActionType.MarkRead => new Dictionary<string, string[]> { ["removeLabelIds"] = ["UNREAD"] },
            MailRuleActionType.MoveToFolder when !string.IsNullOrWhiteSpace(destinationId) => new Dictionary<string, string[]>
            {
                ["addLabelIds"] = [destinationId],
                ["removeLabelIds"] = ["INBOX"]
            },
            _ => throw new InvalidOperationException("La acción solicitada para la regla no es válida.")
        };
    }

    private static async Task<MailRuleDestination?> ResolveDestinationAsync(
        HttpClient client,
        MailRuleActionType action,
        string? destinationId,
        string? destinationName,
        IDictionary<string, string> labels,
        CancellationToken cancellationToken)
    {
        if (action != MailRuleActionType.MoveToFolder) return null;

        var normalizedId = destinationId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedId))
        {
            if (SystemLabelIds.Contains(normalizedId) || !labels.TryGetValue(normalizedId, out var displayName))
                throw new InvalidOperationException("La carpeta o etiqueta de destino ya no está disponible en Gmail.");
            return new MailRuleDestination(normalizedId, displayName);
        }

        var normalizedName = Regex.Replace(destinationName?.Trim() ?? string.Empty, @"\s+", " ");
        if (normalizedName.Length is < 1 or > 120)
            throw new InvalidOperationException("Escribe un nombre de carpeta o etiqueta de entre 1 y 120 caracteres.");

        var existing = labels.FirstOrDefault(pair =>
            !SystemLabelIds.Contains(pair.Key)
            && string.Equals(pair.Value, normalizedName, StringComparison.CurrentCultureIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing.Key))
            return new MailRuleDestination(existing.Key, existing.Value);

        using var response = await client.PostAsJsonAsync("users/me/labels", new
        {
            name = normalizedName,
            labelListVisibility = "labelShow",
            messageListVisibility = "show"
        }, cancellationToken);
        await EnsureRulePermissionAsync(response, cancellationToken);

        using var document = await ReadJsonAsync(response, cancellationToken)
            ?? throw new InvalidOperationException("Gmail creó la etiqueta, pero no devolvió un identificador válido.");
        var id = document.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var name = document.RootElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : normalizedName;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Gmail creó la etiqueta, pero no devolvió un identificador válido.");

        var resolvedName = string.IsNullOrWhiteSpace(name) ? normalizedName : name;
        labels[id] = resolvedName;
        return new MailRuleDestination(id, resolvedName);
    }

    private static string[] ReadStringArray(JsonElement source, string property)
    {
        if (!source.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Array) return [];
        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }

    private static async Task<Dictionary<string, string>> GetLabelMapAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("users/me/labels?fields=labels(id,name,type)", cancellationToken);
        await EnsureRulePermissionAsync(response, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (document is null || !document.RootElement.TryGetProperty("labels", out var labels) || labels.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var label in labels.EnumerateArray())
        {
            var id = label.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var name = label.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) result[id] = name;
        }
        return result;
    }

    private static string NormalizeIncomingQuery(string query)
    {
        var value = Regex.Replace(query.Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (GmailOperatorPattern.IsMatch(value)) return value;

        var email = EmailPattern.Match(value);
        if (email.Success)
        {
            var before = value[..email.Index];
            var isRecipient = Regex.IsMatch(before, @"\b(?:destinatari[oa]s?|dirigid[oa]s?\s+a|enviad[oa]s?\s+a)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return $"{(isRecipient ? "to" : "from")}:{email.Value}";
        }

        var sender = Regex.Match(
            value,
            @"\b(?:correos?\s+(?:de|del)|mensajes?\s+(?:de|del)|remitente|desde)\s+(?<value>.+?)(?=\s+(?:se\s+)?(?:vayan|vaya|env[ií]en|env[ií]e|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (sender.Success)
        {
            var senderValue = CleanCandidate(sender.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(senderValue)) return $"from:{QuoteIfNeeded(senderValue)}";
        }

        var subject = Regex.Match(
            value,
            @"\basunto\s+(?:sea|es|contenga|contiene|con)?\s*(?<value>.+?)(?=\s+(?:se\s+)?(?:vayan|vaya|env[ií]en|env[ií]e|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (subject.Success)
        {
            var subjectValue = CleanCandidate(subject.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(subjectValue)) return $"subject:{QuoteIfNeeded(subjectValue)}";
        }

        value = Regex.Replace(value, @"\b(?:crea|crear|cr[eé]ame|configura|configurar|haz|hacer|genera|generar|define|definir|necesito)\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"\b(?:una|un|la|el)\s+(?:regla|filtro)\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"\bpara\s+que\b|\bpara\s+cuando\b|\bcuando\s+(?:entren|lleguen|llegue|entre|reciba|recibas)\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"\b(?:todos?|todas?)\s+(?:los|las)\s+(?:correos?|mensajes?)\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        value = Regex.Replace(value, @"\b(?:se\s+)?(?:vayan|vaya|env[ií]en|env[ií]e|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+.+$", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return CleanCandidate(value);
    }

    private static string BuildCriteriaQuery(JsonElement criteria)
    {
        var parts = new List<string>();
        AddOperator(parts, criteria, "from", "from");
        AddOperator(parts, criteria, "to", "to");
        AddOperator(parts, criteria, "subject", "subject");

        if (criteria.TryGetProperty("query", out var queryElement) && queryElement.ValueKind == JsonValueKind.String)
        {
            var query = queryElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(query)) parts.Add(query);
        }

        if (criteria.TryGetProperty("negatedQuery", out var negatedElement) && negatedElement.ValueKind == JsonValueKind.String)
        {
            var negated = negatedElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(negated)) parts.Add($"-({negated})");
        }

        if (criteria.TryGetProperty("hasAttachment", out var attachmentElement) && attachmentElement.ValueKind is JsonValueKind.True)
            parts.Add("has:attachment");

        if (criteria.TryGetProperty("excludeChats", out var excludeChatsElement) && excludeChatsElement.ValueKind is JsonValueKind.True)
            parts.Add("-label:chat");

        if (criteria.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var size) && size > 0)
        {
            var comparison = criteria.TryGetProperty("sizeComparison", out var comparisonElement)
                ? comparisonElement.GetString()?.Trim().ToLowerInvariant()
                : null;
            if (comparison is "larger" or "smaller") parts.Add($"{comparison}:{size}");
        }

        return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
    }

    private static void AddOperator(List<string> parts, JsonElement criteria, string property, string gmailOperator)
    {
        if (!criteria.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String) return;
        var value = element.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        parts.Add($"{gmailOperator}:{QuoteIfNeeded(value)}");
    }

    private static string QuoteIfNeeded(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("(") && trimmed.EndsWith(")"))))
            return trimmed;
        return trimmed.Any(char.IsWhiteSpace)
            ? $"\"{trimmed.Replace("\"", "\\\"")}\""
            : trimmed;
    }

    private static string CleanCandidate(string value)
    {
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim(' ', ',', ':', ';', '-', '–', '—');
        if (cleaned.Length >= 2 && ((cleaned.StartsWith("“") && cleaned.EndsWith("”")) || (cleaned.StartsWith("‘") && cleaned.EndsWith("’")) || (cleaned.StartsWith("'") && cleaned.EndsWith("'"))))
            cleaned = cleaned[1..^1].Trim();
        return cleaned;
    }

    private async Task<HttpClient> CreateClientAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var credential = await database.OAuthCredentials.AsNoTracking()
            .SingleOrDefaultAsync(value => value.MailAccountId == accountId, cancellationToken)
            ?? throw new InvalidOperationException("No existe una credencial OAuth para esta cuenta. Vuelve a conectar la cuenta desde Configuración.");

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
            throw new InvalidOperationException("La integración de Google no está configurada en este servidor.");

        string refreshToken;
        try
        {
            refreshToken = tokenProtector.Unprotect(credential.EncryptedRefreshToken);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("La autorización guardada de Google ya no puede utilizarse. Vuelve a conectar esta cuenta desde Configuración.");
        }

        var tokenClient = httpClientFactory.CreateClient();
        using var tokenResponse = await tokenClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }), cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            var tokenError = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            if (tokenResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                || tokenError.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Google rechazó la autorización guardada. Vuelve a conectar esta cuenta desde Configuración para renovar los permisos de reglas.");
            }
            throw new HttpRequestException($"Google no pudo renovar la autorización ({(int)tokenResponse.StatusCode}).", null, tokenResponse.StatusCode);
        }

        using var tokenDocument = await ReadJsonAsync(tokenResponse, cancellationToken)
            ?? throw new InvalidOperationException("Google no entregó un token de acceso válido. Vuelve a conectar la cuenta desde Configuración.");
        var accessToken = tokenDocument.RootElement.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Google no entregó un token de acceso válido. Vuelve a conectar la cuenta desde Configuración.");

        var client = httpClientFactory.CreateClient("Gmail");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("El proveedor devolvió una respuesta no válida al administrar las reglas.", exception, response.StatusCode);
        }
    }

    private static async Task EnsureRulePermissionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
            || body.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase)
            || body.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Esta cuenta todavía no autorizó el permiso para administrar reglas. Vuelve a conectar la cuenta desde Configuración y acepta los permisos del proveedor.");
        }

        throw new HttpRequestException($"Gmail rechazó la operación sobre reglas ({(int)response.StatusCode}).", null, response.StatusCode);
    }
}
