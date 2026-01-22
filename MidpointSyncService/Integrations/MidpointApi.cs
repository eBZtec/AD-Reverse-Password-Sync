using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using static JsonHelper;

public static class MidPointApi
{
    public static string midpointAttribute = RegistryReader.GetMidpointAttribute() ?? "name";
    public static string midpointUrl = RegistryReader.GetMidpointURL();
    private static readonly HttpClient _client = CreateClient();

    public static async Task<bool> AuthenticateUser(string namepattern, string newPass)
    {
        HttpClient client = InitClient();

        string url = midpointUrl + "/ws/rest/self";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var byteArray = Encoding.ASCII.GetBytes($"{namepattern}:{newPass}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        try
        {
            HttpResponseMessage response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode || response.StatusCode.ToString() == "Forbidden")
            {
                return true;
            }
            return false;
        }catch (TaskCanceledException ex)
        {
            LogManager.Log($"[MidpointApi] AuthenticateUser: TIMEOUT/CANCELED -> {ex.GetType().Name} | Message: {ex.Message} | Inner: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
            return false;
        }
        catch (HttpRequestException ex)
        {
            LogManager.Log($"[MidpointApi] SearchUser: HTTP REQUEST ERROR -> {ex.Message} | Inner: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
            return false;
        }
        catch (Exception ex)
        {
            LogManager.Log($"[MidpointApi] SearchUser: UNEXPECTED -> {ex.GetType().FullName} | {ex.Message} | Inner: {ex.InnerException?.Message}");
            return false;
        }
    }

    public static async Task<(string? oid, string? statusCode)> SearchUser(string namepattern)
    {
        HttpClient client = InitClient();

        var payload = new { query = new { filter = new { text = $"{RegistryReader.GetMidpointAttribute()} =[stringIgnoreCase] \"{namepattern}\"" } } };

        if(midpointAttribute == "name")
            payload = new { query = new { filter = new { text = $"{RegistryReader.GetMidpointAttribute()} =[polyStringNorm] \"{namepattern}\"" } } };

        string jsonBody = JsonSerializer.Serialize(payload);

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        string url = midpointUrl + "/ws/rest/users/search";
        
        try
        {
            HttpResponseMessage response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                LogManager.Log($"[MidpointApi] SearchUser: Failed search, response status code: {response.StatusCode}");
                return (null, "failed_search");
            }

            string result = await response.Content.ReadAsStringAsync();

            if (!JsonHelper.Deserialize(result, out MidPointResponse? user))
            {
                LogManager.Log($"[MidpointApi] SearchUser: Error, deserialization failed");
                return (null, "failed_search");
            }

            if (user.Object.UsersList == null)
            {
                LogManager.Log($"[MidpointApi] SearchUser: No user found in MidPoint with {midpointAttribute}: {namepattern}");
                return (null, "no_oid");
            }
            
            if (string.IsNullOrEmpty(user.Object.UsersList.First().Oid))
            {
                LogManager.Log($"[MidpointApi] SearchUser: No Oid found in MidPoint for user: {namepattern} [att: {midpointAttribute}]");
                return (null, "no_oid");
            }

            return (user.Object.UsersList.First().Oid,"success");
        }
        catch (TaskCanceledException ex)
        {
            LogManager.Log($"[MidpointApi] SearchUser: TIMEOUT/CANCELED -> {ex.GetType().Name} | Message: {ex.Message} | Inner: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
            return (null, "failed_search");
        }
        catch (HttpRequestException ex)
        {
            LogManager.Log($"[MidpointApi] SearchUser: HTTP REQUEST ERROR -> {ex.Message} | Inner: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
            return (null, "failed_search");
        }
        catch (Exception ex)
        {
            LogManager.Log($"[MidpointApi] SearchUser: UNEXPECTED -> {ex.GetType().FullName} | {ex.Message} | Inner: {ex.InnerException?.Message}");
            return (null, "failed_search");
        }
    }

    public static async Task<bool> PatchChange(string Oid, string NewPass)
    {
        HttpClient client = InitClient();
        var payload = new { objectModification = new { itemDelta = new { modificationType = "replace", path = "credentials/password/value", value = NewPass } } };
        string jsonBody = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        string url = midpointUrl + $"/ws/rest/users/{Oid}";

        try
        {
            HttpResponseMessage response = await client.PatchAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                Deserialize<MidpointPatchResponse>(error, out var patchResponse);

                LogManager.Log($"[MidpointApi] PatchChange: Response status code: {patchResponse.Response.Status} - Message: {patchResponse.Response.Message}");

                if(patchResponse.Response.Status == "fatal_error")
                    return false;
            }

            return true;
        }
        catch (TaskCanceledException ex)
        {
            LogManager.Log($"[MidpointApi] PatchChange: TIMEOUT/CANCELED -> {ex.GetType().Name} | Message: {ex.Message} | Inner: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
            return false;
        }
        catch (HttpRequestException ex)
        {
            LogManager.Log($"[MidpointApi] PatchChange: HTTP REQUEST ERROR -> {ex.Message} | Inner: {ex.InnerException?.GetType().Name} - {ex.InnerException?.Message}");
            return false;
        }
        catch (Exception ex)
        {
            LogManager.Log($"[MidpointApi] PatchChange: UNEXPECTED -> {ex.GetType().FullName} | {ex.Message} | Inner: {ex.InnerException?.Message}");
            return false;
        }
    }


    public static HttpClient CreateClient()
    {
        HttpClient client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);
    
        var (user, pass) = WinCred.ReadGeneric("MidPointSync");

        if(user == null)
        {
            LogManager.Log($"[MidpointApi] InitClient: No credential found for Midpoint");
        }
        var byteArray = Encoding.ASCII.GetBytes($"{user}:{pass}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    }
    public static HttpClient InitClient() => _client;
}