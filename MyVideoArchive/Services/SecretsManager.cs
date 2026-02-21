using Extenso;

namespace MyVideoArchive.Services;

public class SecretsManager : ISecretsManager
{
    private readonly string secretsPath;

    public SecretsManager(IConfiguration configuration)
    {
        secretsPath = configuration.GetSection("SecretsPath").Value;
        Secrets = File.ReadAllText(secretsPath).JsonDeserialize<IEnumerable<Secret>>();
    }

    public SecretsManager(string secretsPath)
    {
        this.secretsPath = secretsPath;
        Secrets = File.ReadAllText(secretsPath).JsonDeserialize<IEnumerable<Secret>>();
    }

    public IEnumerable<Secret> Secrets { get; set; }

    public string GetSecret(string secretName) => Secrets.FirstOrDefault(x => x.Name == secretName)?.Value;
}

public class Secret
{
    public string Name { get; set; }

    public string Value { get; set; }
}