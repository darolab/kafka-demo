using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;

namespace KafkaDemo.Services;

public class VaultService
{
    private readonly IVaultClient _vaultClient;

    public VaultService(IConfiguration configuration)
    {
        var vaultAddress =
            configuration["Vault:Address"]
            ?? "http://localhost:8200";

        var vaultToken =
            configuration["Vault:Token"];

        var authMethod =
            new TokenAuthMethodInfo(vaultToken);

        _vaultClient = new VaultClient(
            new VaultClientSettings(
                vaultAddress,
                authMethod
            )
        );
    }

   public async Task<IDictionary<string, object>> GetSecretAsync()
{
    var result = await _vaultClient
        .V1
        .Secrets
        .KeyValue
        .V2
        .ReadSecretAsync(
            path: "my-api",
            mountPoint: "secret"
        );

    return result.Data.Data;
}
}