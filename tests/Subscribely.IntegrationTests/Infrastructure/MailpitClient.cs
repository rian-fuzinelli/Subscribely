using System.Net.Http.Json;
using System.Text.Json;

namespace Subscribely.IntegrationTests.Infrastructure;

/// <summary>Lê as mensagens recebidas pelo Mailpit (compartilhado pela suíte; o filtro é o destinatário único do teste).</summary>
public sealed class MailpitClient(ContainersFixture containers)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async Task<List<JsonElement>> MessagesToAsync(string address)
    {
        using var http = new HttpClient { BaseAddress = containers.MailpitApiBaseAddress };
        var result = await http.GetFromJsonAsync<JsonElement>($"api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}", Ct);
        return result.GetProperty("messages").EnumerateArray().ToList();
    }

    public async Task<JsonElement> SingleToAsync(string address, string subjectContains)
    {
        var message = (await MessagesToAsync(address)).Where(m => m.GetProperty("Subject").GetString()!.Contains(subjectContains, StringComparison.Ordinal)).ShouldHaveSingleItem();
        using var http = new HttpClient { BaseAddress = containers.MailpitApiBaseAddress };
        return await http.GetFromJsonAsync<JsonElement>($"api/v1/message/{message.GetProperty("ID").GetString()}", Ct);
    }
}
