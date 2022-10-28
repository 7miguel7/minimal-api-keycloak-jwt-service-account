using System.Net; // can move it to globals.cs
using System.Net.Http.Headers; // can move it to globals.cs

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTransient<KeycloakMiddleware>();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseMiddleware(typeof(KeycloakMiddleware));
app.MapGet("/", () => Task.FromResult("Hello World!"));

app.Run();

public class KeycloakMiddleware : IMiddleware
{
    private readonly ILogger<KeycloakMiddleware> _logger;

    private const string MyKeycloak =
        "https://keycloak-dev.local/auth/realms/MyRealm/protocol/openid-connect/userinfo";

    public KeycloakMiddleware(ILogger<KeycloakMiddleware> logger) => _logger = logger;

    /// <inheritdoc></inheritdoc>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Request.Headers.TryGetValue("Authorization", out var keycloakToken);

        if (string.IsNullOrEmpty(keycloakToken) || !keycloakToken[0].ToLower().Contains($"bearer"))
        {
            _logger.LogError("JWT Token was not provided");
            throw new BadHttpRequestException("JWT Token is required");
        }

        var httpClient = context.RequestServices.GetService<IHttpClientFactory>();

        using var myRequest = httpClient.CreateClient();
        myRequest.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        myRequest.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue($"Bearer", keycloakToken);

        try
        {
            using var result = await myRequest.GetAsync(MyKeycloak);

            if (result is not { IsSuccessStatusCode: true })
            {
                if (result.StatusCode == HttpStatusCode.BadRequest)
                {
                    _logger.LogError("Access denied {ResultStatusCode}", result.StatusCode);
                    throw new BadHttpRequestException(
                        "Access denied. Invalid token");
                }
                _logger.LogError(result.StatusCode.ToString());
                throw new HttpRequestException(
                    "An internal error occurred");
                
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex.Message);
            throw new HttpRequestException(
                "An internal error occurred");
        }

        await next(context);
    }
}