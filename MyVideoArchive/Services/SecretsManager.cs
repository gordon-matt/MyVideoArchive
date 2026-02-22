using Extenso;
using MyVideoArchive.Services.Abstractions;

namespace MyVideoArchive.Services;

public class SecretsManager : ISecretsManager
{
    private readonly string secretsPath;

    public SecretsManager(IConfiguration configuration)
    {
        secretsPath = configuration.GetSection("SecretsPath").Value!;
        Secrets = File.ReadAllText(secretsPath).JsonDeserialize<IEnumerable<Secret>>();
    }

    public SecretsManager(string secretsPath)
    {
        this.secretsPath = secretsPath;
        Secrets = File.ReadAllText(secretsPath).JsonDeserialize<IEnumerable<Secret>>();
    }

    public IEnumerable<Secret> Secrets { get; set; }

    public string? GetSecret(string secretName) => Secrets.FirstOrDefault(x => x.Name == secretName)?.Value;
}

public class Secret
{
    public required string Name { get; set; }

    public required string Value { get; set; }
}